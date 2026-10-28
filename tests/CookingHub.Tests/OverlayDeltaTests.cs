using System.Text.Json;
using System.Text.Json.Nodes;
using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>
/// What goes to the page for an overlay: nothing when the page already has it, and a book row the page already has as
/// its recipe id alone, so only the rows that changed travel and get drawn again.
/// </summary>
public class OverlayDeltaTests
{
    private static string Overlay(FakeGame game)
    {
        var kitchen = game.Kitchen();

        return CookView.Overlay(kitchen, kitchen.Stove!, isShowingOdds: true);
    }

    private static FakeGame Stocked() => new()
    {
        Stock = { S(1, Rice, hours: 10), S(2, Pork, owner: Chest), S(3, Salt) },
    };

    /// <summary>The overlay with all of Stir Fry's food said to be in the cellar, every other row as it was.</summary>
    internal static string StirFryElsewhere(string overlay)
    {
        var root = JsonNode.Parse(overlay)!.AsObject();
        var row = root["book"]!.AsArray().Single(node => node is JsonObject dish && (int)dish["id"]! == StirFry)!.AsObject();

        row["places"] = new JsonArray("Cellar");
        row["missing"] = new JsonArray();

        return root.ToJsonString();
    }

    /// <summary>The book as sent: the ids that went alone, and the rows that went whole.</summary>
    private static (List<int> Kept, List<int> Whole) Book(string json)
    {
        using var document = JsonDocument.Parse(json);

        var kept = new List<int>();
        var whole = new List<int>();

        foreach (var row in document.RootElement.GetProperty("book").EnumerateArray())
        {
            if (row.ValueKind == JsonValueKind.Number) kept.Add(row.GetInt32());
            else whole.Add(row.GetProperty("id").GetInt32());
        }

        return (kept, whole);
    }

    [Fact]
    public void The_first_overlay_goes_whole()
    {
        var overlay = Overlay(Stocked());
        var patch = new OverlayDelta().Next(overlay)!;

        Assert.Empty(Book(patch.Json).Kept);
        Assert.Equal(JsonNode.Parse(overlay)!.ToJsonString(), JsonNode.Parse(patch.Json)!.ToJsonString());
    }

    [Fact]
    public void An_overlay_the_page_already_has_sends_nothing()
    {
        var delta = new OverlayDelta();
        var overlay = Overlay(Stocked());

        delta.Sent(delta.Next(overlay)!);

        Assert.Null(delta.Next(Overlay(Stocked())));
    }

    [Fact]
    public void Only_the_row_that_changed_goes_whole_and_the_rest_go_as_their_ids()
    {
        var delta = new OverlayDelta();
        var overlay = Overlay(Stocked());
        var first = delta.Next(overlay)!;

        delta.Sent(first);

        var (kept, whole) = Book(delta.Next(StirFryElsewhere(overlay))!.Json);

        Assert.Equal(new[] { StirFry }, whole);
        Assert.Contains(PorkRice, kept);
        Assert.Equal(Book(first.Json).Whole.Count - 1, kept.Count);
    }

    [Fact]
    public void Food_that_moves_resends_the_row_it_changes()
    {
        var delta = new OverlayDelta();
        var game = Stocked();

        delta.Sent(delta.Next(Overlay(game))!);
        game.MoveStack(2, Chest, Bag);

        var patch = delta.Next(Overlay(game))!;

        Assert.Contains(PorkRice, Book(patch.Json).Whole);
    }

    // A patch the page never took (the page was gone) is not what the page has, so the next one cannot lean on it.
    [Fact]
    public void A_patch_never_sent_leaves_the_next_one_whole()
    {
        var delta = new OverlayDelta();
        var overlay = Overlay(Stocked());

        delta.Next(overlay);

        Assert.Empty(Book(delta.Next(StirFryElsewhere(overlay))!.Json).Kept);
    }

    // Chinese text takes more than one byte a letter, so a cut made in the wrong unit lands inside a row after it.
    [Fact]
    public void Rows_after_chinese_text_are_cut_out_cleanly_and_the_rest_goes_as_it_was()
    {
        const string Cellar = "地窖";

        static string Overlay(string third) =>
            "{\"stove\":{\"name\":\"" + Cellar + "\"},\"book\":[{\"id\":1,\"places\":[\"" + Cellar + "\"]},{\"id\":2,\"places\":[]},{\"id\":3,\"places\":[\"" + third + "\"]}],\"pantry\":[\"" + Cellar + "\"]}";

        var delta = new OverlayDelta();

        delta.Sent(delta.Next(Overlay("Bag"))!);

        var patch = delta.Next(Overlay(Cellar))!;

        Assert.Equal("{\"stove\":{\"name\":\"" + Cellar + "\"},\"book\":[1,2,{\"id\":3,\"places\":[\"" + Cellar + "\"]}],\"pantry\":[\"" + Cellar + "\"]}", patch.Json);
    }

    [Fact]
    public void After_the_page_loads_afresh_the_same_overlay_goes_again_and_whole()
    {
        var delta = new OverlayDelta();
        var overlay = Overlay(Stocked());

        delta.Sent(delta.Next(overlay)!);
        delta.Forget();

        var patch = delta.Next(overlay);

        Assert.NotNull(patch);
        Assert.Empty(Book(patch!.Json).Kept);
    }
}
