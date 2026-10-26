using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CookingHub.Core;

/// <summary>
/// What the session keeps for the open stove: the dish it last started (null when none this game start), the start
/// token, which counts up once per filled pot that should also start the stove (0 for none), its queued dishes, and the
/// food set aside for every stove's queue (null when nothing is queued anywhere).
/// </summary>
public sealed record StationExtras(int? Last, int Start, IReadOnlyList<int> Queue, Reservation? Reserved = null)
{
    public static readonly StationExtras None = new(null, 0, Array.Empty<int>());
}

/// <summary>
/// The JSON page.js draws on the game's own cooking screen. Names and icons live in the catalog, sent once (CatalogView).
/// </summary>
public static class CookView
{
    /// <summary>
    /// What page.js draws on the game's own cooking screen: the stove line, each recipe book row's food and odds, the
    /// pantry and Repeat last, and the odds of each forecast line, keyed by recipe id so the page can check it
    /// is drawing on the lines C# read, with the nudges. The book lists only recipes the game itself shows, unless
    /// isShowingUnlearned asks for the rest too, each marked unlearned with the cooking level the tables show it at.
    /// </summary>
    public static string Overlay(Kitchen kitchen, Stove stove, bool isShowingOdds, StationExtras? extras = null, bool isShowingUnlearned = false) => Json.Object(json =>
    {
        var advice = Advice.For(kitchen, stove);
        var station = extras ?? StationExtras.None;

        json.WriteBoolean("show", true);
        json.WriteBoolean("odds", isShowingOdds);
        json.WriteBoolean("unlearned", isShowingUnlearned);
        json.WriteBoolean("cooking", stove.IsCooking);
        json.WriteBoolean("potEmpty", stove.PotCount == 0);
        json.WriteNumber("level", kitchen.Level);
        json.WriteNumber("floor", kitchen.Book.Rules.Floor(kitchen.Level));
        WriteStation(json, kitchen, stove);
        json.WriteStartArray("book");

        foreach (var dish in Finder.ForStove(kitchen, stove, isShowingUnlearned)) WriteBookRow(json, kitchen, stove, dish, station.Reserved);

        json.WriteEndArray();
        json.WriteStartArray("pantry");

        foreach (var shelf in Pantry.For(kitchen)) WriteShelf(json, shelf);

        json.WriteEndArray();

        if (station.Last is { } last) json.WriteNumber("last", last);
        else json.WriteNull("last");

        json.WriteNumber("start", station.Start);
        WriteIds(json, "queue", station.Queue);
        WriteQueueCovered(json, stove, station);
        json.WriteBoolean("queueFull", station.Queue.Count >= CookQueue.Limit);
        WriteIds(json, "ids", advice.Lines.Select(line => line.RecipeId));
        json.WriteStartArray("lines");

        foreach (var line in advice.Lines) WriteOdds(json, line.Odds);

        json.WriteEndArray();
        json.WriteStartArray("lineGives");

        foreach (var line in advice.Lines)
        {
            if (kitchen.Book.Recipes.TryGetValue(line.RecipeId, out var recipe)) WriteIds(json, null, DishGives.Expected(kitchen.Book, recipe, line.Odds?.Chances));
            else json.WriteNullValue();
        }

        json.WriteEndArray();
        json.WriteStartArray("nudges");

        foreach (var nudge in advice.Nudges)
        {
            json.WriteStartObject();
            json.WriteString("kind", nudge.Kind == NudgeKind.Season ? "season" : "swap");
            json.WriteNumber("item", nudge.ItemId);

            if (nudge.PerfectGain is { } gain) json.WriteNumber("gain", Percent(gain));
            else json.WriteNull("gain");

            json.WriteEndObject();
        }

        json.WriteEndArray();
    });

    /// <summary>The overlay when no cooking panel is open, so the page clears what it drew.</summary>
    public static string NoOverlay() => Json.Object(json => json.WriteBoolean("show", false));

