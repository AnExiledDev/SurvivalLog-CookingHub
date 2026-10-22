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

    public sealed record Stays(ReadyDish Dish) : Stored(Dish);
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

    /// <summary>Puts every waiting dish into the first storage that takes it. A dish no storage takes stays in its stove.</summary>
    public static IReadOnlyList<Stored> PutAway(IDishGame game) =>
        game.WaitingDishes().Select(dish => PutAway(game, dish)).ToList();

    private static Stored PutAway(IDishGame game, ReadyDish dish)
    {
        foreach (var shelf in Targets(game.ShelvesFor(dish)))
        {
            if (game.Move(dish, shelf.OwnerId)) return new Stored.Into(dish, shelf);
        }

        return new Stored.Stays(dish);
    }

    /// <summary>The line shown for a result: where the dish went, or that it stayed because nothing takes it.</summary>
    /// <summary>The notice for a put-away result: a dish left in the stove is a warning, one put away is not.</summary>
    public static DishNote Note(Stored stored, bool isChinese) => new(Describe(stored, isChinese), stored is Stored.Stays);

    public static string Describe(Stored stored, bool isChinese) => stored switch
    {
        Stored.Into into => Texts.DishStored.Say(isChinese, into.Dish.Name, into.Storage.Name),
        Stored.Stays stays => Texts.DishStays.Say(isChinese, stays.Dish.Name),
        _ => throw new InvalidOperationException("unknown put away result"),
    };
}

/// <summary>What to tell the player about one dish: the line, and whether it is a warning.</summary>
public sealed record DishNote(string Text, bool IsWarning);
