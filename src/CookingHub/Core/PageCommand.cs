using System.Globalization;

namespace CookingHub.Core;

/// <summary>A message from the page. Everything else the page could send is ignored.</summary>
public abstract record PageCommand
{
    public const string Prefix = "slmod|cookinghub|";

    private PageCommand()
    {
    }

    public sealed record Toggle : PageCommand;

    public sealed record Close : PageCommand;

    public sealed record Ready : PageCommand;

    public sealed record NoScript : PageCommand;

    /// <summary>A diagnostic line from the page script, for the log.</summary>
    public sealed record Note(string Text) : PageCommand;

    /// <summary>A text box in the page gained or lost focus, so the game's keys should pause or resume.</summary>
    public sealed record Focus(bool IsTyping) : PageCommand;

    /// <summary>Fill the open stove's empty pot with what the recipe needs.</summary>
    public sealed record Fill(int RecipeId) : PageCommand;

    /// <summary>Add one fresh unit of a seasoning to the open stove's pot.</summary>
    public sealed record Season(int ItemId) : PageCommand;

    /// <summary>Swap the spoiled units of an item in the open stove's pot for fresh ones.</summary>
    public sealed record Swap(int ItemId) : PageCommand;

    /// <summary>Whether filling a pot uses the food closest to spoiling first, or the freshest.</summary>
    public sealed record SpoilingFirst(bool IsOn) : PageCommand;

    /// <summary>Whether the odds show on the game's own cooking screen.</summary>
    public sealed record ShowOdds(bool IsOn) : PageCommand;

    public static bool IsOurs(string? message) => message != null && message.StartsWith(Prefix, System.StringComparison.Ordinal);

    public static PageCommand? Parse(string? message)
    {
        if (message == null || !IsOurs(message)) return null;

        var body = message.Substring(Prefix.Length);

        if (body.StartsWith("diag|", System.StringComparison.Ordinal)) return new Note(body.Substring("diag|".Length));

        var parts = body.Split('|');

        return (parts[0], parts.Length) switch
        {
            ("toggle", 1) => new Toggle(),
            ("close", 1) => new Close(),
            ("ready", 1) => new Ready(),
            ("noscript", 1) => new NoScript(),
            ("focus", 2) when IsSwitch(parts[1]) => new Focus(parts[1] == "1"),
            ("fill", 2) when TryId(parts[1], out var recipeId) => new Fill(recipeId),
            ("season", 2) when TryId(parts[1], out var itemId) => new Season(itemId),
            ("swap", 2) when TryId(parts[1], out var itemId) => new Swap(itemId),
            ("spoilfirst", 2) when IsSwitch(parts[1]) => new SpoilingFirst(parts[1] == "1"),
            ("odds", 2) when IsSwitch(parts[1]) => new ShowOdds(parts[1] == "1"),
            _ => null,
        };
    }

    private static bool IsSwitch(string text) => text is "0" or "1";

    private static bool TryId(string text, out int id) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}
