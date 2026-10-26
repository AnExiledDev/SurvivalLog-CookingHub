using System;
using System.Collections.Generic;
using System.Linq;
using CookingHub.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using GameCore.HotUpdate.ReduxUI;
using Il2CppInterop.Runtime;

namespace CookingHub.Adapters;

/// <summary>
/// The game's side of every read and press. Food is read the way the building menu counts materials (the player's bag
/// and every home storage in the cost pool); the open stove is read from the cooking panel's own state; food moves
/// through the handler the panel's quick move and recipe click send their events to. Each call fails soft: an exception
/// is logged once per kind of call and answered as a refusal.
/// </summary>
internal sealed partial class GameCooking : ICookGame, IKitchenWatch
{
    private const int FoodCategory = 1;
    private const int BurnsFuelType = 1;

    private readonly Action<string> _log;
    private const int MaxOrigins = 2000;

    private readonly HashSet<string> _reportedFailures = new();

    /// <summary>Where each unit this mod put in a pot came from, by its instance id; main thread only.</summary>
    private readonly Dictionary<long, long> _origins = new();

    /// <summary>How long what the player has learned, and the names of where food is kept, are trusted between reads.</summary>
    private const long KeepMilliseconds = 5000;

    /// <summary>How long a slice of the cookbook read may take in one frame.</summary>
    private static readonly long SliceTicks = (long)(2.0 * System.Diagnostics.Stopwatch.Frequency / 1000.0);

    /// <summary>A cookbook read that failed is tried again after this long, not every frame.</summary>
    private const long CatalogRetryMilliseconds = 5000;

    private static Il2CppSystem.Type? _cookingType;

    private IconIndex? _icons;
    private System.Threading.Tasks.Task<(IconIndex Icons, string Note)>? _iconScan;
    private (IntPtr Config, bool IsChinese) _catalogKey;
    private Cookbook? _catalog;

    /// <summary>Held so the config the cookbook is keyed on keeps its address while the key is in use.</summary>
    private ConfigManager? _catalogConfig;
    private int _multiPotLevel = Kitchen.ShippedMultiPotLevel;

    private SlicedJob? _catalogJob;
    private (IntPtr Config, bool IsChinese) _catalogJobKey;
    private long _catalogRetryAt;
    private bool _hasCatalogFailed;
    private long _sliceEndsAt;
    private readonly Func<bool> _isSliceSpent;

    /// <summary>The UI system, kept while the game keeps the same one: the screen is checked every frame.</summary>
    private IntPtr _uiPointer;
    private ReduxUISystem? _ui;

    /// <summary>
    /// The tables a read looks rows up in, kept with the config they came from (held, so its address is never reused):
    /// each field read makes a new wrapper, and the game's own lookups log an error for every missing row.
    /// </summary>
    private ConfigManager? _tablesConfig;
    private Il2CppSystem.Collections.Generic.Dictionary<int, Config_Item>? _itemTable;
    private Il2CppSystem.Collections.Generic.Dictionary<int, Config_FurnitureCook>? _cookTable;
    private Il2CppSystem.Collections.Generic.Dictionary<int, Config_CookingRecipe>? _recipeTable;

    /// <summary>The item table rows a read looks at for every stack, by item id; dropped with the tables.</summary>
    private readonly Dictionary<int, ItemRow> _itemRows = new();

    /// <summary>Each stove kind's allowed recipes (null for all), by its cook row id; dropped with the tables.</summary>
    private readonly Dictionary<int, HashSet<int>?> _allowed = new();

    private (IntPtr Record, int Level) _learnedKey;
    private long _learnedUntil;
    private IReadOnlyCollection<int> _cooked = Array.Empty<int>();
    private IReadOnlyCollection<int> _craftUnlocked = Array.Empty<int>();

    private (IntPtr Config, IntPtr Agents, long RoleId, bool IsChinese) _placesKey;
    private long _placesUntil;
    private Places? _places;

    private string? _forecastJson;
    private IReadOnlyList<Forecast> _forecasts = Array.Empty<Forecast>();

    public GameCooking(Action<string> log)
    {
        _log = log;
        _isSliceSpent = () => System.Diagnostics.Stopwatch.GetTimestamp() >= _sliceEndsAt;
    }

    public bool IsLoaded()
    {
        try
        {
            return LoadedGame() != null;
        }
        catch (Exception ex)
        {
            ReportOnce("load check", ex);

            return false;
        }
    }

    public CookRead Read()
    {
        try
        {
            return ReadKitchen();
        }
        catch (Exception ex)
        {
            ReportOnce("read", ex);

            return new CookRead.NotReady(Texts.ReadFailed.Pick(GameLanguage.IsChinese()));
        }
    }

    /// <summary>A whole stack by the panel's quick move, for food coming out of the pot.</summary>
    public bool MoveStack(long instanceId, long fromOwnerId, long toOwnerId) => Press("move", () =>
    {
        var items = LoadedGame()?.World._ItemManager;

        if (items == null || !QuickMove(items, instanceId, fromOwnerId, toOwnerId)) return false;

        BagRefresh.Ask();

        return true;
    });

