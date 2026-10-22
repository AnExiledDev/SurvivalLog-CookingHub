using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>The odds on the game's cooking screen and the changes from the player's own food that would raise them.</summary>
public class AdviceTests
{
    private static FakeGame PorkRiceInPot(bool isPorkSpoiled = false) => new()
    {
        Forecasts = { new Forecast(PorkRice, true, true) },
        Pot = { S(8, Rice, owner: Workbench), S(9, Pork, isSpoiled: isPorkSpoiled, owner: Workbench) },
    };

    private static PotAdvice AdviceOf(FakeGame game)
    {
        var kitchen = game.Kitchen();

        return Advice.For(kitchen, kitchen.Stove ?? throw new InvalidOperationException("the fake stove is shut"));
    }

    [Fact]
    public void Every_forecast_line_gets_odds_except_dark_food()
    {
        var game = PorkRiceInPot();

        game.Forecasts.Add(new Forecast(Recipe.DarkFood, true, true));

        var lines = AdviceOf(game).Lines;

        Assert.Equal(31 / 76.0, lines[0].Odds?.Of(Tier.Perfect) ?? -1, 6);
        Assert.Null(lines[1].Odds);
    }

    [Fact]
    public void A_seasoning_the_player_has_is_suggested_with_what_it_adds_to_perfect()
    {
        var game = PorkRiceInPot();

        game.Stock.Add(S(1, Salt));

        var nudge = Assert.Single(AdviceOf(game).Nudges);

        // +18 moves Perfect from rolls 70..100 to rolls 52..100: 18 more of 76.
        Assert.Equal(NudgeKind.Season, nudge.Kind);
        Assert.Equal(Salt, nudge.ItemId);
        Assert.Equal(18 / 76.0, nudge.PerfectGain ?? -1, 6);
    }

    [Fact]
    public void A_seasoning_already_in_the_pot_is_not_suggested_again()
    {
        var game = PorkRiceInPot();

        game.Pot.Add(S(7, Salt, owner: Workbench));
        game.Stock.Add(S(1, Salt));
        game.Stock.Add(S(2, Pepper));

        Assert.Equal(Pepper, Assert.Single(AdviceOf(game).Nudges).ItemId);
    }

    [Fact]
    public void A_spoiled_item_the_player_has_fresh_is_suggested_as_a_swap()
    {
        var game = PorkRiceInPot(isPorkSpoiled: true);

        game.Stock.Add(S(1, Pork));

        var nudge = Assert.Single(AdviceOf(game).Nudges);

        // Back from -20 to +10: 30 more points on every roll.
        Assert.Equal(NudgeKind.Swap, nudge.Kind);
        Assert.Equal(Pork, nudge.ItemId);
        Assert.True(nudge.PerfectGain > 0.3);
    }

    [Fact]
    public void With_two_spoiled_items_no_single_swap_claims_a_gain()
    {
        var game = PorkRiceInPot(isPorkSpoiled: true);

        game.Pot[0] = game.Pot[0] with { IsSpoiled = true };
        game.Stock.Add(S(1, Pork));
        game.Stock.Add(S(2, Rice));

        var nudges = AdviceOf(game).Nudges;

        Assert.Equal(2, nudges.Count);
        Assert.All(nudges, nudge => Assert.Null(nudge.PerfectGain));
    }

    [Fact]
    public void A_stove_that_is_cooking_gets_no_suggestions()
    {
        var game = PorkRiceInPot();

        game.IsCooking = true;
        game.Stock.Add(S(1, Salt));

        Assert.Empty(AdviceOf(game).Nudges);
    }

    [Fact]
    public void The_overlay_sends_odds_by_recipe_id_and_never_a_recipe_name()
    {
        var game = PorkRiceInPot();
        var kitchen = game.Kitchen();
        var json = CookView.Overlay(kitchen, kitchen.Stove ?? throw new InvalidOperationException(), isShowingOdds: true);

        Assert.Contains("\"ids\":[1]", json);
        Assert.Contains("\"chances\":[0,20,39,41]", json);
        Assert.DoesNotContain("Pork Rice", json);
    }
}
