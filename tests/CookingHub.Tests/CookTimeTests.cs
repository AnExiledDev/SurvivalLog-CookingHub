using CookingHub.Core;
using Xunit;

namespace CookingHub.Tests;

/// <summary>
/// A pot the queue starts with the screen closed must cook as long as the game's Cook button would make it: in Individual
/// mode a pot of repeated dishes takes longer than its first dish. When the game's rule cannot be asked, the pot still
/// starts, for its first dish's time.
/// </summary>
public class CookTimeTests
{
    [Fact]
    public void The_games_time_is_used_when_it_gives_one()
    {
        // Two Watermelon Juices in Individual mode: the game gives 60, the first dish alone would be 30.
        Assert.Equal(60f, CookTime.Pick(60f, CookTime.FirstDish(30, 1f)));
    }

    [Fact]
    public void The_games_time_is_used_even_when_it_is_shorter_than_the_first_dish()
    {
        // Individual mode times from the dish Mix would make first, which can be quicker than the first one started.
        Assert.Equal(20f, CookTime.Pick(20f, 45f));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0f)]
    [InlineData(-5f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void Without_a_usable_game_time_the_pot_cooks_for_its_first_dish(float? gameTime)
    {
        Assert.Equal(15f, CookTime.Pick(gameTime, CookTime.FirstDish(30, 2f)));
    }

    [Fact]
    public void A_stove_without_a_speed_cooks_at_normal_speed()
    {
        Assert.Equal(30f, CookTime.FirstDish(30, 0f));
        Assert.Equal(20f, CookTime.FirstDish(30, 1.5f));
    }
}
