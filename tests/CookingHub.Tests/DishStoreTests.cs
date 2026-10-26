using CookingHub.Core;
using Xunit;

namespace CookingHub.Tests;

/// <summary>A finished dish goes to the storage whose tags want it most, the way Storage Hub ranks put-away.</summary>
public class DishStoreTests
{
    private const long Stove = 700;
    private const long Fridge = 801;
    private const long Pantry = 802;
    private const long Cupboard = 803;
    private const long Box = 804;

    private static readonly ReadyDish PorkRice = new(9001, 1, "Pork Rice", Stove);

    private sealed class FakeDishes : IDishGame
    {
        public List<ReadyDish> Waiting { get; } = new() { PorkRice };
        public List<DishStorage> Shelves { get; } = new();
        public HashSet<long> Full { get; } = new();
        public Dictionary<long, long> Placed { get; } = new();

        public IReadOnlyList<ReadyDish> WaitingDishes() => Waiting;

        public IReadOnlyList<DishStorage> ShelvesFor(ReadyDish dish) => Shelves;

        public bool Move(ReadyDish dish, long toOwnerId)
        {
            if (Full.Contains(toOwnerId)) return false;

            Placed[dish.InstanceId] = toOwnerId;

            return true;
        }
    }

    private static DishStorage Tagged(long id, int level, int specificity, bool holds = false) => new(id, $"S{id}", new PutRank(level, specificity), holds);

    private static DishStorage Untagged(long id, bool holds) => new(id, $"S{id}", null, holds);

    [Fact]
    public void A_storage_tagged_for_the_dish_beats_one_that_only_holds_some()
    {
        var game = new FakeDishes();

        game.Shelves.Add(Untagged(Box, holds: true));
        game.Shelves.Add(Tagged(Fridge, level: 1, specificity: 1));

        var stored = Assert.Single(DishStore.PutAway(game));

        Assert.Equal(Fridge, Assert.IsType<Stored.Into>(stored).Storage.OwnerId);
        Assert.Equal(Fridge, game.Placed[PorkRice.InstanceId]);
    }

    [Fact]
    public void The_more_specific_tag_wins_then_the_lower_level_then_the_storage_already_holding_it()
    {
        var ranked = DishStore.Targets(new[]
        {
            Tagged(Box, level: 0, specificity: 1),
            Tagged(Fridge, level: 2, specificity: 3),
            Tagged(Pantry, level: 1, specificity: 3),
            Tagged(Cupboard, level: 2, specificity: 3, holds: true),
        });

        Assert.Equal(new[] { Pantry, Cupboard, Fridge, Box }, ranked.Select(shelf => shelf.OwnerId));
    }

    [Fact]
    public void A_full_storage_passes_the_dish_to_the_next_best()
    {
        var game = new FakeDishes();

        game.Shelves.Add(Tagged(Fridge, level: 0, specificity: 2));
        game.Shelves.Add(Untagged(Box, holds: true));
        game.Full.Add(Fridge);

        DishStore.PutAway(game);

        Assert.Equal(Box, game.Placed[PorkRice.InstanceId]);
    }

    [Fact]
    public void A_dish_no_storage_wants_stays_in_the_stove()
    {
        var game = new FakeDishes();

        game.Shelves.Add(Untagged(Box, holds: false));

        var stored = Assert.Single(DishStore.PutAway(game));

        Assert.IsType<Stored.Stays>(stored);
        Assert.Empty(game.Placed);
        Assert.StartsWith("Pork Rice stays in the stove", DishStore.Describe(stored, isChinese: false));
    }

    // Trying every full storage costs a game move apiece, so a dish with nowhere to go waits before it is tried again.
    [Fact]
    public void A_dish_that_stayed_is_not_tried_again_until_its_wait_is_over()
    {
        var game = new FakeDishes();
        var retry = new DishRetry();

        game.Shelves.Add(Tagged(Fridge, level: 0, specificity: 1));
        game.Full.Add(Fridge);

        Assert.IsType<Stored.Stays>(Assert.Single(DishStore.PutAway(game, retry, now: 100)));
        Assert.Empty(DishStore.PutAway(game, retry, now: 100 + DishRetry.WaitSeconds - 1));

        game.Full.Clear();

        var stored = Assert.Single(DishStore.PutAway(game, retry, now: 100 + DishRetry.WaitSeconds));

        Assert.Equal(Fridge, Assert.IsType<Stored.Into>(stored).Storage.OwnerId);
    }

    [Fact]
    public void A_dish_that_stopped_waiting_is_forgotten_so_it_is_tried_at_once_if_it_waits_again()
    {
        var game = new FakeDishes();
        var retry = new DishRetry();

        game.Shelves.Add(Tagged(Fridge, level: 0, specificity: 1));
        game.Full.Add(Fridge);
        DishStore.PutAway(game, retry, now: 100);

        game.Waiting.Clear();
        DishStore.PutAway(game, retry, now: 101);
        game.Waiting.Add(PorkRice);
        game.Full.Clear();

        Assert.IsType<Stored.Into>(Assert.Single(DishStore.PutAway(game, retry, now: 102)));
    }

    [Fact]
    public void Another_dish_is_tried_while_the_one_that_stayed_waits()
    {
        var game = new FakeDishes();
        var retry = new DishRetry();
        var stew = new ReadyDish(9002, 2, "Stew", Stove);

        game.Shelves.Add(Tagged(Fridge, level: 0, specificity: 1));
        game.Full.Add(Fridge);
        DishStore.PutAway(game, retry, now: 100);

        game.Full.Clear();
        game.Waiting.Add(stew);

        var stored = Assert.Single(DishStore.PutAway(game, retry, now: 101));

        Assert.Equal(stew, stored.Dish);
    }

    [Fact]
    public void The_line_names_the_dish_and_where_it_went()
    {
        var stored = new Stored.Into(PorkRice, new DishStorage(Fridge, "Fridge", new PutRank(0, 1), false));

        Assert.Equal("Put Pork Rice away in Fridge.", DishStore.Describe(stored, isChinese: false));
    }

    [Fact]
    public void A_dish_put_away_is_good_news_and_one_left_in_the_stove_is_a_warning()
    {
        var into = DishStore.Note(new Stored.Into(PorkRice, new DishStorage(Fridge, "Fridge", new PutRank(0, 1), false)), isChinese: false);
        var stays = DishStore.Note(new Stored.Stays(PorkRice), isChinese: false);

        Assert.Equal(new DishNote("Put Pork Rice away in Fridge.", false), into);
        Assert.True(stays.IsWarning);
        Assert.StartsWith("Pork Rice stays in the stove", stays.Text);
    }
}
