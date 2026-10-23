using System.Collections.Generic;
using System.Linq;

namespace CookingHub.Core;

/// <summary>
/// The dishes queued for each stove, oldest first, by the stove's furniture id. Kept for the loaded game only: a stove's
/// id means nothing in another save.
/// </summary>
public sealed record CookQueue(IReadOnlyDictionary<long, IReadOnlyList<int>> ByStove)
{
    public const int Limit = 20;

    public static readonly CookQueue Empty = new(new Dictionary<long, IReadOnlyList<int>>());

    public IReadOnlyList<int> For(long stoveId) => ByStove.TryGetValue(stoveId, out var ids) ? ids : new List<int>();

    public bool IsFull(long stoveId) => For(stoveId).Count >= Limit;

    public bool IsEmpty => ByStove.Values.All(ids => ids.Count == 0);

    public CookQueue Add(long stoveId, int recipeId) => IsFull(stoveId) ? this : With(stoveId, For(stoveId).Append(recipeId).ToList());

    /// <summary>Removes the first entry for the recipe; the queue is unchanged when the stove has none.</summary>
    public CookQueue Remove(long stoveId, int recipeId)
    {
        var ids = For(stoveId);
        var index = ids.ToList().IndexOf(recipeId);

        return index < 0 ? this : With(stoveId, ids.Where((_, i) => i != index).ToList());
    }

    /// <summary>Queue presses on the open stove: the queue after the press, and the line shown for it.</summary>
    public (CookQueue Queue, Pressed Pressed) Press(Cookbook book, long stoveId, int recipeId, bool isAdding, bool isChinese)
    {
        if (!book.Recipes.TryGetValue(recipeId, out var recipe)) return (this, new Pressed(false, Texts.NoRecipe.Pick(isChinese)));
        if (isAdding && IsFull(stoveId)) return (this, new Pressed(false, Texts.QueueFull.Pick(isChinese)));

        return isAdding
            ? (Add(stoveId, recipeId), new Pressed(true, Texts.Queued.Say(isChinese, recipe.Name)))
            : (Remove(stoveId, recipeId), new Pressed(true, Texts.Unqueued.Say(isChinese, recipe.Name)));
    }

    private CookQueue With(long stoveId, IReadOnlyList<int> ids)
    {
        var byStove = ByStove.ToDictionary(pair => pair.Key, pair => pair.Value);

        if (ids.Count == 0) byStove.Remove(stoveId);
        else byStove[stoveId] = ids;

        return new CookQueue(byStove);
    }
}
