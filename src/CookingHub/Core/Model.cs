using System.Collections.Generic;
using System.Linq;

namespace CookingHub.Core;

/// <summary>A food type, the game's Config_FoodType row: staple, meat, egg and dairy, fish, vegetable and so on.</summary>
public sealed record FoodType(int Id, string Name, string? Icon);

/// <summary>
/// An item a recipe uses or makes. Type is the food type (0 for a dish or a non-food); Life is shelf life in days, 0 for
/// none. Gives is what eating it restores, the item row's ValueDisplay1-5: satiety, morale, stamina, health, vitality.
/// </summary>
public sealed record Item(int Id, string Name, string? Icon, int Type, int Life, bool CanCook, IReadOnlyList<int>? Gives = null);

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
/// One stack of food the player can reach, in their bag or a home storage. Count is the stack size. For an item used up
/// a little at a time (IsByUse) Count is its uses left: the game splits one use off for each that goes in the pot, and
/// a recipe counts each pot item once, so every use is a unit of its own and takes a pot slot. In the pot such an item
/// counts as 1. HoursLeft is null for food that keeps; IsSpoiled is the game's own test, so a spoiled stack costs the
/// pot its fresh bonus.
/// </summary>
public sealed record Stack(long InstanceId, int ItemId, int Count, long OwnerId, string Place, double? HoursLeft, bool IsSpoiled, bool IsByUse = false);

/// <summary>A line of the open cooking panel's dish forecast: the recipe it names, and whether the player knows it.</summary>
public sealed record Forecast(int RecipeId, bool IsExact, bool IsKnown);

/// <summary>
/// A stove's fuel: how many slots it has and how many hold fuel, the kinds it burns (what it holds now and what it
/// came with), and the stacks of those kinds the player has in their bag and home storage.
/// </summary>
public sealed record Fuel(int Slots, int Used, IReadOnlyCollection<int> Preferred, IReadOnlyList<Stack> Stock)
{
    public int Free => System.Math.Max(0, Slots - Used);

    public int Have => Stock.Sum(stack => stack.Count);
}

/// <summary>
/// The cooking panel while it is open: the stove, what is in its pot (the workbench), how much fits, and the dishes the
/// panel forecasts for it. Allowed is null when the stove takes every recipe. Fuel is null for a stove that burns none
/// (a juicer, a microwave). HotPotSeconds is 0 when the stove's row gives no hot pot time. SlotsByUnit is true when
/// each unit of a stack takes a slot of its own in the pot (the Stacking mod's rule), so a stack goes in a unit at a time.
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
    IReadOnlyList<Forecast> Forecasts,
    string Name = "",
    float SpeedRate = 1f,
    int HotPotSeconds = 0,
    Fuel? Fuel = null,
    bool SlotsByUnit = false)
{
    private const int DefaultHotPotSeconds = 3600;

    /// <summary>
    /// How long a recipe cooks on this stove in game seconds (Furniture.StartCooking): a hot pot takes its own fixed
    /// time, anything else the recipe's time divided by the stove's speed.
    /// </summary>
    public double CookSeconds(Recipe recipe)
    {
        if (IsHotPot) return HotPotSeconds > 0 ? HotPotSeconds : DefaultHotPotSeconds;

        return recipe.CookSeconds / (SpeedRate > 0 ? SpeedRate : 1f);
    }

    public int PotCount => Pot.Sum(stack => stack.Count);

    /// <summary>Free slots. The game counts a pot by its stacks, not its units (Reducer_Web_Cooking.CheckCookingAdmission).</summary>
    public int Room => System.Math.Max(0, MaxFood - Pot.Count);

    public bool Takes(int recipeId) => Allowed == null || Allowed.Contains(recipeId);
}

/// <summary>
/// The player's cooking: level, which recipes they have cooked, which they may cook, their food, and the open panel if
/// any. MultiPotLevel is the cooking level from which one pot cooks several dishes at once (Config_GlobalSetting
/// CookingMultiPot_UnlockLevel, 3 as shipped).
/// </summary>
public sealed record Kitchen(Cookbook Book, int Level, IReadOnlyCollection<int> Cooked, IReadOnlyCollection<int> CraftUnlocked, IReadOnlyList<Stack> Stock, Stove? Stove, int MultiPotLevel = Kitchen.ShippedMultiPotLevel)
{
    public const int ShippedMultiPotLevel = 3;

    public bool IsMultiPot => Level >= MultiPotLevel;
}

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
    /// <summary>True while a save is loaded and the player is in the world after the disaster; far cheaper than Read.</summary>
    bool IsLoaded();

    CookRead Read();

    /// <summary>
    /// Moves a stack the way the cooking panel's quick move and recipe click do: a whole stack, or one use of an item that
    /// is used up a little at a time. False when the game refused.
    /// </summary>
    bool MoveStack(long instanceId, long fromOwnerId, long toOwnerId);

    /// <summary>
    /// Puts up to that many units of a stack in the pot: one at a time when each unit takes a slot (Stove.SlotsByUnit),
    /// else the whole stack at once. Answers how many units went in, the whole stack's count when it went in whole.
    /// </summary>
    int MoveUnits(long instanceId, long fromOwnerId, long toOwnerId, int units);

    /// <summary>The owner a unit in the pot came from, when this mod put it there this session; null otherwise.</summary>
    long? OriginOf(long instanceId);

    /// <summary>Puts one unit of a fuel stack into a free fuel slot of the stove. False when the game refused.</summary>
    bool AddFuel(long furnitureId, long instanceId);

    /// <summary>Home stoves ready for their next dish: not cooking, nothing finished waiting in them, the pot empty. Hot pots are left out.</summary>
    IReadOnlyList<IdleStove> IdleStoves();

    /// <summary>Starts the stove on what its pot holds, as the dish among recipeIds the pot makes. False when it did not start.</summary>
    bool Start(long furnitureId, IReadOnlyList<int> recipeIds);
}

/// <summary>
/// A home stove ready for its next dish. IsOpen is true while its cooking screen is open, so the screen's own Cook
/// button starts it; HasHeat is false for a stove out of fuel or power.
/// </summary>
public sealed record IdleStove(Stove Stove, bool IsOpen, bool HasHeat);
