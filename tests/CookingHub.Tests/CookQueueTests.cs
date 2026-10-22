using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>The dishes queued from the window, which survive a game restart and wait for a stove that can cook them.</summary>
public class CookQueueTests
{
    [Fact]
    public void The_queue_reads_back_what_it_wrote()
    {
        var queue = CookQueue.Empty.Add(PorkRice).Add(StirFry).Add(PorkRice);

        Assert.Equal("1,2,1", queue.Write());
        Assert.Equal(new[] { PorkRice, StirFry, PorkRice }, CookQueue.Read(queue.Write()).Ids);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x, 0,-4")]
    public void A_missing_or_broken_queue_file_reads_as_an_empty_queue(string? text)
    {
        Assert.Empty(CookQueue.Read(text).Ids);
    }

    [Fact]
    public void Junk_in_the_queue_file_is_dropped_and_the_rest_kept()
    {
        Assert.Equal(new[] { 3, 5 }, CookQueue.Read("3, x,0,5").Ids);
    }

    [Fact]
    public void Removing_a_dish_takes_out_only_its_first_entry()
    {
        var queue = new CookQueue(new[] { PorkRice, StirFry, PorkRice });

        Assert.Equal(new[] { StirFry, PorkRice }, queue.Remove(PorkRice).Ids);
        Assert.Same(queue, queue.Remove(Feast));
    }

    [Fact]
    public void A_stove_sees_only_the_queued_dishes_it_can_cook_in_queue_order()
    {
        var game = new FakeGame { Allowed = new[] { StirFry, PorkRice } };
        var queue = new CookQueue(new[] { Feast, PorkRice, StirFry });

        Assert.Equal(new[] { PorkRice, StirFry }, queue.For(game.Kitchen().Stove!));
    }

    [Fact]
    public void Queue_and_remove_presses_name_the_dish()
    {
        var (queued, added) = CookQueue.Empty.Press(Book, PorkRice, isAdding: true, isChinese: false);
        var (emptied, removed) = queued.Press(Book, PorkRice, isAdding: false, isChinese: false);

        Assert.Equal("Queued Pork Rice.", added.Message);
        Assert.Equal(new[] { PorkRice }, queued.Ids);
        Assert.Equal("Removed Pork Rice from the queue.", removed.Message);
        Assert.Empty(emptied.Ids);
    }

    [Fact]
    public void A_full_queue_takes_no_more()
    {
        var full = new CookQueue(Enumerable.Repeat(PorkRice, CookQueue.Limit).ToList());
        var (after, pressed) = full.Press(Book, StirFry, isAdding: true, isChinese: false);

        Assert.False(pressed.IsDone);
        Assert.Equal("The queue is full.", pressed.Message);
        Assert.Same(full, after);
        Assert.Equal(CookQueue.Limit, CookQueue.Read(string.Join(",", Enumerable.Repeat("1", CookQueue.Limit + 5))).Ids.Count);
    }

    [Fact]
    public void A_recipe_the_game_does_not_have_is_never_queued()
    {
        var (after, pressed) = CookQueue.Empty.Press(Book, 9999, isAdding: true, isChinese: false);

        Assert.False(pressed.IsDone);
        Assert.Empty(after.Ids);
    }
}
