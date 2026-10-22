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
internal sealed class GameCooking : ICookGame
{
    private const int FoodCategory = 1;

    private readonly Action<string> _log;
    private readonly HashSet<string> _reportedFailures = new();

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

    /// <summary>
    /// ItemManager.OnCookingQuickMove, what the panel's quick move and recipe click both end in. It returns nothing, so
    /// the move counts when what the pot side holds changed.
    /// </summary>
    public bool MoveStack(long instanceId, long fromOwnerId, long toOwnerId) => Press("move", () =>
    {
        var items = LoadedGame()?.World._ItemManager;

        if (items == null) return false;

        var before = Holding(items, toOwnerId);

        items.OnCookingQuickMove(instanceId, fromOwnerId, toOwnerId);

        var isMoved = Holding(items, toOwnerId) != before;

        if (isMoved) Ac_Cooking_RefreshBag.SendAction();
        else _log($"the game did not move stack {instanceId} from {fromOwnerId} to {toOwnerId}");

        return isMoved;
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
        var stove = ReadStove(items, config, hour, places);

        return new CookRead.Ready(new Kitchen(book, Math.Max(1, record.CookingLevel), cooked, craftUnlocked, stock, stove));
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
            if (item.OwnerId != roleId && ItemManager.IsExcludedFromCostPool(item.OwnerId)) continue;

            stock.Add(StackOf(item, config, hour, places.Name(item.OwnerId)));
        }

        return stock;
    }

    /// <summary>The open cooking panel, or null. The pot is the panel's workbench; the panel's bag is where a swap puts spoiled food.</summary>
    private static Stove? ReadStove(ItemManager? items, ConfigManager config, int hour, Places places)
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
            Forecasts.Parse(state.PredictionListJson?.Value));
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

            if (canCook || outputs.Contains(row.ID)) items[row.ID] = new Item(row.ID, ItemName(config, row), ItemIcon(row), row.SubCategory, row.Life, canCook);
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
