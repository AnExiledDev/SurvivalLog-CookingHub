using System;
using System.Collections.Generic;
using CookingHub.Core;
using UnityEngine;

namespace CookingHub.Adapters;

/// <summary>
/// The window's game side, run once a frame: its open state, the view and catalog it sends to page.js, the HUD
/// button's badge, the odds drawn on the game's own cooking screen, and the presses the page asks for. Page messages
/// arrive on the web view's thread, so they are queued and handled here, in this frame.
/// </summary>
internal sealed class CookSession
{
    private const float OpenRefreshSeconds = 1f;
    private const float StoveRefreshSeconds = 0.5f;
    private const float ClosedRefreshSeconds = 5f;
    private const float StartWindowSeconds = 4f;
    private const float DishSweepSeconds = 2f;

    private readonly ICookGame _game;
    private readonly IDishGame _dishes;
    private readonly WebPage _page;
    private readonly Func<DishNote, bool> _toast;
    private readonly Func<bool> _isCookingScreenOpen;
    private readonly WebKeyboard _keyboard = new();
    private readonly Action<bool> _saveSpoilingFirst;
    private readonly Action<bool> _saveShowOdds;
    private readonly Action<string> _saveQueue;
    private readonly Action<string> _log;

    private KeyCode _panelKey;
    private bool _isSpoilingFirst = true;
    private bool _isShowingOdds = true;
    private bool _isPuttingDishesAway;

    private bool _isOpen;
    private string _shownView = "";
    private string _shownOverlay = "";
    private Cookbook? _sentCatalog;
    private int? _sentBadge;
    private bool? _sentLive;
    private float _nextRefreshAt;
    private bool _wasCookingScreenOpen;

    private CookQueue _queue;
    private LastCooked _last = LastCooked.None;
    private Cookbook? _book;
    private int _startToken;
    private float _startUntil;
    private float _nextDishSweepAt;
    private readonly HashSet<long> _toldStays = new();

    public CookSession(ICookGame game, IDishGame dishes, WebPage page, Func<DishNote, bool> toast, Func<bool> isCookingScreenOpen, Action<bool> saveSpoilingFirst, Action<bool> saveShowOdds, CookQueue queue, Action<string> saveQueue, Action<string> log)
    {
        _game = game;
        _dishes = dishes;
        _page = page;
        _toast = toast;
        _isCookingScreenOpen = isCookingScreenOpen;
        _saveSpoilingFirst = saveSpoilingFirst;
        _saveShowOdds = saveShowOdds;
        _queue = queue;
        _saveQueue = saveQueue;
        _log = log;
    }

    private static bool IsChinese => GameLanguage.IsChinese();

    /// <summary>Called on the main thread at start and after each settings change; the page hears them at the next refresh.</summary>
    public void UseSettings(KeyCode panelKey, bool isSpoilingFirst, bool isShowingOdds, bool isPuttingDishesAway)
    {
        _panelKey = panelKey;
        _isSpoilingFirst = isSpoilingFirst;
        _isShowingOdds = isShowingOdds;
        _isPuttingDishesAway = isPuttingDishesAway;
        ForgetSent();
    }

    public void Update()
    {
        var now = Time.unscaledTime;

        _page.KeepInstalled(now);

        while (PageMessages.TryTake(out var command)) HandleSafely(command);

        if (!_keyboard.IsClaimed && _panelKey != KeyCode.None && Input.GetKeyDown(_panelKey))
        {
            Toggle();

            return;
        }

        if (_isOpen && !_keyboard.IsClaimed && Input.GetKeyDown(KeyCode.Escape))
        {
            Close();

            return;
        }

        if (_isPuttingDishesAway && now >= _nextDishSweepAt) PutDishesAway(now);
        if (now >= _nextRefreshAt || HasCookingScreenFlipped()) Refresh(now);
    }

    /// <summary>The game's cooking screen opening or closing is read at once, so the station view is drawn without waiting.</summary>
    private bool HasCookingScreenFlipped()
    {
        var isOpen = _isCookingScreenOpen();

        if (isOpen == _wasCookingScreenOpen) return false;

        _wasCookingScreenOpen = isOpen;

        return true;
    }

    /// <summary>
    /// Every few seconds, moves each finished dish out of its stove into the storage its tags want. Where each went is
    /// shown as a toast when Exiled's Toolkit can show one, else on the page; a dish no storage takes is retried later
    /// but told about only once.
    /// </summary>
    private void PutDishesAway(float now)
    {
        _nextDishSweepAt = now + DishSweepSeconds;

        foreach (var stored in DishStore.PutAway(_dishes))
        {
            if (stored is Stored.Stays && !_toldStays.Add(stored.Dish.InstanceId)) continue;

            var note = DishStore.Note(stored, IsChinese);

            _log(note.Text);
            if (!_toast(note)) _page.Status(note.Text);
        }
    }

