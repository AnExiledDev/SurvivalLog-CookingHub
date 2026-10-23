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

/// <summary>One stove's turn. RecipeId is the dish whose food went in, and leaves the queue; null when none did.</summary>
public sealed record QueueTurn(long StoveId, QueueStep Step, int? RecipeId, string Message);

/// <summary>
/// Cooks each stove's queue once its last dishes are cleared out: an idle home stove with an empty pot gets the first
/// queued dish whose food is set aside (Reservation), filled with that food, and is started. A dish short of food
/// stays queued and the next one is tried.
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

            if (turn.RecipeId is { } cooked) queue = queue.Remove(turn.StoveId, cooked);

            turns.Add(turn);
        }

        return turns;
    }

    /// <summary>The first queued dish this stove can cook now, with the food set aside for it, or null.</summary>
    private static (Recipe Recipe, IReadOnlyList<Take> Takes)? NextDish(Kitchen kitchen, Stove stove, CookQueue queue, bool isSpoilingFirst)
    {
        var reservation = Reservation.Of(kitchen, queue, isSpoilingFirst, stove.SlotsByUnit);
        var queued = queue.For(stove.FurnitureId);

        for (var index = 0; index < queued.Count; index++)
        {
            var takes = reservation.For(stove.FurnitureId, index);

            if (takes != null && kitchen.Book.Recipes.TryGetValue(queued[index], out var recipe) && CanCook(kitchen, stove, recipe, takes)) return (recipe, takes);
        }

        return null;
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
        if (NextDish(kitchen, stove, queue, isSpoilingFirst) is not { } next) return Wait(stove, Texts.QueueShort.Say(isChinese, stove.Name));

        var (recipe, takes) = next;
        var moved = CookRun.MoveIn(game, takes, stove.WorkbenchId);

        if (moved < takes.Count) return Wait(stove, Texts.MovedSome.Say(isChinese, moved, takes.Count));
        if (idle.IsOpen) return new QueueTurn(stove.FurnitureId, QueueStep.Loaded, recipe.Id, Texts.Starting.Say(isChinese, recipe.Name));

        return game.Start(stove.FurnitureId, StartIds(kitchen.Book, recipe))
            ? new QueueTurn(stove.FurnitureId, QueueStep.Started, recipe.Id, Texts.QueueStarted.Say(isChinese, recipe.Name, stove.Name))
            : new QueueTurn(stove.FurnitureId, QueueStep.NotStarted, recipe.Id, Texts.QueueNotStarted.Say(isChinese, recipe.Name, stove.Name));
    }

    /// <summary>The checks Load makes, and the game's own: the dish is learned, the stove cooks it, and its food fits the pot.</summary>
    private static bool CanCook(Kitchen kitchen, Stove stove, Recipe recipe, IReadOnlyList<Take> takes) =>
        recipe.Id != Recipe.DarkFood
        && recipe.IsUnlocked(kitchen.Level, kitchen.CraftUnlocked)
        && stove.Takes(recipe.Id)
        && CookRun.Slots(stove, takes) <= stove.MaxFood;

    private static string TagKey(Recipe recipe) => string.Join(",", recipe.Tags.OrderBy(tag => tag));

    private static QueueTurn Wait(Stove stove, string message) => new(stove.FurnitureId, QueueStep.Waiting, null, message);
}