    /// <summary>
    /// The open stove's name, speed and quality bonus, how long the forecast dish takes on it, and its fuel: free slots,
    /// total slots, how many units of its fuel the player has, and whether it has burned any yet (known), since a top up
    /// only adds kinds it already burns.
    /// </summary>
    private static void WriteStation(Utf8JsonWriter json, Kitchen kitchen, Stove stove)
    {
        var main = stove.Forecasts.FirstOrDefault(forecast => forecast.RecipeId != Recipe.DarkFood);
        var recipe = main != null && kitchen.Book.Recipes.TryGetValue(main.RecipeId, out var found) ? found : null;

        json.WriteStartObject("stove");
        json.WriteString("name", stove.Name);
        json.WriteNumber("speed", Math.Round(stove.SpeedRate, 2));
        json.WriteNumber("bonus", stove.QualityBonus);
        json.WriteBoolean("hotPot", stove.IsHotPot);

        if (recipe != null) json.WriteNumber("cookSecs", Math.Round(stove.CookSeconds(recipe)));
        else json.WriteNull("cookSecs");

        if (stove.Fuel is { } fuel)
        {
            json.WriteStartObject("fuel");
            json.WriteNumber("free", fuel.Free);
            json.WriteNumber("slots", fuel.Slots);
            json.WriteNumber("have", fuel.Have);
            json.WriteBoolean("known", fuel.Preferred.Count > 0);
            json.WriteEndObject();
        }
        else
        {
            json.WriteNull("fuel");
        }

        json.WriteEndObject();
    }

