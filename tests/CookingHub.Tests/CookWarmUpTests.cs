using CookingHub.Core;
using Xunit;

namespace CookingHub.Tests;

public class CookWarmUpTests
{
    /// <summary>
    /// The warm-up only helps the first cooking screen if it goes down the same paths: both book rows drawn, a second
    /// patch that keeps them, a pot planned from the food the queue leaves, the pantry, the forecast and a page command.
    /// </summary>
    [Fact]
    public void The_warm_up_draws_patches_and_plans_like_a_real_stove()
    {
        var result = CookWarmUp.Run();

        Assert.Equal(2, result.BookRows);
        Assert.Equal(2, result.KeptRows);
        Assert.Equal(2, result.PlannedTakes);
        Assert.Equal(3, result.Shelves);
        Assert.Equal(1, result.Forecasts);
        Assert.True(result.HasCommand);
    }

    [Fact]
    public void The_warm_up_can_run_again_and_works_the_same_out()
    {
        Assert.Equal(CookWarmUp.Run(), CookWarmUp.Run());
    }
}
