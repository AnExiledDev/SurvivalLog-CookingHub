using System.Text.Json;
using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>
/// The fresh food a recipe can draw on, looked up by item or by food type. It must find exactly what a pass over all the
/// food would, or the book shows food the pot cannot take, or misses food the player has.
/// </summary>
public class FreshFoodTests
{
    private const int RawGrain = 77;

    private static IEnumerable<long> Ids(IReadOnlyList<Stack> stacks) => stacks.Select(stack => stack.InstanceId);

    // Food of the right type that the pot does not take must not count toward a type, but an exact want names it outright.
    [Fact]
    public void A_type_counts_only_food_the_pot_takes()
    {
        var items = new Dictionary<int, Item>(Book.Items) { [RawGrain] = new(RawGrain, "Raw Grain", null, Grain, 5, false) };
        var fresh = new FreshFood(Book with { Items = items }, new[] { S(1, Rice), S(2, RawGrain) });

        Assert.Equal(new long[] { 1 }, Ids(fresh.Matching(false, Grain)));
        Assert.Equal(new long[] { 2 }, Ids(fresh.Matching(true, RawGrain)));
    }

    [Fact]
    public void Spoiled_and_empty_stacks_are_left_out()
    {
        var fresh = new FreshFood(Book, new[] { S(1, Rice, isSpoiled: true), S(2, Rice, count: 0), S(3, Rice) });

        Assert.Equal(new long[] { 3 }, Ids(fresh.Matching(true, Rice)));
        Assert.Equal(new long[] { 3 }, Ids(fresh.Matching(false, Grain)));
    }

    // The pot is filled in this order, so the lookup must not reorder the player's food.
    [Fact]
    public void Each_list_keeps_the_order_of_the_food()
    {
        var fresh = new FreshFood(Book, new[] { S(5, Wheat), S(1, Rice), S(3, Wheat) });

        Assert.Equal(new long[] { 5, 1, 3 }, Ids(fresh.Matching(false, Grain)));
        Assert.Equal(new long[] { 5, 3 }, Ids(fresh.Matching(true, Wheat)));
    }

    [Fact]
    public void A_want_with_no_food_finds_none()
    {
        var fresh = new FreshFood(Book, new[] { S(1, Rice) });

        Assert.Empty(fresh.Matching(true, Pork));
        Assert.Empty(fresh.Matching(false, Meat));
    }

    // A book row tells the player where the food is and how soon the first of it spoils, across every food the dish wants.
    [Fact]
    public void A_book_row_lists_each_place_once_in_order_and_the_soonest_spoiling_food()
    {
        var game = new FakeGame
        {
            Stock = { S(1, Rice, hours: 30, owner: Chest), S(2, Pork, hours: 5), S(3, Rice, hours: 12), S(4, Pork, hours: 1, isSpoiled: true) },
        };
        var kitchen = game.Kitchen();

        using var overlay = JsonDocument.Parse(CookView.Overlay(kitchen, kitchen.Stove!, isShowingOdds: true));

        var row = overlay.RootElement.GetProperty("book").EnumerateArray().Single(dish => dish.GetProperty("id").GetInt32() == PorkRice);

        Assert.Equal(new[] { "Bag", "Chest" }, row.GetProperty("places").EnumerateArray().Select(place => place.GetString()));
        Assert.Equal(5, row.GetProperty("hours").GetDouble());
    }
}
