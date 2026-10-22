using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CookingHub.Core;

/// <summary>
/// Dishes the player queued from the window, oldest first, shared by every stove in the base. A stove's Load next fills
/// the first one it can cook. Kept as a line of recipe ids so it survives a game restart.
/// </summary>
public sealed record CookQueue(IReadOnlyList<int> Ids)
{
    public const int Limit = 20;

    public static readonly CookQueue Empty = new(new List<int>());

    public bool IsFull => Ids.Count >= Limit;

    public CookQueue Add(int recipeId) => IsFull ? this : new CookQueue(Ids.Append(recipeId).ToList());

    /// <summary>Removes the first entry for the recipe; the queue is unchanged when it holds none.</summary>
    public CookQueue Remove(int recipeId)
    {
        var index = Ids.ToList().IndexOf(recipeId);

        return index < 0 ? this : new CookQueue(Ids.Where((_, i) => i != index).ToList());
    }

    /// <summary>The queued recipes this stove can cook, in queue order.</summary>
    public IReadOnlyList<int> For(Stove stove) => Ids.Where(stove.Takes).ToList();

    /// <summary>Queue presses from the window: the queue after the press, and the line shown for it.</summary>
    public (CookQueue Queue, Pressed Pressed) Press(Cookbook book, int recipeId, bool isAdding, bool isChinese)
    {
        if (!book.Recipes.TryGetValue(recipeId, out var recipe)) return (this, new Pressed(false, Texts.NoRecipe.Pick(isChinese)));
        if (isAdding && IsFull) return (this, new Pressed(false, Texts.QueueFull.Pick(isChinese)));

        return isAdding
            ? (Add(recipeId), new Pressed(true, Texts.Queued.Say(isChinese, recipe.Name)))
            : (Remove(recipeId), new Pressed(true, Texts.Unqueued.Say(isChinese, recipe.Name)));
    }

    public string Write() => string.Join(",", Ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Reads what Write wrote; anything that is not a recipe id is dropped.</summary>
    public static CookQueue Read(string? text)
    {
        var ids = (text ?? "")
            .Split(',')
            .Select(part => int.TryParse(part.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : 0)
            .Where(id => id > 0)
            .Take(Limit)
            .ToList();

        return new CookQueue(ids);
    }
}
