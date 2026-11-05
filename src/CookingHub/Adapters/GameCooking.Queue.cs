using System;
using System.Collections.Generic;
using System.Linq;
using CookingHub.Core;
using ExiledToolkit.Game;
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

    /// <summary>The GameCounter that holds the player's dish mode: 0 Mix, 1 Individual.</summary>
    private const string DishPriorityCounter = "cooking.dishPriority";

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
        var cook = CookRow(config, cooking.CookFurnitureId);
        var ids = new Il2CppSystem.Collections.Generic.List<int>();

        foreach (var id in recipeIds) ids.Add(id);

        var matched = cook == null ? null : furniture.ValidateClientRecipeIds(ids, items.GetItemDataList(furnitureId), cook);
        var recipe = matched is { Count: > 0 } ? RecipeRow(config, matched[0]) : null;

        if (cook == null || recipe == null)
        {
            _log($"stove {furnitureId} was not started: its pot is none of recipes {string.Join(",", recipeIds)}");

            return false;
        }

        var duration = CookTime.Pick(GameCookTime(game.Value.Config, game.Value.Role, furniture, items, cook, matched!), CookTime.FirstDish(recipe.CookTime, cook.SpeedRate));

        furniture.OnCookingStart(new CookingStartData { RecipeIds = ids, CookDuration = duration });

        return cooking.IsCooking;
    });

    /// <summary>
    /// The time the screen's Cook button would give this pot (Reducer_Web_Cooking.CalcCookDuration). The game times a pot
    /// from its own plan of the bench, so the plan is made the way the screen makes it: a stand-in cooking screen state
    /// holds the pot's food, the player's cooking level and dish mode, and the stove's recipes. Null when the plan is not
    /// the dishes being started, or the game could not be asked; the pot then cooks for its first dish's time.
    /// </summary>
    private float? GameCookTime(ConfigManager config, LeadingRole role, Furniture furniture, ItemManager items, Config_FurnitureCook cook, Il2CppSystem.Collections.Generic.List<int> starting)
    {
        try
        {
            if (AgentTools.GetAgentComponent<CookingRecordComponent>(role) is not { } record) return null;

            var state = new State_Web_Cooking
            {
                CookingLevel = record.CookingLevel,
                DishPriority = GameCounter.GetCounter(DishPriorityCounter),
                TagTierFloorRank = Furniture.GetCookTagTierFloorRank(),
            };

            Reducer_Web_Cooking.BuildAllowedRecipeSet(cook, state.AllowedRecipeSet);

            var held = items.GetItemDataList(furniture.InstanceId);

            for (var i = 0; held != null && i < held.Count; i++)
            {
                if (held[i] is not { ItemCount: > 0 } item) continue;

                var food = new Data_Web_Cooking_Item();

                food.ItemId.Value = item.InstanceId;
                food.ConfigId.Value = item.ItemConfigId;
                food.Count.Value = item.ItemCount;
                food.SubCategory.Value = ItemConfig(config, item.ItemConfigId)?.SubCategory ?? 0;
                state.WorkbenchItems.Add(food);
            }

            var plan = Reducer_Web_Cooking.PreMatchRecipes(state, out _);
            var planned = new List<int>();
            var started = new List<int>();

            for (var i = 0; plan != null && i < plan.Count; i++) planned.Add(plan[i].Item1);
            for (var i = 0; i < starting.Count; i++) started.Add(starting[i]);

            if (planned.Count == 0 || !planned.OrderBy(id => id).SequenceEqual(started.OrderBy(id => id)))
            {
                _log($"stove {furniture.InstanceId} cooks for its first dish's time: the game plans recipes {string.Join(",", planned)} for a pot of {string.Join(",", started)}");

                return null;
            }

            return Reducer_Web_Cooking.CalcCookDuration(state, cook, plan);
        }
        catch (Exception ex)
        {
            ReportOnce("cook time", ex);

            return null;
        }
    }

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
            if (CookRow(config, cooking.CookFurnitureId) is not { } cook || cook.CookMode == HotPotMode) continue;
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
    private long OpenStoveId()
    {
        var ui = Ui();
        var screen = CookingType;

        if (ui == null || !ui.IsUIActive(screen)) return 0;

        return ui.GetStateByUI(screen)?.TryCast<State_Web_Cooking>()?.CurrentFurnitureId ?? 0;
    }
}
