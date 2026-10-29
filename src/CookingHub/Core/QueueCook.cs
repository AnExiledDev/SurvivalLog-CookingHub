using System.Collections.Generic;
using System.Linq;

namespace CookingHub.Core;

/// <summary>What the queue did at one idle stove.</summary>
public enum QueueStep
{
    /// <summary>The pot was filled and the stove started cooking.</summary>
    Started,

    /// <summary>The pot of the stove whose cooking screen is open was filled; the page presses the game's Cook button.</summary>
    Loaded,

    /// <summary>The pot was filled but the game did not start the stove.</summary>
    NotStarted,

    /// <summary>Nothing was cooked: no fuel or power, no queued dish the food covers, or the game refused the food.</summary>
    Waiting,
}

/// <summary>One stove's turn. RecipeIds are the dishes whose food went in, in queue order, and leave the queue; empty when none did.</summary>
public sealed record QueueTurn(long StoveId, QueueStep Step, IReadOnlyList<int> RecipeIds, string Message);

/// <summary>
/// Cooks each stove's queue once its last dishes are cleared out: an idle home stove with an empty pot gets the first
/// queued dish whose food is set aside (Reservation), filled with that food, and is started. Once the cook's level lets
/// one pot cook several dishes, every later queued dish whose food still fits goes in too, so the pot cooks as many as
/// it holds. A dish short of food stays queued and the next one is tried.
/// </summary>
public static class QueueCook
{
    /// <summary>
    /// One turn for each of the given idle stoves that has dishes queued. Each stove cooks only food set aside for its own
    /// queued dish, so no stove takes what another stove's queue is waiting on.
    /// </summary>
    public static IReadOnlyList<QueueTurn> Run(ICookGame game, IReadOnlyList<IdleStove> idleStoves, CookQueue queue, bool isSpoilingFirst, bool isChinese)
    {
        var turns = new List<QueueTurn>();

        foreach (var idle in idleStoves)
        {
            if (queue.For(idle.Stove.FurnitureId).Count == 0) continue;

            // Each fill uses up food, so the next stove plans from a fresh read, without the dish just cooked.
            if (game.Read() is not CookRead.Ready { Kitchen: var kitchen }) break;

            var turn = Turn(game, kitchen, idle, queue, isSpoilingFirst, isChinese);

            foreach (var cooked in turn.RecipeIds) queue = queue.Remove(turn.StoveId, cooked);

            turns.Add(turn);
        }

        return turns;
    }

    /// <summary>
    /// The queued dishes this stove cooks now, in queue order, with the food set aside for each: the first one it can
    /// cook, then at the multi-pot level each later one whose food fits in the slots still free. Empty when none.
    /// </summary>
    public static IReadOnlyList<(Recipe Recipe, IReadOnlyList<Take> Takes)> NextDishes(Kitchen kitchen, Stove stove, CookQueue queue, bool isSpoilingFirst)
    {
        var reservation = Reservation.Of(kitchen, queue, isSpoilingFirst, stove.SlotsByUnit);
        var queued = queue.For(stove.FurnitureId);
        var dishes = new List<(Recipe Recipe, IReadOnlyList<Take> Takes)>();
        var used = 0;

        for (var index = 0; index < queued.Count; index++)
        {
            var takes = reservation.For(stove.FurnitureId, index);

            if (takes == null || !kitchen.Book.Recipes.TryGetValue(queued[index], out var recipe) || !CanCook(kitchen, stove, recipe, takes)) continue;

            var slots = CookRun.Slots(stove, takes);

            if (used + slots > stove.MaxFood) continue;

            dishes.Add((recipe, takes));
            used += slots;

            if (!kitchen.IsMultiPot) break;
        }

        return dishes;
    }

