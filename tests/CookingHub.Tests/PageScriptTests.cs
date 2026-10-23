using CookingHub.Core;
using Xunit;
using static CookingHub.Tests.FakeGame;

namespace CookingHub.Tests;

/// <summary>The cooking screen's tabs, presses and odds as the player uses them, driven through page.js in Jint.</summary>
public class PageScriptTests
{
    private static FakeGame Stocked() => new()
    {
        Stock = { S(1, Rice, hours: 10), S(2, Pork, owner: Chest), S(3, Salt) },
    };

    private static int Count(PageScript page, string expression) => (int)double.Parse(page.Text(expression + ".length"));

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

        Assert.Equal("Satiety +40, Stamina +8, Morale +5, Fitness +2, Life +1", page.Text(Chips(ViewRow("Stir Fry"))));
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

    private static string BookRow(string name) =>
        $"cookingPage().querySelectorAll('.book-item').filter(function (r) {{ return r.textContent.indexOf('{name}') >= 0; }})[0]";

    [Fact]
    public void A_recipe_book_row_shows_what_the_dish_gives_like_its_cook_now_card()
    {
        var page = Station(Stocked());

        Assert.Equal("Satiety +40, Stamina +8, Morale +5, Fitness +2, Life +1", page.Text(Chips(BookRow("Stir Fry"))));
    }

    [Fact]
    public void Cook_now_and_pantry_cards_name_their_dish_or_food_for_info_pack()
    {
        var page = Station(Stocked());

        page.Click(StationTab("Cook now"));

        Assert.Equal(FancyBowl.ToString(), page.Text(ViewRow("Stir Fry") + ".getAttribute('data-slip-item')"));

        page.Click(StationTab("Pantry"));

        Assert.Equal(Rice.ToString(), page.Text(ViewRow("Rice") + ".getAttribute('data-slip-item')"));
    }

