namespace CookingHub.Core;

/// <summary>
/// When the open cooking screen's kitchen is read again. A read wraps every stack of food the player owns, so with the
/// screen open it runs on a half-second tick only when something could have changed: the game refreshed the screen's
/// bag (food or fuel moved anywhere, a dish finished), the screen's own state changed (another stove, hot pot, cooking
/// started or stopped, a new hour), or the caller is in a hurry (a start is pending, or the last read found no stove).
/// Anything else is caught by a read every couple of seconds. Nothing is read more often than the tick.
/// </summary>
public sealed class ReadPace
{
    public const float TickSeconds = 0.5f;
    public const float FallbackSeconds = 2f;

    /// <summary>The game may finish what it refreshed in a later frame, so a change is read on the next two ticks.</summary>
    private const int ReadsPerChange = 2;

    private float _nextTickAt;
    private float _fallbackAt;
    private int _owed;
    private ulong? _stamp;

    /// <summary>The game said something changed: the next ticks read, even when the screen's state looks the same.</summary>
    public void Changed() => _owed = ReadsPerChange;

    /// <summary>Whether a tick is due at all. Nothing else is asked of the game between ticks.</summary>
    public bool IsTick(float now) => now >= _nextTickAt;

    /// <summary>On a tick: whether to read now. stamp is the screen's own state, cheap to take; a new one reads.</summary>
    public bool IsDue(float now, bool isHurried, ulong stamp)
    {
        var isNewStamp = _stamp != stamp;

        _nextTickAt = now + TickSeconds;
        _stamp = stamp;

        if (_owed > 0)
        {
            _owed--;

            return true;
        }

        return isHurried || isNewStamp || now >= _fallbackAt;
    }

    /// <summary>A read was made, on a tick or for a press, so the fallback waits from now.</summary>
    public void Read(float now)
    {
        _nextTickAt = now + TickSeconds;
        _fallbackAt = now + FallbackSeconds;
    }

    /// <summary>The next tick reads whatever it finds: the settings changed, the page loaded afresh, or the screen opened.</summary>
    public void Forget()
    {
        _nextTickAt = 0f;
        _fallbackAt = 0f;
        _stamp = null;
    }
}
