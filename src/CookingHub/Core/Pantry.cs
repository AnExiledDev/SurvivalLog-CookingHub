using System.Collections.Generic;
using System.Linq;

namespace CookingHub.Core;

/// <summary>
/// One food the player holds, all stacks together: how many, how many have spoiled, the soonest any fresh one spoils
/// (null when none of it spoils), and where it is kept.
/// </summary>
public sealed record Shelf(int ItemId, int Count, int Spoiled, double? SoonestHours, IReadOnlyList<string> Places);

/// <summary>The player's cooking food, soonest to spoil first.</summary>
public static class Pantry
{
    public static IReadOnlyList<Shelf> For(Kitchen kitchen) =>
        kitchen.Stock
            .Where(stack => stack.Count > 0)
            .GroupBy(stack => stack.ItemId)
            .Select(group => new Shelf(
                group.Key,
                group.Sum(stack => stack.Count),
                group.Where(stack => stack.IsSpoiled).Sum(stack => stack.Count),
                group.Where(stack => !stack.IsSpoiled).Select(stack => stack.HoursLeft).Where(hours => hours != null).Min(),
                group.Select(stack => stack.Place).Distinct().OrderBy(place => place, System.StringComparer.Ordinal).ToList()))
            .OrderByDescending(shelf => shelf.Spoiled > 0)
            .ThenBy(shelf => shelf.SoonestHours ?? double.MaxValue)
            .ThenBy(shelf => kitchen.Book.ItemName(shelf.ItemId), System.StringComparer.Ordinal)
            .ToList();

    /// <summary>The number on the HUD button: fresh food that spoils within the given hours.</summary>
    public static int SpoilingSoon(Kitchen kitchen, double hours) =>
        kitchen.Stock.Where(stack => !stack.IsSpoiled && stack.HoursLeft <= hours).Sum(stack => stack.Count);
}
