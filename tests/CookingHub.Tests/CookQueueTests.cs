using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>
/// Queue and Remove on the open stove: each stove keeps its own queue, oldest first, up to the limit, and a dish is
/// queued only when the food left after every dish already queued covers it.
/// </summary>
public class CookQueueTests
{
    private const long Stove = 800;
    private const long OtherStove = 801;

    private static Kitchen Plenty() => new FakeGame { Stock = { S(1, Rice, count: 5), S(2, Pork, count: 5), S(3, Wheat, count: 5) }, SlotsByUnit = true }.Kitchen();

    private static Kitchen JustOnePorkRice() => new FakeGame { Stock = { S(1, Rice), S(2, Pork) } }.Kitchen();

    private static (CookQueue Queue, Pressed Pressed) Add(CookQueue queue, Kitchen kitchen, long stoveId, int recipeId) =>
        queue.Press(kitchen, stoveId, recipeId, isAdding: true, isSpoilingFirst: true, isChinese: false);

    [Fact]
    public void Queue_adds_to_the_end_of_this_stove_queue_only()
    {
        var (queue, pressed) = Add(CookQueue.Empty.Add(Stove, PorkRice), Plenty(), Stove, StirFry);

        Assert.True(pressed.IsDone);
        Assert.Equal("Queued Stir Fry.", pressed.Message);
        Assert.Equal(new[] { PorkRice, StirFry }, queue.For(Stove));
        Assert.Empty(queue.For(OtherStove));
    }

    [Fact]
    public void Remove_takes_off_only_the_first_of_a_dish_queued_twice()
    {
        var queue = CookQueue.Empty.Add(Stove, StirFry).Add(Stove, PorkRice).Add(Stove, StirFry);
        var (after, pressed) = queue.Press(Plenty(), Stove, StirFry, isAdding: false, isSpoilingFirst: true, isChinese: false);

        Assert.Equal("Removed Stir Fry from the queue.", pressed.Message);
        Assert.Equal(new[] { PorkRice, StirFry }, after.For(Stove));
    }

    [Fact]
    public void A_full_queue_refuses_one_more()
    {
        var queue = Enumerable.Range(0, CookQueue.Limit).Aggregate(CookQueue.Empty, (q, _) => q.Add(Stove, PorkRice));
        var (after, pressed) = Add(queue, Plenty(), Stove, StirFry);

        Assert.False(pressed.IsDone);
        Assert.Equal("The queue is full.", pressed.Message);
        Assert.Equal(CookQueue.Limit, after.For(Stove).Count);
        Assert.DoesNotContain(StirFry, after.For(Stove));
    }

    [Fact]
    public void A_recipe_not_in_the_tables_is_not_queued()
    {
        var (after, pressed) = Add(CookQueue.Empty, Plenty(), Stove, 999);

        Assert.False(pressed.IsDone);
        Assert.True(after.IsEmpty);
    }

    [Fact]
    public void Removing_the_last_dish_leaves_the_queue_empty()
    {
        Assert.True(CookQueue.Empty.Add(Stove, PorkRice).Remove(Stove, PorkRice).IsEmpty);
    }

    [Fact]
    public void A_dish_the_food_does_not_cover_is_not_queued()
    {
        var kitchen = new FakeGame { Stock = { S(1, Rice) } }.Kitchen();
        var (after, pressed) = Add(CookQueue.Empty, kitchen, Stove, PorkRice);

        Assert.False(pressed.IsDone);
        Assert.Equal("Not enough fresh food for Pork Rice.", pressed.Message);
        Assert.True(after.IsEmpty);
    }

    [Fact]
    public void Food_set_aside_for_a_dish_queued_on_another_stove_is_not_queued_again()
    {
        var queued = CookQueue.Empty.Add(OtherStove, PorkRice);
        var (after, pressed) = Add(queued, JustOnePorkRice(), Stove, PorkRice);

        Assert.False(pressed.IsDone);
        Assert.Equal("The food for Pork Rice is set aside for queued dishes.", pressed.Message);
        Assert.Empty(after.For(Stove));
    }

    [Fact]
    public void A_dish_queues_as_many_times_as_the_food_covers_it()
    {
        var kitchen = new FakeGame { Stock = { S(1, Rice, count: 2), S(2, Pork, count: 2) }, SlotsByUnit = true }.Kitchen();
        var (once, first) = Add(CookQueue.Empty, kitchen, Stove, PorkRice);
        var (twice, second) = Add(once, kitchen, OtherStove, PorkRice);
        var (_, third) = Add(twice, kitchen, Stove, PorkRice);

        Assert.True(first.IsDone);
        Assert.True(second.IsDone);
        Assert.False(third.IsDone);
    }
}
