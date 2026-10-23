using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>Queue and Remove on the open stove: each stove keeps its own queue, oldest first, up to the limit.</summary>
public class CookQueueTests
{
    private const long Stove = 800;
    private const long OtherStove = 801;

    [Fact]
    public void Queue_adds_to_the_end_of_this_stove_queue_only()
    {
        var (queue, pressed) = CookQueue.Empty.Add(Stove, PorkRice).Press(Book, Stove, StirFry, isAdding: true, isChinese: false);

        Assert.True(pressed.IsDone);
        Assert.Equal("Queued Stir Fry.", pressed.Message);
        Assert.Equal(new[] { PorkRice, StirFry }, queue.For(Stove));
        Assert.Empty(queue.For(OtherStove));
    }

    [Fact]
    public void Remove_takes_off_only_the_first_of_a_dish_queued_twice()
    {
        var queue = CookQueue.Empty.Add(Stove, StirFry).Add(Stove, PorkRice).Add(Stove, StirFry);
        var (after, pressed) = queue.Press(Book, Stove, StirFry, isAdding: false, isChinese: false);

        Assert.Equal("Removed Stir Fry from the queue.", pressed.Message);
        Assert.Equal(new[] { PorkRice, StirFry }, after.For(Stove));
    }

    [Fact]
    public void A_full_queue_refuses_one_more()
    {
        var queue = Enumerable.Range(0, CookQueue.Limit).Aggregate(CookQueue.Empty, (q, _) => q.Add(Stove, PorkRice));
        var (after, pressed) = queue.Press(Book, Stove, StirFry, isAdding: true, isChinese: false);

        Assert.False(pressed.IsDone);
        Assert.Equal("The queue is full.", pressed.Message);
        Assert.Equal(CookQueue.Limit, after.For(Stove).Count);
        Assert.DoesNotContain(StirFry, after.For(Stove));
    }

    [Fact]
    public void A_recipe_not_in_the_tables_is_not_queued()
    {
        var (after, pressed) = CookQueue.Empty.Press(Book, Stove, 999, isAdding: true, isChinese: false);

        Assert.False(pressed.IsDone);
        Assert.True(after.IsEmpty);
    }

    [Fact]
    public void Removing_the_last_dish_leaves_the_queue_empty()
    {
        Assert.True(CookQueue.Empty.Add(Stove, PorkRice).Remove(Stove, PorkRice).IsEmpty);
    }
}
