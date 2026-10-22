using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>The game's quality roll, counted: every roll from the level's floor to 100 is equally likely.</summary>
public class QualityTests
{
    private static readonly QualityRules Rules = QualityRules.Shipped;

    [Fact]
    public void A_fresh_exact_recipe_adds_both_bonuses_to_every_roll()
    {
        // Level 2 rolls 25..100 (76 rolls), +10 fresh +10 exact: scores 45..100.
        var odds = Quality.For(Rules, 2, Book.Recipes[PorkRice], new PotMix(false, 0, 0));

        Assert.Equal(25, odds.Floor);
        Assert.Equal(20, odds.Bonus);
        Assert.Equal(45, odds.Lowest);
        Assert.Equal(31 / 76.0, odds.Of(Tier.Perfect), 6);
        Assert.Equal(30 / 76.0, odds.Of(Tier.Good), 6);
        Assert.Equal(15 / 76.0, odds.Of(Tier.Normal), 6);
        Assert.Equal(0, odds.Of(Tier.Fail));
    }

    [Fact]
    public void Spoiled_food_takes_points_off_and_can_fail_the_dish()
    {
        // A tag recipe with spoiled food: -20, scores 5..80.
        var odds = Quality.For(Rules, 2, Book.Recipes[StirFry], new PotMix(true, 0, 0));

        Assert.Equal(new[] { PartKind.Rotten }, odds.Parts.Select(part => part.Kind));
        Assert.Equal(5, odds.Lowest);
        Assert.Equal(25 / 76.0, odds.Of(Tier.Fail), 6);
        Assert.Equal(0, odds.Of(Tier.Perfect));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 18)]
    [InlineData(2, 27)]
    [InlineData(3, 32)]
    public void Each_seasoning_adds_half_what_the_one_before_did(int seasonings, int bonus)
    {
        Assert.Equal(bonus, Quality.SeasoningBonus(Rules, seasonings));
    }

    [Fact]
    public void A_score_never_passes_100()
    {
        var odds = Quality.For(Rules, 5, Book.Recipes[PorkRice], new PotMix(false, 2, 0));

        Assert.Equal(100, odds.Lowest);
        Assert.Equal(1.0, odds.Of(Tier.Perfect));
        Assert.Contains(odds.Parts, part => part is { Kind: PartKind.Seasoning, Value: 27, Count: 2 });
    }

    [Fact]
    public void The_stove_bonus_counts_only_when_the_stove_has_one()
    {
        Assert.DoesNotContain(Quality.For(Rules, 2, Book.Recipes[StirFry], new PotMix(false, 0, 0)).Parts, part => part.Kind == PartKind.Stove);
        Assert.Contains(Quality.For(Rules, 2, Book.Recipes[StirFry], new PotMix(false, 0, 5)).Parts, part => part is { Kind: PartKind.Stove, Value: 5 });
    }

    [Fact]
    public void A_recipe_with_a_short_quality_map_always_makes_the_normal_dish()
    {
        Assert.Equal(Tier.Normal, Quality.TierOf(new[] { 0, 50 }, 100));
        Assert.Equal(Tier.Fail, Quality.TierOf(Map, 29));
        Assert.Equal(Tier.Perfect, Quality.TierOf(Map, 90));
    }

    [Fact]
    public void A_level_past_the_table_keeps_the_highest_floor_below_it()
    {
        Assert.Equal(70, Rules.Floor(7));
        Assert.Equal(40, Rules.Floor(3));
    }
}
