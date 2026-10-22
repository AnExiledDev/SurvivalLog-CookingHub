using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>Which dish each stove last started, as Repeat last sees it from one read of the open stove to the next.</summary>
public class LastCookedTests
{
    private static FakeGame Filled(int recipeId) => new()
    {
        Pot = { S(9, Rice, owner: Workbench) },
        Forecasts = { new Forecast(recipeId, true, true) },
    };

    private static LastCooked Watch(LastCooked last, FakeGame game) => last.See(game.Kitchen().Stove);

    [Fact]
    public void A_dish_becomes_the_stoves_last_when_the_stove_starts_cooking_it()
    {
        var game = Filled(PorkRice);
        var last = Watch(LastCooked.None, game);

        Assert.Null(last.For(StoveId));

        game.IsCooking = true;
        last = Watch(last, game);

        Assert.Equal(PorkRice, last.For(StoveId));
    }

    [Fact]
    public void The_last_dish_outlives_closing_the_stove()
    {
        var game = Filled(PorkRice);
        var last = Watch(LastCooked.None, game);

        game.IsCooking = true;
        last = Watch(last, game);
        last = last.See(null);

        Assert.Equal(PorkRice, last.For(StoveId));
    }

    [Fact]
    public void A_stove_already_cooking_when_opened_has_no_last_dish()
    {
        var game = Filled(PorkRice);

        game.IsCooking = true;

        Assert.Null(Watch(Watch(LastCooked.None, game), game).For(StoveId));
    }

    [Fact]
    public void Dark_food_is_never_repeated()
    {
        var game = Filled(Recipe.DarkFood);
        var last = Watch(LastCooked.None, game);

        game.IsCooking = true;

        Assert.Null(Watch(last, game).For(StoveId));
    }

    [Fact]
    public void A_changed_pot_changes_the_dish_that_starts()
    {
        var game = Filled(PorkRice);
        var last = Watch(LastCooked.None, game);

        game.Forecasts.Clear();
        game.Forecasts.Add(new Forecast(StirFry, false, true));
        last = Watch(last, game);
        game.IsCooking = true;

        Assert.Equal(StirFry, Watch(last, game).For(StoveId));
    }
}
