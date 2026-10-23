using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>
/// Cooking the queue once a stove's dishes are cleared out: the first queued dish the fresh food covers goes into the
/// idle stove's pot and the stove is started; a dish short of food waits while the next one cooks.
/// </summary>
public class QueueCookTests
{
    private const long Kitchen = 800;
    private const long Porch = 801;

    private static FakeGame Stocked() => new()
    {
        IsStoveOpen = false,
        Stock = { S(1, Rice, hours: 30), S(2, Pork, hours: 40, owner: Chest) },
    };

    private static IReadOnlyList<QueueTurn> Run(FakeGame game, CookQueue queue) =>
        QueueCook.Run(game, game.IdleStoves(), queue, isSpoilingFirst: true, isChinese: false);

    private static List<long> InPot(FakeGame game, long stoveId) => game.IdlePots[stoveId].Select(stack => stack.InstanceId).OrderBy(id => id).ToList();

    [Fact]
    public void An_idle_empty_stove_gets_the_first_queued_dish_and_is_started()
    {
        var game = Stocked();

        game.AddIdle(Kitchen, "Kitchen Stove");

        var turn = Assert.Single(Run(game, CookQueue.Empty.Add(Kitchen, PorkRice).Add(Kitchen, StirFry)));
        var started = Assert.Single(game.Started);

        Assert.Equal(QueueStep.Started, turn.Step);
        Assert.Equal(PorkRice, turn.RecipeId);
        Assert.Equal("Started Pork Rice from the queue on Kitchen Stove.", turn.Message);
        Assert.Equal(new long[] { 1, 2 }, InPot(game, Kitchen));
        Assert.Equal(Kitchen, started.StoveId);
        Assert.Equal(new[] { PorkRice }, started.RecipeIds);
    }

    [Fact]
    public void A_dish_short_of_food_waits_while_the_next_one_cooks()
    {
        var game = new FakeGame { IsStoveOpen = false, Stock = { S(1, Wheat), S(2, Pork) } };

        game.AddIdle(Kitchen, "Kitchen Stove");

        var turn = Assert.Single(Run(game, CookQueue.Empty.Add(Kitchen, PorkRice).Add(Kitchen, StirFry)));

        Assert.Equal(StirFry, turn.RecipeId);
        Assert.Equal(new long[] { 1, 2 }, InPot(game, Kitchen));
    }

    [Fact]
    public void A_dish_not_yet_learned_or_one_the_stove_cannot_cook_is_passed_over()
    {
        var game = new FakeGame { IsStoveOpen = false, Stock = { S(1, Rice), S(2, Pork, count: 3), S(3, Wheat), S(4, Pork) } };

        game.AddIdle(Kitchen, "Kitchen Stove", allowed: new[] { StirFry, FancyStirFry, Feast });

        var turn = Assert.Single(Run(game, CookQueue.Empty.Add(Kitchen, Feast).Add(Kitchen, PorkRice).Add(Kitchen, StirFry)));

        Assert.Equal(StirFry, turn.RecipeId);
    }

    [Fact]
    public void A_tag_dish_is_started_with_every_tier_of_its_food_types_so_the_game_finds_the_one_the_pot_makes()
    {
        var game = new FakeGame { IsStoveOpen = false, Stock = { S(1, Wheat), S(2, Pork) } };

        game.AddIdle(Kitchen, "Kitchen Stove");
        Run(game, CookQueue.Empty.Add(Kitchen, StirFry));

        Assert.Equal(new[] { StirFry, FancyStirFry }, Assert.Single(game.Started).RecipeIds);
    }

    [Fact]
    public void A_stove_with_no_fuel_or_power_waits_and_keeps_its_food_out()
    {
        var game = Stocked();

        game.AddIdle(Kitchen, "Kitchen Stove", hasHeat: false);

        var turn = Assert.Single(Run(game, CookQueue.Empty.Add(Kitchen, PorkRice)));

        Assert.Equal(QueueStep.Waiting, turn.Step);
        Assert.Null(turn.RecipeId);
        Assert.Equal("Kitchen Stove has dishes queued but no fuel or power.", turn.Message);
        Assert.Empty(game.IdlePots[Kitchen]);
        Assert.Empty(game.Started);
    }