    /// <summary>One command that throws must not cost the rest of the frame its commands or the key checks.</summary>
    private void HandleSafely(PageCommand command)
    {
        try
        {
            Handle(command);
        }
        catch (Exception ex)
        {
            _log($"page command {command} failed: {ex}");
            _page.Status(Texts.Failed.Pick(IsChinese));
        }
    }

    private void Handle(PageCommand command)
    {
        switch (command)
        {
            case PageCommand.Toggle:
                Toggle();
                break;
            case PageCommand.Close:
                Close();
                break;
            case PageCommand.Ready:
                _log("page script ready");
                _page.ForgetWords();
                _sentCatalog = null;
                ForgetSent();
                break;
            case PageCommand.NoScript:
                _page.Install();
                break;
            case PageCommand.Note note:
                _log("page: " + note.Text);
                break;
            case PageCommand.Focus focus:
                if (focus.IsTyping) _keyboard.Claim();
                else _keyboard.Release();
                break;
            case PageCommand.Fill fill:
                Press($"fill {fill.RecipeId}", () => CookRun.Fill(_game, fill.RecipeId, _isSpoilingFirst, IsChinese));
                break;
            case PageCommand.Add add:
                Press($"add {add.ItemId}", () => CookRun.Add(_game, add.ItemId, IsChinese));
                break;
            case PageCommand.Repeat:
                Press("repeat", Repeat);
                break;
            case PageCommand.Next:
                Press("next", LoadNext);
                break;
            case PageCommand.TopUp:
                Press("top up", () => CookRun.TopUp(_game, IsChinese));
                break;
            case PageCommand.Queue queue:
                Press($"queue {queue.RecipeId}", () => ChangeQueue(queue.RecipeId, isAdding: true));
                break;
            case PageCommand.Unqueue unqueue:
                Press($"unqueue {unqueue.RecipeId}", () => ChangeQueue(unqueue.RecipeId, isAdding: false));
                break;
            case PageCommand.Swap swap:
                Press($"swap {swap.ItemId}", () => CookRun.Swap(_game, swap.ItemId, IsChinese));
                break;
            case PageCommand.SpoilingFirst spoilingFirst:
                _isSpoilingFirst = spoilingFirst.IsOn;
                _saveSpoilingFirst(spoilingFirst.IsOn);
                Say((spoilingFirst.IsOn ? Texts.SpoilingFirstOn : Texts.SpoilingFirstOff).Pick(IsChinese));
                break;
            case PageCommand.ShowOdds showOdds:
                _isShowingOdds = showOdds.IsOn;
                _saveShowOdds(showOdds.IsOn);
                Say((showOdds.IsOn ? Texts.OddsOn : Texts.OddsOff).Pick(IsChinese));
                break;
            default:
                throw new InvalidOperationException("unhandled page command " + command);
        }
    }

    /// <summary>
    /// Fills the pot for the dish this stove last started, then asks the page to press the game's own Cook button: the
    /// start token counts up once per filled pot, and stays up only until the stove cooks or a few seconds pass.
    /// </summary>
    private Pressed Repeat()
    {
        var pressed = CookRun.Repeat(_game, _last, _isSpoilingFirst, IsChinese);

        if (!pressed.IsDone || _book == null || _last.WatchedId is not { } stoveId || _last.For(stoveId) is not { } recipeId) return pressed;

        _startToken++;
        _startUntil = Time.unscaledTime + StartWindowSeconds;

        return _book.Recipes.TryGetValue(recipeId, out var recipe) ? new Pressed(true, Texts.Starting.Say(IsChinese, recipe.Name)) : pressed;
    }

    /// <summary>Fills the pot for the first queued dish this stove can cook; a dish that went in leaves the queue.</summary>
    private Pressed LoadNext()
    {
        var (pressed, filled) = CookRun.Next(_game, _queue, _isSpoilingFirst, IsChinese);

        if (filled is { } recipeId) SaveQueue(_queue.Remove(recipeId));

        return pressed;
    }

    private Pressed ChangeQueue(int recipeId, bool isAdding)
    {
        if (_book == null) return new Pressed(false, Texts.NoGame.Pick(IsChinese));

        var (queue, pressed) = _queue.Press(_book, recipeId, isAdding, IsChinese);

        SaveQueue(queue);

        return pressed;
    }

    private void SaveQueue(CookQueue queue)
    {
        if (ReferenceEquals(queue, _queue)) return;

        _queue = queue;
        _saveQueue(queue.Write());
    }

    private void ForgetSent()
    {
        _sentLive = null;
        _sentBadge = null;
        _shownView = "";
        _shownOverlay = "";
        _nextRefreshAt = 0f;
    }

    private void Say(string message)
    {
        _log(message);
        _page.Status(message);
        Refresh(Time.unscaledTime);
    }