    /// <summary>
    /// The recipe ids the game is asked to start: the dish, then for a tag recipe every recipe wanting the same food
    /// types. The game keeps only the one whose tier the pot reaches (Furniture.ValidateClientRecipeIds), so a pot
    /// that turned out a tier higher or lower still starts as that dish.
    /// </summary>
    public static IReadOnlyList<int> StartIds(Cookbook book, Recipe recipe)
    {
        if (recipe.IsExact) return new[] { recipe.Id };

        var tags = TagKey(recipe);
        var siblings = book.Recipes.Values.Where(other => other.Id != recipe.Id && !other.IsExact && TagKey(other) == tags).OrderBy(other => other.Tier).ThenBy(other => other.Id);

        return new[] { recipe.Id }.Concat(siblings.Select(other => other.Id)).ToList();
    }

    private static QueueTurn Turn(ICookGame game, Kitchen kitchen, IdleStove idle, CookQueue queue, bool isSpoilingFirst, bool isChinese)
    {
        var stove = idle.Stove;

        if (!idle.HasHeat) return Wait(stove, Texts.QueueNoHeat.Say(isChinese, stove.Name));
        var dishes = NextDishes(kitchen, stove, queue, isSpoilingFirst);

        if (dishes.Count == 0) return Wait(stove, Texts.QueueShort.Say(isChinese, stove.Name));

        var loaded = new List<Recipe>();

        foreach (var (recipe, takes) in dishes)
        {
            var moved = CookRun.MoveIn(game, takes, stove.WorkbenchId);

            // Food that went in only in part spoils the pot's match: the first dish waits, a later one ends the fill.
            if (moved < takes.Count)
            {
                if (loaded.Count == 0) return Wait(stove, Texts.MovedSome.Say(isChinese, moved, takes.Count));

                break;
            }

            loaded.Add(recipe);
        }

        var ids = loaded.Select(recipe => recipe.Id).ToList();
        var names = Names(loaded, isChinese);

        if (idle.IsOpen) return new QueueTurn(stove.FurnitureId, QueueStep.Loaded, ids, Texts.Starting.Say(isChinese, names));

        return game.Start(stove.FurnitureId, StartIds(kitchen.Book, loaded))
            ? new QueueTurn(stove.FurnitureId, QueueStep.Started, ids, Texts.QueueStarted.Say(isChinese, names, stove.Name))
            : new QueueTurn(stove.FurnitureId, QueueStep.NotStarted, ids, Texts.QueueNotStarted.Say(isChinese, names, stove.Name));
    }

    /// <summary>
    /// The recipe ids for several dishes in one pot, each dish's in turn. The game matches the ids in order and takes
    /// each match's food out of what is left (Furniture.ValidateClientRecipeIds), so a dish listed twice is cooked twice.
    /// </summary>
    public static IReadOnlyList<int> StartIds(Cookbook book, IReadOnlyList<Recipe> recipes) => recipes.SelectMany(recipe => StartIds(book, recipe)).ToList();

    /// <summary>The dishes as one name: "Watermelon Juice x3", or several names in queue order.</summary>
    public static string Names(IReadOnlyList<Recipe> recipes, bool isChinese) =>
        string.Join(isChinese ? "\u3001" : ", ", recipes.GroupBy(recipe => recipe.Id).Select(group => group.Count() > 1 ? $"{group.First().Name} x{group.Count()}" : group.First().Name));

    /// <summary>The checks Load makes, and the game's own: the dish is learned, the stove cooks it, and its food fits the pot.</summary>
    private static bool CanCook(Kitchen kitchen, Stove stove, Recipe recipe, IReadOnlyList<Take> takes) =>
        recipe.Id != Recipe.DarkFood
        && recipe.IsUnlocked(kitchen.Level, kitchen.CraftUnlocked)
        && stove.Takes(recipe.Id)
        && CookRun.Slots(stove, takes) <= stove.MaxFood;

    private static string TagKey(Recipe recipe) => string.Join(",", recipe.Tags.OrderBy(tag => tag));

    private static QueueTurn Wait(Stove stove, string message) => new(stove.FurnitureId, QueueStep.Waiting, System.Array.Empty<int>(), message);
}
