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
    public void Fill_stays_open_on_a_pot_that_already_holds_food()
    {
        var game = Stocked();

        game.Pot.Add(S(8, Wheat, owner: Workbench));

        Assert.False(Opened(game).Is("panelButton('fill','1').disabled"));
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
        page.Cooking("{\"show\":true,\"odds\":true,\"cooking\":false,\"potEmpty\":false,\"ids\":[1],\"lines\":[{\"floor\":25,\"chances\":[0,20,39,41],\"parts\":[{\"kind\":\"fresh\",\"value\":10,\"count\":0}],\"lowest\":45}],\"nudges\":[]}");
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

        Assert.Equal(new PageCommand.Add(Salt), Assert.Single(page.Sent));
    }

    private const string StationBook = "[{recipeId:1,name:'Pork Rice',status:0,variantCount:1},{recipeId:2,name:'Stir Fry',status:1,variantCount:1}]";

    private static PageScript Station(FakeGame game, StationExtras? extras = null, bool isCooking = false)
    {
        var kitchen = game.Kitchen();
        var page = new PageScript();

        page.Catalog(CatalogView.Write(Book));
        page.OpenCooking();
        page.Run($"stationParts(cookingFrame, {StationBook}, {(isCooking ? "true" : "false")})");
        page.Cooking(CookView.Overlay(kitchen, kitchen.Stove ?? throw new InvalidOperationException(), isShowingOdds: true, extras));
        page.Poll();

        return page;
    }

    private static string StationTab(string word) => $"cookingPage().querySelectorAll('.ch-tab').filter(function (t) {{ return t.textContent.indexOf('{word}') === 0; }})[0]";

    [Fact]
    public void A_book_row_the_game_calls_short_gets_Load_when_storage_covers_it()
    {
        var page = Station(Stocked());

        Assert.Contains("Chest", page.Text("cookingPage().querySelectorAll('.book-item')[0].textContent"));
        Assert.Equal(1, Count(page, "cookingPage().querySelectorAll('.book-item')[0].querySelectorAll('.st-fill.ch-st')"));
        Assert.True(page.Is("cookingPage().querySelectorAll('.book-item')[0].classList.contains('ch-storage')"));
        Assert.False(page.Is("cookingPage().querySelectorAll('.book-item')[1].classList.contains('ch-storage')"), "a row the game already fills keeps the game's own button");

        page.Click("cookingPage().querySelectorAll('.book-item')[0].querySelector('.st-fill.ch-st')");

        Assert.Equal(new PageCommand.Fill(PorkRice), Assert.Single(page.Sent));
    }

    private static string SortChip(string word) => $"cookingPage().querySelector('.ch-sorts').querySelectorAll('.ch-chip').filter(function (c) {{ return c.textContent === '{word}'; }})[0]";

    [Fact]
    public void A_sort_chip_puts_the_book_row_whose_dish_gives_the_most_first_and_Default_undoes_it()
    {
        var page = Station(Stocked());

        page.Click(SortChip("Satiety"));

        Assert.True(page.Is("cookingPage().getElementById('recipeList').classList.contains('ch-sorted')"));
        Assert.Equal("0", page.Text("cookingPage().querySelectorAll('.book-item')[1].style.order"));
        Assert.Equal("2", page.Text("cookingPage().querySelectorAll('.book-item')[0].style.order"));

        page.Click(SortChip("Default"));

        Assert.False(page.Is("cookingPage().getElementById('recipeList').classList.contains('ch-sorted')"));
        Assert.Equal("", page.Text("cookingPage().querySelectorAll('.book-item')[1].style.order"));
    }

    [Fact]
    public void A_sort_chip_orders_the_cook_now_tab_by_what_the_dish_gives()
    {
        var page = Station(Stocked());

        page.Click(StationTab("Cook now"));
        page.Click(SortChip("Stamina"));

        Assert.StartsWith("Stir Fry", page.Text("cookingPage().querySelector('.ch-view').querySelectorAll('.book-nm')[0].textContent"));
    }

    [Fact]
    public void Icons_on_the_cooking_screen_load_from_the_game_web_folder()
    {
        var page = Station(Stocked());

        page.Click(StationTab("Pantry"));

        Assert.Contains("https://game.local/Res/Item/rice.png", page.Text("cookingPage().querySelector('.ch-view').querySelectorAll('img').map(function (i) { return i.src; }).join(' ')"));
    }

    private static string ViewRow(string name) =>
        $"cookingPage().querySelector('.ch-view').querySelectorAll('.book-item').filter(function (r) {{ return r.textContent.indexOf('{name}') >= 0; }})[0]";

    private const string Tooltip = "cookingPage().getElementById('recipeTooltip')";

    private static string Chips(string node) =>
        $"{node}.querySelectorAll('.ch-give').map(function (c) {{ return c.getAttribute('title') + ' ' + c.textContent + (c.querySelectorAll('svg').length ? '' : ' (no icon)'); }}).join(', ')";

    [Fact]
    public void A_cook_now_card_shows_what_the_dish_gives_at_its_foot_with_an_icon_per_stat()
    {
        var page = Station(Stocked());

        page.Click(StationTab("Cook now"));

        Assert.Equal("Satiety +40, Stamina +8, Morale +5, Health +2, Vitality +1", page.Text(Chips(ViewRow("Stir Fry"))));
    }

    [Fact]
    public void Hovering_a_cook_now_card_fills_the_page_tooltip_the_way_a_book_row_does_and_leaving_hides_it()
    {
        var page = Station(Stocked());

        page.Click(StationTab("Cook now"));
        page.Run($"fire({ViewRow("Stir Fry")}, 'mouseenter')");

        Assert.Equal("block", page.Text(Tooltip + ".style.display"));
        Assert.Equal("Stir Fry\nCan cook", page.Text(Tooltip + ".textContent"));

        page.Run($"fire({ViewRow("Stir Fry")}, 'mouseleave')");

        Assert.Equal("none", page.Text(Tooltip + ".style.display"));
    }

    [Fact]
    public void Hovering_a_pantry_card_shows_the_food_through_the_page_own_item_tooltip_and_its_stats()
    {
        var page = Station(Stocked());

        page.Click(StationTab("Pantry"));
        page.Run($"fire({ViewRow("Rice")}, 'mouseenter')");

        Assert.Equal(Rice.ToString(), page.Text("cookingFrame.contentWindow.shownItem.configId"));
        Assert.Equal("Rice", page.Text(Tooltip + ".textContent"));
        Assert.Equal("Satiety +6, Stamina +1", page.Text(Chips(ViewRow("Rice"))));
        Assert.Equal("", page.Text(Chips(ViewRow("Pork"))));
    }

    [Fact]
    public void Changing_tabs_under_the_cursor_hides_the_tooltip_its_card_opened()
    {
        var page = Station(Stocked());

        page.Click(StationTab("Cook now"));
        page.Run($"fire({ViewRow("Stir Fry")}, 'mouseenter')");
        page.Click(StationTab("Pantry"));
        page.Poll();

        Assert.Equal("none", page.Text(Tooltip + ".style.display"));
    }

    [Fact]
    public void A_known_pot_card_shows_what_its_dish_gives_even_with_odds_off_and_an_untried_one_does_not()
    {
        var game = Stocked();

        game.Forecasts.Add(new Forecast(StirFry, true, true));
        game.Pot.Add(S(8, Rice, owner: Workbench));

        var kitchen = game.Kitchen();
        var page = new PageScript();

        page.Catalog(CatalogView.Write(Book));
        page.OpenCooking(StirFry);
        page.Cooking(CookView.Overlay(kitchen, kitchen.Stove ?? throw new InvalidOperationException(), isShowingOdds: false));
        page.Poll();

        Assert.Equal(0, Count(page, "cookingPage().querySelectorAll('.ch-odds')"));
        Assert.Equal("Satiety +40, Stamina +8, Morale +5, Health +2, Vitality +1", page.Text(Chips("cookingPage().querySelector('.pot-card')")));

        page.Run($"forecast(cookingFrame, [{StirFry}], 'mystery')");
        page.Poll();

        Assert.Equal(0, Count(page, "cookingPage().querySelectorAll('.ch-give')"));
    }

    [Fact]
    public void Load_next_shows_on_a_pot_that_already_holds_food()
    {
        var game = Stocked();

        game.Pot.Add(S(8, Wheat, owner: Workbench));

        var page = Station(game, new StationExtras(null, new[] { PorkRice }, 0));

        page.Click(StationTab("Queued"));

        Assert.Equal(1, Count(page, "cookingPage().querySelector('.ch-view').querySelectorAll('.st-fill')"));
    }

    [Fact]
    public void Searching_the_book_hides_rows_that_do_not_match()
    {
        var page = Station(Stocked());

        page.Run("type(cookingPage().querySelector('.ch-search'), 'stir')");

        Assert.Equal("none", page.Text("cookingPage().querySelectorAll('.book-item')[0].style.display"));
        Assert.Equal("", page.Text("cookingPage().querySelectorAll('.book-item')[1].style.display"));
        Assert.Contains(page.Sent, command => command is PageCommand.Focus { IsTyping: true });
    }

    [Fact]
    public void The_queued_tab_loads_the_first_dish_and_removes_any()
    {
        var page = Station(Stocked(), new StationExtras(null, new[] { PorkRice, StirFry }, 0));

        page.Click(StationTab("Queued"));

        Assert.Equal("none", page.Text("cookingPage().getElementById('recipeList').style.display"));
        Assert.Contains("1. Pork Rice", page.Text("cookingPage().querySelector('.ch-view').textContent"));

        page.Click("cookingPage().querySelector('.ch-view').querySelector('.st-fill')");
        page.Status("ok");
        page.Click("cookingPage().querySelectorAll('.ch-x')[1]");

        Assert.Equal(new PageCommand[] { new PageCommand.Next(), new PageCommand.Unqueue(StirFry) }, page.Sent);
    }

    [Fact]
    public void Load_next_waits_while_the_stove_cooks()
    {
        var game = Stocked();

        game.IsCooking = true;

        var page = Station(game, new StationExtras(null, new[] { PorkRice }, 0), isCooking: true);

        page.Click(StationTab("Queued"));

        Assert.Equal(0, Count(page, "cookingPage().querySelector('.ch-view').querySelectorAll('.st-fill')"));
    }

    [Fact]
    public void Repeat_last_shows_on_an_idle_empty_stove_and_asks_for_the_last_dish()
    {
        var page = Station(Stocked(), new StationExtras(PorkRice, new List<int>(), 0));

        Assert.Contains("Pork Rice", page.Text("cookingPage().getElementById('ch-repeat').textContent"));

        page.Click("cookingPage().getElementById('ch-repeat')");

        Assert.IsType<PageCommand.Repeat>(Assert.Single(page.Sent));
    }

    [Fact]
    public void The_game_cook_button_is_pressed_once_per_start_token_and_never_on_a_cooking_stove()
    {
        var game = Stocked();

        game.Pot.Add(S(8, Rice, owner: Workbench));

        var page = Station(game, new StationExtras(PorkRice, new List<int>(), 7));

        page.Poll();

        Assert.Equal("1", page.Text("cookPresses"));

        var busy = Station(game, new StationExtras(PorkRice, new List<int>(), 9), isCooking: true);

        Assert.Equal("0", busy.Text("cookPresses"));
    }

    [Fact]
    public void Top_up_shows_with_fuel_in_storage_and_asks_for_it()
    {
        var game = Stocked();

        game.FuelStock.Add(S(50, Firewood, count: 5, owner: Chest));

        var page = Station(game);

        page.Click("cookingPage().getElementById('ch-topup')");

        Assert.IsType<PageCommand.TopUp>(Assert.Single(page.Sent));
        Assert.False(Station(Stocked()).Is("cookingPage().getElementById('ch-topup')"));
    }

    [Fact]
    public void The_window_queues_a_dish_once_and_lists_it_under_Queued()
    {
        var game = Stocked();
        var page = new PageScript();

        page.Catalog(CatalogView.Write(Book));
        page.Open(CookView.Ready(game.Kitchen(), Settings, new CookQueue(new[] { StirFry })));

        Assert.True(page.Is("panelButton('queue','2').disabled"));

        page.Click("panelButton('queue','1')");
        page.Status("ok");
        page.Click("panelButton('tab','queue')");
        page.Click("panelButton('unqueue','2')");

        Assert.Equal(new PageCommand[] { new PageCommand.Queue(PorkRice), new PageCommand.Unqueue(StirFry) }, page.Sent);
    }
}