    /// <summary>
    /// With Stacking, each unit goes in by its one-unit move. That move puts the unit in under the stack's own id and
    /// leaves the rest as a new stack in the same place, so the next unit comes off the stack that appeared there.
    /// Without Stacking, or for a single item, the stack goes in whole by the panel's quick move. An item used a little
    /// at a time goes in a use at a time (MoveUses).
    /// </summary>
    public int MoveUnits(long instanceId, long fromOwnerId, long toOwnerId, int units) => PressCount("move units", () =>
    {
        var game = LoadedGame();
        var items = game?.World._ItemManager;
        var moved = 0;
        var current = instanceId;

        if (items != null && items.GetItemData(instanceId) is { ItemCount: > 0 } first && RowOf(game!.Value.Config, first.ItemConfigId).IsUsedUp)
        {
            moved = MoveUses(items, instanceId, fromOwnerId, toOwnerId, units);

            if (moved > 0) BagRefresh.Ask();

            return moved;
        }

        while (items != null && moved < units && items.GetItemData(current) is { ItemCount: > 0 } item)
        {
            var configId = item.ItemConfigId;
            var count = item.ItemCount;
            var before = IdsOf(items, fromOwnerId, configId);
            var one = count >= 2 ? StackingUnits.MoveOne(current, fromOwnerId, toOwnerId) : null;

            if (one == null)
            {
                if (QuickMove(items, current, fromOwnerId, toOwnerId)) moved += Remember(current, fromOwnerId, count);

                break;
            }

            if (one == false)
            {
                _log($"stacking did not move a unit of stack {current} from {fromOwnerId} to {toOwnerId}");

                break;
            }

            moved += Remember(current, fromOwnerId, 1);
            current = IdsOf(items, fromOwnerId, configId).FirstOrDefault(id => !before.Contains(id));
        }

        if (moved > 0) BagRefresh.Ask();

        return moved;
    });

    /// <summary>
    /// The panel's quick move (ItemManager.OnCookingQuickMove) splits one use off an item used a little at a time, then
    /// merges it into a matching item already in the pot. Recipe matching counts each pot item once, so two dishes that
    /// each want a use of the same food would see only one. This does the same split and move without the merge: each
    /// use becomes a pot item of its own. The item keeps its id until its last use goes in.
    /// </summary>
    private int MoveUses(ItemManager items, long instanceId, long fromOwnerId, long toOwnerId, int units)
    {
        var moved = 0;

        while (moved < units && items.GetItemData(instanceId) is { ItemCount: > 0 } item)
        {
            var isLast = !ItemManager.WouldSplitForCooking(item);
            var pieceId = isLast ? instanceId : items.SplitItem(instanceId, 1)?.InstanceId ?? 0;

            if (pieceId == 0)
            {
                _log($"the game did not split a use off item {instanceId}");

                break;
            }

            var before = Holding(items, toOwnerId);
            var isMoved = items.TryQuickMoveItem(pieceId, fromOwnerId, toOwnerId, false, false) && Holding(items, toOwnerId) != before;

            items.RefreshCookingPendingIfSource(fromOwnerId);

            if (!isMoved)
            {
                _log($"the game did not move a use of item {instanceId} from {fromOwnerId} to {toOwnerId}");

                // The use split off stays where it was: put it back on its item, as the game does when a quick move merges.
                if (!isLast) items.TryMergeIntoOwner(pieceId, fromOwnerId);

                break;
            }

            moved += Remember(pieceId, fromOwnerId, 1);

            if (isLast) break;
        }

        return moved;
    }

    public long? OriginOf(long instanceId) => _origins.TryGetValue(instanceId, out var ownerId) ? ownerId : null;

    /// <summary>Notes where a unit that went into a pot came from, so emptying the pot can put it back there.</summary>
    private int Remember(long instanceId, long fromOwnerId, int units)
    {
        if (_origins.Count >= MaxOrigins) _origins.Clear();

        _origins[instanceId] = fromOwnerId;

        return units;
    }

    /// <summary>
    /// ItemManager.OnCookingQuickMove, what the panel's quick move and recipe click both end in. It returns nothing, so
    /// the move counts when what the target holds changed.
    /// </summary>
    private bool QuickMove(ItemManager items, long instanceId, long fromOwnerId, long toOwnerId)
    {
        var before = Holding(items, toOwnerId);

        items.OnCookingQuickMove(instanceId, fromOwnerId, toOwnerId);

        var isMoved = Holding(items, toOwnerId) != before;

        if (!isMoved) _log($"the game did not move stack {instanceId} from {fromOwnerId} to {toOwnerId}");

        return isMoved;
    }

    private static HashSet<long> IdsOf(ItemManager items, long ownerId, int configId)
    {
        var ids = new HashSet<long>();
        var held = items.GetItemDataList(ownerId);

        for (var i = 0; held != null && i < held.Count; i++)
        {
            if (held[i] is { } item && item.ItemConfigId == configId) ids.Add(item.InstanceId);
        }

        return ids;
    }

