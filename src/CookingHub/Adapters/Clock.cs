using System.Diagnostics;

namespace CookingHub.Adapters;

/// <summary>Seconds since the mod loaded. Unity's own clock crosses into the game for every read; this one does not.</summary>
internal static class Clock
{
    private static readonly Stopwatch Watch = Stopwatch.StartNew();

    public static float Now => (float)Watch.Elapsed.TotalSeconds;
}
