using System.Globalization;

namespace CookingHub.Core;

/// <summary>A message from the page. Everything else the page could send is ignored.</summary>
public abstract record PageCommand
{
    public const string Prefix = "slmod|cookinghub|";

    private PageCommand()
    {
    }

    public sealed record Ready : PageCommand;

    public sealed record NoScript : PageCommand;

    /// <summary>A diagnostic line from the page script, for the log.</summary>
    public sealed record Note(string Text) : PageCommand;

    /// <summary>A text box in the page gained or lost focus, so the game's keys should pause or resume.</summary>
    public sealed record Focus(bool IsTyping) : PageCommand;

    /// <summary>Fill the open stove's empty pot with what the recipe needs.</summary>
    public sealed record Fill(int RecipeId) : PageCommand;

    /// <summary>Add one fresh stack of a food (a seasoning, or a pantry pick) to the open stove's pot.</summary>
    public sealed record Add(int ItemId) : PageCommand;

    /// <summary>Fill the open stove's empty pot for the dish it cooked last.</summary>
    public sealed record Repeat : PageCommand;

    /// <summary>Fill the open stove's free fuel slots from the bag and storage.</summary>
    public sealed record TopUp : PageCommand;

    /// <summary>Swap the spoiled units of an item in the open stove's pot for fresh ones.</summary>
    public sealed record Swap(int ItemId) : PageCommand;

    public static bool IsOurs(string? message) => message != null && message.StartsWith(Prefix, System.StringComparison.Ordinal);

    public static PageCommand? Parse(string? message)
    {
        if (message == null || !IsOurs(message)) return null;

        var body = message.Substring(Prefix.Length);

        if (body.StartsWith("diag|", System.StringComparison.Ordinal)) return new Note(body.Substring("diag|".Length));

        var parts = body.Split('|');

        return (parts[0], parts.Length) switch
        {
            ("ready", 1) => new Ready(),
            ("noscript", 1) => new NoScript(),
            ("focus", 2) when IsSwitch(parts[1]) => new Focus(parts[1] == "1"),
            ("fill", 2) when TryId(parts[1], out var recipeId) => new Fill(recipeId),
            ("add" or "season", 2) when TryId(parts[1], out var itemId) => new Add(itemId),
            ("repeat", 1) => new Repeat(),
            ("topup", 1) => new TopUp(),
            ("swap", 2) when TryId(parts[1], out var itemId) => new Swap(itemId),
            _ => null,
        };
    }

    private static bool IsSwitch(string text) => text is "0" or "1";

    private static bool TryId(string text, out int id) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;
}
