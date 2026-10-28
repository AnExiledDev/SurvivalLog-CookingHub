using System;
using System.Collections.Generic;

namespace CookingHub.Core;

/// <summary>
/// The player's fresh food, sorted once per read by item and by food type, so what a recipe wants is a lookup rather
/// than a pass over every stack. The recipe book checks some five hundred recipes, each against all of the food in the
/// bag and home storage several times over, which made one overlay take a tenth of a second with a large pantry. A
/// type counts only food a pot takes, as <see cref="Finder.Matching"/> does. Each list keeps the stock's order.
/// </summary>
public sealed class FreshFood
{
    private static readonly IReadOnlyList<Stack> None = Array.Empty<Stack>();

    private readonly Dictionary<int, List<Stack>> _byItem = new();
    private readonly Dictionary<int, List<Stack>> _byType = new();

    public FreshFood(Cookbook book, IEnumerable<Stack> stock)
    {
        foreach (var stack in stock)
        {
            if (stack.IsSpoiled || stack.Count <= 0) continue;

            Add(_byItem, stack.ItemId, stack);

            if (book.Items.TryGetValue(stack.ItemId, out var item) && item.CanCook) Add(_byType, item.Type, stack);
        }
    }

    /// <summary>The fresh stacks that count for one want: that item, or any cookable food of that type.</summary>
    public IReadOnlyList<Stack> Matching(bool isItem, int id) => (isItem ? _byItem : _byType).TryGetValue(id, out var stacks) ? stacks : None;

    private static void Add(Dictionary<int, List<Stack>> index, int key, Stack stack)
    {
        if (!index.TryGetValue(key, out var stacks)) index[key] = stacks = new List<Stack>();

        stacks.Add(stack);
    }
}
