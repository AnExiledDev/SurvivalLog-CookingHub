using System.Collections.Generic;
using System.Linq;

namespace CookingHub.Core;

/// <summary>One stack to move into the pot, kept by which owner.</summary>
public readonly record struct Take(long InstanceId, long OwnerId, int ItemId, int Count);

/// <summary>What a press did, as the line shown to the player. IsDone is false when nothing moved.</summary>
public readonly record struct Pressed(bool IsDone, string Message);

/// <summary>
/// The presses that put food in the open cooking panel's pot: fill it for a dish, add a seasoning, swap a spoiled item
/// for a fresh one. Food moves a stack at a time, the way the panel's own recipe click fills a pot, and a press stops at
/// the first refusal.
/// </summary>
public static class CookRun
{
    public static Pressed Fill(ICookGame game, int recipeId, bool isSpoilingFirst, bool isChinese) => Reach(game, isChinese) switch
    {
        Reached.Open open => Fill(game, open.Kitchen, open.Stove, recipeId, isSpoilingFirst, isChinese),
        Reached.Refused refused => refused.Pressed,
        _ => throw new System.InvalidOperationException("unknown reach"),
    };

    /// <summary>Puts a fresh seasoning in the pot, the one closest to spoiling.</summary>
    public static Pressed Season(ICookGame game, int itemId, bool isChinese) => Reach(game, isChinese) switch
    {
        Reached.Open open => Season(game, open.Kitchen, open.Stove, itemId, isChinese),
        Reached.Refused refused => refused.Pressed,
        _ => throw new System.InvalidOperationException("unknown reach"),
    };

    /// <summary>
    /// Takes every spoiled stack of the item out of the pot into the panel's bag, then puts fresh stacks of the same
    /// item in. The kitchen is read again between the two, since what went out can land on a stack in the bag.
    /// </summary>
    public static Pressed Swap(ICookGame game, int itemId, bool isChinese) => Reach(game, isChinese) switch
    {
        Reached.Open open => SwapOut(game, open.Kitchen, open.Stove, itemId, isChinese),
        Reached.Refused refused => refused.Pressed,
        _ => throw new System.InvalidOperationException("unknown reach"),
    };

    private static Pressed Fill(ICookGame game, Kitchen kitchen, Stove stove, int recipeId, bool isSpoilingFirst, bool isChinese)
    {
        if (!kitchen.Book.Recipes.TryGetValue(recipeId, out var recipe)) return No(Texts.NoRecipe, isChinese);
        if (stove.Pot.Count > 0) return No(Texts.PotNotEmpty, isChinese);
        if (!stove.Takes(recipeId)) return new Pressed(false, Texts.StoveRefuses.Say(isChinese, recipe.Name));

        var takes = Plan(kitchen, recipe, isSpoilingFirst);

        if (takes == null) return new Pressed(false, Texts.NotEnough.Say(isChinese, recipe.Name));
        if (takes.Count > stove.MaxFood) return new Pressed(false, Texts.TooBig.Say(isChinese, recipe.Name, takes.Count, stove.MaxFood));

        var moved = MoveIn(game, takes, stove.WorkbenchId);

        return moved == takes.Count
            ? new Pressed(true, Texts.Filled.Say(isChinese, recipe.Name))
            : new Pressed(moved > 0, Texts.MovedSome.Say(isChinese, moved, takes.Count));
    }

    private static Pressed Season(ICookGame game, Kitchen kitchen, Stove stove, int itemId, bool isChinese)
    {
        if (stove.Room == 0) return No(Texts.PotFull, isChinese);

        var stack = Advice.FreshFor(kitchen, itemId);

        if (stack == null) return new Pressed(false, Texts.NoneLeft.Say(isChinese, kitchen.Book.ItemName(itemId)));

        return game.MoveStack(stack.InstanceId, stack.OwnerId, stove.WorkbenchId)
            ? new Pressed(true, Texts.Added.Say(isChinese, kitchen.Book.ItemName(itemId)))
            : No(Texts.MoveRefused, isChinese);
    }

    private static Pressed SwapOut(ICookGame game, Kitchen kitchen, Stove stove, int itemId, bool isChinese)
    {
        var name = kitchen.Book.ItemName(itemId);
        var spoiled = stove.Pot.Where(stack => stack.ItemId == itemId && stack.IsSpoiled).ToList();
        var count = spoiled.Sum(stack => stack.Count);

        if (count == 0) return new Pressed(false, Texts.NothingSpoiled.Say(isChinese, name));
        if (Cover(Ordered(kitchen.Stock.Where(stack => stack.ItemId == itemId), isSpoilingFirst: true), count) == null) return new Pressed(false, Texts.NoneLeft.Say(isChinese, name));

        foreach (var stack in spoiled)
        {
            if (!game.MoveStack(stack.InstanceId, stove.WorkbenchId, stove.BagId)) return No(Texts.MoveRefused, isChinese);
        }

        return Reach(game, isChinese) switch
        {
            Reached.Open open => SwapIn(game, open.Kitchen, open.Stove, itemId, count, isChinese),
            Reached.Refused refused => refused.Pressed,
            _ => throw new System.InvalidOperationException("unknown reach"),
        };
    }

