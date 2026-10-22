using System.Collections.Generic;
using System.Linq;

namespace CookingHub.Core;

/// <summary>The odds of one dish the cooking panel forecasts. Odds is null for a line with none to give (dark food, a message).</summary>
public sealed record ForecastOdds(int RecipeId, Odds? Odds);

public enum NudgeKind
{
    /// <summary>Add a seasoning the pot does not hold yet.</summary>
    Season,

    /// <summary>Take a spoiled item out and put in a fresh one of the same.</summary>
    Swap,
}

/// <summary>One change that would help the pot, and how many points it adds to the Perfect chance, when that is known.</summary>
public sealed record Nudge(NudgeKind Kind, int ItemId, double? PerfectGain);

public sealed record PotAdvice(IReadOnlyList<ForecastOdds> Lines, IReadOnlyList<Nudge> Nudges);

/// <summary>What the open cooking panel's pot will likely make, and the changes from the player's own food that would make it better.</summary>
public static class Advice
{
    public static PotMix MixOf(Cookbook book, Stove stove) =>
        new(stove.Pot.Any(stack => stack.IsSpoiled), stove.Pot.Count(stack => book.TypeOf(stack.ItemId) == QualityRules.SeasoningType), stove.QualityBonus);

    public static PotAdvice For(Kitchen kitchen, Stove stove)
    {
        var mix = MixOf(kitchen.Book, stove);
        var lines = stove.Forecasts.Select(forecast => new ForecastOdds(forecast.RecipeId, OddsOf(kitchen, forecast.RecipeId, mix))).ToList();
        var main = lines.FirstOrDefault(line => line.Odds != null);

        if (main?.Odds == null || stove.IsCooking) return new PotAdvice(lines, new List<Nudge>());

        var recipe = kitchen.Book.Recipes[main.RecipeId];
        var nudges = new List<Nudge>();
        var seasoning = SeasoningToAdd(kitchen, stove);

        if (seasoning != null)
        {
            var gain = Gain(kitchen, recipe, main.Odds, mix with { Seasonings = mix.Seasonings + 1 });

            if (gain > 0) nudges.Add(new Nudge(NudgeKind.Season, seasoning.Value, gain));
        }

        nudges.AddRange(Swaps(kitchen, stove, recipe, main.Odds, mix));

        return new PotAdvice(lines, nudges);
    }

    /// <summary>A fresh stack of the same item that can stand in for a spoiled one in the pot, or null when the player has none.</summary>
    public static Stack? FreshFor(Kitchen kitchen, int itemId) =>
        kitchen.Stock.Where(stack => stack.ItemId == itemId && !stack.IsSpoiled && stack.Count > 0).OrderBy(stack => stack.HoursLeft ?? double.MaxValue).FirstOrDefault();

    /// <summary>A seasoning from the player's food that the pot lacks, the one closest to spoiling first, or null.</summary>
    public static int? SeasoningToAdd(Kitchen kitchen, Stove stove)
    {
        if (stove.Room == 0) return null;

        var inPot = stove.Pot.Select(stack => stack.ItemId).ToHashSet();
        var pick = kitchen.Stock
            .Where(stack => !stack.IsSpoiled && stack.Count > 0 && !inPot.Contains(stack.ItemId) && kitchen.Book.TypeOf(stack.ItemId) == QualityRules.SeasoningType)
            .OrderBy(stack => stack.HoursLeft ?? double.MaxValue)
            .FirstOrDefault();

        return pick?.ItemId;
    }

    private static Odds? OddsOf(Kitchen kitchen, int recipeId, PotMix mix) =>
        kitchen.Book.Recipes.TryGetValue(recipeId, out var recipe) && recipe.HasOdds ? Quality.For(kitchen.Book.Rules, kitchen.Level, recipe, mix) : null;

    /// <summary>
    /// One swap per spoiled item the player has fresh. Its gain is given only when it is the pot's one spoiled item,
    /// since the fresh bonus comes back only once nothing in the pot is spoiled.
    /// </summary>
    private static IEnumerable<Nudge> Swaps(Kitchen kitchen, Stove stove, Recipe recipe, Odds now, PotMix mix)
    {
        var spoiled = stove.Pot.Where(stack => stack.IsSpoiled).Select(stack => stack.ItemId).Distinct().ToList();
        var gain = spoiled.Count == 1 ? Gain(kitchen, recipe, now, mix with { HasSpoiled = false }) : (double?)null;

        return spoiled.Where(itemId => FreshFor(kitchen, itemId) != null).Select(itemId => new Nudge(NudgeKind.Swap, itemId, gain));
    }

    private static double Gain(Kitchen kitchen, Recipe recipe, Odds now, PotMix changed) =>
        Quality.For(kitchen.Book.Rules, kitchen.Level, recipe, changed).Of(Tier.Perfect) - now.Of(Tier.Perfect);
}
