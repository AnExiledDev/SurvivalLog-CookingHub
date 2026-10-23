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

    private IconIndex? _icons;
    private (IntPtr Config, bool IsChinese) _catalogKey;
    private Cookbook? _catalog;

    public GameCooking(Action<string> log)
    {
        _log = log;
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

        Ac_Cooking_RefreshBag.SendAction();

        return true;
    });

    /// <summary>
    /// With Stacking, each unit goes in by its one-unit move. That move puts the unit in under the stack's own id and
    /// leaves the rest as a new stack in the same place, so the next unit comes off the stack that appeared there.
    /// Without Stacking, or for a single item, the stack goes in whole by the panel's quick move.
    /// </summary>
    public int MoveUnits(long instanceId, long fromOwnerId, long toOwnerId, int units) => PressCount("move units", () =>
    {
        var items = LoadedGame()?.World._ItemManager;
        var moved = 0;
        var current = instanceId;

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

        if (moved > 0) Ac_Cooking_RefreshBag.SendAction();

        return moved;
    });

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

        if (isAdded) Ac_Cooking_RefreshBag.SendAction();
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
        var places = new Places(config, agents, role.InstanceId, isChinese);
        var stock = ReadStock(items, book, config, role.InstanceId, hour, places);
        var cooked = book.Recipes.Keys.Where(record.IsRecipeUnlocked).ToHashSet();
        var craftUnlocked = book.Recipes.Keys.Where(record.IsCraftRecipeUnlocked).ToHashSet();
        var stove = ReadStove(items, config, agents, role.InstanceId, hour, places);

        var multiPot = config.TryGet_Config_GlobalSetting("CookingMultiPot_UnlockLevel");

        return new CookRead.Ready(new Kitchen(book, Math.Max(1, record.CookingLevel), cooked, craftUnlocked, stock, stove, multiPot == null ? Kitchen.ShippedMultiPotLevel : (int)multiPot.Params2));
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
    /// the game keeps out of that pool. Stoves and pots are not storage, so food already in a pot is not counted here.
    /// </summary>
    private static List<Core.Stack> ReadStock(ItemManager? items, Cookbook book, ConfigManager config, long roleId, int hour, Places places)
    {
        var stock = new List<Core.Stack>();
        var available = items?.GetAllAvailableItems(roleId);

        for (var i = 0; available != null && i < available.Count; i++)
        {
            var item = available[i];

            if (item == null || item.ItemCount <= 0 || !book.Items.TryGetValue(item.ItemConfigId, out var known) || !known.CanCook) continue;
            if (item.OwnerId != roleId && IsSetApart(item.OwnerId)) continue;

            stock.Add(StackOf(item, config, hour, places.Name(item.OwnerId)));
        }

        return stock;
    }

    /// <summary>The open cooking panel, or null. The pot is the panel's workbench; the panel's bag is where a swap puts spoiled food.</summary>
    private static Stove? ReadStove(ItemManager? items, ConfigManager config, AgentManager agents, long roleId, int hour, Places places)
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
            if (held[i] is { ItemCount: > 0 } item) pot.Add(StackOf(item, config, hour, places.Pot));
        }

        var allowed = cook.AllowedRecipes is { Count: > 0 } list ? Enumerable.Range(0, list.Count).Select(i => list[i]).ToHashSet() : null;

        return new Stove(
            state.CurrentFurnitureId,
            workbenchId,
            state.BagOwnerId?.Value ?? 0,
            state.MaxFoodCount?.Value ?? cook.MaxFoodCount,
            state.HotPotMode?.Value == true,
            state.IsCooking?.Value == true,
            cook.QualityBonus,
            allowed,
            pot,
            Forecasts.Parse(state.PredictionListJson?.Value),
            places.Name(state.CurrentFurnitureId),
            cook.SpeedRate,
            cook.HotPotCookTime,
            ReadFuel(items, config, agents, cook, state.CurrentFurnitureId, roleId, hour, places),
            StackingUnits.IsPresent);
    }

    /// <summary>
    /// A stove that burns fuel (CookType 1): its slots, the kinds it burns (what its slots hold now, else what it was
    /// built with), and the player's burnable stacks of those kinds. Null for a cooker with no fire.
    /// </summary>
    private static Fuel? ReadFuel(ItemManager items, ConfigManager config, AgentManager agents, Config_FurnitureCook cook, long furnitureId, long roleId, int hour, Places places)
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

        return new Fuel(maxSlots, slots?.Count ?? 0, preferred, ReadFuelStock(items, config, preferred, roleId, hour, places));
    }

    /// <summary>The player's stacks of the given fuel kinds, from the same bag and storages food is read from.</summary>
    private static List<Core.Stack> ReadFuelStock(ItemManager items, ConfigManager config, HashSet<int> kinds, long roleId, int hour, Places places)
    {
        var stock = new List<Core.Stack>();
        var available = items.GetAllAvailableItems(roleId);

        for (var i = 0; available != null && kinds.Count > 0 && i < available.Count; i++)
        {
            var item = available[i];

            if (item == null || item.ItemCount <= 0 || !kinds.Contains(item.ItemConfigId)) continue;
            if (item.OwnerId != roleId && IsSetApart(item.OwnerId)) continue;
            if (config.Get_Config_Item(item.ItemConfigId) is not { Burnable: true }) continue;

            stock.Add(StackOf(item, config, hour, places.Name(item.OwnerId)) with { Count = item.ItemCount });
        }

        return stock;
    }

    /// <summary>
    /// Storage the game keeps out of its cost pool, and the neighbor's basket, which it does not: what the player
    /// staged there is going to her, so it is neither food to cook with nor a place for dishes.
    /// </summary>
    private static bool IsSetApart(long ownerId) =>
        ItemManager.IsExcludedFromCostPool(ownerId) || IsNeighborBasket(GameSingleton.Existing<BattleLogicWorld>()?._AgentManager?.GetAgent(ownerId)?.TryCast<Furniture>());

    /// <summary>The basket's config id is 0 when there is none.</summary>
    private static bool IsNeighborBasket(Furniture? furniture)
    {
        var basketId = NeighborRules.GetBasketFurnitureId();

        return furniture != null && basketId > 0 && furniture.AgentConfigId == basketId;
    }

    private static Furniture? FurnitureOf(AgentManager? agents, long furnitureId)
    {
        var furnitures = agents?.GetAllFurnitures();

        for (var i = 0; furnitures != null && i < furnitures.Count; i++)
        {
            if (furnitures[i]?.InstanceId == furnitureId) return furnitures[i];
        }

        return null;
    }

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
    /// its end hour reached. An item used a little at a time counts as one, as recipe matching counts it.
    /// </summary>
    private static Core.Stack StackOf(ItemData item, ConfigManager config, int hour, string place)
    {
        var row = config.Get_Config_Item(item.ItemConfigId);
        var hasLife = row != null && row.Life > 0;
        var hoursLeft = hasLife ? item.StartTime + item.TimeLeft - hour : (double?)null;
        var count = row != null && row.StackLimit <= 1 ? 1 : item.ItemCount;

        return new Core.Stack(item.InstanceId, item.ItemConfigId, count, item.OwnerId, place, hoursLeft, hasLife && hoursLeft <= 0);
    }

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

        _catalog = new Cookbook(ReadItems(config, outputs), recipes, ReadTypes(config), ReadRules(config));
        _catalogKey = key;
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

        private string? FurnitureName(long ownerId)
        {
            var furnitures = _agents.GetAllFurnitures();
            var rows = _config._Config_Furniture_Dict;

            for (var i = 0; furnitures != null && rows != null && i < furnitures.Count; i++)
            {
                var furniture = furnitures[i];

                if (furniture?.InstanceId != ownerId || !rows.ContainsKey(furniture.AgentConfigId)) continue;

                return Translate(_config, rows[furniture.AgentConfigId]?.Name);
            }

            return null;
        }
    }
}