    /// <summary>
    /// Reads the kitchen once: the HUD button shows while a game is loaded, its badge counts food spoiling soon, the
    /// game's cooking screen gets its odds while one is open, and an open window is redrawn when its view changed. A
    /// read that throws is logged by the game adapter and read as not ready.
    /// </summary>
    private void Refresh(float now)
    {
        var read = _game.Read();
        var kitchen = (read as CookRead.Ready)?.Kitchen;
        var isStoveOpen = kitchen?.Stove != null;

        _last = _last.See(kitchen?.Stove);

        if (kitchen != null) _book = kitchen.Book;
        if (kitchen?.Stove is not { IsCooking: false } || now >= _startUntil) _startUntil = 0f;

        _nextRefreshAt = now + (isStoveOpen || _wasCookingScreenOpen ? StoveRefreshSeconds : _isOpen ? OpenRefreshSeconds : ClosedRefreshSeconds);
        SendLive(read is not CookRead.NoGame);
        SendBadge(kitchen == null ? 0 : Pantry.SpoilingSoon(kitchen, Finder.SpoilingHours));
        SendOverlay(kitchen);

        if (_isOpen) Show(read, isOpening: false);
    }

    private void SendLive(bool isLive)
    {
        if (isLive == _sentLive) return;
        if (_page.Live(isLive, KeyName())) _sentLive = isLive;

        _log($"HUD button {(isLive ? "shown" : "hidden")}");
    }

    private void SendBadge(int count)
    {
        if (count == _sentBadge) return;
        if (_page.Badge(count)) _sentBadge = count;
    }

    /// <summary>The odds on the game's cooking screen, sent only when they changed. The catalog goes first, since nudges name items by id.</summary>
    private void SendOverlay(Kitchen? kitchen)
    {
        var overlay = kitchen?.Stove is { } stove ? CookView.Overlay(kitchen, stove, _isShowingOdds, Extras(stove)) : CookView.NoOverlay();

        if (overlay == _shownOverlay) return;
        if (kitchen != null) SendCatalog(kitchen.Book);
        if (_page.Cooking(overlay)) _shownOverlay = overlay;
    }

    /// <summary>The start token goes out only while the pot forecasts the repeated dish, so a pot the game half filled is never started.</summary>
    private StationExtras Extras(Stove stove)
    {
        var last = _last.For(stove.FurnitureId);
        var isStarting = _startUntil > 0f && last != null && _last.Pending == last;

        return new StationExtras(last, _queue.For(stove), isStarting ? _startToken : 0);
    }

    private void Toggle()
    {
        if (_isOpen) Close();
        else Open();
    }

    /// <summary>With no game loaded there is nothing to show and the window stays shut; anything else opens it.</summary>
    private void Open()
    {
        var read = _game.Read();

        if (read is CookRead.NoGame)
        {
            _page.Status(Texts.NoGame.Pick(IsChinese));

            return;
        }

        _isOpen = true;
        InputGate.PanelOpened();
        Show(read, isOpening: true);
        _log("window opened");
    }

    private void Close()
    {
        if (!_isOpen) return;

        _isOpen = false;
        _keyboard.Release();
        InputGate.PanelClosed();
        _page.Close();
    }

    /// <summary>The catalog goes first, once per game and language, since the view names items only by id.</summary>
    private void Show(CookRead read, bool isOpening)
    {
        var settings = new ViewSettings(KeyName(), _isSpoilingFirst, _isShowingOdds);
        string view;

        switch (read)
        {
            case CookRead.Ready ready:
                SendCatalog(ready.Kitchen.Book);
                view = CookView.Ready(ready.Kitchen, settings, _queue);
                break;
            case CookRead.NotReady notReady:
                view = CookView.NotReady(Texts.NotReady.Say(IsChinese, notReady.Reason), settings);
                break;
            default:
                Close();
                return;
        }

        if (isOpening)
        {
            _shownView = view;
            _page.Open(view);

            return;
        }

        if (view == _shownView) return;

        _shownView = view;
        _page.Show(view);
    }

    private void SendCatalog(Cookbook book)
    {
        if (ReferenceEquals(book, _sentCatalog)) return;
        if (_page.Catalog(CatalogView.Write(book))) _sentCatalog = book;
    }

    /// <summary>Runs one press and shows what it did, then reads again so the pot and the odds show the change at once.</summary>
    private void Press(string name, Func<Pressed> press)
    {
        string message;

        try
        {
            var pressed = press();

            message = pressed.Message;
            _log($"{name}: {(pressed.IsDone ? "done" : "refused")} -> {message}");
        }
        catch (Exception ex)
        {
            message = Texts.Failed.Pick(IsChinese);
            _log($"{name} failed: {ex}");
        }

        _page.Status(message);
        Refresh(Time.unscaledTime);
    }

    private string KeyName() => _panelKey == KeyCode.None ? "" : _panelKey.ToString();
}
