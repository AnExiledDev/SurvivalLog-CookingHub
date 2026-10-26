using CookingHub.Core;
using Xunit;

namespace CookingHub.Tests;

/// <summary>
/// With the cooking screen open, each read wraps every stack of food the player owns. A screen nobody touches must not be
/// read every half second, but anything that changes what it shows must still be read on the next tick.
/// </summary>
public class ReadPaceTests
{
    private const ulong Screen = 42;

    /// <summary>Runs the pace a tick at a time, as CookSession does, and answers the ticks that read.</summary>
    private static List<float> Reads(ReadPace pace, float from, float until, Func<float, ulong>? stamp = null, Func<float, bool>? isHurried = null)
    {
        var reads = new List<float>();

        for (var now = from; now <= until; now += 0.1f)
        {
            if (!pace.IsTick(now) || !pace.IsDue(now, isHurried?.Invoke(now) ?? false, stamp?.Invoke(now) ?? Screen)) continue;

            pace.Read(now);
            reads.Add(now);
        }

        return reads;
    }

    private static ReadPace Settled()
    {
        var pace = new ReadPace();

        Assert.True(pace.IsDue(0f, false, Screen));
        pace.Read(0f);

        return pace;
    }

    [Fact]
    public void An_unchanged_screen_is_read_only_by_the_fallback()
    {
        var reads = Reads(Settled(), 0.1f, 10f);

        Assert.InRange(reads.Count, 4, 5);
        Assert.All(reads.Zip(reads.Skip(1)), pair => Assert.True(pair.Second - pair.First >= ReadPace.FallbackSeconds - 0.01f));
    }

    [Fact]
    public void Between_ticks_nothing_is_asked()
    {
        var pace = Settled();

        Assert.False(pace.IsTick(0.2f));
        Assert.False(pace.IsTick(0.45f));
        Assert.True(pace.IsTick(0.5f));
    }

    [Fact]
    public void A_change_the_game_signals_is_read_on_the_next_tick_and_the_one_after()
    {
        var pace = Settled();

        Assert.False(pace.IsDue(0.5f, false, Screen));

        pace.Changed();

        Assert.True(pace.IsDue(1.0f, false, Screen));
        pace.Read(1.0f);
        Assert.True(pace.IsDue(1.5f, false, Screen));
        pace.Read(1.5f);
        Assert.False(pace.IsDue(2.0f, false, Screen));
    }

    [Fact]
    public void A_new_screen_state_is_read_at_once_and_only_once()
    {
        var pace = Settled();

        Assert.True(pace.IsDue(0.5f, false, Screen + 1));
        pace.Read(0.5f);
        Assert.False(pace.IsDue(1.0f, false, Screen + 1));
    }

    [Fact]
    public void A_hurried_caller_reads_every_tick()
    {
        var reads = Reads(Settled(), 0.1f, 3f, isHurried: _ => true);

        Assert.InRange(reads.Count, 5, 6);
    }

    [Fact]
    public void Forget_reads_on_the_next_frame()
    {
        var pace = Settled();

        pace.Forget();

        Assert.True(pace.IsTick(0.1f));
        Assert.True(pace.IsDue(0.1f, false, Screen));
    }

    [Fact]
    public void A_press_read_pushes_the_fallback_back()
    {
        var pace = Settled();

        pace.Read(1.9f);

        Assert.False(pace.IsDue(2.4f, false, Screen));
        Assert.True(pace.IsDue(3.9f, false, Screen));
    }

    [Fact]
    public void It_never_reads_more_often_than_the_old_half_second_refresh()
    {
        var pace = Settled();
        var tick = 0;
        var reads = Reads(pace, 0.1f, 10f, stamp: _ => (ulong)tick++);

        Assert.All(reads.Zip(reads.Skip(1)), pair => Assert.True(pair.Second - pair.First >= ReadPace.TickSeconds - 0.01f));
    }
}
