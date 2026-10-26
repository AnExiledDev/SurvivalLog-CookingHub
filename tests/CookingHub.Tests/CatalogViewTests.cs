using System.Text.Json;
using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

public class CatalogViewTests
{
    // Info Pack names a recipe's dish by its Normal result, falling back to Good, Perfect, then Fail, so a card tagged
    // with this id shows the same tooltip as the game's recipe row.
    [Theory]
    [InlineData(new[] { Bowl, FancyBowl, Bowl, Bowl }, FancyBowl)]
    [InlineData(new[] { Bowl, 0, FancyBowl, Bowl }, FancyBowl)]
    [InlineData(new[] { FancyBowl, 0, 0, 0 }, FancyBowl)]
    [InlineData(new[] { 0, 0, 0, 0 }, 0)]
    public void A_recipe_names_the_dish_info_pack_names_it_for(int[] outputs, int expected)
    {
        var book = Book with { Recipes = new Dictionary<int, Recipe> { [PorkRice] = R(PorkRice, "Pork Rice", outputs: outputs) } };

        using var json = JsonDocument.Parse(CatalogView.Write(book));

        Assert.Equal(expected, json.RootElement.GetProperty("recipes")[0].GetProperty("dish").GetInt32());
    }

    // The page names food only (the pantry, what a dish is missing, a seasoning to add), so the thousands of dishes and
    // other items the game knows stay out of the catalog; a recipe that asks for one by name keeps it.
    [Fact]
    public void The_catalog_carries_food_and_items_a_recipe_names_and_leaves_out_the_dishes()
    {
        var book = Book with
        {
            Recipes = new Dictionary<int, Recipe>(Book.Recipes) { [Secret] = R(Secret, "Secret", specific: new[] { Rice, Firewood }) },
        };

        using var json = JsonDocument.Parse(CatalogView.Write(book));

        var ids = json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt32()).ToList();

        Assert.Equal(new[] { Rice, Wheat, Pork, Salt, Pepper, Firewood }, ids);
    }
}
