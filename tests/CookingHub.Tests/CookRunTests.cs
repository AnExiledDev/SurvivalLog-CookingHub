using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>The presses that put food in the open stove's pot, judged by what ends up in it.</summary>
public class CookRunTests
{
    private static FakeGame Stocked() => new()
    {
        Stock = { S(1, Rice, hours: 30), S(2, Rice, hours: 3, owner: Chest), S(3, Pork, hours: 40, owner: Chest) },
    };

    private static List<long> InPot(FakeGame game) => game.Pot.Select(stack => stack.InstanceId).OrderBy(id => id).ToList();

    [Fact]
    public void Fill_pulls_what_the_recipe_needs_from_the_bag_and_storage()
    {
        var game = Stocked();
        var pressed = CookRun.Fill(game, PorkRice, isSpoilingFirst: true, isChinese: false);

        Assert.True(pressed.IsDone);
        Assert.Equal("The pot is filled for Pork Rice.", pressed.Message);
        Assert.Equal(new long[] { 2, 3 }, InPot(game));
    }

    [Fact]
    public void Fill_can_use_the_freshest_food_instead()
    {
        var game = Stocked();

        CookRun.Fill(game, PorkRice, isSpoilingFirst: false, isChinese: false);

        Assert.Equal(new long[] { 1, 3 }, InPot(game));
    }

    [Fact]
    public void Fill_for_a_tag_recipe_takes_any_food_of_each_type()
    {
        var game = new FakeGame { Stock = { S(1, Wheat), S(2, Pork) } };

        Assert.True(CookRun.Fill(game, StirFry, true, false).IsDone);
        Assert.Equal(new long[] { 1, 2 }, InPot(game));
    }

    [Fact]
    public void Fill_never_uses_spoiled_food()
    {
        var game = new FakeGame { Stock = { S(1, Rice), S(2, Pork, isSpoiled: true) } };
        var pressed = CookRun.Fill(game, PorkRice, true, false);

        Assert.False(pressed.IsDone);
        Assert.Equal("Not enough fresh food for Pork Rice.", pressed.Message);
        Assert.Empty(game.Pot);
    }

    [Fact]
    public void Fill_leaves_a_pot_that_already_holds_food_alone()
    {
        var game = Stocked();

        game.Pot.Add(S(9, Pork, owner: Workbench));

        Assert.Equal("Empty the pot first.", CookRun.Fill(game, PorkRice, true, false).Message);
        Assert.Equal(new long[] { 9 }, InPot(game));
    }

    [Fact]
    public void Fill_refuses_a_recipe_the_stove_cannot_cook()
    {
        var game = Stocked();

        game.Allowed = new[] { StirFry };

        Assert.Equal("This stove can't cook Pork Rice.", CookRun.Fill(game, PorkRice, true, false).Message);
        Assert.Empty(game.Pot);
    }

    [Fact]
    public void Fill_refuses_a_recipe_bigger_than_the_pot()
    {
        var game = Stocked();

        game.MaxFood = 1;

        Assert.Equal("Pork Rice needs 2 slots and this stove has 1.", CookRun.Fill(game, PorkRice, true, false).Message);
        Assert.Empty(game.Pot);
    }

    [Fact]
    public void Fill_says_how_far_it_got_when_the_game_refuses_a_move()
    {
        var game = Stocked();

        game.Stuck.Add(3);

        var pressed = CookRun.Fill(game, PorkRice, true, false);

        Assert.True(pressed.IsDone);
        Assert.Equal("Moved 1 of 2; the game refused the rest.", pressed.Message);
    }

    [Fact]
    public void No_press_touches_a_stove_that_is_cooking_or_a_panel_that_is_shut()
    {
        var cooking = Stocked();
        var shut = Stocked();
        var away = Stocked();

        cooking.IsCooking = true;
        shut.IsStoveOpen = false;
        away.Override = new CookRead.NotReady("you are away from home");

        Assert.Equal("This stove is already cooking.", CookRun.Fill(cooking, PorkRice, true, false).Message);
        Assert.Equal("Open a stove's cooking screen first.", CookRun.Season(shut, Salt, false).Message);
        Assert.Equal("Can't cook right now: you are away from home.", CookRun.Swap(away, Pork, false).Message);
        Assert.Empty(cooking.Pot);
    }

    [Fact]
    public void Season_adds_the_seasoning_closest_to_spoiling()
    {
        var game = new FakeGame { Stock = { S(1, Salt, hours: 50), S(2, Salt, hours: 5) } };

        Assert.Equal("Added Salt.", CookRun.Season(game, Salt, false).Message);
        Assert.Equal(new long[] { 2 }, InPot(game));
    }

    [Fact]
    public void Season_refuses_a_full_pot()
    {
        var game = new FakeGame { MaxFood = 1, Stock = { S(1, Salt) }, Pot = { S(9, Rice, owner: Workbench) } };

        Assert.Equal("The pot is full.", CookRun.Season(game, Salt, false).Message);
    }

    [Fact]
    public void Swap_puts_the_spoiled_food_in_the_bag_and_fresh_food_in_the_pot()
    {
        var game = new FakeGame { Stock = { S(1, Pork, hours: 20, owner: Chest) }, Pot = { S(9, Pork, isSpoiled: true, owner: Workbench), S(8, Rice, owner: Workbench) } };
        var pressed = CookRun.Swap(game, Pork, false);

        Assert.Equal("Swapped the spoiled Pork for fresh.", pressed.Message);
        Assert.Equal(new long[] { 1, 8 }, InPot(game));
        Assert.Contains(game.Stock, stack => stack.InstanceId == 9 && stack.OwnerId == Bag);
    }

    [Fact]
    public void Swap_takes_nothing_out_when_there_is_no_fresh_food_to_put_back()
    {
        var game = new FakeGame { Pot = { S(9, Pork, isSpoiled: true, owner: Workbench) } };

        Assert.Equal("No fresh Pork left.", CookRun.Swap(game, Pork, false).Message);
        Assert.Equal(new long[] { 9 }, InPot(game));
    }

    [Fact]
    public void Press_results_come_in_chinese_when_the_game_is_in_chinese()
    {
        Assert.Equal("锅已经满了。", CookRun.Season(new FakeGame { MaxFood = 0 }, Salt, true).Message);
    }
}
