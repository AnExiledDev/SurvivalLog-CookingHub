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
internal sealed partial class GameCooking : ICookGame
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

    private IconIndex? _icons;
    private (IntPtr Config, bool IsChinese) _catalogKey;
    private Cookbook? _catalog;
    private int _multiPotLevel = Kitchen.ShippedMultiPotLevel;

    /// <summary>The item table rows a read looks at for every stack, by item id; dropped with the tables.</summary>
    private readonly Dictionary<int, ItemRow> _itemRows = new();
    private IntPtr _itemRowsConfig;

    /// <summary>Each stove kind's allowed recipes (null for all), by its cook row id; dropped with the tables.</summary>
    private readonly Dictionary<int, HashSet<int>?> _allowed = new();
    private IntPtr _allowedConfig;

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

        var book = Catalog(config, isChinese);
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
        var ui = GameSingleton.Existing<ReduxUISystem>();
        var screen = Il2CppType.Of<WebUI_Cooking>();

        if (ui == null || items == null || !ui.IsUIActive(screen)) return null;

        var state = ui.GetStateByUI(screen)?.TryCast<State_Web_Cooking>();
        var cook = state == null ? null : config.Get_Config_FurnitureCook(state.CookFurnitureId);
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
        if (_allowedConfig != config.Pointer)
        {
            _allowed.Clear();
            _allowedConfig = config.Pointer;
        }

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

        var preferred = held.Count > 0 ? held : Ints(cook.InitialFuel).ToHashSet();
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
        if (_itemRowsConfig != config.Pointer)
        {
            _itemRows.Clear();
            _itemRowsConfig = config.Pointer;
        }

        if (_itemRows.TryGetValue(itemId, out var known)) return known;

        var row = config.Get_Config_Item(itemId);

        return _itemRows[itemId] = row == null ? default : new ItemRow(row.Life, row.StackLimit <= 1, row.Burnable);
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

    /// <summary>The tables only change with the language, so the catalog is built once per game and language.</summary>
    private Cookbook Catalog(ConfigManager config, bool isChinese)
    {
        var key = (config.Pointer, isChinese);

        if (_catalog != null && _catalogKey == key) return _catalog;

        var recipes = ReadRecipes(config);
        var outputs = recipes.Values.SelectMany(recipe => recipe.Outputs).ToHashSet();

        var multiPot = config.TryGet_Config_GlobalSetting("CookingMultiPot_UnlockLevel");

        _catalog = new Cookbook(ReadItems(config, outputs), recipes, ReadTypes(config), ReadRules(config));
        _catalogKey = key;
        _multiPotLevel = multiPot == null ? Kitchen.ShippedMultiPotLevel : (int)multiPot.Params2;
        _log($"read {_catalog.Items.Count} foods and dishes, {_catalog.Recipes.Count} recipes, {_catalog.Types.Count} food types");

        return _catalog;
    }

    /// <summary>
    /// Every food the cooking panel admits (Reducer_Web_Cooking.CheckCookingAdmission: a food that can be cooked and is
    /// not cut first), and every dish a recipe makes. An item's type is its SubCategory, the key recipe tags match on.
    /// </summary>
    private Dictionary<int, Item> ReadItems(ConfigManager config, HashSet<int> outputs)
    {
        var items = new Dictionary<int, Item>();
        var rows = config._Config_Item_Dict;

        if (rows == null) return items;

        foreach (var row in rows.Values)
        {
            if (row == null) continue;

            var canCook = row.Category == FoodCategory && row.CanCook && row.CutProductId <= 0;

            if (canCook || outputs.Contains(row.ID)) items[row.ID] = new Item(row.ID, ItemName(config, row), ItemIcon(row), row.SubCategory, row.Life, canCook, Gives(row));
        }

        return items;
    }

    private static Dictionary<int, Recipe> ReadRecipes(ConfigManager config)
    {
        var recipes = new Dictionary<int, Recipe>();
        var rows = config._Config_CookingRecipe_Dict;

        if (rows == null) return recipes;

        foreach (var row in rows.Values)
        {
            if (row == null) continue;

            var name = Translate(config, row.RecipeName) ?? NonEmpty(row.RecipeName_Local) ?? $"Recipe {row.ID}";
            var outputs = new[] { row.FailItemID, row.NormalItemID, row.GoodItemID, row.PerfectItemID };

            recipes[row.ID] = new Recipe(row.ID, name, Ints(row.SpecificItems), Ints(row.TagCombo), row.Tier, Ints(row.QualityMap), row.craft_level, row.show_level, row.CookTime, outputs);
        }

        return recipes;
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
            return GameSingleton.Existing<ReduxUISystem>() is { } ui && ui.IsUIActive(Il2CppType.Of<WebUI_Cooking>());
        }
        catch (Exception ex)
        {
            ReportOnce("cooking screen check", ex);

            return false;
        }
    }

    private IconIndex Icons() => _icons ??= WebIcons.Load(_log);

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
