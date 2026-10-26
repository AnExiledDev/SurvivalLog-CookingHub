using CookingHub.Core;
using Xunit;

namespace CookingHub.Tests;

public class SlicedJobTests
{
    private static IEnumerable<bool> Steps(List<int> done, int count)
    {
        for (var i = 0; i < count; i++)
        {
            done.Add(i);

            yield return true;
        }
    }

    [Fact]
    public void A_job_with_time_left_in_the_slice_runs_to_the_end()
    {
        var done = new List<int>();
        var job = new SlicedJob(Steps(done, 5));

        Assert.True(job.Advance(() => false));
        Assert.Equal(5, done.Count);
        Assert.True(job.IsDone);
    }

    [Fact]
    public void A_spent_slice_stops_the_job_until_the_next_frame()
    {
        var done = new List<int>();
        var job = new SlicedJob(Steps(done, 5));

        Assert.False(job.Advance(() => done.Count >= 2));
        Assert.Equal(2, done.Count);
        Assert.False(job.IsDone);
    }

    [Fact]
    public void Each_slice_moves_the_job_on_even_when_the_time_is_already_spent()
    {
        var done = new List<int>();
        var job = new SlicedJob(Steps(done, 3));
        var slices = 0;

        while (!job.Advance(() => true)) slices++;

        Assert.Equal(new[] { 0, 1, 2 }, done);
        Assert.True(slices <= 3);
    }

    [Fact]
    public void A_step_that_asks_to_wait_ends_the_slice()
    {
        var done = new List<int>();
        var job = new SlicedJob(Waiting(done));

        Assert.False(job.Advance(() => false));
        Assert.Equal(new[] { 0 }, done);
        Assert.True(job.Advance(() => false));
        Assert.Equal(new[] { 0, 1 }, done);
    }

    [Fact]
    public void A_finished_job_does_nothing_more()
    {
        var done = new List<int>();
        var job = new SlicedJob(Steps(done, 1));

        job.Advance(() => false);

        Assert.True(job.Advance(() => false));
        Assert.Single(done);
    }

    private static IEnumerable<bool> Waiting(List<int> done)
    {
        done.Add(0);

        yield return false;

        done.Add(1);
    }
}
