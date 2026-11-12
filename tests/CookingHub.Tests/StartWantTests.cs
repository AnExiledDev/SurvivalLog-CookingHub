using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>
/// When the page may press the game's Cook button for a pot Cooking Hub just filled. The game picks the dish for a pot
/// itself, so a pot the queue filled for a tag recipe can forecast an exact recipe the same food makes instead; it must
/// still start, as the Cook button would start it. Repeat last keeps waiting for the dish it asked for.
/// </summary>
public class StartWantTests
{
    private static Stove Filled(params Forecast[] forecasts)
    {
        var game = new FakeGame { Pot = { S(9, Rice, owner: Workbench), S(8, Pork, owner: Workbench) } };

        game.Forecasts.AddRange(forecasts);

        return game.Kitchen().Stove!;
    }

    [Fact]
    public void A_queued_pot_starts_when_the_game_plans_another_dish_from_its_food()
    {
        // The queue put food in for Stir Fry, and the game plans Pork Rice from that food.
        var stove = Filled(new Forecast(PorkRice, true, true));

        Assert.True(new StartWant.QueuedPot().IsMetBy(stove, PorkRice));
        Assert.False(new StartWant.Dishes(new[] { StirFry, FancyStirFry }).IsMetBy(stove, PorkRice), "the rule before 0.9.4, which left the stove idle");
    }

    [Fact]
    public void A_queued_pot_starts_on_a_dish_the_player_has_not_cooked_yet()
    {
        var stove = Filled(new Forecast(StirFry, false, false));

        Assert.True(new StartWant.QueuedPot().IsMetBy(stove, null));
    }

    [Fact]
    public void A_queued_pot_that_would_make_dark_food_never_starts()
    {
        Assert.False(new StartWant.QueuedPot().IsMetBy(Filled(new Forecast(Recipe.DarkFood, false, true)), null));
        Assert.False(new StartWant.QueuedPot().IsMetBy(Filled(new Forecast(PorkRice, true, true), new Forecast(Recipe.DarkFood, false, true)), PorkRice));
    }

    [Fact]
    public void A_queued_pot_waits_while_the_screen_forecasts_no_dish_yet()
    {
        Assert.False(new StartWant.QueuedPot().IsMetBy(Filled(), null));
        Assert.False(new StartWant.QueuedPot().IsMetBy(Filled(new Forecast(0, false, false)), null));
    }

    [Fact]
    public void A_queued_pot_is_never_started_empty_or_while_it_cooks()
    {
        var stove = Filled(new Forecast(PorkRice, true, true));

        Assert.False(new StartWant.QueuedPot().IsMetBy(stove with { Pot = System.Array.Empty<Stack>() }, PorkRice));
        Assert.False(new StartWant.QueuedPot().IsMetBy(stove with { IsCooking = true }, PorkRice));
    }

    [Fact]
    public void Repeat_last_starts_only_once_the_pot_forecasts_its_dish()
    {
        var stove = Filled(new Forecast(PorkRice, true, true));
        var want = new StartWant.Dishes(new[] { PorkRice });

        Assert.True(want.IsMetBy(stove, PorkRice));
        Assert.False(want.IsMetBy(stove, StirFry));
        Assert.False(want.IsMetBy(stove, null));
    }
}
