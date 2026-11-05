namespace CookingHub.Core;

/// <summary>
/// How long a pot started with its cooking screen closed cooks. The game's own rule (Reducer_Web_Cooking.CalcCookDuration)
/// is asked first, so a pot of several dishes in Individual mode takes as long as the Cook button would give it. When
/// that rule cannot be asked, the pot cooks for its first dish's time, which is what Mix mode gives anyway.
/// </summary>
public static class CookTime
{
    /// <summary>The first dish's time over the stove's speed; a stove with no speed set cooks at normal speed.</summary>
    public static float FirstDish(int recipeCookTime, float speedRate) => recipeCookTime / (speedRate > 0 ? speedRate : 1f);

    /// <summary>The game's time when it gave a usable one, else the first dish's time.</summary>
    public static float Pick(float? gameTime, float firstDish) =>
        gameTime is { } time && time > 0 && !float.IsNaN(time) && !float.IsInfinity(time) ? time : firstDish;
}
