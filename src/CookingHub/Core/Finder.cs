using System.Collections.Generic;
using System.Linq;

namespace CookingHub.Core;

public enum Readiness
{
    Ready,
    MissingOne,
    Short,
    Locked,
}

/// <summary>One ingredient a dish wants: an item, or any food of a type. Have counts only fresh food.</summary>
public sealed record Need(int Id, bool IsType, int Want, int Have)
{
    public int Missing => System.Math.Max(0, Want - Have);
}

/// <summary>
/// A dish the player could aim for. Tag recipes that want the same food types are one dish: which of them the pot makes
/// depends on the ingredients, so Varies is true and the dish goes by its plain (tier 2) recipe.
/// </summary>
public sealed record Dish(Recipe Recipe, bool Varies, Readiness State, IReadOnlyList<Need> Needs, bool IsNew, bool UsesSpoiling, double? Perfect)
{
    public int Missing => Needs.Sum(need => need.Missing);
}

/// <summary>Every recipe the player can see, checked against all the fresh food in their bag and home storage.</summary>
public static class Finder
{
    public const double SpoilingHours = 24;
    private const int PlainTier = 2;

    public static IReadOnlyList<Dish> For(Kitchen kitchen)
    {
        var fresh = kitchen.Stock.Where(stack => !stack.IsSpoiled && stack.Count > 0).ToList();
        var dishes = new List<Dish>();

        foreach (var recipe in Shown(kitchen).Where(recipe => recipe.IsExact)) dishes.Add(Check(kitchen, fresh, recipe, varies: false));

        foreach (var group in Shown(kitchen).Where(recipe => !recipe.IsExact).GroupBy(recipe => string.Join(",", recipe.Tags.OrderBy(tag => tag))))
        {
            var plain = group.OrderBy(recipe => recipe.Tier == PlainTier ? 0 : 1).ThenBy(recipe => recipe.Id).First();

            dishes.Add(Check(kitchen, fresh, plain, varies: group.Count() > 1));
        }

        return dishes.OrderBy(dish => dish.State).ThenByDescending(dish => dish.UsesSpoiling).ThenBy(dish => dish.Recipe.Name, System.StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Every recipe the open stove cooks that the game lists, one by one as its recipe book shows them, so each book row
    /// can be told where its food is and what it lacks.
    /// </summary>
    public static IReadOnlyList<Dish> ForStove(Kitchen kitchen, Stove stove)
    {
        var fresh = kitchen.Stock.Where(stack => !stack.IsSpoiled && stack.Count > 0).ToList();

        return Shown(kitchen).Where(recipe => stove.Takes(recipe.Id)).OrderBy(recipe => recipe.Id).Select(recipe => Check(kitchen, fresh, recipe, varies: false)).ToList();
    }

    /// <summary>The fresh stacks a dish could draw on, wherever they are kept.</summary>
    public static IReadOnlyList<Stack> Sources(Kitchen kitchen, Dish dish)
    {
        var fresh = kitchen.Stock.Where(stack => !stack.IsSpoiled && stack.Count > 0).ToList();

        return dish.Needs.SelectMany(need => Matching(kitchen.Book, fresh, dish.Recipe.IsExact, need.Id)).Distinct().ToList();
    }

    /// <summary>The odds of a dish cooked from fresh food on this stove with nothing added, or null when it has none.</summary>
    public static Odds? PlainOdds(Kitchen kitchen, Recipe recipe, int stoveBonus) =>
        recipe.HasOdds ? Quality.For(kitchen.Book.Rules, kitchen.Level, recipe, new PotMix(false, 0, stoveBonus)) : null;

    /// <summary>What a recipe wants, as item or type counts.</summary>
    public static IReadOnlyList<(int Id, int Want)> Wants(Recipe recipe) =>
        (recipe.IsExact ? recipe.Specific : recipe.Tags).GroupBy(id => id).Select(group => (group.Key, group.Count())).ToList();

    /// <summary>
    /// Recipes the game would list: not dark food, and either one the player already knows or one the tables show at their level
    /// (Reducer_Web_Cooking.IsRecipeSelfKnown).
    /// </summary>
    private static IEnumerable<Recipe> Shown(Kitchen kitchen) =>
        kitchen.Book.Recipes.Values.Where(recipe => recipe.Id != Recipe.DarkFood && (kitchen.Cooked.Contains(recipe.Id) || recipe.ShowLevel >= 0 && recipe.ShowLevel <= kitchen.Level) && (recipe.IsExact || recipe.Tags.Count > 0));

    private static Dish Check(Kitchen kitchen, IReadOnlyList<Stack> fresh, Recipe recipe, bool varies)
    {
        var needs = Wants(recipe).Select(want => new Need(want.Id, !recipe.IsExact, want.Want, Matching(kitchen.Book, fresh, recipe.IsExact, want.Id).Sum(stack => stack.Count))).ToList();
        var missing = needs.Sum(need => need.Missing);
        var isUnlocked = recipe.IsUnlocked(kitchen.Level, kitchen.CraftUnlocked);
        var state = !isUnlocked ? Readiness.Locked : missing == 0 ? Readiness.Ready : missing == 1 ? Readiness.MissingOne : Readiness.Short;
        var usesSpoiling = needs.Any(need => Matching(kitchen.Book, fresh, recipe.IsExact, need.Id).Any(stack => stack.HoursLeft <= SpoilingHours));
        var perfect = state == Readiness.Ready ? PlainOdds(kitchen, recipe, kitchen.Stove?.QualityBonus ?? 0)?.Of(Tier.Perfect) : null;

        return new Dish(recipe, varies, state, needs, !kitchen.Cooked.Contains(recipe.Id), usesSpoiling, perfect);
    }

    /// <summary>The fresh stacks that count for one want: that item, or any cookable food of that type.</summary>
    public static IEnumerable<Stack> Matching(Cookbook book, IEnumerable<Stack> fresh, bool isItem, int id) =>
        fresh.Where(stack => isItem ? stack.ItemId == id : book.Items.TryGetValue(stack.ItemId, out var item) && item.CanCook && item.Type == id);
}
