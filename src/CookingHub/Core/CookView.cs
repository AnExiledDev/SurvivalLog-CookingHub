using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CookingHub.Core;

/// <summary>The settings the window shows and changes: the panel key, which food a fill uses first, and whether odds show on the cooking screen.</summary>
public sealed record ViewSettings(string Key, bool IsSpoilingFirst, bool IsShowingOdds);

/// <summary>
/// The JSON the window draws: the cooking level, the open stove, every dish the player can see with what it needs, and
/// the pantry. Names and icons live in the catalog, sent once (CatalogView).
/// </summary>
public static class CookView
{
    public static string NotReady(string note, ViewSettings settings) => Json.Object(json =>
    {
        json.WriteBoolean("ready", false);
        json.WriteString("note", note);
        WriteSettings(json, settings);
    });

    public static string Ready(Kitchen kitchen, ViewSettings settings) => Json.Object(json =>
    {
        json.WriteBoolean("ready", true);
        json.WriteNumber("level", kitchen.Level);
        json.WriteNumber("floor", kitchen.Book.Rules.Floor(kitchen.Level));
        WriteStove(json, kitchen.Stove);
        json.WriteStartArray("dishes");

        foreach (var dish in Finder.For(kitchen)) WriteDish(json, dish);

        json.WriteEndArray();
        json.WriteStartArray("pantry");

        foreach (var shelf in Pantry.For(kitchen)) WriteShelf(json, shelf);

        json.WriteEndArray();
        json.WriteNumber("spoiling", Pantry.SpoilingSoon(kitchen, Finder.SpoilingHours));
        WriteSettings(json, settings);
    });

    /// <summary>
    /// What page.js draws on the game's own cooking screen: the odds of each forecast line, keyed by recipe id so the page
    /// can check it is drawing on the lines C# read, and the nudges. Recipe names are never sent, so a recipe the player has
    /// not learned stays hidden.
    /// </summary>
    public static string Overlay(Kitchen kitchen, Stove stove, bool isShowingOdds) => Json.Object(json =>
    {
        var advice = Advice.For(kitchen, stove);

        json.WriteBoolean("show", isShowingOdds);
        json.WriteBoolean("cooking", stove.IsCooking);
        json.WriteBoolean("potEmpty", stove.PotCount == 0);
        json.WriteStartArray("ids");

        foreach (var line in advice.Lines) json.WriteNumberValue(line.RecipeId);

        json.WriteEndArray();
        json.WriteStartArray("lines");

        foreach (var line in advice.Lines) WriteOdds(json, line.Odds);

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

    private static void WriteStove(Utf8JsonWriter json, Stove? stove)
    {
        if (stove == null)
        {
            json.WriteNull("stove");

            return;
        }

        json.WriteStartObject("stove");
        json.WriteNumber("max", stove.MaxFood);
        json.WriteNumber("room", stove.Room);
        json.WriteBoolean("cooking", stove.IsCooking);
        json.WriteBoolean("potEmpty", stove.PotCount == 0);
        json.WriteNumber("bonus", stove.QualityBonus);
        json.WriteStartArray("allowed");

        foreach (var id in stove.Allowed?.OrderBy(id => id) ?? Enumerable.Empty<int>()) json.WriteNumberValue(id);

        json.WriteEndArray();
        json.WriteBoolean("takesAll", stove.Allowed == null);
        json.WriteEndObject();
    }

    private static void WriteDish(Utf8JsonWriter json, Dish dish)
    {
        json.WriteStartObject();
        json.WriteNumber("id", dish.Recipe.Id);
        json.WriteBoolean("varies", dish.Varies);
        json.WriteString("state", StateName(dish.State));
        json.WriteNumber("size", dish.Needs.Sum(need => need.Want));
        json.WriteNumber("craftLevel", dish.Recipe.CraftLevel);
        json.WriteBoolean("isNew", dish.IsNew);
        json.WriteBoolean("spoiling", dish.UsesSpoiling);

        if (dish.Perfect is { } perfect) json.WriteNumber("perfect", Percent(perfect));
        else json.WriteNull("perfect");

        json.WriteStartArray("needs");

        foreach (var need in dish.Needs)
        {
            json.WriteStartObject();
            json.WriteNumber("id", need.Id);
            json.WriteBoolean("type", need.IsType);
            json.WriteNumber("want", need.Want);
            json.WriteNumber("have", need.Have);
            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
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
        json.WriteStartArray("chances");

        foreach (var chance in odds.Chances) json.WriteNumberValue(Percent(chance));

        json.WriteEndArray();
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

    private static void WriteSettings(Utf8JsonWriter json, ViewSettings settings)
    {
        json.WriteStartObject("settings");
        json.WriteString("key", settings.Key);
        json.WriteBoolean("spoilFirst", settings.IsSpoilingFirst);
        json.WriteBoolean("odds", settings.IsShowingOdds);
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
/// The catalog as the window needs it, sent once per game and language: food types, the items recipes use or make, and
/// recipe names with the icon of their best dish.
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

        foreach (var item in book.Items.Values.OrderBy(item => item.Id))
        {
            json.WriteStartObject();
            json.WriteNumber("id", item.Id);
            json.WriteString("name", item.Name);
            json.WriteString("icon", item.Icon);
            json.WriteNumber("type", item.Type);
            json.WriteBoolean("season", item.Type == QualityRules.SeasoningType);
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
            json.WriteEndObject();
        }

        json.WriteEndArray();
    });

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
