using System;
using System.Collections.Generic;
using System.Linq;

namespace CookingHub.Core;

/// <summary>How much a storage's tag rule wants an item, as the game's FurnitureTagComponent.TryGetPutRank reports it.</summary>
public readonly record struct PutRank(int Level, int Specificity);

/// <summary>A finished dish left in a stove that is no longer cooking.</summary>
public sealed record ReadyDish(long InstanceId, int ItemId, string Name, long StoveId);

/// <summary>A home storage a dish could go into. TagRank is null when the storage's tag rule does not want the dish.</summary>
public sealed record DishStorage(long OwnerId, string Name, PutRank? TagRank, bool HoldsSameItem);

/// <summary>What happened to one dish: put away into a storage, or left in the stove because no storage took it.</summary>
public abstract record Stored(ReadyDish Dish)
{
    public sealed record Into(ReadyDish Dish, DishStorage Storage) : Stored(Dish);

    /// <summary>Refused names the storages that wanted the dish but had no room, best first; empty when none wants it.</summary>
    public sealed record Stays(ReadyDish Dish, IReadOnlyList<string> Refused) : Stored(Dish)
    {
        public Stays(ReadyDish dish) : this(dish, Array.Empty<string>())
        {
        }
    }
}

/// <summary>The game side of putting dishes away: the dishes waiting in stoves, the storages one could go to, and the move.</summary>
public interface IDishGame
{
    /// <summary>Dishes waiting in home stoves that are not cooking; empty when no game is loaded or the player is away.</summary>
    IReadOnlyList<ReadyDish> WaitingDishes();

    /// <summary>Home storages, each with how its tag rule ranks this dish and whether it already holds some.</summary>
    IReadOnlyList<DishStorage> ShelvesFor(ReadyDish dish);

    /// <summary>Moves the whole dish stack out of its stove. False when the game refused, usually because the storage is full.</summary>
    bool Move(ReadyDish dish, long toOwnerId);
}

public static class DishStore
{
    /// <summary>
    /// Storages to try, best first, the way Storage Hub puts items away. A storage whose tag rule wants the dish beats
    /// one that only holds some already, and a storage with neither is never picked. Among tag matches the more specific
    /// rule wins, then the lower level, then the storage already holding that dish.
    /// </summary>
    public static IReadOnlyList<DishStorage> Targets(IEnumerable<DishStorage> shelves) =>
        shelves
            .Where(shelf => shelf.TagRank.HasValue || shelf.HoldsSameItem)
            .OrderByDescending(shelf => shelf.TagRank.HasValue)
            .ThenByDescending(shelf => shelf.TagRank?.Specificity ?? 0)
            .ThenBy(shelf => shelf.TagRank?.Level ?? 0)
            .ThenByDescending(shelf => shelf.HoldsSameItem)
            .ToList();

    /// <summary>
    /// Puts every waiting dish into the first storage that takes it. A dish no storage takes stays in its stove, and with
    /// a retry list it is not tried again until its wait is over: ranking every storage and trying each full one is a
    /// game move apiece, too dear to repeat every sweep for a dish that has nowhere to go.
    /// </summary>
    public static IReadOnlyList<Stored> PutAway(IDishGame game, DishRetry? retry = null, double now = 0)
    {
        var waiting = game.WaitingDishes();
        var results = new List<Stored>();

        retry?.KeepOnly(waiting);

        foreach (var dish in waiting)
        {
            if (retry != null && !retry.IsDue(dish, now)) continue;

            var stored = PutAway(game, dish);

            retry?.Saw(stored, now);
            results.Add(stored);
        }

        return results;
    }

    private static Stored PutAway(IDishGame game, ReadyDish dish)
    {
        var refused = new List<string>();

        foreach (var shelf in Targets(game.ShelvesFor(dish)))
        {
            if (game.Move(dish, shelf.OwnerId)) return new Stored.Into(dish, shelf);

            refused.Add(shelf.Name);
        }

        return new Stored.Stays(dish, refused);
    }

    /// <summary>
    /// The notice for a put-away result: a dish left in the stove is a warning, one put away is not. A stove still holding
    /// a dish takes nothing from its queue, so when dishes are queued there the warning says the queue waits for it.
    /// </summary>
    public static DishNote Note(Stored stored, bool isQueued, bool isChinese)
    {
        var text = Describe(stored, isChinese);

        if (stored is Stored.Stays && isQueued) text += Texts.DishStaysQueue.Say(isChinese, (int)DishRetry.WaitSeconds);

        return new DishNote(text, stored is Stored.Stays);
    }

    /// <summary>The line shown for a result: where the dish went, or why it stayed: nothing takes it, or all that do are full.</summary>
    public static string Describe(Stored stored, bool isChinese) => stored switch
    {
        Stored.Into into => Texts.DishStored.Say(isChinese, into.Dish.Name, into.Storage.Name),
        Stored.Stays { Refused.Count: 0 } stays => Texts.DishStays.Say(isChinese, stays.Dish.Name),
        Stored.Stays { Refused.Count: 1 } stays => Texts.DishStaysFull.Say(isChinese, stays.Dish.Name, stays.Refused[0]),
        Stored.Stays stays => Texts.DishStaysAllFull.Say(isChinese, stays.Dish.Name, stays.Refused[0]),
        _ => throw new InvalidOperationException("unknown put away result"),
    };
}

/// <summary>
/// Dishes that stayed in their stove, each with when it may be tried again. A dish leaves the list once it is put away
/// or no longer waits (taken out by hand, or eaten).
/// </summary>
public sealed class DishRetry
{
    public const double WaitSeconds = 30;

    private readonly Dictionary<long, double> _nextTryAt = new();

    public bool IsDue(ReadyDish dish, double now) => !_nextTryAt.TryGetValue(dish.InstanceId, out var at) || now >= at;

    public void Saw(Stored stored, double now)
    {
        if (stored is Stored.Stays) _nextTryAt[stored.Dish.InstanceId] = now + WaitSeconds;
        else _nextTryAt.Remove(stored.Dish.InstanceId);
    }

    public void KeepOnly(IReadOnlyList<ReadyDish> waiting)
    {
        if (_nextTryAt.Count == 0) return;

        var ids = new HashSet<long>();

        foreach (var dish in waiting) ids.Add(dish.InstanceId);
        foreach (var id in _nextTryAt.Keys.Where(id => !ids.Contains(id)).ToList()) _nextTryAt.Remove(id);
    }
}

/// <summary>What to tell the player about one dish: the line, and whether it is a warning.</summary>
public sealed record DishNote(string Text, bool IsWarning);
