using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>
/// The overlay's fingerprint: two reads of an unchanged kitchen print alike, so the overlay is neither built nor sent
/// again, and anything the overlay is drawn from changes it, so a real change still reaches the page.
/// </summary>
public class OverlayPrintTests
{
    private static FakeGame Stocked() => new()
    {
        Stock = { S(1, Rice, hours: 10), S(2, Pork, owner: Chest), S(3, Salt) },
        Forecasts = { new Forecast(StirFry, false, true) },
    };

    private static ulong Print(Kitchen kitchen, CookQueue? queue = null, int? last = null, int start = 0, bool isShowingOdds = true) =>
        OverlayPrint.Of(kitchen, isShowingOdds, isSpoilingFirst: true, queue ?? CookQueue.Empty, last, start);

    [Fact]
    public void Two_reads_of_an_unchanged_kitchen_print_alike()
    {
        var game = Stocked();

        Assert.Equal(Print(game.Kitchen()), Print(game.Kitchen()));
    }

    [Fact]
    public void Learned_recipes_print_alike_in_any_order()
    {
        var game = Stocked();
        var before = Print(game.Kitchen());

        game.CraftUnlocked.Insert(0, StirFry);

        var stirFryFirst = Print(game.Kitchen());

        game.CraftUnlocked.Remove(StirFry);
        game.CraftUnlocked.Add(StirFry);

        Assert.NotEqual(before, stirFryFirst);
        Assert.Equal(stirFryFirst, Print(game.Kitchen()));
    }

    [Fact]
    public void One_more_unit_in_a_stack_changes_the_print()
    {
        var game = Stocked();
        var before = Print(game.Kitchen());

        game.Stock[0] = game.Stock[0] with { Count = 2 };

        Assert.NotEqual(before, Print(game.Kitchen()));
    }

    [Fact]
    public void Food_moved_from_the_chest_to_the_bag_changes_the_print()
    {
        var game = Stocked();
        var before = Print(game.Kitchen());

        game.MoveStack(2, Chest, Bag);

        Assert.NotEqual(before, Print(game.Kitchen()));
    }

    [Fact]
    public void Food_put_in_the_pot_changes_the_print()
    {
        var game = Stocked();
        var before = Print(game.Kitchen());

        game.MoveStack(1, Bag, Workbench);

        Assert.NotEqual(before, Print(game.Kitchen()));
    }

    [Fact]
    public void A_new_forecast_changes_the_print()
    {
        var game = Stocked();
        var before = Print(game.Kitchen());

        game.Forecasts[0] = new Forecast(PorkRice, true, true);

        Assert.NotEqual(before, Print(game.Kitchen()));
    }

    // A recipe book read afresh (a save loaded, or the catalog read again) may hold other recipes under the same ids,
    // so what was worked out from the old one is dropped even when every other field reads the same.
    [Fact]
    public void A_recipe_book_read_afresh_changes_the_print_even_when_it_holds_the_same()
    {
        var kitchen = Stocked().Kitchen();
        var reloaded = kitchen with { Book = Book with { } };

        Assert.NotEqual(Print(kitchen), Print(reloaded));
    }

    [Fact]
    public void A_new_cooking_level_changes_the_print()
    {
        var game = Stocked();
        var before = Print(game.Kitchen());

        game.Level = 5;

        Assert.NotEqual(before, Print(game.Kitchen()));
    }

    [Fact]
    public void Queuing_a_dish_on_any_stove_changes_the_print()
    {
        var kitchen = Stocked().Kitchen();

        Assert.NotEqual(Print(kitchen), Print(kitchen, CookQueue.Empty.Add(StoveId, PorkRice)));
        Assert.NotEqual(Print(kitchen), Print(kitchen, CookQueue.Empty.Add(801, PorkRice)));
        Assert.NotEqual(Print(kitchen, CookQueue.Empty.Add(StoveId, PorkRice).Add(StoveId, StirFry)), Print(kitchen, CookQueue.Empty.Add(StoveId, StirFry).Add(StoveId, PorkRice)));
    }

    [Fact]
    public void The_start_token_the_last_dish_and_the_odds_setting_each_change_the_print()
    {
        var kitchen = Stocked().Kitchen();
        var before = Print(kitchen);

        Assert.NotEqual(before, Print(kitchen, start: 1));
        Assert.NotEqual(before, Print(kitchen, last: PorkRice));
        Assert.NotEqual(before, Print(kitchen, isShowingOdds: false));
    }

    [Fact]
    public void Turning_unlearned_recipes_on_changes_the_print_so_the_book_is_sent_again()
    {
        var kitchen = Stocked().Kitchen();

        Assert.NotEqual(Print(kitchen), OverlayPrint.Of(kitchen, true, isSpoilingFirst: true, CookQueue.Empty, null, 0, isShowingUnlearned: true));
    }

    [Fact]
    public void Fuel_brought_from_storage_changes_the_print()
    {
        var game = Stocked();

        game.FuelStock.Add(S(50, Firewood, count: 3));

        var before = Print(game.Kitchen());

        game.FuelStock[0] = game.FuelStock[0] with { Count = 2 };

        Assert.NotEqual(before, Print(game.Kitchen()));
    }
}
