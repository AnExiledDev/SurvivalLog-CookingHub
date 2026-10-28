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
/// depends on the ingredients, so Varies is true and the dish goes by its plain (tier 2) recipe. IsUnlearned marks a
/// recipe the game's own book does not list yet, shown only when the player asks to see those too.
/// </summary>
public sealed record Dish(Recipe Recipe, bool Varies, Readiness State, IReadOnlyList<Need> Needs, bool IsNew, bool UsesSpoiling, double? Perfect, bool IsUnlearned = false)
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
        var fresh = new FreshFood(kitchen.Book, kitchen.Stock);
        var isListed = Listed(kitchen);
        var shown = Cookable(kitchen).Where(isListed).ToList();
        var dishes = new List<Dish>();

        foreach (var recipe in shown.Where(recipe => recipe.IsExact)) dishes.Add(Check(kitchen, fresh, recipe, varies: false));

        foreach (var group in shown.Where(recipe => !recipe.IsExact).GroupBy(recipe => string.Join(",", recipe.Tags.OrderBy(tag => tag))))
        {
            var plain = group.OrderBy(recipe => recipe.Tier == PlainTier ? 0 : 1).ThenBy(recipe => recipe.Id).First();

            dishes.Add(Check(kitchen, fresh, plain, varies: group.Count() > 1));
        }

        return dishes.OrderBy(dish => dish.State).ThenByDescending(dish => dish.UsesSpoiling).ThenBy(dish => dish.Recipe.Name, System.StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Every recipe the open stove cooks that the game lists, one by one as its recipe book shows them, so each book row
    /// can be told where its food is and what it lacks. With isShowingUnlearned, the recipes the game does not list yet
    /// come too, marked IsUnlearned.
    /// </summary>
    public static IReadOnlyList<Dish> ForStove(Kitchen kitchen, Stove stove, bool isShowingUnlearned = false) =>
        ForStove(kitchen, stove, new FreshFood(kitchen.Book, kitchen.Stock), isShowingUnlearned);

    /// <summary>The same, with the kitchen's fresh food already sorted, so the caller can look up each row's sources in it too.</summary>
    public static IReadOnlyList<Dish> ForStove(Kitchen kitchen, Stove stove, FreshFood fresh, bool isShowingUnlearned = false)
    {
        var isListed = Listed(kitchen);

        return Cookable(kitchen)
            .Where(recipe => stove.Takes(recipe.Id) && (isShowingUnlearned || isListed(recipe)))
            .OrderBy(recipe => recipe.Id)
            .Select(recipe => Check(kitchen, fresh, recipe, varies: false, isUnlearned: !isListed(recipe)))
            .ToList();
    }

    /// <summary>The odds of a dish cooked from fresh food on this stove with nothing added, or null when it has none.</summary>
    public static Odds? PlainOdds(Kitchen kitchen, Recipe recipe, int stoveBonus) =>
        recipe.HasOdds ? Quality.For(kitchen.Book.Rules, kitchen.Level, recipe, new PotMix(false, 0, stoveBonus)) : null;

    /// <summary>What a recipe wants, as item or type counts.</summary>
    public static IReadOnlyList<(int Id, int Want)> Wants(Recipe recipe) =>
        (recipe.IsExact ? recipe.Specific : recipe.Tags).GroupBy(id => id).Select(group => (group.Key, group.Count())).ToList();

    /// <summary>Recipes a pot can be filled for: not dark food, and naming items or food types.</summary>
    private static IEnumerable<Recipe> Cookable(Kitchen kitchen) =>
        kitchen.Book.Recipes.Values.Where(recipe => recipe.Id != Recipe.DarkFood && (recipe.IsExact || recipe.Tags.Count > 0));

    /// <summary>
    /// Whether the game's book lists a recipe: the player knows it or a recipe it is grouped with
    /// (Reducer_Web_Cooking.IsRecipeKnownToPlayer).
    /// </summary>
    private static System.Func<Recipe, bool> Listed(Kitchen kitchen)
    {
        var knownGroups = new HashSet<int>(kitchen.Book.Recipes.Values.Where(recipe => IsSelfKnown(kitchen, recipe)).Select(recipe => recipe.GroupId));

        return recipe => knownGroups.Contains(recipe.GroupId);
    }

    /// <summary>
    /// Learned, by cooking it or from a recipe item, or shown by the tables from a cooking level above 0
    /// (Reducer_Web_Cooking.IsRecipeSelfKnown). A show level of 0 never shows a recipe on its own.
    /// </summary>
    private static bool IsSelfKnown(Kitchen kitchen, Recipe recipe) =>
        kitchen.Cooked.Contains(recipe.Id) || recipe.ShowLevel > 0 && kitchen.Level >= recipe.ShowLevel;

    private static Dish Check(Kitchen kitchen, FreshFood fresh, Recipe recipe, bool varies, bool isUnlearned = false)
    {
        var needs = Wants(recipe).Select(want => new Need(want.Id, !recipe.IsExact, want.Want, fresh.Matching(recipe.IsExact, want.Id).Sum(stack => stack.Count))).ToList();
        var missing = needs.Sum(need => need.Missing);
        var isUnlocked = recipe.IsUnlocked(kitchen.Level, kitchen.CraftUnlocked);
        var state = !isUnlocked ? Readiness.Locked : missing == 0 ? Readiness.Ready : missing == 1 ? Readiness.MissingOne : Readiness.Short;
        var usesSpoiling = needs.Any(need => fresh.Matching(recipe.IsExact, need.Id).Any(stack => stack.HoursLeft <= SpoilingHours));
        var perfect = state == Readiness.Ready ? PlainOdds(kitchen, recipe, kitchen.Stove?.QualityBonus ?? 0)?.Of(Tier.Perfect) : null;

        return new Dish(recipe, varies, state, needs, !kitchen.Cooked.Contains(recipe.Id), usesSpoiling, perfect, isUnlearned);
    }

    /// <summary>The fresh stacks that count for one want: that item, or any cookable food of that type.</summary>
    public static IEnumerable<Stack> Matching(Cookbook book, IEnumerable<Stack> fresh, bool isItem, int id) =>
        fresh.Where(stack => isItem ? stack.ItemId == id : book.Items.TryGetValue(stack.ItemId, out var item) && item.CanCook && item.Type == id);
}
