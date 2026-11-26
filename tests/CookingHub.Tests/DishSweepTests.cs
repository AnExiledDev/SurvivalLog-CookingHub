using CookingHub.Core;
using Xunit;

namespace CookingHub.Tests;

/// <summary>Finished dishes are looked for when something could have made one, not on a timer.</summary>
public class DishSweepTests
{
    private const double Start = 1000;

    private static DishSweep AfterFirstSweep()
    {
        var sweep = new DishSweep();

        sweep.Ran(Start);

        return sweep;
    }

    [Fact]
    public void The_first_frame_sweeps_once_then_nothing_until_something_happens()
    {
        var sweep = new DishSweep();

        Assert.True(sweep.IsDue(Start, null, isWatching: true));

        sweep.Ran(Start);

        Assert.False(sweep.IsDue(Start + 60, null, isWatching: true));
    }

    [Fact]
    public void A_finished_cook_sweeps_once_the_interval_allows()
    {
        var sweep = AfterFirstSweep();

        sweep.Ask();

        Assert.False(sweep.IsDue(Start + DishSweep.IntervalSeconds - 0.5, null, isWatching: true));
        Assert.True(sweep.IsDue(Start + DishSweep.IntervalSeconds, null, isWatching: true));
    }

    /// <summary>The world may not be ready on the first frame after a load, so play starting sweeps for a while.</summary>
    [Fact]
    public void Play_starting_sweeps_for_a_while_then_stops()
    {
        var sweep = AfterFirstSweep();
        var started = Start + 100;

        sweep.PlayStarted(started);
        sweep.Ran(started);

        Assert.True(sweep.IsDue(started + DishSweep.IntervalSeconds, null, isWatching: true));

        sweep.Ran(started + DishSweep.AfterStartSeconds - 1);

        Assert.False(sweep.IsDue(started + DishSweep.AfterStartSeconds + 1, null, isWatching: true));
    }

    [Fact]
    public void A_dish_that_stayed_is_swept_again_when_its_wait_is_over()
    {
        var sweep = AfterFirstSweep();
        var retryAt = Start + 30;

        Assert.False(sweep.IsDue(retryAt - 1, retryAt, isWatching: true));
        Assert.True(sweep.IsDue(retryAt, retryAt, isWatching: true));
    }

    [Fact]
    public void Without_the_game_signals_it_sweeps_on_every_interval()
    {
        var sweep = AfterFirstSweep();

        Assert.False(sweep.IsDue(Start + 1, null, isWatching: false));
        Assert.True(sweep.IsDue(Start + DishSweep.IntervalSeconds, null, isWatching: false));
    }
}
