using System;
using System.Collections.Generic;
using CookingHub.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using GameCore.HotUpdate.ReduxUI;

namespace CookingHub.Adapters;

/// <summary>
/// Finished dishes and where they could go. A dish is what the game itself calls an uncollected cooking product (an item
/// that is not a cooking ingredient, Furniture.HasUncollectedCookingProduct) that its codex lists as a cooking output,
/// so ash or anything else a stove returns is left alone. Storages are the home furnitures with a bag the game counts
/// for building costs, minus stoves, ranked by each one's own tag rule.
/// </summary>
internal sealed partial class GameCooking : IDishGame
{
    private const float NeverSpoils = 9999f;

    public IReadOnlyList<ReadyDish> WaitingDishes()
    {
        try
        {
            return ReadWaitingDishes();
        }
        catch (Exception ex)
        {
            ReportOnce("finished dish read", ex);

            return Array.Empty<ReadyDish>();
        }
    }

    public IReadOnlyList<DishStorage> ShelvesFor(ReadyDish dish)
    {
        try
        {
            return ReadShelves(dish);
        }
        catch (Exception ex)
        {
            ReportOnce("storage rank", ex);

            return Array.Empty<DishStorage>();
        }
    }

    /// <summary>ItemManager.TryQuickMoveItem, the move Storage Hub's put-away uses; an open cooking screen is told to redraw.</summary>
    public bool Move(ReadyDish dish, long toOwnerId) => Press("dish move", () =>
    {
        var items = LoadedGame()?.World._ItemManager;

        if (items == null || !items.TryQuickMoveItem(dish.InstanceId, dish.StoveId, toOwnerId, true, false)) return false;

        Ac_Cooking_RefreshBag.SendAction();

        return true;
    });

    private List<ReadyDish> ReadWaitingDishes()
    {
        var dishes = new List<ReadyDish>();
        var game = LoadedGame();

        if (game == null || !IsHome(game.Value.Agents) || game.Value.World._ItemManager is not { } items || game.Value.World._CodexManager is not { } codex) return dishes;

        var (_, config, agents, _) = game.Value;

        foreach (var stove in HomeBagFurnitures(agents))
        {
            if (AgentTools.GetAgentComponent<CookingComponent>(stove) is not { IsCooking: false }) continue;

            var held = items.GetItemDataList(stove.InstanceId);

            for (var i = 0; held != null && i < held.Count; i++)
            {
                if (held[i] is not { ItemCount: > 0 } item || !IsDish(config, codex, item.ItemConfigId)) continue;

                dishes.Add(new ReadyDish(item.InstanceId, item.ItemConfigId, ItemName(config, config.Get_Config_Item(item.ItemConfigId)), stove.InstanceId));
            }
        }

        return dishes;
    }

    private static bool IsDish(ConfigManager config, CodexManager codex, int itemId) =>
        config.Get_Config_Item(itemId) is { CanCook: false } && codex.IsCookingOutput(itemId);

    private List<DishStorage> ReadShelves(ReadyDish dish)
    {
        var shelves = new List<DishStorage>();
        var game = LoadedGame();

        if (game == null || game.Value.World._ItemManager is not { } items || Find(items, dish) is not { } item) return shelves;

        var (_, config, agents, role) = game.Value;
        var itemConfig = config.Get_Config_Item(dish.ItemId);
        var places = new Places(config, agents, role.InstanceId, GameLanguage.IsChinese());
        var daysLeft = DaysLeft(itemConfig?.Life ?? 0, item);

        foreach (var storage in HomeBagFurnitures(agents))
        {
            if (AgentTools.GetAgentComponent<CookingComponent>(storage) != null || ItemManager.IsExcludedFromCostPool(storage.InstanceId)) continue;

            var tags = storage.GetAgentComponent<FurnitureTagComponent>();
            var rank = itemConfig != null && tags != null && tags.TryGetPutRank(storage.AgentConfigId, itemConfig, daysLeft, out var level, out var specificity)
                ? new PutRank(level, specificity)
                : (PutRank?)null;

            shelves.Add(new DishStorage(storage.InstanceId, places.Name(storage.InstanceId), rank, Holds(items, storage.InstanceId, dish.ItemId)));
        }

        return shelves;
    }

    private static ItemData? Find(ItemManager items, ReadyDish dish)
    {
        var held = items.GetItemDataList(dish.StoveId);

        for (var i = 0; held != null && i < held.Count; i++)
        {
            if (held[i]?.InstanceId == dish.InstanceId) return held[i];
        }

        return null;
    }

    private static List<Furniture> HomeBagFurnitures(AgentManager agents)
    {
        var found = agents.GetFurnituresWithBag(agents.GetHomeMapId(), true, true);
        var homes = new List<Furniture>();

        for (var i = 0; found != null && i < found.Count; i++)
        {
            if (found[i] != null && !IsNeighborBasket(found[i])) homes.Add(found[i]);
        }

        return homes;
    }

    private static bool Holds(ItemManager items, long ownerId, int itemId)
    {
        var held = items.GetItemDataList(ownerId);

        for (var i = 0; held != null && i < held.Count; i++)
        {
            if (held[i] is { ItemCount: > 0 } item && item.ItemConfigId == itemId) return true;
        }

        return false;
    }

    /// <summary>
    /// Days until the dish spoils, as Storage Hub gives it to the tag rules: the hours left on its clock, stretched by the
    /// shelf factor of where it sits. Fridge rules use it to pick fresh food over food about to turn.
    /// </summary>
    private static float DaysLeft(int life, ItemData item)
    {
        if (life < 1) return NeverSpoils;

        var shelfScale = item.PresetShelfScale > 0f ? item.PresetShelfScale : 1f;

        return Math.Max(0f, (item.TimeLeft + item.StartTime - Furniture.GetCompressorNowHours()) * shelfScale / 24f);
    }
}
