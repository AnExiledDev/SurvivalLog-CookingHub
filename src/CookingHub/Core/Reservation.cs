using System.Collections.Generic;
using System.Linq;

namespace CookingHub.Core;

/// <summary>
/// The food set aside for queued dishes. Every stove's queue is walked in order (stoves by id) and each dish is planned
/// from what earlier dishes left, the way a fill would take it. A dish the food left no longer covers sets nothing aside,
/// so it never holds food back from the dishes after it. Left is the kitchen with the set-aside food taken out of
/// its stock: Queue, Load and Repeat last plan from it, so a queued dish never runs short because of another press.
/// A fill moves a whole stack unless units go in one at a time (Stove.SlotsByUnit), so a whole stack is set aside then.
/// </summary>
public sealed class Reservation
{
    private readonly IReadOnlyDictionary<long, IReadOnlyList<IReadOnlyList<Take>?>> _takes;
    private readonly bool _isSpoilingFirst;

    private Reservation(Kitchen left, IReadOnlyDictionary<long, IReadOnlyList<IReadOnlyList<Take>?>> takes, bool isSpoilingFirst)
    {
        Left = left;
        _takes = takes;
        _isSpoilingFirst = isSpoilingFirst;
    }

    /// <summary>The kitchen without the food set aside for queued dishes.</summary>
    public Kitchen Left { get; }

    public static Reservation Of(Kitchen kitchen, CookQueue queue, bool isSpoilingFirst, bool isByUnit)
    {
        var left = kitchen;
        var takes = new Dictionary<long, IReadOnlyList<IReadOnlyList<Take>?>>();

        foreach (var stoveId in queue.ByStove.Keys.OrderBy(id => id))
        {
            var stoveTakes = new List<IReadOnlyList<Take>?>();

            foreach (var recipeId in queue.For(stoveId))
            {
                var plan = kitchen.Book.Recipes.TryGetValue(recipeId, out var recipe) && IsCookable(kitchen, recipe) ? CookRun.Plan(left, recipe, isSpoilingFirst) : null;

                if (plan != null) left = Without(left, plan, isByUnit);

                stoveTakes.Add(plan);
            }

            takes[stoveId] = stoveTakes;
        }

        return new Reservation(left, takes, isSpoilingFirst);
    }

    /// <summary>The food set aside for the stove's queued dish at that place in its queue, or null when the food left did not cover it.</summary>
    public IReadOnlyList<Take>? For(long stoveId, int index) =>
        _takes.TryGetValue(stoveId, out var stoveTakes) && index >= 0 && index < stoveTakes.Count ? stoveTakes[index] : null;

    /// <summary>True when the food left after every queued dish covers the recipe.</summary>
    public bool Covers(Recipe recipe) => CookRun.Plan(Left, recipe, _isSpoilingFirst) != null;

    /// <summary>A dish the cook cannot make yet is never cooked from the queue, so it holds no food.</summary>
    private static bool IsCookable(Kitchen kitchen, Recipe recipe) => recipe.Id != Recipe.DarkFood && recipe.IsUnlocked(kitchen.Level, kitchen.CraftUnlocked);

    private static Kitchen Without(Kitchen kitchen, IReadOnlyList<Take> takes, bool isByUnit)
    {
        var units = takes.GroupBy(take => take.InstanceId).ToDictionary(group => group.Key, group => group.Sum(take => isByUnit ? take.Units : take.Count));
        var stock = kitchen.Stock
            .Select(stack => units.TryGetValue(stack.InstanceId, out var used) ? stack with { Count = stack.Count - used } : stack)
            .Where(stack => stack.Count > 0)
            .ToList();

        return kitchen with { Stock = stock };
    }
}
