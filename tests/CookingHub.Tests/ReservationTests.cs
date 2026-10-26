using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>
/// The food set aside for queued dishes: each queued dish holds the food it will cook with, earlier dishes first, so
/// nothing else the player presses can leave a queued dish short.
/// </summary>
public class ReservationTests
{
    private const long Kitchen = 800;
    private const long Porch = 801;

    private static int Units(Kitchen kitchen, int itemId) => kitchen.Stock.Where(stack => stack.ItemId == itemId).Sum(stack => stack.Count);

    [Fact]
    public void A_queued_dish_sets_aside_only_the_units_it_cooks_with_when_units_go_in_one_at_a_time()
    {
        var kitchen = new FakeGame { Stock = { S(1, Rice, count: 3), S(2, Pork, count: 3) } }.Kitchen();
        var left = Reservation.Of(kitchen, CookQueue.Empty.Add(Kitchen, PorkRice), isSpoilingFirst: true, isByUnit: true).Left;

        Assert.Equal(2, Units(left, Rice));
        Assert.Equal(2, Units(left, Pork));
    }

    [Fact]
    public void A_queued_dish_sets_aside_only_the_uses_it_cooks_with_of_an_item_used_a_little_at_a_time()
    {
        var kitchen = new FakeGame { Stock = { S(1, Rice, count: 3), Uses(2, Pork, uses: 7) } }.Kitchen();
        var left = Reservation.Of(kitchen, CookQueue.Empty.Add(Kitchen, PorkRice), isSpoilingFirst: true, isByUnit: false).Left;

        Assert.Equal(6, Units(left, Pork));
    }

    [Fact]
    public void A_queued_dish_sets_aside_whole_stacks_when_a_fill_moves_whole_stacks()
    {
        var kitchen = new FakeGame { Stock = { S(1, Rice, count: 3), S(2, Pork, count: 3) } }.Kitchen();
        var left = Reservation.Of(kitchen, CookQueue.Empty.Add(Kitchen, PorkRice), isSpoilingFirst: true, isByUnit: false).Left;

        Assert.Equal(0, Units(left, Rice));
        Assert.Equal(0, Units(left, Pork));
    }

    [Fact]
    public void The_dish_queued_first_gets_the_food_and_a_later_one_left_short_holds_nothing_back()
    {
        var kitchen = new FakeGame { Stock = { S(1, Rice), S(2, Pork), S(3, Wheat) } }.Kitchen();
        var queue = CookQueue.Empty.Add(Kitchen, PorkRice).Add(Kitchen, StirFry).Add(Porch, Feast);
        var reservation = Reservation.Of(kitchen, queue, isSpoilingFirst: true, isByUnit: true);

        Assert.Equal(new long[] { 1, 2 }, reservation.For(Kitchen, 0)?.Select(take => take.InstanceId).OrderBy(id => id));
        Assert.Null(reservation.For(Kitchen, 1));
        Assert.Null(reservation.For(Porch, 0));
        Assert.Equal(1, Units(reservation.Left, Wheat));
    }

    [Fact]
    public void A_dish_is_covered_only_by_food_no_queued_dish_holds()
    {
        var kitchen = new FakeGame { Stock = { S(1, Rice), S(2, Pork), S(3, Wheat), S(4, Pork) } }.Kitchen();
        var reservation = Reservation.Of(kitchen, CookQueue.Empty.Add(Porch, PorkRice), isSpoilingFirst: true, isByUnit: true);

        Assert.False(reservation.Covers(Book.Recipes[PorkRice]));
        Assert.True(reservation.Covers(Book.Recipes[StirFry]));
    }
}