    /// <summary>
    /// Furniture.OnCookingFuelAdd, what the panel's fuel slot drop sends: one unit of the stack goes into the stove's
    /// fuel and leaves wherever it was kept. It returns nothing, so it counts when the stove's fuel grew.
    /// </summary>
    public bool AddFuel(long furnitureId, long instanceId) => Press("fuel", () =>
    {
        var furniture = FurnitureOf(LoadedGame()?.Agents, furnitureId);
        var cooking = furniture == null ? null : AgentTools.GetAgentComponent<CookingComponent>(furniture);

        if (furniture == null || cooking == null) return false;

        var before = FuelHeld(cooking);

        furniture.OnCookingFuelAdd(instanceId);

        var isAdded = FuelHeld(cooking) != before;

        if (isAdded) BagRefresh.Ask();
        else _log($"the game did not burn stack {instanceId} in stove {furnitureId}");

        return isAdded;
    });

    private CookRead ReadKitchen()
    {
        var game = LoadedGame();

        if (game == null) return new CookRead.NoGame();

        var (world, config, agents, role) = game.Value;
        var isChinese = GameLanguage.IsChinese();

        if (!IsHome(agents)) return new CookRead.NotReady(Texts.Away.Pick(isChinese));

        if (Catalog(config, isChinese) is not { } book) return new CookRead.NotReady((_hasCatalogFailed ? Texts.ReadFailed : Texts.Loading).Pick(isChinese));

        var record = AgentTools.GetAgentComponent<CookingRecordComponent>(role);

        if (record == null) return new CookRead.NotReady(Texts.ReadFailed.Pick(isChinese));

        var hour = world._GameTimeManager?.GetHour() ?? 0;
        var items = world._ItemManager;
        var places = PlacesFor(config, agents, role.InstanceId, isChinese);
        var available = items?.GetAllAvailableItems(role.InstanceId);
        var stock = ReadStock(available, book, config, role.InstanceId, hour, places);
        var level = Math.Max(1, record.CookingLevel);

        Learned(book, record, level);

        var stove = ReadStove(items, available, config, agents, role.InstanceId, hour, places);

        return new CookRead.Ready(new Kitchen(book, level, _cooked, _craftUnlocked, stock, stove, _multiPotLevel));
    }

    /// <summary>
    /// The recipes the player has cooked and may cook: two game calls per recipe, so they are asked again only after a
    /// few seconds, at a new cooking level, or for another save. A recipe learned meanwhile shows a moment later.
    /// </summary>
    private void Learned(Cookbook book, CookingRecordComponent record, int level)
    {
        var key = (record.Pointer, level);
        var now = Environment.TickCount64;

        if (key == _learnedKey && now < _learnedUntil) return;

        var cooked = new HashSet<int>();
        var craftUnlocked = new HashSet<int>();

        foreach (var id in book.Recipes.Keys)
        {
            if (record.IsRecipeUnlocked(id)) cooked.Add(id);
            if (record.IsCraftRecipeUnlocked(id)) craftUnlocked.Add(id);
        }

        if (!cooked.SetEquals(_cooked)) _cooked = cooked;
        if (!craftUnlocked.SetEquals(_craftUnlocked)) _craftUnlocked = craftUnlocked;

        _learnedKey = key;
        _learnedUntil = now + KeepMilliseconds;
    }

    /// <summary>Where food is kept, by name, kept for a few seconds: each storage is otherwise looked up again every read.</summary>
    private Places PlacesFor(ConfigManager config, AgentManager agents, long roleId, bool isChinese)
    {
        var key = (config.Pointer, agents.Pointer, roleId, isChinese);
        var now = Environment.TickCount64;

        if (_places != null && key == _placesKey && now < _placesUntil) return _places;

        _places = new Places(config, agents, roleId, isChinese);
        _placesKey = key;
        _placesUntil = now + KeepMilliseconds;

        return _places;
    }

    private static (BattleLogicWorld World, ConfigManager Config, AgentManager Agents, LeadingRole Role)? LoadedGame()
    {
        var world = GameSingleton.Existing<BattleLogicWorld>();
        var config = GameSingleton.Existing<ConfigManager>();
        var agents = world?._AgentManager;
        var role = agents?.GetLeadingRole();

        if (world == null || config == null || agents == null || role == null || world._GameTimeManager?.WorldStateType != WorldStateType.PostDisaster) return null;

        return (world, config, agents, role);
    }

    /// <summary>
    /// Food the player can reach: their bag and every home storage the game counts for building costs, skipping storage
    /// the game keeps out of that pool. That pool lists what every stove's pot holds too, so pots are skipped: taking food
    /// out of a cooking pot makes the game stop that stove (Cooking_RunningState.Update, CancelCookingByIngredientLost).
    /// </summary>
    private List<Core.Stack> ReadStock(Il2CppSystem.Collections.Generic.List<ItemData>? available, Cookbook book, ConfigManager config, long roleId, int hour, Places places)
    {
        var stock = new List<Core.Stack>();

        for (var i = 0; available != null && i < available.Count; i++)
        {
            var item = available[i];

            if (item == null || item.ItemCount <= 0 || !book.Items.TryGetValue(item.ItemConfigId, out var known) || !known.CanCook) continue;
            if (item.OwnerId != roleId && places.IsSetApart(item.OwnerId)) continue;

            stock.Add(StackOf(item, config, hour, places.Name(item.OwnerId)));
        }

        return stock;
    }

