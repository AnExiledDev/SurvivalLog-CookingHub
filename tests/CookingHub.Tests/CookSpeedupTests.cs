using System;
using System.Text.Json;
using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>
/// The cook time on the stove line is how long the forecast dish will really take. When Machine Speed makes cooking
/// faster, the time shown is the sped up one. A speedup that is not a usable number shows the game's time.
/// </summary>
public class CookSpeedupTests
{
    private static Kitchen PotOfPorkRice()
    {
        var game = new FakeGame { Pot = { S(9, Rice, owner: Workbench), S(8, Pork, owner: Workbench) } };

        game.Forecasts.Add(new Forecast(PorkRice, true, true));

        return game.Kitchen();
    }

    private static double ShownCookSeconds(Kitchen kitchen, float speedup)
    {
        var extras = new StationExtras(null, 0, Array.Empty<int>(), CookSpeedup: speedup);

        using var overlay = JsonDocument.Parse(CookView.Overlay(kitchen, kitchen.Stove!, isShowingOdds: true, extras));

        return overlay.RootElement.GetProperty("stove").GetProperty("cookSecs").GetDouble();
    }

    [Fact]
    public void A_faster_cook_shows_the_shorter_time()
    {
        var kitchen = PotOfPorkRice();
        var plain = kitchen.Stove!.CookSeconds(kitchen.Book.Recipes[PorkRice]);

        Assert.Equal(plain, ShownCookSeconds(kitchen, 1f));
        Assert.Equal(plain / 2, ShownCookSeconds(kitchen, 2f));
        Assert.Equal(Math.Round(plain / 3), ShownCookSeconds(kitchen, 3f));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-2f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void A_speedup_that_is_not_a_usable_number_shows_the_games_time(float speedup)
    {
        var kitchen = PotOfPorkRice();

        Assert.Equal(kitchen.Stove!.CookSeconds(kitchen.Book.Recipes[PorkRice]), ShownCookSeconds(kitchen, speedup));
    }

    [Fact]
    public void A_new_speedup_redraws_the_screen()
    {
        var kitchen = PotOfPorkRice();

        Assert.NotEqual(
            OverlayPrint.Of(kitchen, true, true, CookQueue.Empty, null, 0, cookSpeedup: 1f),
            OverlayPrint.Of(kitchen, true, true, CookQueue.Empty, null, 0, cookSpeedup: 2f));
        Assert.Equal(
            OverlayPrint.Of(kitchen, true, true, CookQueue.Empty, null, 0, cookSpeedup: 2f),
            OverlayPrint.Of(kitchen, true, true, CookQueue.Empty, null, 0, cookSpeedup: 2f));
    }
}
