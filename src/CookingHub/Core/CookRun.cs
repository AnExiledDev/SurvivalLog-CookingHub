using System.Collections.Generic;
using System.Linq;

namespace CookingHub.Core;

/// <summary>One stack to move into the pot, kept by which owner: Units of its Count are wanted.</summary>
public readonly record struct Take(long InstanceId, long OwnerId, int ItemId, int Count, int Units);

/// <summary>What a press did, as the line shown to the player. IsDone is false when nothing moved.</summary>
public readonly record struct Pressed(bool IsDone, string Message);

/// <summary>
/// The presses that put food in the open cooking panel's pot: fill it for a dish, add one food,
/// swap a spoiled item for a fresh one, and top the stove's fuel up. Food moves a stack at a time, the way the panel's
/// own recipe click fills a pot (a unit at a time where units never share a slot), and a press stops at the first refusal.
/// </summary>
public static class CookRun
{
    /// <summary>Fills the pot for the dish from food not set aside for the queue.</summary>
    public static Pressed Fill(ICookGame game, int recipeId, bool isSpoilingFirst, bool isChinese, CookQueue? queue = null) => Reach(game, isChinese) switch
    {
        Reached.Open open => Fill(game, open.Kitchen, open.Stove, recipeId, isSpoilingFirst, isChinese, queue ?? CookQueue.Empty),
        Reached.Refused refused => refused.Pressed,
        _ => throw new System.InvalidOperationException("unknown reach"),
    };

    /// <summary>Puts one unit of a food in the pot, off the fresh stack closest to spoiling, from wherever it is kept.</summary>
    public static Pressed Add(ICookGame game, int itemId, bool isChinese) => Reach(game, isChinese) switch
    {
        Reached.Open open => Add(game, open.Kitchen, open.Stove, itemId, isChinese),
        Reached.Refused refused => refused.Pressed,
        _ => throw new System.InvalidOperationException("unknown reach"),
    };

    /// <summary>Fills the empty pot for the dish this stove last started.</summary>
    public static Pressed Repeat(ICookGame game, LastCooked last, bool isSpoilingFirst, bool isChinese, CookQueue? queue = null) => Reach(game, isChinese) switch
    {
        Reached.Open { Stove: var stove } open when last.For(stove.FurnitureId) is { } recipeId => Fill(game, open.Kitchen, stove, recipeId, isSpoilingFirst, isChinese, queue ?? CookQueue.Empty),
        Reached.Open => No(Texts.NoLast, isChinese),
        Reached.Refused refused => refused.Pressed,
        _ => throw new System.InvalidOperationException("unknown reach"),
    };