    [Fact]
    public void With_info_pack_installed_a_card_leaves_the_page_tooltip_to_info_pack()
    {
        var page = Station(Stocked());

        page.Run("cookingFrame.contentWindow.__slipTagged = { configId: 0 }");
        page.Click(StationTab("Cook now"));
        page.Run($"fire({ViewRow("Stir Fry")}, 'mouseenter')");

        Assert.Equal("none", page.Text(Tooltip + ".style.display"));

        page.Click(StationTab("Pantry"));
        page.Run($"fire({ViewRow("Rice")}, 'mouseenter')");

        Assert.Equal("none", page.Text(Tooltip + ".style.display"));
        Assert.True(page.Is("!cookingFrame.contentWindow.shownItem"));
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
        Assert.Equal("Satiety +40, Stamina +8, Morale +5, Fitness +2, Life +1", page.Text(Chips("cookingPage().querySelector('.pot-card')")));

        page.Run($"forecast(cookingFrame, [{StirFry}], 'mystery')");
        page.Poll();

        Assert.Equal(0, Count(page, "cookingPage().querySelectorAll('.ch-give')"));
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
    public void Repeat_last_shows_on_an_idle_empty_stove_and_asks_for_the_last_dish()
    {
        var page = Station(Stocked(), new StationExtras(PorkRice, 0, Array.Empty<int>()));

        Assert.Contains("Pork Rice", page.Text("cookingPage().getElementById('ch-repeat').textContent"));

        page.Click("cookingPage().getElementById('ch-repeat')");

        Assert.IsType<PageCommand.Repeat>(Assert.Single(page.Sent));
    }

    [Fact]
    public void The_game_cook_button_is_pressed_once_per_start_token_and_never_on_a_cooking_stove()
    {
        var game = Stocked();

        game.Pot.Add(S(8, Rice, owner: Workbench));

        var page = Station(game, new StationExtras(PorkRice, 7, Array.Empty<int>()));

        page.Poll();

        Assert.Equal("1", page.Text("cookPresses"));

        var busy = Station(game, new StationExtras(PorkRice, 9, Array.Empty<int>()), isCooking: true);

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
    public void The_cooking_screen_has_a_Queued_tab_counting_the_stove_queue()
    {
        var page = Station(Stocked(), new StationExtras(null, 0, new[] { StirFry, PorkRice }));

        Assert.Equal("Recipe Book,Cook now,Queued,Pantry", page.Text("cookingPage().querySelectorAll('.ch-tab').map(function (t) { return t.getAttribute('data-label').split('|')[0]; }).join(',')"));
        Assert.Equal("2", page.Text(StationTab("Queued") + ".getAttribute('data-label').split('|')[1]"));
    }

    [Fact]
    public void Queue_sits_beside_Load_on_book_rows_and_cook_now_cards_and_asks_for_that_dish()
    {
        var page = Station(Stocked());

        Assert.Equal("Load,Queue", page.Text("cookingPage().querySelectorAll('.book-item')[0].querySelectorAll('.ch-st').filter(function (b) { return b.classList.contains('interactive-btn'); }).map(function (b) { return b.textContent; }).join(',')"));

        page.Click(QueueOnBookRow(1));

        Assert.Equal(new PageCommand.Queue(StirFry), Assert.Single(page.Sent));

        page.Status("Queued Stir Fry.");
        page.Click(StationTab("Cook now"));
        page.Click(ViewRow("Pork Rice") + ".querySelector('.ch-x')");

        Assert.Equal(new PageCommand.Queue(PorkRice), page.Sent[1]);
    }

    [Fact]
    public void A_full_queue_offers_no_Queue_button()
    {
        var page = Station(Stocked(), new StationExtras(null, 0, Enumerable.Repeat(StirFry, CookQueue.Limit).ToList()));

        Assert.Equal(0, Count(page, "cookingPage().querySelectorAll('.book-item')[1].querySelectorAll('.ch-x')"));
    }

    [Fact]
    public void The_Queued_tab_lists_the_queue_in_order_and_Remove_takes_one_off()
    {
        var page = Station(Stocked(), new StationExtras(null, 0, new[] { StirFry, PorkRice }));

        page.Click(StationTab("Queued"));

        Assert.Equal("1. Stir Fry", page.Text("cookingPage().querySelector('.ch-view').querySelectorAll('.book-nm')[0].textContent"));
        Assert.Equal("2. Pork Rice", page.Text("cookingPage().querySelector('.ch-view').querySelectorAll('.book-nm')[1].textContent"));

        page.Click(ViewRow("Pork Rice") + ".querySelector('.ch-x')");

        Assert.Equal(new PageCommand.Unqueue(PorkRice), Assert.Single(page.Sent));
    }

    private static string QueueOnBookRow(int index) => $"cookingPage().querySelectorAll('.book-item')[{index}].querySelector('.ch-x')";

    [Fact]
    public void Load_sends_one_press_until_the_plugin_answers()
    {
        var page = Station(Stocked());

        page.Click(StationTab("Cook now"));
        page.Click(LoadOn("Stir Fry"));
        page.Click(LoadOn("Stir Fry"));

        Assert.Equal(new PageCommand.Fill(StirFry), Assert.Single(page.Sent));

        page.Status("The pot is filled for Stir Fry.");
        page.Click(LoadOn("Stir Fry"));

        Assert.Equal(2, page.Sent.Count);
    }

    [Fact]
    public void A_press_result_shows_as_a_toast_over_the_game_and_an_empty_one_hides_it()
    {
        var page = new PageScript();

        page.Status("The pot is filled for Stir Fry.");

        Assert.Equal("block", page.Text("document.getElementById('ch-toast').style.display"));
        Assert.Equal("The pot is filled for Stir Fry.", page.Text("document.getElementById('ch-toast').textContent"));

        page.Status("");

        Assert.Equal("none", page.Text("document.getElementById('ch-toast').style.display"));
    }

    [Fact]
    public void The_hud_gets_no_cooking_hub_button_and_the_script_offers_no_window()
    {
        var page = new PageScript();

        page.OpenHud();
        page.Poll();

        Assert.False(page.Is("hudButton()"));
        Assert.False(page.Is("window.__slcookinghub.open || window.__slcookinghub.live || window.__slcookinghub.badge"));
        Assert.False(page.Is("document.getElementById('ch-root')"));
    }

    private static string LoadOn(string name) => ViewRow(name) + ".querySelector('.st-fill')";
}