    [Fact]
    public void With_no_queued_dish_covered_the_stove_waits()
    {
        var game = new FakeGame { IsStoveOpen = false, Stock = { S(1, Rice) } };

        game.AddIdle(Kitchen, "Kitchen Stove");

        var turn = Assert.Single(Run(game, CookQueue.Empty.Add(Kitchen, PorkRice)));

        Assert.Equal(QueueStep.Waiting, turn.Step);
        Assert.Equal("Kitchen Stove has dishes queued but not enough fresh food for any of them.", turn.Message);
        Assert.Empty(game.Started);
    }

    [Fact]
    public void The_stove_whose_screen_is_open_is_filled_and_left_for_the_page_to_start()
    {
        var game = Stocked();

        game.AddIdle(Kitchen, "Kitchen Stove", isOpen: true);

        var turn = Assert.Single(Run(game, CookQueue.Empty.Add(Kitchen, PorkRice)));

        Assert.Equal(QueueStep.Loaded, turn.Step);
        Assert.Equal(PorkRice, turn.RecipeId);
        Assert.Equal(new long[] { 1, 2 }, InPot(game, Kitchen));
        Assert.Empty(game.Started);
    }

    [Fact]
    public void A_start_the_game_refuses_is_told_and_the_dish_still_leaves_the_queue()
    {
        var game = Stocked();

        game.RefusesStart = true;
        game.AddIdle(Kitchen, "Kitchen Stove");

        var turn = Assert.Single(Run(game, CookQueue.Empty.Add(Kitchen, PorkRice)));

        Assert.Equal(QueueStep.NotStarted, turn.Step);
        Assert.Equal(PorkRice, turn.RecipeId);
    }

    [Fact]
    public void Food_one_stove_used_is_not_planned_again_for_the_next()
    {
        var game = Stocked();

        game.AddIdle(Kitchen, "Kitchen Stove");
        game.AddIdle(Porch, "Porch Stove");

        var turns = Run(game, CookQueue.Empty.Add(Kitchen, PorkRice).Add(Porch, PorkRice));

        Assert.Equal(new[] { QueueStep.Started, QueueStep.Waiting }, turns.Select(turn => turn.Step));
        Assert.Empty(game.IdlePots[Porch]);
    }

    [Fact]
    public void A_stove_with_nothing_queued_is_left_alone()
    {
        var game = Stocked();

        game.AddIdle(Kitchen, "Kitchen Stove");
        game.AddIdle(Porch, "Porch Stove");

        var turn = Assert.Single(Run(game, CookQueue.Empty.Add(Porch, PorkRice)));

        Assert.Equal(Porch, turn.StoveId);
        Assert.Empty(game.IdlePots[Kitchen]);
    }

    [Fact]
    public void A_stove_never_cooks_food_set_aside_for_another_stove_queue()
    {
        var game = new FakeGame { IsStoveOpen = false, Stock = { S(1, Rice), S(2, Pork), S(3, Wheat) } };

        game.AddIdle(Porch, "Porch Stove");

        var turn = Assert.Single(Run(game, CookQueue.Empty.Add(Kitchen, PorkRice).Add(Porch, StirFry)));

        Assert.Equal(QueueStep.Waiting, turn.Step);
        Assert.Empty(game.IdlePots[Porch]);
        Assert.Contains(game.Stock, stack => stack.InstanceId == 2);
    }

    [Fact]
    public void A_stove_cooks_with_the_food_set_aside_for_its_own_dish()
    {
        var game = new FakeGame { IsStoveOpen = false, Stock = { S(1, Rice, hours: 10), S(2, Rice, hours: 50), S(3, Pork), S(4, Pork) } };

        game.AddIdle(Porch, "Porch Stove");

        var turn = Assert.Single(Run(game, CookQueue.Empty.Add(Kitchen, PorkRice).Add(Porch, PorkRice)));

        Assert.Equal(QueueStep.Started, turn.Step);
        Assert.Equal(new long[] { 2, 4 }, InPot(game, Porch));
        Assert.Contains(game.Stock, stack => stack.InstanceId == 1);
    }
}
