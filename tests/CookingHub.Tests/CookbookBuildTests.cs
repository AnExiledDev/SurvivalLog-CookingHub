using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>
/// The cookbook is read a step at a time across frames. However it is sliced it must end with the book a read of the
/// whole tables makes, and it must not start on the items before the icon folder is read.
/// </summary>
public class CookbookBuildTests
{
    /// <summary>A food no recipe makes, that cannot be cooked: the book leaves it out.</summary>
    private const int Stone = 77;

    private sealed class Tables
    {
        public bool AreIconsIn = true;
        public int RecipeReads;
        public int ItemReads;
        public Cookbook? Built;

        public IEnumerable<bool> Steps() => CookbookBuild.Steps(
            () => AreIconsIn,
            () => Book.Recipes.Keys.ToList(),
            id =>
            {
                RecipeReads++;

                return Book.Recipes[id];
            },
            () => Book.Items.Keys.Append(Stone).ToList(),
            (id, outputs) =>
            {
                ItemReads++;

                return Book.Items.TryGetValue(id, out var item) && (item.CanCook || outputs.Contains(id)) ? item : null;
            },
            () => Book.Types,
            () => Book.Rules,
            book => Built = book);
    }

    [Fact]
    public void A_book_read_one_step_a_frame_matches_the_book_read_at_once()
    {
        var atOnce = new Tables();
        var sliced = new Tables();
        var whole = new SlicedJob(atOnce.Steps());
        var job = new SlicedJob(sliced.Steps());
        var frames = 0;

        Assert.True(whole.Advance(() => false));

        while (!job.Advance(() => true))
        {
            frames++;

            Assert.Null(sliced.Built);
        }

        Assert.NotNull(atOnce.Built);
        Assert.NotNull(sliced.Built);
        Assert.True(frames > Book.Items.Count, "each item should take a step of its own");
        Assert.Equal(CatalogView.Write(atOnce.Built!), CatalogView.Write(sliced.Built!));
        Assert.Equal(atOnce.Built!.Items.Keys.OrderBy(id => id), sliced.Built!.Items.Keys.OrderBy(id => id));
        Assert.Equal(atOnce.Built.Recipes.Keys.OrderBy(id => id), sliced.Built.Recipes.Keys.OrderBy(id => id));
    }

    [Fact]
    public void Dishes_a_recipe_makes_are_listed_and_a_row_no_recipe_uses_is_not()
    {
        var tables = new Tables();

        new SlicedJob(tables.Steps()).Advance(() => false);

        Assert.Contains(Bowl, tables.Built!.Items.Keys);
        Assert.Contains(FancyBowl, tables.Built.Items.Keys);
        Assert.DoesNotContain(Stone, tables.Built.Items.Keys);
        Assert.DoesNotContain(Firewood, tables.Built.Items.Keys);
    }

    [Fact]
    public void Nothing_is_read_until_the_icons_are_in()
    {
        var tables = new Tables { AreIconsIn = false };
        var job = new SlicedJob(tables.Steps());

        Assert.False(job.Advance(() => false));
        Assert.False(job.Advance(() => false));
        Assert.Equal(0, tables.RecipeReads);
        Assert.Equal(0, tables.ItemReads);

        tables.AreIconsIn = true;

        Assert.True(job.Advance(() => false));
        Assert.Equal(Book.Recipes.Count, tables.RecipeReads);
        Assert.Equal(Book.Items.Count + 1, tables.ItemReads);
        Assert.NotNull(tables.Built);
    }
}
