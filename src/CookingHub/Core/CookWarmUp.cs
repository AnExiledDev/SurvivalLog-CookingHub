using System;
using System.Collections.Generic;

namespace CookingHub.Core;

/// <summary>What one warm-up pass worked out, so a test can tell it really drew the screen, kept rows and planned a pot.</summary>
public readonly record struct WarmUpResult(int BookRows, int KeptRows, int PlannedTakes, int Shelves, int Forecasts, bool HasCommand);

/// <summary>
/// Does once, in a made-up kitchen, the work the first cooking screen of a session does: the recipe book with its odds
/// and food, the patch the page gets, the catalog, the food set aside for the queue and a pot planned for a dish. The
/// code it runs, and the dictionaries, sorts and JSON it builds, are then compiled before the player opens a stove,
/// instead of on the game's frame. Plain .NET only, so it can run on a worker at load.
/// </summary>
public static class CookWarmUp
{
    private const int Grain = 1;
    private const int Meat = 2;
    private const int Rice = 10;
    private const int Pork = 20;
    private const int Salt = 30;
    private const int Firewood = 40;
    private const int Bowl = 900;
    private const int PorkRice = 1;
    private const int StirFry = 2;
    private const long Bag = 500;
    private const long Chest = 501;
    private const long Workbench = 600;
    private const long StoveId = 700;

    public static WarmUpResult Run()
    {
        var book = new Cookbook(
            new Dictionary<int, Item>
            {
                [Rice] = new(Rice, "Rice", null, Grain, 5, true, new[] { 6, 0, 1, 0, 0 }),
                [Pork] = new(Pork, "Pork", null, Meat, 2, true),
                [Salt] = new(Salt, "Salt", null, QualityRules.SeasoningType, 0, true),
                [Firewood] = new(Firewood, "Firewood", null, 0, 0, false),
                [Bowl] = new(Bowl, "Bowl", null, 0, 1, false, new[] { 10, 1, 2, 0, 0 }),
            },
            new Dictionary<int, Recipe>
            {
                [PorkRice] = new(PorkRice, "Pork Rice", new[] { Rice, Pork }, Array.Empty<int>(), 2, new[] { 0, 30, 60, 90 }, 0, 1, 60, new[] { Bowl, Bowl, Bowl, Bowl }),
                [StirFry] = new(StirFry, "Stir Fry", Array.Empty<int>(), new[] { Grain, Meat }, 2, new[] { 0, 30, 60, 90 }, 1, 1, 60, new[] { Bowl, Bowl, Bowl, Bowl }),
            },
            new[] { new FoodType(Grain, "Grain", null), new FoodType(Meat, "Meat", null), new FoodType(QualityRules.SeasoningType, "Seasoning", null) },
            QualityRules.Shipped);
        // A queued dish takes whole stacks, so there are two of each food: one set aside for the queue, one left to plan with.
        var stock = new[]
        {
            new Stack(1, Rice, 1, Bag, "Bag", 10, false),
            new Stack(2, Rice, 1, Bag, "Bag", 0, true),
            new Stack(3, Pork, 1, Chest, "Chest", 30, false),
            new Stack(4, Salt, 2, Bag, "Bag", null, false),
            new Stack(7, Rice, 1, Chest, "Chest", 20, false),
            new Stack(8, Pork, 1, Bag, "Bag", 40, false),
        };
        var fuel = new Fuel(4, 1, new[] { Firewood }, new[] { new Stack(5, Firewood, 6, Chest, "Chest", null, false) });
        var stove = new Stove(StoveId, Workbench, Bag, 6, false, false, 0, null, new[] { new Stack(6, Salt, 1, Workbench, "Pot", null, false) },
            Forecasts.Parse("[{\"RecipeId\":1,\"IsExact\":true,\"Level\":2}]"), "Stove", 1f, 0, fuel);
        var kitchen = new Kitchen(book, 2, new[] { PorkRice }, new[] { PorkRice }, stock, stove);
        var queue = CookQueue.Empty.Add(StoveId, StirFry);
        var reserved = Reservation.Of(kitchen, queue, true, false);
        var delta = new OverlayDelta();
        var first = delta.Next(CookView.Overlay(kitchen, stove, true, new StationExtras(PorkRice, 1, queue.For(StoveId), reserved)))!;

        delta.Sent(first);

        // The next overlay differs only in its start token, so every book row goes as its id.
        var next = delta.Next(CookView.Overlay(kitchen, stove, true, new StationExtras(PorkRice, 2, queue.For(StoveId), reserved), isShowingUnlearned: true));

        CatalogView.Write(book);
        OverlayPrint.Of(kitchen, true, true, queue, PorkRice, 1);
        Finder.For(kitchen);
        Advice.For(kitchen, stove);

        var takes = CookRun.Plan(reserved.Left, book.Recipes[PorkRice], true);
        var command = PageCommand.Parse(PageCommand.Prefix + "fill|1");

        return new WarmUpResult(first.Rows.Count, next == null ? 0 : CountKept(next.Json), takes?.Count ?? 0, Pantry.For(kitchen).Count, stove.Forecasts.Count, command != null);
    }

    /// <summary>The book rows that went as their id alone.</summary>
    private static int CountKept(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);

        var kept = 0;

        foreach (var row in document.RootElement.GetProperty("book").EnumerateArray())
        {
            if (row.ValueKind == System.Text.Json.JsonValueKind.Number) kept++;
        }

        return kept;
    }
}
