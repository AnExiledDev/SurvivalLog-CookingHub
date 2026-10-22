using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>The pantry tab and the HUD badge: what the player holds and what will go off soon.</summary>
public class PantryTests
{
    private static FakeGame Stocked() => new()
    {
        Stock = { S(1, Rice, 3, hours: 30), S(2, Rice, 2, hours: 8, owner: Chest), S(3, Pork, 1, isSpoiled: true), S(4, Salt, 4, hours: null), S(5, Wheat, 2, hours: 70) },
    };

    [Fact]
    public void Each_food_is_one_line_with_its_stacks_added_up()
    {
        var rice = Pantry.For(Stocked().Kitchen()).Single(shelf => shelf.ItemId == Rice);

        Assert.Equal(5, rice.Count);
        Assert.Equal(8, rice.SoonestHours);
        Assert.Equal(new[] { "Bag", "Chest" }, rice.Places);
    }

    [Fact]
    public void Spoiled_food_comes_first_then_the_soonest_to_spoil_and_food_that_keeps_last()
    {
        var order = Pantry.For(Stocked().Kitchen()).Select(shelf => shelf.ItemId);

        Assert.Equal(new[] { Pork, Rice, Wheat, Salt }, order);
    }

    [Fact]
    public void The_badge_counts_fresh_food_spoiling_within_a_day()
    {
        Assert.Equal(2, Pantry.SpoilingSoon(Stocked().Kitchen(), Finder.SpoilingHours));
    }
}
