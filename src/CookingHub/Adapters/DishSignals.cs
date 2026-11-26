using ExiledToolkit.Game;
using GameCore.HotUpdate;
using GameCore.HotUpdate.Battle.Logic;

namespace CookingHub.Adapters;

/// <summary>
/// What tells the dish sweep to look. Furniture.OnCookingComplete (0x314b420) handles the game's CookingComplete event
/// and puts the dish in the stove. AgentManager.StartBattle (0x310c360) runs at every start of play: a load, a new game
/// and each trip out and home. Neither has a relative jump or call in its first 14 bytes. Both hooks only set a flag the
/// session reads on its next frame. Static because Harmony patches are; main thread only.
/// </summary>
internal static class DishSignals
{
    private static bool _isCooked;
    private static bool _isStarted;

    /// <summary>False when a hook could not go in; the sweep then runs every couple of seconds.</summary>
    public static bool IsWatching { get; private set; }

    public static void Apply(PatchKit hooks)
    {
        var isCookHooked = hooks.Patch(typeof(DishSignals), typeof(Furniture), nameof(Furniture.OnCookingComplete), postfix: nameof(AfterCook),
            consequence: "finished dishes are looked for every couple of seconds instead");
        var isStartHooked = hooks.Patch(typeof(DishSignals), typeof(AgentManager), nameof(AgentManager.StartBattle), new[] { typeof(GameSaveData), typeof(GameMode) },
            postfix: nameof(AfterStart), consequence: "finished dishes are looked for every couple of seconds instead");

        IsWatching = isCookHooked && isStartHooked;
    }

    public static bool TakeCooked()
    {
        var isCooked = _isCooked;

        _isCooked = false;

        return isCooked;
    }

    public static bool TakeStarted()
    {
        var isStarted = _isStarted;

        _isStarted = false;

        return isStarted;
    }

    private static void AfterCook() => _isCooked = true;

    private static void AfterStart() => _isStarted = true;
}
