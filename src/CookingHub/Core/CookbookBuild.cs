using System;
using System.Collections.Generic;

namespace CookingHub.Core;

/// <summary>
/// The cookbook (every recipe, every food and dish, the food types and the quality rules) read a step at a time, to run
/// as a <see cref="SlicedJob"/>: reading every item row and its name in one frame was a hitch of over a hundred
/// milliseconds. It waits for the icon folder first, then takes one recipe per step and one item per step, so it ends
/// with the same book a read of the whole tables at once makes.
/// </summary>
public static class CookbookBuild
{
    /// <summary>
    /// recipeOf and itemOf answer a row, or null for one the book leaves out; itemOf also gets every dish a recipe makes,
    /// since a dish is listed whether or not it can be cooked with. done gets the finished book.
    /// </summary>
    public static IEnumerable<bool> Steps(
        Func<bool> areIconsIn,
        Func<IReadOnlyList<int>> recipeIds,
        Func<int, Recipe?> recipeOf,
        Func<IReadOnlyList<int>> itemIds,
        Func<int, IReadOnlySet<int>, Item?> itemOf,
        Func<IReadOnlyList<FoodType>> typesOf,
        Func<QualityRules> rulesOf,
        Action<Cookbook> done)
    {
        while (!areIconsIn()) yield return false;

        var recipes = new Dictionary<int, Recipe>();
        var recipeList = recipeIds();

        yield return true;

        foreach (var id in recipeList)
        {
            if (recipeOf(id) is { } recipe) recipes[recipe.Id] = recipe;

            yield return true;
        }

        var outputs = new HashSet<int>();

        foreach (var recipe in recipes.Values) outputs.UnionWith(recipe.Outputs);

        var items = new Dictionary<int, Item>();
        var itemList = itemIds();

        yield return true;

        foreach (var id in itemList)
        {
            if (itemOf(id, outputs) is { } item) items[item.Id] = item;

            yield return true;
        }

        var types = typesOf();

        yield return true;

        done(new Cookbook(items, recipes, types, rulesOf()));
    }
}