    /// <summary>
    /// One recipe book row: whether the player's food covers it, where that food is, what is missing, the soonest any
    /// of it spoils, and the odds of cooking it fresh on this stove (null while locked).
    /// </summary>
    private static void WriteBookRow(Utf8JsonWriter json, Kitchen kitchen, Stove stove, Dish dish, Reservation? reserved)
    {
        var sources = Finder.Sources(kitchen, dish);
        var soonest = sources.Select(stack => stack.HoursLeft).Where(hours => hours != null).Min();
        var odds = dish.State == Readiness.Locked ? null : Finder.PlainOdds(kitchen, dish.Recipe, stove.QualityBonus);

        json.WriteStartObject();
        json.WriteNumber("id", dish.Recipe.Id);
        json.WriteString("state", StateName(dish.State));
        json.WriteBoolean("isNew", dish.IsNew);
        json.WriteBoolean("setAside", dish.State == Readiness.Ready && reserved != null && !reserved.Covers(dish.Recipe));

        if (dish.IsUnlearned)
        {
            json.WriteBoolean("unlearned", true);
            json.WriteNumber("showLevel", dish.Recipe.ShowLevel);
        }

        if (odds != null) WriteChances(json, odds);
        else json.WriteNull("chances");

        WriteIds(json, "gives", DishGives.Expected(kitchen.Book, dish.Recipe, odds?.Chances));
        json.WriteStartArray("places");

        foreach (var place in sources.Select(stack => stack.Place).Distinct().OrderBy(place => place, StringComparer.Ordinal)) json.WriteStringValue(place);

        json.WriteEndArray();
        json.WriteStartArray("missing");

        foreach (var need in dish.Needs.Where(need => need.Missing > 0))
        {
            json.WriteStartObject();
            json.WriteNumber("id", need.Id);
            json.WriteBoolean("type", need.IsType);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        if (soonest is { } hours) json.WriteNumber("hours", Math.Round(Math.Max(0, hours), 1));
        else json.WriteNull("hours");

        json.WriteEndObject();
    }

    /// <summary>For each dish queued on this stove, whether food is set aside for it; false means it waits for more food.</summary>
    private static void WriteQueueCovered(Utf8JsonWriter json, Stove stove, StationExtras station)
    {
        json.WriteStartArray("queueCovered");

        for (var index = 0; index < station.Queue.Count; index++) json.WriteBooleanValue(station.Reserved is not { } reserved || reserved.For(stove.FurnitureId, index) != null);

        json.WriteEndArray();
    }

    /// <summary>An array of numbers, named, or unnamed when it is a value inside another array.</summary>
    private static void WriteIds(Utf8JsonWriter json, string? name, IEnumerable<int> ids)
    {
        if (name == null) json.WriteStartArray();
        else json.WriteStartArray(name);

        foreach (var id in ids) json.WriteNumberValue(id);

        json.WriteEndArray();
    }

    private static void WriteChances(Utf8JsonWriter json, Odds odds)
    {
        json.WriteStartArray("chances");

        foreach (var chance in odds.Chances) json.WriteNumberValue(Percent(chance));

        json.WriteEndArray();
    }

    private static void WriteShelf(Utf8JsonWriter json, Shelf shelf)
    {
        json.WriteStartObject();
        json.WriteNumber("item", shelf.ItemId);
        json.WriteNumber("count", shelf.Count);
        json.WriteNumber("spoiled", shelf.Spoiled);

        if (shelf.SoonestHours is { } hours) json.WriteNumber("hours", Math.Round(Math.Max(0, hours), 1));
        else json.WriteNull("hours");

        json.WriteStartArray("places");

        foreach (var place in shelf.Places) json.WriteStringValue(place);

        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static void WriteOdds(Utf8JsonWriter json, Odds? odds)
    {
        if (odds == null)
        {
            json.WriteNullValue();

            return;
        }

        json.WriteStartObject();
        json.WriteNumber("floor", odds.Floor);
        WriteChances(json, odds);
        json.WriteStartArray("parts");

        foreach (var part in odds.Parts)
        {
            json.WriteStartObject();
            json.WriteString("kind", PartName(part.Kind));
            json.WriteNumber("value", part.Value);
            json.WriteNumber("count", part.Count);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteNumber("lowest", odds.Lowest);
        json.WriteEndObject();
    }

    /// <summary>A chance as a whole percent, rounded half away from zero, the way a player reads "33%".</summary>
    private static int Percent(double chance) => (int)Math.Round(chance * 100, MidpointRounding.AwayFromZero);

    private static string StateName(Readiness state) => state switch
    {
        Readiness.Ready => "ready",
        Readiness.MissingOne => "one",
        Readiness.Short => "short",
        Readiness.Locked => "locked",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown readiness"),
    };

    private static string PartName(PartKind kind) => kind switch
    {
        PartKind.Fresh => "fresh",
        PartKind.Rotten => "rotten",
        PartKind.Seasoning => "season",
        PartKind.Exact => "exact",
        PartKind.Stove => "stove",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "unknown part"),
    };
}

/// <summary>
/// The catalog as the window needs it, sent once per game and language: food types, the items a pot takes or a recipe
/// names, and recipe names with the icon of their best dish. The page names only food (the pantry, a recipe's missing
/// food, a nudge), so the two thousand dishes recipes make stay out of it; a recipe's dish icon is written on the recipe.
/// </summary>
public static class CatalogView
{
    public static string Write(Cookbook book) => Json.Object(json =>
    {
        json.WriteStartArray("types");

        foreach (var type in book.Types.OrderBy(type => type.Id))
        {
            json.WriteStartObject();
            json.WriteNumber("id", type.Id);
            json.WriteString("name", type.Name);
            json.WriteString("icon", type.Icon);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("items");

        var named = new HashSet<int>(book.Recipes.Values.SelectMany(recipe => recipe.Specific));

        foreach (var item in book.Items.Values.Where(item => item.CanCook || named.Contains(item.Id)).OrderBy(item => item.Id))
        {
            json.WriteStartObject();
            json.WriteNumber("id", item.Id);
            json.WriteString("name", item.Name);
            json.WriteString("icon", item.Icon);
            json.WriteNumber("type", item.Type);
            json.WriteBoolean("season", item.Type == QualityRules.SeasoningType);

            if (item.Gives != null)
            {
                json.WriteStartArray("gives");

                foreach (var value in item.Gives) json.WriteNumberValue(value);

                json.WriteEndArray();
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteStartArray("recipes");

        foreach (var recipe in book.Recipes.Values.OrderBy(recipe => recipe.Id))
        {
            json.WriteStartObject();
            json.WriteNumber("id", recipe.Id);
            json.WriteString("name", recipe.Name);
            json.WriteString("icon", DishIcon(book, recipe));
            json.WriteNumber("dish", NamedDish(recipe));
            json.WriteEndObject();
        }

        json.WriteEndArray();
    });

    /// <summary>
    /// The dish item a recipe is named for: its Normal dish, else Good, Perfect, then Fail; 0 for none. Info Pack picks
    /// a recipe's dish the same way, so a row tagged with it shows the same tooltip as the book's row.
    /// </summary>
    private static int NamedDish(Recipe recipe) =>
        new[] { 1, 2, 3, 0 }.Where(tier => tier < recipe.Outputs.Count).Select(tier => recipe.Outputs[tier]).FirstOrDefault(id => id > 0);

    /// <summary>The picture of the best dish the recipe makes that has one.</summary>
    private static string? DishIcon(Cookbook book, Recipe recipe) =>
        recipe.Outputs.Reverse().Select(id => book.Items.TryGetValue(id, out var item) ? item.Icon : null).FirstOrDefault(icon => icon != null);
}

/// <summary>Writes one JSON object to a string.</summary>
internal static class Json
{
    public static string Object(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();

        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            body(json);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
