using System;
using System.Collections.Generic;
using CookingHub.Core;
using UnityEngine;

namespace CookingHub.Adapters;

/// <summary>
/// Cooking Hub's game side, run once a frame: the catalog and odds page.js draws on the game's own cooking screen, the
/// presses the page asks for, and putting finished dishes away. Page messages arrive on the web view's thread, so they
/// are queued and handled here, in this frame.
/// </summary>
internal sealed class CookSession
{
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
    private readonly Action<string> _log;

    private bool _isSpoilingFirst = true;
    private bool _isShowingOdds = true;
    private bool _isPuttingDishesAway;

    private string _shownOverlay = "";
    private Cookbook? _sentCatalog;
    private float _nextRefreshAt;
    private bool _wasCookingScreenOpen;

    private LastCooked _last = LastCooked.None;
    private Cookbook? _book;
    private int _startToken;
    private float _startUntil;
    private float _nextDishSweepAt;
    private readonly HashSet<long> _toldStays = new();

    public CookSession(ICookGame game, IDishGame dishes, WebPage page, Func<DishNote, bool> toast, Func<bool> isCookingScreenOpen, Action<string> log)
    {
        _game = game;
        _dishes = dishes;
        _page = page;
        _toast = toast;
        _isCookingScreenOpen = isCookingScreenOpen;
        _log = log;
    }

    private static bool IsChinese => GameLanguage.IsChinese();

    /// <summary>Called on the main thread at start and after each settings change; the page hears them at the next refresh.</summary>
    public void UseSettings(bool isSpoilingFirst, bool isShowingOdds, bool isPuttingDishesAway)
    {
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

    /// <summary>One command that throws must not cost the rest of the frame its commands.</summary>
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
            case PageCommand.TopUp:
                Press("top up", () => CookRun.TopUp(_game, IsChinese));
                break;
            case PageCommand.Swap swap:
                Press($"swap {swap.ItemId}", () => CookRun.Swap(_game, swap.ItemId, IsChinese));
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

    private void ForgetSent()
    {
        _shownOverlay = "";
        _nextRefreshAt = 0f;
    }

    /// <summary>
    /// Reads the kitchen once, so the game's cooking screen gets its odds while one is open. A read that throws is logged
    /// by the game adapter and read as not ready.
    /// </summary>
    private void Refresh(float now)
    {
        var read = _game.Read();
        var kitchen = (read as CookRead.Ready)?.Kitchen;
        var isStoveOpen = kitchen?.Stove != null;

        _last = _last.See(kitchen?.Stove);

        if (kitchen != null) _book = kitchen.Book;
        if (kitchen?.Stove is not { IsCooking: false } || now >= _startUntil) _startUntil = 0f;

        _nextRefreshAt = now + (isStoveOpen || _wasCookingScreenOpen ? StoveRefreshSeconds : ClosedRefreshSeconds);
        SendOverlay(kitchen);
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

        return new StationExtras(last, isStarting ? _startToken : 0);
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

}