    private static Pressed SwapIn(ICookGame game, Kitchen kitchen, Stove stove, int itemId, int count, bool isChinese)
    {
        var name = kitchen.Book.ItemName(itemId);
        var takes = Cover(Ordered(kitchen.Stock.Where(stack => stack.ItemId == itemId), isSpoilingFirst: true), count);

        if (takes == null) return new Pressed(true, Texts.NoneLeft.Say(isChinese, name));

        var moved = MoveIn(game, takes, stove.WorkbenchId);

        return new Pressed(true, moved == takes.Count ? Texts.Swapped.Say(isChinese, name) : Texts.MovedSome.Say(isChinese, moved, takes.Count));
    }

    /// <summary>
    /// The stacks that fill a pot for the recipe, or null when fresh food runs short. An exact recipe takes its own items;
    /// a tag recipe takes any cookable food of each type. Each want takes stacks until it has enough, so a stack bigger
    /// than the want goes in whole, as it does when the player clicks the recipe in the game's own panel. Spoiling first
    /// uses up what goes off soonest; otherwise the freshest goes first.
    /// </summary>
    public static IReadOnlyList<Take>? Plan(Kitchen kitchen, Recipe recipe, bool isSpoilingFirst)
    {
        var used = new HashSet<long>();
        var takes = new List<Take>();
        var fresh = kitchen.Stock.Where(stack => !stack.IsSpoiled && stack.Count > 0).ToList();

        foreach (var (id, want) in Finder.Wants(recipe))
        {
            var free = Finder.Matching(kitchen.Book, fresh, recipe.IsExact, id).Where(stack => !used.Contains(stack.InstanceId));
            var cover = Cover(Ordered(free, isSpoilingFirst), want);

            if (cover == null) return null;

            foreach (var take in cover) used.Add(take.InstanceId);

            takes.AddRange(cover);
        }

        return takes;
    }

    /// <summary>Fresh stacks in the order a fill uses them; food that keeps comes last when spoiling first, first otherwise.</summary>
    private static IEnumerable<Stack> Ordered(IEnumerable<Stack> stacks, bool isSpoilingFirst)
    {
        var fresh = stacks.Where(stack => !stack.IsSpoiled && stack.Count > 0);
        var ordered = isSpoilingFirst
            ? fresh.OrderBy(stack => stack.HoursLeft ?? double.MaxValue)
            : fresh.OrderByDescending(stack => stack.HoursLeft ?? double.MaxValue);

        return ordered.ThenBy(stack => stack.InstanceId);
    }

    /// <summary>The first stacks, in order, whose units reach the want, or null when they all together fall short.</summary>
    private static List<Take>? Cover(IEnumerable<Stack> ordered, int want)
    {
        var takes = new List<Take>();
        var units = 0;

        foreach (var stack in ordered)
        {
            if (units >= want) break;

            takes.Add(new Take(stack.InstanceId, stack.OwnerId, stack.ItemId, stack.Count));
            units += stack.Count;
        }

        return units >= want ? takes : null;
    }

    private static int MoveIn(ICookGame game, IReadOnlyList<Take> takes, long workbenchId)
    {
        var moved = 0;

        foreach (var take in takes)
        {
            if (!game.MoveStack(take.InstanceId, take.OwnerId, workbenchId)) break;

            moved++;
        }

        return moved;
    }

    /// <summary>A press needs a loaded game with a stove's cooking panel open and not cooking.</summary>
    private static Reached Reach(ICookGame game, bool isChinese) => game.Read() switch
    {
        CookRead.Ready { Kitchen: { Stove: { IsCooking: true } } } => new Reached.Refused(No(Texts.Cooking, isChinese)),
        CookRead.Ready { Kitchen: { Stove: { } stove } kitchen } => new Reached.Open(kitchen, stove),
        CookRead.Ready => new Reached.Refused(No(Texts.NoStove, isChinese)),
        CookRead.NotReady notReady => new Reached.Refused(new Pressed(false, Texts.NotReady.Say(isChinese, notReady.Reason))),
        _ => new Reached.Refused(No(Texts.NoGame, isChinese)),
    };

    private static Pressed No(Phrase phrase, bool isChinese) => new(false, phrase.Pick(isChinese));

    private abstract record Reached
    {
        private Reached()
        {
        }

        public sealed record Open(Kitchen Kitchen, Stove Stove) : Reached;

        public sealed record Refused(Pressed Pressed) : Reached;
    }
}
