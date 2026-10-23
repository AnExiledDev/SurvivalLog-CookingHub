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
/// queued dish the player's fresh food covers, filled the way Load fills a pot, and is started. A dish short of food
/// stays queued and the next one is tried.
/// </summary>
public static class QueueCook
{
    /// <summary>One turn for each of the given idle stoves that has dishes queued.</summary>
    public static IReadOnlyList<QueueTurn> Run(ICookGame game, IReadOnlyList<IdleStove> idleStoves, CookQueue queue, bool isSpoilingFirst, bool isChinese)
    {
        var turns = new List<QueueTurn>();

        foreach (var idle in idleStoves)
        {
            var queued = queue.For(idle.Stove.FurnitureId);

            if (queued.Count == 0) continue;

            // Each fill uses up food, so the next stove plans from a fresh read.
            if (game.Read() is not CookRead.Ready { Kitchen: var kitchen }) break;

            turns.Add(Turn(game, kitchen, idle, queued, isSpoilingFirst, isChinese));
        }

        return turns;
    }

    /// <summary>The first queued dish this stove can cook now from fresh food, or null.</summary>
    public static Recipe? NextDish(Kitchen kitchen, Stove stove, IReadOnlyList<int> queued, bool isSpoilingFirst) =>
        queued.Select(id => kitchen.Book.Recipes.TryGetValue(id, out var recipe) ? recipe : null).FirstOrDefault(recipe => recipe != null && CanCook(kitchen, stove, recipe, isSpoilingFirst));

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

    private static QueueTurn Turn(ICookGame game, Kitchen kitchen, IdleStove idle, IReadOnlyList<int> queued, bool isSpoilingFirst, bool isChinese)
    {
        var stove = idle.Stove;

        if (!idle.HasHeat) return Wait(stove, Texts.QueueNoHeat.Say(isChinese, stove.Name));

        var recipe = NextDish(kitchen, stove, queued, isSpoilingFirst);
        var takes = recipe == null ? null : CookRun.Plan(kitchen, recipe, isSpoilingFirst);

        if (recipe == null || takes == null) return Wait(stove, Texts.QueueShort.Say(isChinese, stove.Name));

        var moved = CookRun.MoveIn(game, takes, stove.WorkbenchId);

        if (moved < takes.Count) return Wait(stove, Texts.MovedSome.Say(isChinese, moved, takes.Count));
        if (idle.IsOpen) return new QueueTurn(stove.FurnitureId, QueueStep.Loaded, recipe.Id, Texts.Starting.Say(isChinese, recipe.Name));

        return game.Start(stove.FurnitureId, StartIds(kitchen.Book, recipe))
            ? new QueueTurn(stove.FurnitureId, QueueStep.Started, recipe.Id, Texts.QueueStarted.Say(isChinese, recipe.Name, stove.Name))
            : new QueueTurn(stove.FurnitureId, QueueStep.NotStarted, recipe.Id, Texts.QueueNotStarted.Say(isChinese, recipe.Name, stove.Name));
    }

    /// <summary>The checks Load makes, and the game's own: the dish is learned, the stove cooks it, and its food fits the pot.</summary>
    private static bool CanCook(Kitchen kitchen, Stove stove, Recipe recipe, bool isSpoilingFirst)
    {
        if (recipe.Id == Recipe.DarkFood || !recipe.IsUnlocked(kitchen.Level, kitchen.CraftUnlocked) || !stove.Takes(recipe.Id)) return false;

        var takes = CookRun.Plan(kitchen, recipe, isSpoilingFirst);

        return takes != null && CookRun.Slots(stove, takes) <= stove.MaxFood;
    }

    private static string TagKey(Recipe recipe) => string.Join(",", recipe.Tags.OrderBy(tag => tag));

    private static QueueTurn Wait(Stove stove, string message) => new(stove.FurnitureId, QueueStep.Waiting, null, message);
}