    /// <summary>The open cooking panel, or null. The pot is the panel's workbench; the panel's bag is where a swap puts spoiled food.</summary>
    private Stove? ReadStove(ItemManager? items, Il2CppSystem.Collections.Generic.List<ItemData>? available, ConfigManager config, AgentManager agents, long roleId, int hour, Places places)
    {
        var ui = Ui();
        var screen = CookingType;

        if (ui == null || items == null || !ui.IsUIActive(screen)) return null;

        var state = ui.GetStateByUI(screen)?.TryCast<State_Web_Cooking>();
        var cook = state == null ? null : CookRow(config, state.CookFurnitureId);
        var workbenchId = state?.WorkbenchOwnerId?.Value ?? 0;

        if (state == null || cook == null || workbenchId == 0) return null;

        var pot = new List<Core.Stack>();
        var held = items.GetItemDataList(workbenchId);

        for (var i = 0; held != null && i < held.Count; i++)
        {
            if (held[i] is { ItemCount: > 0 } item) pot.Add(StackOf(item, config, hour, places.Pot, isInPot: true));
        }

        return new Stove(
            state.CurrentFurnitureId,
            workbenchId,
            state.BagOwnerId?.Value ?? 0,
            state.MaxFoodCount?.Value ?? cook.MaxFoodCount,
            state.HotPotMode?.Value == true,
            state.IsCooking?.Value == true,
            cook.QualityBonus,
            AllowedOn(config, state.CookFurnitureId, cook),
            pot,
            ForecastsOf(state.PredictionListJson?.Value),
            places.Name(state.CurrentFurnitureId),
            cook.SpeedRate,
            cook.HotPotCookTime,
            ReadFuel(available, config, agents, cook, state.CurrentFurnitureId, roleId, hour, places),
            StackingUnits.IsPresent);
    }

    /// <summary>The recipes a kind of stove takes, null for all: table data, read once per kind.</summary>
    private HashSet<int>? AllowedOn(ConfigManager config, int cookId, Config_FurnitureCook cook)
    {
        UseTables(config);

        if (_allowed.TryGetValue(cookId, out var known)) return known;

        HashSet<int>? allowed = null;

        if (cook.AllowedRecipes is { Count: > 0 } list)
        {
            allowed = new HashSet<int>();

            for (var i = 0; i < list.Count; i++) allowed.Add(list[i]);
        }

        return _allowed[cookId] = allowed;
    }

    /// <summary>The panel's forecast only changes with the pot, so the same text is not parsed again.</summary>
    private IReadOnlyList<Forecast> ForecastsOf(string? json)
    {
        if (json == _forecastJson) return _forecasts;

        _forecasts = Forecasts.Parse(json);
        _forecastJson = json;

        return _forecasts;
    }

    /// <summary>
    /// A stove that burns fuel (CookType 1): its slots, the kinds it burns (what its slots hold now, else what it was
    /// built with), and the player's burnable stacks of those kinds. Null for a cooker with no fire.
    /// </summary>
    private Fuel? ReadFuel(Il2CppSystem.Collections.Generic.List<ItemData>? available, ConfigManager config, AgentManager agents, Config_FurnitureCook cook, long furnitureId, long roleId, int hour, Places places)
    {
        var furniture = FurnitureOf(agents, furnitureId);
        var cooking = furniture == null ? null : AgentTools.GetAgentComponent<CookingComponent>(furniture);

        if (cook.CookType != BurnsFuelType || cooking == null) return null;

        var slots = cooking.GetFuelSlots();
        var held = new HashSet<int>();

        for (var i = 0; slots != null && i < slots.Count; i++)
        {
            if (slots[i] is { Count: > 0 } slot) held.Add(slot.ConfigId);
        }

        if (held.Count == 0 && cook.InitialFuel is { } initial)
        {
            for (var i = 0; i < initial.Count; i++) held.Add(initial[i]);
        }

        var preferred = held;
        var maxSlots = cooking.MaxFuelSlots > 0 ? cooking.MaxFuelSlots : cook.FuelSlotCount;

        return new Fuel(maxSlots, slots?.Count ?? 0, preferred, ReadFuelStock(available, config, preferred, roleId, hour, places));
    }

    /// <summary>The player's stacks of the given fuel kinds, from the same bag and storages food is read from.</summary>
    private List<Core.Stack> ReadFuelStock(Il2CppSystem.Collections.Generic.List<ItemData>? available, ConfigManager config, HashSet<int> kinds, long roleId, int hour, Places places)
    {
        var stock = new List<Core.Stack>();

        for (var i = 0; available != null && kinds.Count > 0 && i < available.Count; i++)
        {
            var item = available[i];

            if (item == null || item.ItemCount <= 0 || !kinds.Contains(item.ItemConfigId)) continue;
            if (item.OwnerId != roleId && places.IsSetApart(item.OwnerId)) continue;
            if (!RowOf(config, item.ItemConfigId).Burnable) continue;

            stock.Add(StackOf(item, config, hour, places.Name(item.OwnerId)) with { Count = item.ItemCount, IsByUse = false });
        }

        return stock;
    }

