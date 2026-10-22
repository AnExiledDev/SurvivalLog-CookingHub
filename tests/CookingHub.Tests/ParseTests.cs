using CookingHub.Core;
using Xunit;

namespace CookingHub.Tests;

/// <summary>What the plugin reads from outside: the page's messages and the cooking screen's forecast JSON.</summary>
public class ParseTests
{
    [Fact]
    public void The_page_presses_parse_to_their_commands()
    {
        Assert.Equal(new PageCommand.Fill(12), PageCommand.Parse("slmod|cookinghub|fill|12"));
        Assert.Equal(new PageCommand.Add(30), PageCommand.Parse("slmod|cookinghub|add|30"));
        Assert.Equal(new PageCommand.Add(30), PageCommand.Parse("slmod|cookinghub|season|30"));
        Assert.Equal(new PageCommand.Repeat(), PageCommand.Parse("slmod|cookinghub|repeat"));
        Assert.Equal(new PageCommand.Queue(12), PageCommand.Parse("slmod|cookinghub|queue|12"));
        Assert.Equal(new PageCommand.Unqueue(12), PageCommand.Parse("slmod|cookinghub|unqueue|12"));
        Assert.Equal(new PageCommand.Next(), PageCommand.Parse("slmod|cookinghub|next"));
        Assert.Equal(new PageCommand.TopUp(), PageCommand.Parse("slmod|cookinghub|topup"));
        Assert.Equal(new PageCommand.Swap(20), PageCommand.Parse("slmod|cookinghub|swap|20"));
        Assert.Equal(new PageCommand.SpoilingFirst(false), PageCommand.Parse("slmod|cookinghub|spoilfirst|0"));
        Assert.Equal(new PageCommand.ShowOdds(true), PageCommand.Parse("slmod|cookinghub|odds|1"));
        Assert.Equal(new PageCommand.Note("a|b"), PageCommand.Parse("slmod|cookinghub|diag|a|b"));
    }

    [Theory]
    [InlineData("slmod|cookinghub|fill|0")]
    [InlineData("slmod|cookinghub|fill|-3")]
    [InlineData("slmod|cookinghub|fill|abc")]
    [InlineData("slmod|cookinghub|fill")]
    [InlineData("slmod|cookinghub|queue|0")]
    [InlineData("slmod|cookinghub|repeat|3")]
    [InlineData("slmod|cookinghub|odds|2")]
    [InlineData("slmod|farmhub|toggle")]
    [InlineData(null)]
    public void Anything_else_the_page_could_send_is_ignored(string? message)
    {
        Assert.Null(PageCommand.Parse(message));
    }

    [Fact]
    public void The_forecast_reads_each_line_and_whether_the_player_knows_it()
    {
        var forecasts = Forecasts.Parse("[{\"RecipeId\":12,\"Level\":2,\"IsExact\":true},{\"RecipeId\":7,\"Level\":1},{\"RecipeId\":0}]");

        Assert.Equal(new[] { new Forecast(12, true, true), new Forecast(7, false, false), new Forecast(0, false, false) }, forecasts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"RecipeId\":1}")]
    public void An_unreadable_forecast_reads_as_none(string json)
    {
        Assert.Empty(Forecasts.Parse(json));
    }
}
