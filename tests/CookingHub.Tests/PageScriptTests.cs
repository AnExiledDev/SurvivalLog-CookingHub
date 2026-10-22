using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>The window, the HUD button and the odds on the cooking screen as the player uses them, driven through page.js in Jint.</summary>
public class PageScriptTests
{
    private static readonly ViewSettings Settings = new("K", true, true);

    private static FakeGame Stocked() => new()
    {
        Stock = { S(1, Rice, hours: 10), S(2, Pork, owner: Chest), S(3, Salt) },
    };

    private static PageScript Opened(FakeGame? game = null)
    {
        game ??= Stocked();

        var page = new PageScript();

        page.Catalog(CatalogView.Write(Book));
        page.Open(CookView.Ready(game.Kitchen(), Settings));

        return page;
    }

    private static string[] ShownDishes(PageScript page) =>
        page.Text("panel().querySelectorAll('[data-dish]').map(function (n) { return n.getAttribute('data-dish'); }).join(',')").Split(',', StringSplitOptions.RemoveEmptyEntries);

    private static int Count(PageScript page, string expression) => (int)double.Parse(page.Text(expression + ".length"));

    [Fact]
    public void The_hud_button_appears_with_a_game_loaded_and_shows_food_spoiling_soon()
    {
        var page = new PageScript();

        page.OpenHud();
        page.Live(true, "K");
        page.Badge(3);
        page.Poll();

        Assert.Equal("3", page.Text("hudButton().querySelector('.ch-hud-badge').textContent"));

        page.Badge(140);
        page.Poll();

        Assert.Equal("99+", page.Text("hudButton().querySelector('.ch-hud-badge').textContent"));

        page.Click("hudButton()");

        Assert.IsType<PageCommand.Toggle>(Assert.Single(page.Sent));
    }

    [Fact]
    public void Every_dish_the_player_can_see_is_listed_and_only_a_ready_one_can_be_filled()
    {
        var page = Opened();

        Assert.Equal(new[] { "1", "2", "4" }, ShownDishes(page));
        Assert.False(page.Is("panelButton('fill','1').disabled"));
        Assert.True(page.Is("panelButton('fill','4').disabled"));
        Assert.Contains("Cooking level 5", page.Text("panel().querySelectorAll('[data-dish]').filter(function (n) { return n.getAttribute('data-dish') === '4'; })[0].textContent"));
    }

    [Fact]
    public void Fill_is_shut_while_the_stove_is_cooking_or_none_is_open()
    {
        var cooking = Stocked();
        var shut = Stocked();

        cooking.IsCooking = true;
        shut.IsStoveOpen = false;

        Assert.True(Opened(cooking).Is("panelButton('fill','1').disabled"));
        Assert.True(Opened(shut).Is("panelButton('fill','1').disabled"));
    }

    [Fact]
    public void Fill_sends_one_press_until_the_plugin_answers()
    {
        var page = Opened();

        page.Click("panelButton('fill','1')");
        page.Click("panelButton('fill','1')");

        Assert.Equal(new PageCommand.Fill(PorkRice), Assert.Single(page.Sent));

        page.Status("The pot is filled for Pork Rice.");
        page.Click("panelButton('fill','2')");

        Assert.Equal(new PageCommand.Fill(StirFry), page.Sent[^1]);
    }

    [Fact]
    public void The_filter_and_the_search_narrow_the_list()
    {
        var page = Opened();

        page.Click("panelButton('show','locked')");

        Assert.Equal(new[] { "4" }, ShownDishes(page));

        page.Click("panelButton('show','all')");
        page.Type("search", "STIR");

        Assert.Equal(new[] { "2" }, ShownDishes(page));
        Assert.Contains(page.Sent, command => command is PageCommand.Focus { IsTyping: true });
    }

    [Fact]
    public void The_pantry_shows_food_spoiling_within_a_day_in_red()
    {
        var page = Opened();

        page.Click("panelButton('tab','pantry')");

        Assert.Equal(1, Count(page, "panel().querySelectorAll('.ch-red')"));
        Assert.Contains("Rice", page.Text("panel().querySelector('.ch-red').parentElement.textContent"));
    }

    [Fact]
    public void The_settings_switches_send_the_opposite_of_what_they_show()
    {
        var page = Opened();

        page.Click("panelButton('tab','settings')");
        page.Click("panelButton('spoilfirst')");
        page.Status("ok");
        page.Click("panelButton('odds')");

        Assert.Equal(new PageCommand[] { new PageCommand.SpoilingFirst(false), new PageCommand.ShowOdds(false) }, page.Sent);
    }

    [Fact]
    public void The_odds_are_drawn_only_on_the_dishes_they_were_worked_out_for()
    {
        var game = Stocked();

        game.Forecasts.Add(new Forecast(PorkRice, true, true));
        game.Pot.Add(S(8, Rice, owner: Workbench));

        var kitchen = game.Kitchen();
        var overlay = CookView.Overlay(kitchen, kitchen.Stove ?? throw new InvalidOperationException(), isShowingOdds: true);
        var page = new PageScript();

        page.Catalog(CatalogView.Write(Book));
        page.OpenCooking(PorkRice);
        page.Cooking(overlay);
        page.Poll();

        Assert.Equal(1, Count(page, "cookingPage().querySelectorAll('.ch-odds')"));
        Assert.Contains("Perfect", page.Text("cookingPage().querySelector('.ch-odds').textContent"));

        page.Forecast(StirFry);
        page.Poll();

        Assert.Equal(0, Count(page, "cookingPage().querySelectorAll('.ch-odds')"));
    }

    [Fact]
    public void Turning_the_odds_off_clears_them_from_the_cooking_screen()
    {
        var page = new PageScript();

        page.OpenCooking(PorkRice);
        page.Cooking("{\"show\":true,\"cooking\":false,\"potEmpty\":false,\"ids\":[1],\"lines\":[{\"floor\":25,\"chances\":[0,20,39,41],\"parts\":[{\"kind\":\"fresh\",\"value\":10,\"count\":0}],\"lowest\":45}],\"nudges\":[]}");
        page.Poll();
        page.Cooking(CookView.NoOverlay());
        page.Poll();

        Assert.Equal(0, Count(page, "cookingPage().querySelectorAll('.ch-odds')"));
    }

    [Fact]
    public void A_suggested_seasoning_is_a_button_that_adds_it()
    {
        var game = Stocked();

        game.Forecasts.Add(new Forecast(PorkRice, true, true));
        game.Pot.Add(S(8, Rice, owner: Workbench));

        var kitchen = game.Kitchen();
        var page = new PageScript();

        page.Catalog(CatalogView.Write(Book));
        page.OpenCooking(PorkRice);
        page.Cooking(CookView.Overlay(kitchen, kitchen.Stove ?? throw new InvalidOperationException(), isShowingOdds: true));
        page.Poll();

        Assert.Contains("Add Salt", page.Text("cookingPage().querySelector('.ch-nudge').textContent"));

        page.Click("cookingPage().querySelector('.ch-nudge')");

        Assert.Equal(new PageCommand.Season(Salt), Assert.Single(page.Sent));
    }
}