    /// <summary>
    /// Storage the game keeps out of its cost pool; the neighbor's basket, which it does not: what the player staged
    /// there is going to her, so it is neither food to cook with nor a place for dishes; and every stove, whose pot food
    /// belongs to the dish it is cooking or about to cook.
    /// </summary>
    private static bool IsSetApart(long ownerId)
    {
        if (ItemManager.IsExcludedFromCostPool(ownerId)) return true;

        var furniture = GameSingleton.Existing<BattleLogicWorld>()?._AgentManager?.GetAgent(ownerId)?.TryCast<Furniture>();

        return IsNeighborBasket(furniture) || IsStove(furniture);
    }

    private static bool IsStove(Furniture? furniture) => furniture != null && AgentTools.GetAgentComponent<CookingComponent>(furniture) != null;

    /// <summary>The basket's config id is 0 when there is none.</summary>
    private static bool IsNeighborBasket(Furniture? furniture)
    {
        var basketId = NeighborRules.GetBasketFurnitureId();

        return furniture != null && basketId > 0 && furniture.AgentConfigId == basketId;
    }

    /// <summary>By the agent table, as storage names are found, rather than walking every furniture in the world.</summary>
    private static Furniture? FurnitureOf(AgentManager? agents, long furnitureId) => agents?.GetAgent(furnitureId)?.TryCast<Furniture>();

    /// <summary>How much fuel a stove holds, so an added unit can be seen: slots, and units across them.</summary>
    private static (int Slots, int Units) FuelHeld(CookingComponent cooking)
    {
        var slots = cooking.GetFuelSlots();
        var units = 0;

        for (var i = 0; slots != null && i < slots.Count; i++) units += slots[i]?.Count ?? 0;

        return (slots?.Count ?? 0, units);
    }

    /// <summary>
    /// One stack as the cooking code sees it. Spoiled is the test Furniture.RollCookingQuality uses: a shelf life, and
    /// its end hour reached. In the pot an item used a little at a time counts as one, as recipe matching
    /// (Reducer_Web_Cooking.PreMatchRecipes) counts each pot item once. Outside it, an item the game splits a use off
    /// for cooking (ItemManager.WouldSplitForCooking) counts its uses, since each use can go in as a pot item of its own.
    /// </summary>
    private Core.Stack StackOf(ItemData item, ConfigManager config, int hour, string place, bool isInPot = false)
    {
        var row = RowOf(config, item.ItemConfigId);
        var hasLife = row.Life > 0;
        var hoursLeft = hasLife ? item.StartTime + item.TimeLeft - hour : (double?)null;
        var isByUse = !isInPot && row.IsUsedUp && ItemManager.WouldSplitForCooking(item);
        var count = isByUse ? item.UseTimes : row.IsUsedUp ? 1 : item.ItemCount;

        return new Core.Stack(item.InstanceId, item.ItemConfigId, count, item.OwnerId, place, hoursLeft, hasLife && hoursLeft <= 0, isByUse);
    }

    /// <summary>What a read needs of an item's table row, looked up once per item and kept until the tables change.</summary>
    private ItemRow RowOf(ConfigManager config, int itemId)
    {
        UseTables(config);

        if (_itemRows.TryGetValue(itemId, out var known)) return known;

        var row = ItemConfig(config, itemId);

        return _itemRows[itemId] = row == null ? default : new ItemRow(row.Life, row.StackLimit <= 1, row.Burnable);
    }

    /// <summary>
    /// Takes the config's tables once per config. A config the game replaced drops every row read from the old one; the
    /// old config is held until then, so its address cannot come back as a new one's.
    /// </summary>
    private void UseTables(ConfigManager config)
    {
        var isSameConfig = _tablesConfig != null && _tablesConfig.Pointer == config.Pointer;

        if (isSameConfig && _itemTable != null && _cookTable != null && _recipeTable != null) return;

        if (!isSameConfig)
        {
            _itemRows.Clear();
            _allowed.Clear();
        }

        // A table the game has not loaded yet is asked for again at the next lookup.
        _tablesConfig = config;
        _itemTable = config._Config_Item_Dict;
        _cookTable = config._Config_FurnitureCook_Dict;
        _recipeTable = config._Config_CookingRecipe_Dict;
    }

    /// <summary>A row of the item table, or null. The game's own Get_Config_Item logs an error for every missing row.</summary>
    private Config_Item? ItemConfig(ConfigManager config, int itemId)
    {
        UseTables(config);

        return _itemTable != null && _itemTable.TryGetValue(itemId, out var row) ? row : null;
    }

    private Config_FurnitureCook? CookRow(ConfigManager config, int cookId)
    {
        UseTables(config);

        return _cookTable != null && _cookTable.TryGetValue(cookId, out var row) ? row : null;
    }

    private Config_CookingRecipe? RecipeRow(ConfigManager config, int recipeId)
    {
        UseTables(config);

        return _recipeTable != null && _recipeTable.TryGetValue(recipeId, out var row) ? row : null;
    }

