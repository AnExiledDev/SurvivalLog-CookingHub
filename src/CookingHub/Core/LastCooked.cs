using System.Collections.Generic;
using System.Linq;

namespace CookingHub.Core;

/// <summary>
/// The dish each stove last started, for Repeat last. While the open stove is not cooking, the dish its pot forecasts
/// is pending; when the stove goes from not cooking to cooking, the pending dish becomes that stove's last one. Kept for
/// this game start only.
/// </summary>
public sealed record LastCooked(IReadOnlyDictionary<long, int> ByStove, long? WatchedId, int? Pending, bool WasCooking)
{
    public static readonly LastCooked None = new(new Dictionary<long, int>(), null, null, false);

    public int? For(long furnitureId) => ByStove.TryGetValue(furnitureId, out var recipeId) ? recipeId : null;

    /// <summary>The next state after one read of the open stove, or of no stove.</summary>
    public LastCooked See(Stove? stove)
    {
        if (stove == null) return this with { WatchedId = null, Pending = null, WasCooking = false };
        if (stove.FurnitureId != WatchedId) return this with { WatchedId = stove.FurnitureId, Pending = PendingOf(stove), WasCooking = stove.IsCooking };

        var byStove = !WasCooking && stove.IsCooking && Pending is { } started
            ? new Dictionary<long, int>(ByStove.ToDictionary(pair => pair.Key, pair => pair.Value)) { [stove.FurnitureId] = started }
            : ByStove;

        return new LastCooked(byStove, stove.FurnitureId, stove.IsCooking ? Pending : PendingOf(stove), stove.IsCooking);
    }

    /// <summary>The main dish a filled, idle pot forecasts, when the player knows it; dark food is never repeated.</summary>
    private static int? PendingOf(Stove stove)
    {
        if (stove.IsCooking || stove.Pot.Count == 0) return null;

        var main = stove.Forecasts.FirstOrDefault();

        return main is { IsKnown: true } && main.RecipeId != Recipe.DarkFood ? main.RecipeId : null;
    }
}