    public static Pressed TopUp(ICookGame game, bool isChinese) => game.Read() switch
    {
        CookRead.Ready { Kitchen: { Stove: { } stove } } => TopUp(game, stove, isChinese),
        CookRead.Ready => No(Texts.NoStove, isChinese),
        CookRead.NotReady notReady => new Pressed(false, Texts.NotReady.Say(isChinese, notReady.Reason)),
        _ => No(Texts.NoGame, isChinese),
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

    /// <summary>
    /// A pot that already holds food keeps it and takes the dish as one more when the pot can cook several dishes at
    /// once (KeepsPot); otherwise its food goes back where it came from first, and the pot is filled from what is left.
    /// Food set aside for queued dishes is left alone.
    /// </summary>
    private static Pressed Fill(ICookGame game, Kitchen kitchen, Stove stove, int recipeId, bool isSpoilingFirst, bool isChinese, CookQueue queue)
    {
        if (!kitchen.Book.Recipes.TryGetValue(recipeId, out var recipe)) return No(Texts.NoRecipe, isChinese);
        if (!stove.Takes(recipeId)) return new Pressed(false, Texts.StoveRefuses.Say(isChinese, recipe.Name));

        var takes = Plan(Reservation.Of(kitchen, queue, isSpoilingFirst, stove.SlotsByUnit).Left, recipe, isSpoilingFirst);

        if (takes == null) return Short(kitchen, recipe, isSpoilingFirst, isChinese);

        var slots = Slots(stove, takes);

        if (slots > stove.MaxFood) return new Pressed(false, Texts.TooBig.Say(isChinese, recipe.Name, slots, stove.MaxFood));
        if (stove.Pot.Count == 0) return Load(game, stove, recipe, takes, Texts.Filled, isChinese);
        if (KeepsPot(kitchen, stove, slots)) return Load(game, stove, recipe, takes, Texts.AddedDish, isChinese);
        if (!EmptyPot(game, stove)) return No(Texts.EmptyRefused, isChinese);

        return Reach(game, isChinese) switch
        {
            Reached.Open { Stove: { Pot: { Count: 0 } } } open => FillEmpty(game, open.Kitchen, open.Stove, recipe, isSpoilingFirst, isChinese, queue),
            Reached.Open => No(Texts.EmptyRefused, isChinese),
            Reached.Refused refused => refused.Pressed,
            _ => throw new System.InvalidOperationException("unknown reach"),
        };
    }

    /// <summary>The fill after the pot was emptied: the food that came out may be what the dish now takes.</summary>
    private static Pressed FillEmpty(ICookGame game, Kitchen kitchen, Stove stove, Recipe recipe, bool isSpoilingFirst, bool isChinese, CookQueue queue)
    {
        var takes = Plan(Reservation.Of(kitchen, queue, isSpoilingFirst, stove.SlotsByUnit).Left, recipe, isSpoilingFirst);

        if (takes == null) return Short(kitchen, recipe, isSpoilingFirst, isChinese);

        return Load(game, stove, recipe, takes, Texts.Filled, isChinese);
    }

    /// <summary>Why a fill found too little food: there is not enough at all, or the rest is set aside for queued dishes.</summary>
    private static Pressed Short(Kitchen kitchen, Recipe recipe, bool isSpoilingFirst, bool isChinese) =>
        new(false, (Plan(kitchen, recipe, isSpoilingFirst) == null ? Texts.NotEnough : Texts.SetAside).Say(isChinese, recipe.Name));

    private static Pressed Load(ICookGame game, Stove stove, Recipe recipe, IReadOnlyList<Take> takes, Phrase done, bool isChinese)
    {
        var moved = MoveIn(game, takes, stove.WorkbenchId);

        return moved == takes.Count
            ? new Pressed(true, done.Say(isChinese, recipe.Name))
            : new Pressed(moved > 0, Texts.MovedSome.Say(isChinese, moved, takes.Count));
    }

    /// <summary>
    /// True when the food already in the pot stays and the dish goes in beside it: the cook's level unlocks cooking
    /// several dishes in one pot, what is in it now forecasts real dishes (no dark food), and the free slots hold the
    /// new dish's food.
    /// </summary>
    public static bool KeepsPot(Kitchen kitchen, Stove stove, int slots) =>
        kitchen.IsMultiPot
        && stove.Forecasts.Count > 0
        && stove.Forecasts.All(forecast => forecast.RecipeId != Recipe.DarkFood)
        && stove.Room >= slots;

    /// <summary>The pot slots a fill takes: a slot per unit when units never share one, else a slot per stack.</summary>
    public static int Slots(Stove stove, IReadOnlyList<Take> takes) => stove.SlotsByUnit ? takes.Sum(take => take.Units) : takes.Count;

    /// <summary>Puts every stack in the pot back where this mod took it from, or in the panel's bag. False when one stayed.</summary>
    private static bool EmptyPot(ICookGame game, Stove stove)
    {
        foreach (var stack in stove.Pot)
        {
            var origin = game.OriginOf(stack.InstanceId);
            var isBack = origin is { } from && from != stove.BagId && game.MoveStack(stack.InstanceId, stove.WorkbenchId, from);

            if (!isBack && !game.MoveStack(stack.InstanceId, stove.WorkbenchId, stove.BagId)) return false;
        }

        return true;
    }

    private static Pressed TopUp(ICookGame game, Stove stove, bool isChinese)
    {
        if (stove.Fuel is not { } fuel) return No(Texts.NoFuelStove, isChinese);
        if (fuel.Free == 0) return No(Texts.FuelFull, isChinese);
        if (fuel.Preferred.Count == 0) return No(Texts.FuelByHand, isChinese);
        if (fuel.Have == 0) return No(Texts.NoFuelLeft, isChinese);

        var added = 0;

        foreach (var stack in fuel.Stock)
        {
            for (var unit = 0; unit < stack.Count && added < fuel.Free; unit++)
            {
                if (!game.AddFuel(stove.FurnitureId, stack.InstanceId)) return Topped(added, isChinese);

                added++;
            }
        }

        return Topped(added, isChinese);
    }

    private static Pressed Topped(int added, bool isChinese) => added == 0 ? No(Texts.FuelRefused, isChinese) : new Pressed(true, Texts.FuelAdded.Say(isChinese, added));

    private static Pressed Add(ICookGame game, Kitchen kitchen, Stove stove, int itemId, bool isChinese)
    {
        if (stove.Room == 0) return No(Texts.PotFull, isChinese);

        var stack = Advice.FreshFor(kitchen, itemId);

        if (stack == null) return new Pressed(false, Texts.NoneLeft.Say(isChinese, kitchen.Book.ItemName(itemId)));

        return game.MoveUnits(stack.InstanceId, stack.OwnerId, stove.WorkbenchId, 1) > 0
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

    /// <summary>
    /// The first stacks, in order, whose units reach the want, or null when they all together fall short. Each take
    /// wants only the units still missing, so the last stack gives part of itself when units go in one at a time.
    /// </summary>
    private static List<Take>? Cover(IEnumerable<Stack> ordered, int want)
    {
        var takes = new List<Take>();
        var units = 0;

        foreach (var stack in ordered)
        {
            if (units >= want) break;

            var wanted = System.Math.Min(stack.Count, want - units);

            takes.Add(new Take(stack.InstanceId, stack.OwnerId, stack.ItemId, stack.Count, wanted));
            units += wanted;
        }

        return units >= want ? takes : null;
    }

    /// <summary>How many takes went in in full; it stops at the first the game refused, or gave short.</summary>
    internal static int MoveIn(ICookGame game, IReadOnlyList<Take> takes, long workbenchId)
    {
        var moved = 0;

        foreach (var take in takes)
        {
            if (game.MoveUnits(take.InstanceId, take.OwnerId, workbenchId, take.Units) < take.Units) break;

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