    /// <summary>A missing row reads as no shelf life, counted by the stack, and not fuel, as the null checks before it did.</summary>
    private readonly record struct ItemRow(int Life, bool IsUsedUp, bool Burnable);

    /// <summary>What an owner holds, so a move can be seen: stacks, units, and uses of items used a little at a time.</summary>
    private static (int Stacks, int Units, int Uses) Holding(ItemManager items, long ownerId)
    {
        var held = items.GetItemDataList(ownerId);
        var (stacks, units, uses) = (0, 0, 0);

        for (var i = 0; held != null && i < held.Count; i++)
        {
            if (held[i] == null) continue;

            stacks++;
            units += held[i].ItemCount;
            uses += held[i].UseTimes;
        }

        return (stacks, units, uses);
    }

    /// <summary>
    /// The cookbook for this game and language, or null while it is still being read (or its read failed and waits to
    /// be tried again). The tables only change with the language, so it is read once per game and language, a slice
    /// each frame by <see cref="AdvanceCatalog"/>: read whole, it was a hitch of over a hundred milliseconds.
    /// </summary>
    private Cookbook? Catalog(ConfigManager config, bool isChinese)
    {
        var key = (config.Pointer, isChinese);

        if (_catalog != null && _catalogKey == key) return _catalog;
        if (_catalogJob != null && _catalogJobKey == key) return null;
        if (_hasCatalogFailed && _catalogJobKey == key && Environment.TickCount64 < _catalogRetryAt) return null;

        StartCatalog(config, key);

        return null;
    }

    private void StartCatalog(ConfigManager config, (IntPtr Config, bool IsChinese) key)
    {
        _catalogJobKey = key;
        _hasCatalogFailed = false;
        UseTables(config);

        var recipeTable = _recipeTable;
        var itemTable = _itemTable;

        _catalogJob = new SlicedJob(CookbookBuild.Steps(
            AreIconsIn,
            () => KeysOf(recipeTable),
            id => recipeTable != null && recipeTable.TryGetValue(id, out var row) && row != null ? RecipeOf(config, row) : null,
            () => KeysOf(itemTable),
            (id, outputs) => itemTable != null && itemTable.TryGetValue(id, out var row) && row != null ? ItemOf(config, row, outputs) : null,
            () => ReadTypes(config),
            () => ReadRules(config),
            book =>
            {
                var multiPot = config.TryGet_Config_GlobalSetting("CookingMultiPot_UnlockLevel");

                _catalog = book;
                _catalogKey = key;
                _catalogConfig = config;
                _multiPotLevel = multiPot == null ? Kitchen.ShippedMultiPotLevel : (int)multiPot.Params2;
                _log($"read {book.Items.Count} foods and dishes, {book.Recipes.Count} recipes, {book.Types.Count} food types");
            }));
    }

    /// <summary>Starts reading the cookbook while the cooking screen is closed, so it is ready by the time the screen opens.</summary>
    public void WarmCatalog()
    {
        try
        {
            if (LoadedGame() is { } game) Catalog(game.Config, GameLanguage.IsChinese());
        }
        catch (Exception ex)
        {
            ReportOnce("cookbook warm-up", ex);
        }
    }

    /// <summary>One slice of the cookbook read, if one is under way. Free when none is: called every frame.</summary>
    public void AdvanceCatalog()
    {
        if (_catalogJob == null) return;

        try
        {
            _sliceEndsAt = System.Diagnostics.Stopwatch.GetTimestamp() + SliceTicks;

            if (_catalogJob.Advance(_isSliceSpent)) _catalogJob = null;
        }
        catch (Exception ex)
        {
            ReportOnce("cookbook read", ex);
            _catalogJob = null;
            _hasCatalogFailed = true;
            _catalogRetryAt = Environment.TickCount64 + CatalogRetryMilliseconds;
        }
    }

    private static IReadOnlyList<int> KeysOf<T>(Il2CppSystem.Collections.Generic.Dictionary<int, T>? table)
    {
        var ids = new List<int>();

        if (table == null) return ids;

        foreach (var id in table.Keys) ids.Add(id);

        return ids;
    }

    /// <summary>
    /// A food the cooking panel admits (Reducer_Web_Cooking.CheckCookingAdmission: a food that can be cooked and is not
    /// cut first), or a dish a recipe makes; null for any other row. An item's type is its SubCategory, the key recipe
    /// tags match on.
    /// </summary>
    private Item? ItemOf(ConfigManager config, Config_Item row, IReadOnlySet<int> outputs)
    {
        var canCook = row.Category == FoodCategory && row.CanCook && row.CutProductId <= 0;

        return canCook || outputs.Contains(row.ID) ? new Item(row.ID, ItemName(config, row), ItemIcon(row), row.SubCategory, row.Life, canCook, Gives(row)) : null;
    }

    private static Recipe RecipeOf(ConfigManager config, Config_CookingRecipe row)
    {
        var name = Translate(config, row.RecipeName) ?? NonEmpty(row.RecipeName_Local) ?? $"Recipe {row.ID}";
        var outputs = new[] { row.FailItemID, row.NormalItemID, row.GoodItemID, row.PerfectItemID };

        return new Recipe(row.ID, name, Ints(row.SpecificItems), Ints(row.TagCombo), row.Tier, Ints(row.QualityMap), row.craft_level, row.show_level, row.CookTime, outputs, GroupOf(row.ID));
    }


