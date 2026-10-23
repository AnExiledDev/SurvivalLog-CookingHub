using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>What a dish restores, which the cooking screen sorts recipes by.</summary>
public class DishGivesTests
{
    [Fact]
    public void Without_odds_a_recipe_gives_what_its_Normal_dish_gives()
    {
        Assert.Equal(new[] { 10, 1, 2, 0, 0 }, DishGives.Expected(Book, Book.Recipes[PorkRice], null));
    }

    [Fact]
    public void With_odds_each_quality_counts_by_its_chance()
    {
        // Pork Rice is a Rice Bowl (10 satiety) up to Good and a Fancy Bowl (40) at Perfect.
        var gives = DishGives.Expected(Book, Book.Recipes[PorkRice], new[] { 0.0, 0.5, 0.0, 0.5 });

        Assert.Equal(new[] { 25, 3, 5, 1, 1 }, gives);
    }

    [Fact]
    public void A_dish_with_nothing_known_gives_nothing()
    {
        var kindling = R(99, "Kindling", specific: new[] { Wheat }, outputs: new[] { Firewood, Firewood, Firewood, Firewood });

        Assert.Equal(new int[DishGives.Stats], DishGives.Expected(Book, kindling, null));
    }
}
