using System.Collections.Generic;
using System.Linq;

namespace CookingHub.Core;

/// <summary>
/// What the open stove's pot must show before the page presses the game's Cook button for it. Repeat last waits for
/// the dish it asked for, since it can top up a pot that already held other food. A pot the queue just filled was
/// empty, so it starts as whatever dish the game plans from that food: the game picks the dish for a pot on its own
/// (Reducer_Web_Cooking.PreMatchRecipes), and it can name an exact recipe the same food also makes instead of the
/// queued tag recipe. Pressing Cook by hand would cook that same dish.
/// </summary>
public abstract record StartWant
{
    private StartWant()
    {
    }

    /// <summary>Whether this stove, read now, is ready to start. Pending is the known main dish its pot forecasts (LastCooked).</summary>
    public abstract bool IsMetBy(Stove stove, int? pending);

    /// <summary>The pot must forecast one of these dishes first.</summary>
    public sealed record Dishes(IReadOnlyList<int> RecipeIds) : StartWant
    {
        public override bool IsMetBy(Stove stove, int? pending) => pending is { } id && RecipeIds.Contains(id);
    }

    /// <summary>The idle pot holds food and forecasts at least one dish and no dark food.</summary>
    public sealed record QueuedPot : StartWant
    {
        public override bool IsMetBy(Stove stove, int? pending)
        {
            if (stove.IsCooking || stove.Pot.Count == 0) return false;

            var dishes = stove.Forecasts.Where(forecast => forecast.RecipeId != 0).ToList();

            return dishes.Count > 0 && dishes.All(forecast => forecast.RecipeId != Recipe.DarkFood);
        }
    }
}
