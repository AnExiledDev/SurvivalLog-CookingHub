using System;
using System.Collections.Generic;
using System.Linq;
using CookingHub.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;
using GameCore.HotUpdate.ReduxUI;
using Il2CppInterop.Runtime;

namespace CookingHub.Adapters;

/// <summary>
/// The stoves a queue can cook on, and starting one with its cooking screen closed. A stove is started the way the
/// screen's Cook button ends (Reducer_Web_Cooking.RA_StartCook sends Furniture.OnCookingStart), after the game's own
/// recipe check has said which dish the pot makes, so a pot it does not recognise is never started as dark food.
/// </summary>
internal sealed partial class GameCooking
{
    private const int HotPotMode = 1;
    private const int UsesPowerType = 2;

    public IReadOnlyList<IdleStove> IdleStoves()
    {
        try
        {
            return ReadIdleStoves();
        }
        catch (Exception ex)
        {
            ReportOnce("idle stove read", ex);

            return Array.Empty<IdleStove>();
        }
    }

    public bool Start(long furnitureId, IReadOnlyList<int> recipeIds) => Press("start", () =>
    {
        var game = LoadedGame();
        var furniture = FurnitureOf(game?.Agents, furnitureId);
        var cooking = furniture == null ? null : AgentTools.GetAgentComponent<CookingComponent>(furniture);

        if (game == null || furniture == null || cooking == null || cooking.IsCooking || game.Value.World._ItemManager is not { } items) return false;

        var config = game.Value.Config;
        var cook = config.Get_Config_FurnitureCook(cooking.CookFurnitureId);
        var ids = new Il2CppSystem.Collections.Generic.List<int>();

        foreach (var id in recipeIds) ids.Add(id);

        var matched = cook == null ? null : furniture.ValidateClientRecipeIds(ids, items.GetItemDataList(furnitureId), cook);
        var recipe = matched is { Count: > 0 } ? config.Get_Config_CookingRecipe(matched[0]) : null;

        if (cook == null || recipe == null)
        {
            _log($"stove {furnitureId} was not started: its pot is none of recipes {string.Join(",", recipeIds)}");

            return false;
        }

        furniture.OnCookingStart(new CookingStartData { RecipeIds = ids, CookDuration = recipe.CookTime / (cook.SpeedRate > 0 ? cook.SpeedRate : 1f) });

        return cooking.IsCooking;
    });

    private List<IdleStove> ReadIdleStoves()
    {
        var idle = new List<IdleStove>();
        var game = LoadedGame();

        if (game == null || !IsHome(game.Value.Agents) || game.Value.World._ItemManager is not { } items) return idle;

        var (world, config, agents, role) = game.Value;
        var places = PlacesFor(config, agents, role.InstanceId, GameLanguage.IsChinese());
        var openId = OpenStoveId();

        foreach (var furniture in HomeBagFurnitures(agents))
        {
            if (AgentTools.GetAgentComponent<CookingComponent>(furniture) is not { IsCooking: false, HasPendingProduct: false } cooking) continue;
            if (config.Get_Config_FurnitureCook(cooking.CookFurnitureId) is not { } cook || cook.CookMode == HotPotMode) continue;
            if (HoldsAnything(items, furniture.InstanceId)) continue;

            var allowed = AllowedOn(config, cooking.CookFurnitureId, cook);
            var stove = new Stove(
                furniture.InstanceId,
                furniture.InstanceId,
                0,
                cook.MaxFoodCount,
                false,
                false,
                cook.QualityBonus,
                allowed,
                Array.Empty<Core.Stack>(),
                Array.Empty<Forecast>(),
                places.Name(furniture.InstanceId),
                cook.SpeedRate,
                cook.HotPotCookTime,
                null,
                StackingUnits.IsPresent);

            idle.Add(new IdleStove(stove, furniture.InstanceId == openId, HasHeat(world, cook, cooking)));
        }

        return idle;
    }

    private static bool HoldsAnything(ItemManager items, long ownerId)
    {
        var held = items.GetItemDataList(ownerId);

        for (var i = 0; held != null && i < held.Count; i++)
        {
            if (held[i] is { ItemCount: > 0 }) return true;
        }

        return false;
    }

    /// <summary>The checks the Cook button makes before it starts: a fuel stove has fuel in, an electric one has power.</summary>
    private static bool HasHeat(BattleLogicWorld world, Config_FurnitureCook cook, CookingComponent cooking)
    {
        if (cook.CookType == UsesPowerType) return world._PowerManager is not { } power || !power.IsPowerOff();
        if (cook.CookType != BurnsFuelType) return true;

        var slots = cooking.GetFuelSlots();

        for (var i = 0; slots != null && i < slots.Count; i++)
        {
            if (slots[i] is { Count: > 0 }) return true;
        }

        return false;
    }

    /// <summary>The furniture whose cooking screen is open, or 0.</summary>
    private static long OpenStoveId()
    {
        var ui = GameSingleton.Existing<ReduxUISystem>();
        var screen = Il2CppType.Of<WebUI_Cooking>();

        if (ui == null || !ui.IsUIActive(screen)) return 0;

        return ui.GetStateByUI(screen)?.TryCast<State_Web_Cooking>()?.CurrentFurnitureId ?? 0;
    }
}
