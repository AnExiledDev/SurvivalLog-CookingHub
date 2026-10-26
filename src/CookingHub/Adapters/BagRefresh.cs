using System;
using GameCore.HotUpdate.ReduxUI;
using HarmonyLib;

namespace CookingHub.Adapters;

/// <summary>
/// The cooking screen's bag refresh (Ac_Cooking_RefreshBag) matches every recipe against the pot twice, and the game
/// sends it once per item added or taken while the screen is open. A press that moves several stacks would send it
/// once per move; while a batch of moves is held, each refresh is only noted, and one goes out when the batch ends.
/// Static because a Harmony prefix has nowhere else to look; main thread only.
/// </summary>
internal static class BagRefresh
{
    private static bool _isHooked;
    private static int _depth;
    private static bool _isAsked;
    private static Action<string> _log = _ => { };

    public static void Apply(Harmony harmony, Action<string> log)
    {
        _log = log;

        try
        {
            harmony.Patch(AccessTools.Method(typeof(Ac_Cooking_RefreshBag), nameof(Ac_Cooking_RefreshBag.SendAction)), prefix: new HarmonyMethod(typeof(BagRefresh), nameof(HoldWhileBatched)));
            _isHooked = true;
        }
        catch (Exception ex)
        {
            log("could not hook the cooking screen's bag refresh, each food move refreshes it on its own: " + ex.Message);
        }
    }

    /// <summary>After a move: refreshes the cooking screen now, or once at the end of the batch it is part of.</summary>
    public static void Ask()
    {
        if (_isHooked && _depth > 0)
        {
            _isAsked = true;

            return;
        }

        Ac_Cooking_RefreshBag.SendAction();
    }

    /// <summary>Holds every bag refresh until the returned batch is disposed, which sends one if any was asked for.</summary>
    public static Batch Hold()
    {
        _depth++;

        return new Batch();
    }

    /// <summary>The game's own refresh goes ahead as if the mod were not here unless a batch is open.</summary>
    private static bool HoldWhileBatched()
    {
        if (_depth == 0) return true;

        _isAsked = true;

        return false;
    }

    /// <summary>Runs from a using block's end, so a failure is logged here rather than thrown over the batch's own result.</summary>
    private static void Release()
    {
        if (_depth == 0 || --_depth > 0 || !_isAsked) return;

        _isAsked = false;

        try
        {
            Ac_Cooking_RefreshBag.SendAction();
        }
        catch (Exception ex)
        {
            _log("bag refresh after a batch failed: " + ex.Message);
        }
    }

    public readonly struct Batch : IDisposable
    {
        public void Dispose() => Release();
    }
}