    /// <summary>
    /// The recipe the game's book lists this one under (DishCodexGrouping.Representative), or 0 for its own row. A failed
    /// read gives every recipe its own row, so a grouped recipe lists only once learned itself.
    /// </summary>
    private static int GroupOf(int recipeId)
    {
        try
        {
            var group = GameCore.HotUpdate.Codex.DishCodexGrouping.Representative(recipeId);

            return group == recipeId ? 0 : group;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private List<FoodType> ReadTypes(ConfigManager config)
    {
        var types = new List<FoodType>();
        var rows = config._Config_FoodType_Dict;

        if (rows == null) return types;

        foreach (var row in rows.Values)
        {
            if (row != null) types.Add(new FoodType(row.ID, Translate(config, row.Name) ?? NonEmpty(row.Name_Local) ?? $"Type {row.ID}", Icons().Resolve(NonEmpty(row.IT_Icon))));
        }

        return types;
    }

    /// <summary>The settings Furniture.RollCookingQuality reads, falling back to the shipped values for a missing row.</summary>
    private static QualityRules ReadRules(ConfigManager config)
    {
        var shipped = QualityRules.Shipped;
        var fresh = config.TryGet_Config_GlobalSetting("CookingQuality_FreshBonus");
        var rotten = config.TryGet_Config_GlobalSetting("CookingQuality_RottenPenalty");
        var seasoning = config.TryGet_Config_GlobalSetting("CookingQuality_SeasoningBonus");
        var exact = config.TryGet_Config_GlobalSetting("CookingQuality_ExactMatchBonus");
        var floors = new Dictionary<int, int>();
        var levels = config._Config_CookingLv_Dict;

        if (levels != null)
        {
            foreach (var row in levels.Values)
            {
                if (row != null) floors[row.Lv] = row.QualityFloor;
            }
        }

        return new QualityRules(
            fresh?.Params1 ?? shipped.Fresh,
            rotten?.Params1 ?? shipped.Rotten,
            seasoning?.Params1 ?? shipped.SeasoningFirst,
            seasoning?.Params2 ?? shipped.SeasoningRatio,
            exact?.Params1 ?? shipped.Exact,
            floors.Count > 0 ? floors : shipped.Floors);
    }

    private static List<int> Ints(Il2CppSystem.Collections.Generic.List<int>? list)
    {
        var ints = new List<int>();

        for (var i = 0; list != null && i < list.Count; i++) ints.Add(list[i]);

        return ints;
    }

    private bool IsHome(AgentManager agents)
    {
        try
        {
            return agents.IsHomeMap(ExploreSearchStateManager.GetCurrentMapId());
        }
        catch (Exception ex)
        {
            ReportOnce("home check", ex);

            return false;
        }
    }

    private int PressCount(string operation, Func<int> press)
    {
        try
        {
            return press();
        }
        catch (Exception ex)
        {
            ReportOnce(operation, ex);

            return 0;
        }
    }

    private bool Press(string operation, Func<bool> press)
    {
        try
        {
            return press();
        }
        catch (Exception ex)
        {
            ReportOnce(operation, ex);

            return false;
        }
    }

    private static string ItemName(ConfigManager config, Config_Item item) =>
        Translate(config, item.ItemName) ?? NonEmpty(item.ItemName_Local) ?? NonEmpty(item.ItemName) ?? $"Item {item.ID}";

    private string? ItemIcon(Config_Item item) => Icons().Resolve(NonEmpty(item.WebIcon) ?? NonEmpty(item.Icon));

    /// <summary>What eating the item restores, in the game's item tip order: satiety, morale, stamina, health, vitality.</summary>
    private static int[] Gives(Config_Item item) =>
        System.Array.ConvertAll(new[] { item.ValueDisplay1, item.ValueDisplay2, item.ValueDisplay3, item.ValueDisplay4, item.ValueDisplay5 }, value => (int)System.Math.Round(value));

    /// <summary>
    /// True while the game's cooking screen is up. Cheap enough for every frame, so the window's state is read the
    /// moment the screen opens rather than at the next slow refresh.
    /// </summary>
    public bool IsCookingScreenOpen()
    {
        try
        {
            return Ui() is { } ui && ui.IsUIActive(CookingType);
        }
        catch (Exception ex)
        {
            ReportOnce("cooking screen check", ex);

            return false;
        }
    }

    /// <summary>
    /// A cheap print of what the open cooking screen shows: which stove, its pot and bag, its size, hot pot or not,
    /// cooking or not, and the hour, which moves spoilage. A new print means the kitchen is worth reading again. Zero
    /// with the screen closed or on a failed read.
    /// </summary>
    public ulong ScreenStamp()
    {
        try
        {
            if (Ui() is not { } ui || !ui.IsUIActive(CookingType)) return 0UL;
            if (ui.GetStateByUI(CookingType)?.TryCast<State_Web_Cooking>() is not { } state) return 0UL;

            var hour = GameSingleton.Existing<BattleLogicWorld>()?._GameTimeManager?.GetHour() ?? -1;
            var stamp = 14695981039346656037UL;

            stamp = Mix(stamp, state.Pointer.ToInt64());
            stamp = Mix(stamp, state.CurrentFurnitureId);
            stamp = Mix(stamp, state.CookFurnitureId);
            stamp = Mix(stamp, state.WorkbenchOwnerId?.Value ?? 0);
            stamp = Mix(stamp, state.BagOwnerId?.Value ?? 0);
            stamp = Mix(stamp, state.MaxFoodCount?.Value ?? -1);
            stamp = Mix(stamp, state.HotPotMode?.Value == true ? 1 : 0);
            stamp = Mix(stamp, state.IsCooking?.Value == true ? 1 : 0);
            stamp = Mix(stamp, hour);

            return stamp == 0UL ? 1UL : stamp;
        }
        catch (Exception ex)
        {
            ReportOnce("cooking screen print", ex);

            return 0UL;
        }
    }

    private static ulong Mix(ulong stamp, long value) => (stamp ^ (ulong)value) * 1099511628211UL;

    private static Il2CppSystem.Type CookingType => _cookingType ??= Il2CppType.Of<WebUI_Cooking>();

    /// <summary>The UI system, a new wrapper only when the game made a new one.</summary>
    private ReduxUISystem? Ui()
    {
        var pointer = GameSingleton.Pointer<ReduxUISystem>();

        if (pointer == IntPtr.Zero)
        {
            _ui = null;
            _uiPointer = IntPtr.Zero;

            return null;
        }

        if (pointer != _uiPointer || _ui == null)
        {
            _ui = GameSingleton.Existing<ReduxUISystem>();
            _uiPointer = pointer;
        }

        return _ui;
    }

    /// <summary>Only what the icon folder read found; empty until it is in, and nothing asks before then.</summary>
    private IconIndex Icons() => _icons ?? IconIndex.Empty;

    /// <summary>
    /// Whether the icon folder has been read, starting the read (on a worker thread) the first time it is asked. The
    /// result is taken in here, on the main thread, so the cookbook never sees a half-built index.
    /// </summary>
    private bool AreIconsIn()
    {
        if (_icons != null) return true;

        _iconScan ??= StartIconScan();

        if (!_iconScan.IsCompleted) return false;

        var (icons, note) = _iconScan.Status == System.Threading.Tasks.TaskStatus.RanToCompletion
            ? _iconScan.Result
            : (IconIndex.Empty, "no web icons, foods and dishes will show letters instead: " + (_iconScan.Exception?.GetBaseException().Message ?? "the read was cancelled"));

        _icons = icons;
        _iconScan = null;
        _log(note);

        return true;
    }

    private static System.Threading.Tasks.Task<(IconIndex Icons, string Note)> StartIconScan()
    {
        try
        {
            return WebIcons.StartScan();
        }
        catch (Exception ex)
        {
            return System.Threading.Tasks.Task.FromResult((IconIndex.Empty, "no web icons, foods and dishes will show letters instead: " + ex.Message));
        }
    }

    private static string? Translate(ConfigManager config, string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        var text = config.GetLocalTxt(key);

        return string.IsNullOrEmpty(text) || text == key ? null : text;
    }

    private static string? NonEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private void ReportOnce(string operation, Exception ex)
    {
        if (_reportedFailures.Add(operation)) _log($"{operation} failed (each kind reported once): {ex}");
    }

    /// <summary>Where a stack is kept, by the name the game gives that storage. Looked up once per owner per read.</summary>
    private sealed class Places
    {
        private readonly ConfigManager _config;
        private readonly AgentManager _agents;
        private readonly long _roleId;
        private readonly bool _isChinese;
        private readonly Dictionary<long, string> _names = new();
        private readonly Dictionary<long, bool> _setApart = new();

        public Places(ConfigManager config, AgentManager agents, long roleId, bool isChinese)
        {
            _config = config;
            _agents = agents;
            _roleId = roleId;
            _isChinese = isChinese;
        }

        public string Pot => Texts.Pot.Pick(_isChinese);

        public string Name(long ownerId)
        {
            if (ownerId == _roleId) return Texts.Bag.Pick(_isChinese);
            if (_names.TryGetValue(ownerId, out var known)) return known;

            var name = FurnitureName(ownerId) ?? Texts.Storage.Pick(_isChinese);

            _names[ownerId] = name;

            return name;
        }

        /// <summary>Asked for every stack the player can reach, and a few storages hold most of them, so each owner is looked up once.</summary>
        public bool IsSetApart(long ownerId)
        {
            if (_setApart.TryGetValue(ownerId, out var known)) return known;

            return _setApart[ownerId] = GameCooking.IsSetApart(ownerId);
        }

        private string? FurnitureName(long ownerId)
        {
            var furniture = _agents.GetAgent(ownerId)?.TryCast<Furniture>();
            var rows = _config._Config_Furniture_Dict;

            if (furniture == null || rows == null || !rows.ContainsKey(furniture.AgentConfigId)) return null;

            return Translate(_config, rows[furniture.AgentConfigId]?.Name);
        }
    }
}
