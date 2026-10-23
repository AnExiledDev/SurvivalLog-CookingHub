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
    public void Fill_puts_food_already_in_the_pot_back_where_it_came_from_first()
    {
        var game = new FakeGame { Stock = { S(1, Rice, owner: Chest), S(2, Pork), S(3, Wheat, owner: Chest) } };

        CookRun.Add(game, Wheat, false);

        var pressed = CookRun.Fill(game, PorkRice, true, false);

        Assert.True(pressed.IsDone);
        Assert.Equal("The pot is filled for Pork Rice.", pressed.Message);
        Assert.Equal(new long[] { 1, 2 }, InPot(game));
        Assert.Contains(game.Stock, stack => stack.InstanceId == 3 && stack.OwnerId == Chest);
    }

    [Fact]
    public void Fill_puts_pot_food_of_unknown_origin_in_the_bag()
    {
        var game = Stocked();

        game.Pot.Add(S(9, Wheat, owner: Workbench));

        Assert.True(CookRun.Fill(game, PorkRice, true, false).IsDone);
        Assert.Equal(new long[] { 2, 3 }, InPot(game));
        Assert.Contains(game.Stock, stack => stack.InstanceId == 9 && stack.OwnerId == Bag);
    }

    [Fact]
    public void Fill_loads_nothing_when_the_pot_food_cannot_come_out()
    {
        var game = Stocked();

        game.Pot.Add(S(9, Wheat, owner: Workbench));
        game.Stuck.Add(9);

        var pressed = CookRun.Fill(game, PorkRice, true, false);

        Assert.False(pressed.IsDone);
        Assert.Equal("The food in the pot could not be put back, so nothing was loaded.", pressed.Message);
        Assert.Equal(new long[] { 9 }, InPot(game));
    }

    [Fact]
    public void Fill_at_the_multi_pot_level_adds_the_dish_beside_a_real_one()
    {
        var game = Stocked();

        game.Level = Kitchen.ShippedMultiPotLevel;
        game.Pot.Add(S(9, Wheat, owner: Workbench));
        game.Pot.Add(S(8, Pork, owner: Workbench));
        game.Forecasts.Add(new Forecast(StirFry, false, true));

        var pressed = CookRun.Fill(game, PorkRice, true, false);

        Assert.True(pressed.IsDone);
        Assert.Equal("Added Pork Rice to the pot as one more dish.", pressed.Message);
        Assert.Equal(new long[] { 2, 3, 8, 9 }, InPot(game));
    }

    [Fact]
    public void Fill_below_the_multi_pot_level_empties_the_pot_instead()
    {
        var game = Stocked();

        game.Level = Kitchen.ShippedMultiPotLevel - 1;
        game.Pot.Add(S(9, Wheat, owner: Workbench));
        game.Forecasts.Add(new Forecast(StirFry, false, true));

        Assert.Equal("The pot is filled for Pork Rice.", CookRun.Fill(game, PorkRice, true, false).Message);
        Assert.Equal(new long[] { 2, 3 }, InPot(game));
    }

    [Fact]
    public void Fill_empties_a_pot_headed_for_dark_food_even_at_the_multi_pot_level()
    {
        var game = Stocked();

        game.Level = Kitchen.ShippedMultiPotLevel;
        game.Pot.Add(S(9, Wheat, owner: Workbench));
        game.Forecasts.Add(new Forecast(Recipe.DarkFood, false, true));

        Assert.Equal("The pot is filled for Pork Rice.", CookRun.Fill(game, PorkRice, true, false).Message);
        Assert.Equal(new long[] { 2, 3 }, InPot(game));
    }

    [Fact]
    public void Fill_empties_the_pot_when_the_second_dish_does_not_fit_beside_the_first()
    {
        var game = Stocked();

        game.Level = Kitchen.ShippedMultiPotLevel;
        game.MaxFood = 2;
        game.Pot.Add(S(9, Wheat, owner: Workbench));
        game.Forecasts.Add(new Forecast(StirFry, false, true));

        Assert.Equal("The pot is filled for Pork Rice.", CookRun.Fill(game, PorkRice, true, false).Message);
        Assert.Equal(new long[] { 2, 3 }, InPot(game));
    }

    [Fact]
    public void Fill_with_slots_by_unit_takes_only_the_units_the_recipe_wants()
    {
        var game = new FakeGame { SlotsByUnit = true, Stock = { S(1, Pork, count: 5) } };

        Assert.True(CookRun.Fill(game, Feast, true, false).IsDone);
        Assert.Equal(3, game.Pot.Count);
        Assert.All(game.Pot, stack => Assert.Equal(1, stack.Count));
        Assert.Equal(2, game.Stock.Single().Count);
    }

    [Fact]
    public void Fill_with_slots_by_unit_counts_units_against_the_pot_size()
    {
        var game = new FakeGame { SlotsByUnit = true, MaxFood = 2, Stock = { S(1, Pork, count: 5) } };

        Assert.Equal("Feast needs 3 slots and this stove has 2.", CookRun.Fill(game, Feast, true, false).Message);
        Assert.Empty(game.Pot);
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
        Assert.Equal("Open a stove's cooking screen first.", CookRun.Add(shut, Salt, false).Message);
        Assert.Equal("Can't cook right now: you are away from home.", CookRun.Swap(away, Pork, false).Message);
        Assert.Empty(cooking.Pot);
    }

    [Fact]
    public void Add_puts_in_the_food_closest_to_spoiling()
    {
        var game = new FakeGame { Stock = { S(1, Salt, hours: 50), S(2, Salt, hours: 5) } };

        Assert.Equal("Added Salt.", CookRun.Add(game, Salt, false).Message);
        Assert.Equal(new long[] { 2 }, InPot(game));
    }

    [Fact]
    public void Add_refuses_a_full_pot()
    {
        var game = new FakeGame { MaxFood = 1, Stock = { S(1, Salt) }, Pot = { S(9, Rice, owner: Workbench) } };

        Assert.Equal("The pot is full.", CookRun.Add(game, Salt, false).Message);
    }

    [Fact]
    public void Add_takes_food_from_storage_when_the_bag_has_none()
    {
        var game = new FakeGame { Stock = { S(4, Pork, owner: Chest) } };

        Assert.True(CookRun.Add(game, Pork, false).IsDone);
        Assert.Equal(new long[] { 4 }, InPot(game));
    }

    [Fact]
    public void Repeat_fills_the_pot_for_the_dish_this_stove_last_started()
    {
        var game = Stocked();
        var last = new LastCooked(new Dictionary<long, int> { [StoveId] = PorkRice }, null, null, false);
        var pressed = CookRun.Repeat(game, last, isSpoilingFirst: true, isChinese: false);

        Assert.True(pressed.IsDone);
        Assert.Equal(new long[] { 2, 3 }, InPot(game));
    }

    [Fact]
    public void Repeat_does_nothing_on_a_stove_that_has_not_cooked_yet()
    {
        var game = Stocked();
        var otherStove = new LastCooked(new Dictionary<long, int> { [StoveId + 1] = PorkRice }, null, null, false);

        Assert.Equal("Nothing has been cooked on this stove since the game started.", CookRun.Repeat(game, otherStove, true, false).Message);
        Assert.Empty(game.Pot);
    }

    [Fact]
    public void Top_up_fills_the_free_fuel_slots_with_the_fuel_the_stove_burns()
    {
        var game = new FakeGame { FuelSlots = 4, FuelUsed = 1, FuelStock = { S(50, Firewood, count: 5, owner: Chest) } };
        var pressed = CookRun.TopUp(game, false);

        Assert.Equal("Added 3 fuel.", pressed.Message);
        Assert.Equal(4, game.FuelUsed);
        Assert.Equal(2, game.FuelStock.Single().Count);
    }

    [Fact]
    public void Top_up_adds_fuel_while_the_stove_cooks()
    {
        var game = new FakeGame { IsCooking = true, FuelUsed = 3, FuelStock = { S(50, Firewood, count: 5) } };

        Assert.True(CookRun.TopUp(game, false).IsDone);
        Assert.Equal(4, game.FuelUsed);
    }

    [Fact]
    public void Top_up_waits_for_a_first_fuel_put_in_by_hand()
    {
        var game = new FakeGame { FuelStock = { S(50, Firewood, count: 5) } };

        game.FuelPreferred.Clear();

        Assert.Equal("Put one fuel in by hand first; topping up adds more of the same.", CookRun.TopUp(game, false).Message);
        Assert.Equal(1, game.FuelUsed);
    }

    [Fact]
    public void Top_up_reports_how_much_went_in_when_the_game_refuses_part_way()
    {
        var game = new FakeGame { FuelStock = { S(50, Firewood), S(51, Firewood) } };

        game.FuelStuck.Add(51);

        Assert.Equal("Added 1 fuel.", CookRun.TopUp(game, false).Message);
    }

    [Fact]
    public void Top_up_refuses_a_cooker_with_no_fire_and_full_slots()
    {
        Assert.Equal("This stove does not burn fuel.", CookRun.TopUp(new FakeGame { BurnsFuel = false }, false).Message);
        Assert.Equal("The fuel slots are full.", CookRun.TopUp(new FakeGame { FuelUsed = 4, FuelStock = { S(50, Firewood) } }, false).Message);
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
        Assert.Equal("锅已经满了。", CookRun.Add(new FakeGame { MaxFood = 0 }, Salt, true).Message);
    }

    [Fact]
    public void Load_leaves_food_set_aside_for_a_queued_dish_alone_and_says_so()
    {
        var game = new FakeGame { Stock = { S(1, Rice), S(2, Pork) } };
        var pressed = CookRun.Fill(game, PorkRice, true, false, CookQueue.Empty.Add(801, PorkRice));

        Assert.False(pressed.IsDone);
        Assert.Equal("The food for Pork Rice is set aside for queued dishes.", pressed.Message);
        Assert.Empty(game.Pot);
    }

    [Fact]
    public void Load_fills_from_the_food_no_queued_dish_holds()
    {
        var game = new FakeGame { Stock = { S(1, Rice, hours: 10), S(2, Rice, hours: 50), S(3, Pork), S(4, Pork) } };

        Assert.True(CookRun.Fill(game, PorkRice, true, false, CookQueue.Empty.Add(801, PorkRice)).IsDone);
        Assert.Equal(new long[] { 2, 4 }, InPot(game));
    }

    [Fact]
    public void Repeat_last_leaves_food_set_aside_for_a_queued_dish_alone()
    {
        var game = new FakeGame { Stock = { S(1, Rice), S(2, Pork) } };
        var last = new LastCooked(new Dictionary<long, int> { [StoveId] = PorkRice }, null, null, false);
        var pressed = CookRun.Repeat(game, last, true, false, CookQueue.Empty.Add(801, PorkRice));

        Assert.False(pressed.IsDone);
        Assert.Empty(game.Pot);
    }
}
