using CookingHub.Core;

namespace CookingHub.Tests;

/// <summary>
/// A small kitchen with the game's quality rules: grain and meat to cook with, two seasonings, and recipes of every kind
/// the finder tells apart. Moving a stack really moves it, so a press can be judged by what ends up in the pot.
/// </summary>
internal sealed class FakeGame : ICookGame
{
    public const int Grain = 1;
    public const int Meat = 2;
    public const int Seasoning = QualityRules.SeasoningType;

    public const int Rice = 10;
    public const int Wheat = 11;
    public const int Pork = 20;
    public const int Salt = 30;
    public const int Pepper = 31;
    public const int Bowl = 900;
    public const int FancyBowl = 901;

    /// <summary>Exact: Rice and Pork. Learned from a book, not by level.</summary>
    public const int PorkRice = 1;

    /// <summary>Tag: any grain and any meat. Its tier 3 sibling wants the same types, so the dish varies.</summary>
    public const int StirFry = 2;

    public const int FancyStirFry = 3;

    /// <summary>Exact: three Pork, unlocked at cooking level 5.</summary>
    public const int Feast = 4;

    /// <summary>Hidden until cooking level 9.</summary>
    public const int Secret = 5;

    public const long Bag = 500;
    public const long Chest = 501;
    public const long Workbench = 600;
    public const long StoveId = 700;
    public const int Firewood = 40;

    public static readonly IReadOnlyList<int> Map = new[] { 0, 30, 60, 90 };

    public static readonly Cookbook Book = new(
        new Dictionary<int, Item>
        {
            [Rice] = new(Rice, "Rice", "Res/Item/rice.png", Grain, 5, true),
            [Wheat] = new(Wheat, "Wheat", null, Grain, 5, true),
            [Pork] = new(Pork, "Pork", null, Meat, 2, true),
            [Salt] = new(Salt, "Salt", null, Seasoning, 0, true),
            [Pepper] = new(Pepper, "Pepper", null, Seasoning, 0, true),
            [Bowl] = new(Bowl, "Rice Bowl", "Res/Item/bowl.png", 0, 1, false),
            [FancyBowl] = new(FancyBowl, "Fancy Bowl", "Res/Item/fancy.png", 0, 1, false),
            [Firewood] = new(Firewood, "Firewood", null, 0, 0, false),
        },
        new Dictionary<int, Recipe>
        {
            [PorkRice] = R(PorkRice, "Pork Rice", specific: new[] { Rice, Pork }, outputs: new[] { Bowl, Bowl, Bowl, FancyBowl }),
            [StirFry] = R(StirFry, "Stir Fry", tags: new[] { Grain, Meat }, tier: 2, craftLevel: 1),
            [FancyStirFry] = R(FancyStirFry, "Fancy Stir Fry", tags: new[] { Meat, Grain }, tier: 3, craftLevel: 1),
            [Feast] = R(Feast, "Feast", specific: new[] { Pork, Pork, Pork }, craftLevel: 5),
            [Secret] = R(Secret, "Secret", specific: new[] { Rice }, craftLevel: 1, showLevel: 9),
            [Recipe.DarkFood] = R(Recipe.DarkFood, "Dark Food", specific: new[] { Rice }, craftLevel: 1),
        },
        new[] { new FoodType(Grain, "Grain", null), new FoodType(Meat, "Meat", null), new FoodType(Seasoning, "Seasoning", null) },
        QualityRules.Shipped);

    public int Level { get; set; } = 2;

    public List<int> Cooked { get; } = new();

    public List<int> CraftUnlocked { get; } = new() { PorkRice };

    /// <summary>The player's food outside the pot, in the bag and the chest.</summary>
    public List<Stack> Stock { get; } = new();

    public List<Stack> Pot { get; } = new();

    /// <summary>The dishes the open cooking screen forecasts for the pot, main dish first.</summary>
    public List<Forecast> Forecasts { get; } = new();

    public bool IsStoveOpen { get; set; } = true;

    public bool IsCooking { get; set; }

    public int MaxFood { get; set; } = 6;

    public IReadOnlyCollection<int>? Allowed { get; set; }

    /// <summary>False for a cooker with no fire, like a juicer.</summary>
    public bool BurnsFuel { get; set; } = true;

    public int FuelSlots { get; set; } = 4;

    public int FuelUsed { get; set; } = 1;

    /// <summary>The fuel kinds the stove burns now; empty means none was ever put in.</summary>
    public List<int> FuelPreferred { get; } = new() { Firewood };

    /// <summary>The player's fuel of the preferred kinds, in the bag and the chest.</summary>
    public List<Stack> FuelStock { get; } = new();

    /// <summary>Fuel stacks the game refuses to burn.</summary>
    public HashSet<long> FuelStuck { get; } = new();

    public CookRead? Override { get; set; }

    /// <summary>Stacks the game refuses to move.</summary>
    public HashSet<long> Stuck { get; } = new();

    public static Recipe R(int id, string name, int[]? specific = null, int[]? tags = null, int tier = 2, int craftLevel = 0, int showLevel = 0, int[]? outputs = null) =>
        new(id, name, specific ?? Array.Empty<int>(), tags ?? Array.Empty<int>(), tier, Map, craftLevel, showLevel, 60, outputs ?? new[] { Bowl, Bowl, Bowl, Bowl });

    public static Stack S(long instanceId, int itemId, int count = 1, double? hours = 48, bool isSpoiled = false, long owner = Bag) =>
        new(instanceId, itemId, count, owner, owner == Bag ? "Bag" : "Chest", hours, isSpoiled);

    public Kitchen Kitchen() =>
        new(Book, Level, Cooked, CraftUnlocked, Stock.ToList(), IsStoveOpen ? OpenStove() : null);

    private Stove OpenStove() =>
        new(StoveId, Workbench, Bag, MaxFood, false, IsCooking, 0, Allowed, Pot.ToList(), Forecasts.ToList(), "Stove", 1f, 0, BurnsFuel ? new Fuel(FuelSlots, FuelUsed, FuelPreferred.ToList(), FuelStock.ToList()) : null);

    public CookRead Read() => Override ?? new CookRead.Ready(Kitchen());

    public bool MoveStack(long instanceId, long fromOwnerId, long toOwnerId)
    {
        if (Stuck.Contains(instanceId)) return false;

        var from = fromOwnerId == Workbench ? Pot : Stock;
        var stack = from.FirstOrDefault(s => s.InstanceId == instanceId && s.OwnerId == fromOwnerId);

        if (stack == null) return false;

        from.Remove(stack);

        if (toOwnerId == Workbench) Pot.Add(stack with { OwnerId = Workbench, Place = "Pot" });
        else Stock.Add(stack with { OwnerId = toOwnerId, Place = "Bag" });

        return true;
    }

    /// <summary>Burns one unit of the stack into a new fuel slot, the way Furniture.OnCookingFuelAdd does.</summary>
    public bool AddFuel(long furnitureId, long instanceId)
    {
        var stack = FuelStock.FirstOrDefault(s => s.InstanceId == instanceId);

        if (furnitureId != StoveId || stack == null || FuelStuck.Contains(instanceId) || FuelUsed >= FuelSlots) return false;

        FuelStock.Remove(stack);

        if (stack.Count > 1) FuelStock.Add(stack with { Count = stack.Count - 1 });

        FuelUsed++;

        return true;
    }
}
