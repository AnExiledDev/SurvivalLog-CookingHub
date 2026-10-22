using System.Collections.Generic;
using System.Linq;

namespace CookingHub.Core;

/// <summary>A food type, the game's Config_FoodType row: staple, meat, egg and dairy, fish, vegetable and so on.</summary>
public sealed record FoodType(int Id, string Name, string? Icon);

/// <summary>An item a recipe uses or makes. Type is the food type (0 for a dish or a non-food); Life is shelf life in days, 0 for none.</summary>
public sealed record Item(int Id, string Name, string? Icon, int Type, int Life, bool CanCook);

/// <summary>
/// One row of Config_CookingRecipe. An exact recipe names its items (Specific); a tag recipe names food types (Tags).
/// Outputs are the dish made at each tier, Fail to Perfect. QualityMap holds the lowest score of each tier, same order.
/// </summary>
public sealed record Recipe(
    int Id,
    string Name,
    IReadOnlyList<int> Specific,
    IReadOnlyList<int> Tags,
    int Tier,
    IReadOnlyList<int> QualityMap,
    int CraftLevel,
    int ShowLevel,
    int CookSeconds,
    IReadOnlyList<int> Outputs)
{
    public const int DarkFood = 100;

    public bool IsExact => Specific.Count > 0;

    /// <summary>Dark food, what a pot that matches nothing turns into: every tier is the same dish, so it has no odds.</summary>
    public bool HasOdds => Id != DarkFood && QualityMap.Count >= 4;

    /// <summary>The game's own unlock rule (CookingRecordComponent.IsRecipeCraftUnlocked).</summary>
    public bool IsUnlocked(int level, IReadOnlyCollection<int> craftUnlocked) => CraftLevel > 0 ? level >= CraftLevel : craftUnlocked.Contains(Id);
}

/// <summary>The flat quality bonuses from Config_GlobalSetting and the roll floor of each cooking level from Config_CookingLv.</summary>
public sealed record QualityRules(int Fresh, int Rotten, float SeasoningFirst, float SeasoningRatio, int Exact, IReadOnlyDictionary<int, int> Floors)
{
    public const int SeasoningType = 8;

    /// <summary>The values the game ships with, for tests and as the fallback when a table row is missing.</summary>
    public static readonly QualityRules Shipped = new(10, -20, 18f, 0.5f, 10, new Dictionary<int, int> { [0] = 0, [1] = 10, [2] = 25, [3] = 40, [4] = 55, [5] = 70 });

    public int Floor(int level) => Floors.TryGetValue(level, out var floor) ? floor : Floors.Where(pair => pair.Key <= level).Select(pair => pair.Value).DefaultIfEmpty(0).Max();
}

/// <summary>Everything the tables say about cooking, read once per game and language.</summary>
public sealed record Cookbook(IReadOnlyDictionary<int, Item> Items, IReadOnlyDictionary<int, Recipe> Recipes, IReadOnlyList<FoodType> Types, QualityRules Rules)
{
    public string ItemName(int id) => Items.TryGetValue(id, out var item) ? item.Name : "#" + id;

    public int TypeOf(int itemId) => Items.TryGetValue(itemId, out var item) ? item.Type : 0;

    public string TypeName(int typeId) => Types.FirstOrDefault(type => type.Id == typeId)?.Name ?? "#" + typeId;
}

/// <summary>
/// One stack of food the player can reach, in their bag or a home storage. Count is what a recipe counts it as: the
/// stack size, or 1 for an item used up a little at a time. HoursLeft is null for food that keeps; IsSpoiled is the
/// game's own test, so a spoiled stack costs the pot its fresh bonus.
/// </summary>
public sealed record Stack(long InstanceId, int ItemId, int Count, long OwnerId, string Place, double? HoursLeft, bool IsSpoiled);

/// <summary>A line of the open cooking panel's dish forecast: the recipe it names, and whether the player knows it.</summary>
public sealed record Forecast(int RecipeId, bool IsExact, bool IsKnown);

/// <summary>
/// The cooking panel while it is open: the stove, what is in its pot (the workbench), how much fits, and the dishes the
/// panel forecasts for it. Allowed is null when the stove takes every recipe.
/// </summary>
public sealed record Stove(
    long FurnitureId,
    long WorkbenchId,
    long BagId,
    int MaxFood,
    bool IsHotPot,
    bool IsCooking,
    int QualityBonus,
    IReadOnlyCollection<int>? Allowed,
    IReadOnlyList<Stack> Pot,
    IReadOnlyList<Forecast> Forecasts)
{
    public int PotCount => Pot.Sum(stack => stack.Count);

    /// <summary>Free slots. The game counts a pot by its stacks, not its units (Reducer_Web_Cooking.CheckCookingAdmission).</summary>
    public int Room => System.Math.Max(0, MaxFood - Pot.Count);

    public bool Takes(int recipeId) => Allowed == null || Allowed.Contains(recipeId);
}

/// <summary>The player's cooking: level, which recipes they have cooked, which they may cook, their food, and the open panel if any.</summary>
public sealed record Kitchen(Cookbook Book, int Level, IReadOnlyCollection<int> Cooked, IReadOnlyCollection<int> CraftUnlocked, IReadOnlyList<Stack> Stock, Stove? Stove);

/// <summary>What a read of the game found.</summary>
public abstract record CookRead
{
    private CookRead()
    {
    }

    public sealed record NoGame : CookRead;

    public sealed record NotReady(string Reason) : CookRead;

    public sealed record Ready(Kitchen Kitchen) : CookRead;
}

/// <summary>The game's side of the mod: reading the kitchen, and moving a stack of food from one owner to another.</summary>
public interface ICookGame
{
    CookRead Read();

    /// <summary>
    /// Moves a stack the way the cooking panel's quick move and recipe click do: a whole stack, or one use of an item that
    /// is used up a little at a time. False when the game refused.
    /// </summary>
    bool MoveStack(long instanceId, long fromOwnerId, long toOwnerId);
}
