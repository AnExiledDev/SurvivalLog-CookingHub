using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>"What can I cook": every recipe the player can see, checked against their fresh food.</summary>
public class FinderTests
{
    private static FakeGame Stocked() => new()
    {
        Stock = { S(1, Rice, hours: 10), S(2, Pork, hours: 40, owner: Chest) },
    };

    private static Dish DishOf(FakeGame game, int recipeId) => Finder.For(game.Kitchen()).Single(dish => dish.Recipe.Id == recipeId);

    [Fact]
    public void A_dish_with_everything_in_the_bag_or_storage_is_ready()
    {
        var dish = DishOf(Stocked(), PorkRice);

        Assert.Equal(Readiness.Ready, dish.State);
        Assert.Equal(31 / 76.0, dish.Perfect ?? -1, 6);
        Assert.True(dish.IsNew);
    }

    [Fact]
    public void Spoiled_food_does_not_count_toward_a_dish()
    {
        var game = Stocked();

        game.Stock.RemoveAll(stack => stack.ItemId == Pork);
        game.Stock.Add(S(3, Pork, 5, isSpoiled: true));

        var dish = DishOf(game, PorkRice);

        Assert.Equal(Readiness.MissingOne, dish.State);
        Assert.Null(dish.Perfect);
        Assert.Equal(0, dish.Needs.Single(need => need.Id == Pork).Have);
    }

    [Fact]
    public void Tag_recipes_wanting_the_same_food_types_are_one_dish_that_varies()
    {
        var dishes = Finder.For(Stocked().Kitchen());
        var stirFry = dishes.Single(dish => dish.Recipe.Id is StirFry or FancyStirFry);

        Assert.Equal(StirFry, stirFry.Recipe.Id);
        Assert.True(stirFry.Varies);
        Assert.All(stirFry.Needs, need => Assert.True(need.IsType));
        Assert.Equal(Readiness.Ready, stirFry.State);
    }

    [Fact]
    public void A_recipe_above_the_players_level_is_locked_and_listed_last()
    {
        var dishes = Finder.For(Stocked().Kitchen());

        Assert.Equal(Feast, dishes[^1].Recipe.Id);
        Assert.Equal(Readiness.Locked, dishes[^1].State);
    }

    [Fact]
    public void A_book_recipe_is_locked_until_it_is_learned()
    {
        var game = Stocked();

        game.CraftUnlocked.Clear();

        Assert.Equal(Readiness.Locked, DishOf(game, PorkRice).State);
    }

    [Fact]
    public void Hidden_recipes_and_dark_food_are_not_listed()
    {
        var ids = Finder.For(Stocked().Kitchen()).Select(dish => dish.Recipe.Id).ToList();

        Assert.DoesNotContain(Secret, ids);
        Assert.DoesNotContain(Recipe.DarkFood, ids);
    }

    // Family Bucket shows from level 5, but a player who learned it earlier sees it in the game's own book.
    [Fact]
    public void A_recipe_the_player_already_knows_is_listed_before_its_show_level()
    {
        var game = Stocked();

        game.Cooked.Add(Secret);

        Assert.Contains(Secret, Finder.For(game.Kitchen()).Select(dish => dish.Recipe.Id));
    }

    [Fact]
    public void A_dish_that_uses_up_food_spoiling_soon_comes_before_one_that_does_not()
    {
        var game = new FakeGame { Stock = { S(1, Wheat, hours: 100), S(2, Pork, hours: 100), S(3, Rice, hours: 100) } };

        game.Stock.Add(S(4, Wheat, hours: 5));

        var ready = Finder.For(game.Kitchen()).Where(dish => dish.State == Readiness.Ready).Select(dish => dish.Recipe.Id).ToList();

        Assert.Equal(new[] { StirFry, PorkRice }, ready);
    }

    [Fact]
    public void A_cooked_dish_is_not_new()
    {
        var game = Stocked();

        game.Cooked.Add(PorkRice);

        Assert.False(DishOf(game, PorkRice).IsNew);
    }
}
