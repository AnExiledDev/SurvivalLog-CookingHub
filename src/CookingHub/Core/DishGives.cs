using System;
using System.Collections.Generic;

namespace CookingHub.Core;

/// <summary>What a recipe's dish restores when eaten, so the book can be sorted by it.</summary>
public static class DishGives
{
    public const int Stats = 5;
    private const int NormalTier = 1;

    /// <summary>
    /// Satiety, morale, stamina, health and vitality the dish gives, weighed by the chance of each quality when the
    /// odds are known (Fail to Perfect), else the Normal dish's. Rounded to whole points.
    /// </summary>
    public static int[] Expected(Cookbook book, Recipe recipe, IReadOnlyList<double>? chances)
    {
        var sums = new double[Stats];

        for (var tier = 0; tier < recipe.Outputs.Count; tier++)
        {
            var weight = chances != null && tier < chances.Count ? chances[tier] : tier == NormalTier ? 1 : 0;

            if (weight == 0 || !book.Items.TryGetValue(recipe.Outputs[tier], out var dish) || dish.Gives == null) continue;

            for (var stat = 0; stat < Stats && stat < dish.Gives.Count; stat++) sums[stat] += weight * dish.Gives[stat];
        }

        return Array.ConvertAll(sums, sum => (int)Math.Round(sum, MidpointRounding.AwayFromZero));
    }
}
