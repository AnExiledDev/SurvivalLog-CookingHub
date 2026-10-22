using System.Collections.Generic;
using System.Text.Json;

namespace CookingHub.Core;

/// <summary>
/// Reads the cooking panel's dish forecast (State_Web_Cooking.PredictionListJson): a JSON list of
/// {RecipeId, Name, Tier, Level, IsExact, Icon}, one per dish the pot would make. Level 2 is a recipe the player knows.
/// A RecipeId of 0 is a message line, not a dish. Anything unreadable reads as no forecast.
/// </summary>
public static class Forecasts
{
    private const int KnownLevel = 2;

    public static IReadOnlyList<Forecast> Parse(string? json)
    {
        var forecasts = new List<Forecast>();

        if (string.IsNullOrWhiteSpace(json)) return forecasts;

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Array) return forecasts;

            foreach (var entry in document.RootElement.EnumerateArray()) forecasts.Add(Read(entry));
        }
        catch (JsonException)
        {
            forecasts.Clear();
        }

        return forecasts;
    }

    private static Forecast Read(JsonElement entry) =>
        entry.ValueKind != JsonValueKind.Object
            ? new Forecast(0, false, false)
            : new Forecast(Number(entry, "RecipeId"), Flag(entry, "IsExact"), Number(entry, "Level") >= KnownLevel);

    private static int Number(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : 0;

    private static bool Flag(JsonElement entry, string name) => entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
