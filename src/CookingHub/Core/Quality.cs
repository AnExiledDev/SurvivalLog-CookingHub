using System;
using System.Collections.Generic;
using System.Linq;

namespace CookingHub.Core;

/// <summary>Dish tiers, in the order the recipe tables list their outputs.</summary>
public enum Tier
{
    Fail = 0,
    Normal = 1,
    Good = 2,
    Perfect = 3,
}

public enum PartKind
{
    Fresh,
    Rotten,
    Seasoning,
    Exact,
    Stove,
}

/// <summary>One flat bonus on the roll. Count is how many seasonings a seasoning bonus is for, 0 otherwise.</summary>
public readonly record struct Part(PartKind Kind, int Value, int Count = 0);

/// <summary>What goes into a pot as far as quality cares: any spoiled food, how many seasonings, and the stove's own bonus.</summary>
public readonly record struct PotMix(bool HasSpoiled, int Seasonings, int StoveBonus);

/// <summary>
/// The chance of each tier (Fail to Perfect), with the parts that make it: the level's roll floor, each flat bonus,
/// and the lowest score a roll can land on.
/// </summary>
public sealed record Odds(int Floor, IReadOnlyList<Part> Parts, IReadOnlyList<double> Chances, int Lowest)
{
    public int Bonus => Parts.Sum(part => part.Value);

    public double Of(Tier tier) => Chances[(int)tier];
}

/// <summary>
/// The game's quality roll (Furniture.RollCookingQuality): a whole number from the level's floor to 100, plus flat
/// bonuses, clamped to 0..100, then placed on the recipe's quality map. Every roll is equally likely, so the odds are
/// a count. Talents that change cooking quality are not counted.
/// </summary>
public static class Quality
{
    private const int Top = 100;

    public static Odds For(QualityRules rules, int level, Recipe recipe, PotMix mix)
    {
        var floor = rules.Floor(level);
        var parts = Parts(rules, recipe, mix);
        var bonus = parts.Sum(part => part.Value);
        var counts = new int[4];

        for (var roll = floor; roll <= Top; roll++) counts[(int)TierOf(recipe.QualityMap, Score(roll, bonus))]++;

        var rolls = (double)(Top + 1 - floor);

        return new Odds(floor, parts, counts.Select(count => count / rolls).ToList(), Score(floor, bonus));
    }

    /// <summary>The tier a score earns: the highest tier whose lowest score it reaches. A short map always gives Normal, as in the game.</summary>
    public static Tier TierOf(IReadOnlyList<int> qualityMap, int score)
    {
        if (qualityMap.Count < 4) return Tier.Normal;

        var tier = 3;

        while (tier > 0 && score < qualityMap[tier]) tier--;

        return (Tier)tier;
    }

    /// <summary>
    /// The seasoning bonus: the first seasoning adds 18, each next one half the one before, summed in single precision
    /// and rounded half to even, as the game does.
    /// </summary>
    public static int SeasoningBonus(QualityRules rules, int seasonings)
    {
        var sum = 0f;
        var step = rules.SeasoningFirst;

        for (var i = 0; i < seasonings; i++)
        {
            sum += step;
            step *= rules.SeasoningRatio;
        }

        return (int)Math.Round((double)sum, MidpointRounding.ToEven);
    }

    private static List<Part> Parts(QualityRules rules, Recipe recipe, PotMix mix)
    {
        var parts = new List<Part> { mix.HasSpoiled ? new Part(PartKind.Rotten, rules.Rotten) : new Part(PartKind.Fresh, rules.Fresh) };

        if (mix.Seasonings > 0) parts.Add(new Part(PartKind.Seasoning, SeasoningBonus(rules, mix.Seasonings), mix.Seasonings));
        if (recipe.IsExact) parts.Add(new Part(PartKind.Exact, rules.Exact));
        if (mix.StoveBonus != 0) parts.Add(new Part(PartKind.Stove, mix.StoveBonus));

        return parts;
    }

    private static int Score(int roll, int bonus) => Math.Clamp(roll + bonus, 0, Top);
}
