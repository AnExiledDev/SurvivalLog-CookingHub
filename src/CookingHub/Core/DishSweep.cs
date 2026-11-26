using System;

namespace CookingHub.Core;

/// <summary>
/// When to look for finished dishes to put away. A dish appears when a cook finishes, and the stoves change when play
/// starts (a load or a trip), so the sweep runs after either of those, when the setting is turned on, and when a dish
/// that stayed is due another try. Play starting gets a short window of sweeps, because the world may not be ready on
/// the first frame after. Without the game's signals it sweeps on every interval, as it did before.
/// </summary>
public sealed class DishSweep
{
    public const double IntervalSeconds = 2;
    public const double AfterStartSeconds = 10;

    private bool _isAsked = true;
    private double _windowUntil = double.NegativeInfinity;
    private double _nextAt = double.NegativeInfinity;

    /// <summary>A cook finished, or the setting was turned on: one sweep, as soon as the interval allows.</summary>
    public void Ask() => _isAsked = true;

    /// <summary>Play started: sweeps for a while, until the world is surely ready.</summary>
    public void PlayStarted(double now) => _windowUntil = Math.Max(_windowUntil, now + AfterStartSeconds);

    /// <param name="retryAt">When the first dish that stayed is due another try; null when none stayed.</param>
    /// <param name="isWatching">False when the game's signals could not be hooked.</param>
    public bool IsDue(double now, double? retryAt, bool isWatching)
    {
        if (now < _nextAt) return false;
        if (!isWatching) return true;

        return _isAsked || now < _windowUntil || (retryAt is { } at && now >= at);
    }

    public void Ran(double now)
    {
        _isAsked = false;
        _nextAt = now + IntervalSeconds;
    }
}
