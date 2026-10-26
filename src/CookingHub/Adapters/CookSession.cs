using System;
using System.Collections.Generic;
using CookingHub.Core;

namespace CookingHub.Adapters;

/// <summary>
/// Cooking Hub's game side, run once a frame: the catalog and odds page.js draws on the game's own cooking screen, the
/// presses the page asks for, and putting finished dishes away. Page messages arrive on the web view's thread, so they
/// are queued and handled here, in this frame.
/// </summary>
internal sealed class CookSession
{
    private const float ClosedRefreshSeconds = 5f;
    private const float StartWindowSeconds = 4f;
    private const float DishSweepSeconds = 2f;
    private const float QueueSeconds = 1f;

    private readonly ICookGame _game;
    private readonly IDishGame _dishes;
    private readonly WebPage _page;
    private readonly Func<DishNote, bool> _toast;
    private readonly IKitchenWatch _watch;
    private readonly WebKeyboard _keyboard = new();
    private readonly Action<string> _log;

    private bool _isSpoilingFirst = true;
    private bool _isShowingOdds = true;
    private bool _isShowingUnlearned;
    private bool _isPuttingDishesAway;

    private readonly OverlayDelta _delta = new();
    private ulong? _shownPrint;
    private Cookbook? _sentCatalog;
    private float _nextRefreshAt;
    private bool _wasCookingScreenOpen;
    private readonly ReadPace _pace = new();
    private bool _wasStoveRead;
    private bool _wasHotPot;

    private LastCooked _last = LastCooked.None;
    private Cookbook? _book;
    private int _startToken;
    private float _startUntil;
    private long? _startStoveId;
    private IReadOnlyList<int> _startIds = Array.Empty<int>();
    private CookQueue _queue = CookQueue.Empty;
    private float _nextQueueAt;
    private HashSet<long> _idleBefore = new();
    private readonly Dictionary<long, string> _toldWaiting = new();
    private float _nextDishSweepAt;
    private readonly HashSet<long> _toldStays = new();
    private readonly DishRetry _dishRetry = new();

    public CookSession(ICookGame game, IDishGame dishes, IKitchenWatch watch, WebPage page, Func<DishNote, bool> toast, Action<string> log)
    {
        _game = game;
        _dishes = dishes;
        _watch = watch;
        _page = page;
        _toast = toast;
        _log = log;
    }

    private static bool IsChinese => GameLanguage.IsChinese();

    /// <summary>Called on the main thread at start and after each settings change; the page hears them at the next refresh.</summary>
    public void UseSettings(bool isSpoilingFirst, bool isShowingOdds, bool isPuttingDishesAway, bool isShowingUnlearned)
    {
        _isSpoilingFirst = isSpoilingFirst;
        _isShowingOdds = isShowingOdds;
        _isShowingUnlearned = isShowingUnlearned;
        _isPuttingDishesAway = isPuttingDishesAway;
        ForgetSent();
    }

    public void Update()
    {
        var now = Clock.Now;

        _page.KeepInstalled(now);

        while (PageMessages.TryTake(out var command)) HandleSafely(command);

        _watch.AdvanceCatalog();

        if (_isPuttingDishesAway && now >= _nextDishSweepAt) PutDishesAway(now);
        if (!_queue.IsEmpty && now >= _nextQueueAt) CookQueued(now);

        if (HasCookingScreenFlipped())
        {
            _pace.Forget();
            Refresh(now);
        }
        else if (_wasCookingScreenOpen)
        {
            RefreshOpenWhenDue(now);
        }
        else if (now >= _nextRefreshAt)
        {
            Refresh(now);
        }
    }

    /// <summary>
    /// With the cooking screen open, the kitchen is read on a half-second tick only when something could have changed
    /// (ReadPace): the game refreshed the screen's bag, the screen's own state moved, or a start, an unread stove or a hot
    /// pot needs every tick.
    /// </summary>
    private void RefreshOpenWhenDue(float now)
    {
        if (!_pace.IsTick(now)) return;
        if (BagRefresh.TakeChanged()) _pace.Changed();

        var isHurried = _startUntil > 0f || !_wasStoveRead || _wasHotPot;

        if (_pace.IsDue(now, isHurried, _watch.ScreenStamp())) Refresh(now);
    }

    /// <summary>The game's cooking screen opening or closing is read at once, so the station view is drawn without waiting.</summary>
    private bool HasCookingScreenFlipped()
    {
        var isOpen = _watch.IsCookingScreenOpen();

        if (isOpen == _wasCookingScreenOpen) return false;

        _wasCookingScreenOpen = isOpen;

        return true;
    }

    /// <summary>
    /// Every few seconds, moves each finished dish out of its stove into the storage its tags want. Where each went is
    /// shown as a toast when Exiled's Toolkit can show one, else on the page; a dish no storage takes is retried after a
    /// wait but told about only once. The moves share one refresh of an open cooking screen.
    /// </summary>
    private void PutDishesAway(float now)
    {
        _nextDishSweepAt = now + DishSweepSeconds;

        IReadOnlyList<Stored> results;

        using (BagRefresh.Hold()) results = DishStore.PutAway(_dishes, _dishRetry, now);

        foreach (var stored in results)
        {
            if (stored is Stored.Stays && !_toldStays.Add(stored.Dish.InstanceId)) continue;

            var note = DishStore.Note(stored, IsChinese);

            _log(note.Text);
            if (!_toast(note)) _page.Status(note.Text);
        }
    }

    /// <summary>
    /// Cooks the queues: a stove is taken once it has been seen idle with an empty pot on two checks in a row, so a pot
    /// the game is still emptying or filling is left alone. Waiting is told once per stove until its reason changes.
    /// The pots filled in one go share one refresh of an open cooking screen, sent before the screen is read again.
    /// </summary>
    private void CookQueued(float now)
    {
        _nextQueueAt = now + QueueSeconds;

        var idle = _game.IdleStoves();
        var settled = new List<IdleStove>();

        foreach (var stove in idle)
        {
            if (_idleBefore.Contains(stove.Stove.FurnitureId)) settled.Add(stove);
        }

        _idleBefore.Clear();

        foreach (var stove in idle) _idleBefore.Add(stove.Stove.FurnitureId);

        if (settled.Count == 0) return;

        IReadOnlyList<QueueTurn> turns;

        using (BagRefresh.Hold()) turns = QueueCook.Run(_game, settled, _queue, _isSpoilingFirst, IsChinese);

        var hasActed = false;

        foreach (var turn in turns)
        {
            Take(turn, now);
            hasActed |= turn.Step != QueueStep.Waiting;
        }

        if (hasActed) Refresh(now);
    }

    private void Take(QueueTurn turn, float now)
    {
        if (turn.Step == QueueStep.Waiting)
        {
            if (_toldWaiting.TryGetValue(turn.StoveId, out var told) && told == turn.Message) return;

            _toldWaiting[turn.StoveId] = turn.Message;
        }
        else
        {
            _toldWaiting.Remove(turn.StoveId);
            _idleBefore.Remove(turn.StoveId);
        }

        if (turn.RecipeId is { } recipeId)
        {
            _queue = _queue.Remove(turn.StoveId, recipeId);

            if (turn.Step == QueueStep.Loaded && _book?.Recipes.TryGetValue(recipeId, out var recipe) == true) AskToStart(turn.StoveId, QueueCook.StartIds(_book, recipe), now);
        }

        var note = new DishNote(turn.Message, turn.Step is QueueStep.Waiting or QueueStep.NotStarted);

        _log($"queue {turn.StoveId}: {turn.Step} -> {turn.Message}");
        if (!_toast(note)) _page.Status(turn.Message);
    }

    /// <summary>
    /// Asks the page to press the game's own Cook button for the open stove: the start token counts up once per filled
    /// pot, and stays up only while the pot forecasts one of these dishes, until the stove cooks or a few seconds pass.
    /// </summary>
    private void AskToStart(long stoveId, IReadOnlyList<int> recipeIds, float now)
    {
        _startToken++;
        _startUntil = now + StartWindowSeconds;
        _startStoveId = stoveId;
        _startIds = recipeIds;
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
                Press($"fill {fill.RecipeId}", () => CookRun.Fill(_game, fill.RecipeId, _isSpoilingFirst, IsChinese, _queue));
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
            case PageCommand.Queue queue:
                Press($"queue {queue.RecipeId}", () => PressQueue(queue.RecipeId, true));
                break;
            case PageCommand.Unqueue unqueue:
                Press($"unqueue {unqueue.RecipeId}", () => PressQueue(unqueue.RecipeId, false));
                break;
            default:
                throw new InvalidOperationException("unhandled page command " + command);
        }
    }

    /// <summary>Adds a dish to the open stove's queue, or takes one off; the queue is tried at once, not at the next tick.</summary>
    private Pressed PressQueue(int recipeId, bool isAdding)
    {
        if (_game.Read() is not CookRead.Ready { Kitchen: { Stove: { } stove } kitchen }) return new Pressed(false, Texts.NoStove.Pick(IsChinese));

        var (queue, pressed) = _queue.Press(kitchen, stove.FurnitureId, recipeId, isAdding, _isSpoilingFirst, IsChinese);

        _queue = queue;
        _nextQueueAt = 0f;

        return pressed;
    }

    /// <summary>Fills the pot for the dish this stove last started, then asks the page to press the game's own Cook button.</summary>
    private Pressed Repeat()
    {
        var pressed = CookRun.Repeat(_game, _last, _isSpoilingFirst, IsChinese, _queue);

        if (!pressed.IsDone || _book == null || _last.WatchedId is not { } stoveId || _last.For(stoveId) is not { } recipeId) return pressed;

        AskToStart(stoveId, new[] { recipeId }, Clock.Now);

        return _book.Recipes.TryGetValue(recipeId, out var recipe) ? new Pressed(true, Texts.Starting.Say(IsChinese, recipe.Name)) : pressed;
    }

    private void ForgetSent()
    {
        _shownPrint = null;
        _delta.Forget();
        _pace.Forget();
        _nextRefreshAt = 0f;
    }

    /// <summary>
    /// Reads the kitchen once, so the game's cooking screen gets its odds while one is open. A read that throws is logged
    /// by the game adapter and read as not ready.
    /// </summary>
    private void Refresh(float now)
    {
        if (!_watch.IsCookingScreenOpen())
        {
            RefreshClosed(now);

            return;
        }

        var read = _game.Read();

        if (read is CookRead.NoGame) ForgetQueue();

        var kitchen = (read as CookRead.Ready)?.Kitchen;

        _last = _last.See(kitchen?.Stove);
        _wasStoveRead = kitchen?.Stove != null;
        _wasHotPot = kitchen?.Stove?.IsHotPot == true;

        if (kitchen != null) _book = kitchen.Book;
        if (kitchen?.Stove is not { IsCooking: false } || now >= _startUntil) _startUntil = 0f;

        _pace.Read(now);
        _nextRefreshAt = now + ClosedRefreshSeconds;
        SendOverlay(kitchen);
    }

    /// <summary>
    /// With the cooking screen closed there is no stove to draw odds for, so the kitchen is not read: reading every food
    /// in every storage only to clear an overlay was most of what this mod cost while the player did something else.
    /// The cookbook is started here instead, so it is read a slice a frame before the screen first opens.
    /// </summary>
    private void RefreshClosed(float now)
    {
        if (!_game.IsLoaded()) ForgetQueue();
        else _watch.WarmCatalog();

        _last = _last.See(null);
        _startUntil = 0f;
        _nextRefreshAt = now + ClosedRefreshSeconds;
        SendOverlay(null);
    }

    /// <summary>
    /// The odds on the game's cooking screen, sent only when they changed. A read that found the kitchen as it was skips
    /// building the overlay at all; otherwise only the recipe book rows that changed go whole. The catalog goes first,
    /// since nudges name items by id.
    /// </summary>
    private void SendOverlay(Kitchen? kitchen)
    {
        ulong print;
        string overlay;

        if (kitchen?.Stove is { } stove)
        {
            var start = StartToken(stove);
            var last = _last.For(stove.FurnitureId);

            print = OverlayPrint.Of(kitchen, _isShowingOdds, _isSpoilingFirst, _queue, last, start, _isShowingUnlearned);

            if (print == _shownPrint) return;

            var reserved = _queue.IsEmpty ? null : Reservation.Of(kitchen, _queue, _isSpoilingFirst, stove.SlotsByUnit);

            overlay = CookView.Overlay(kitchen, stove, _isShowingOdds, new StationExtras(last, start, _queue.For(stove.FurnitureId), reserved), _isShowingUnlearned);
        }
        else
        {
            print = 0UL;

            if (print == _shownPrint) return;

            overlay = CookView.NoOverlay();
        }

        if (_delta.Next(overlay) is not { } patch)
        {
            _shownPrint = print;

            return;
        }

        if (kitchen != null) SendCatalog(kitchen.Book);

        if (!_page.Cooking(patch.Json)) return;

        _delta.Sent(patch);
        _shownPrint = print;
    }

    /// <summary>The start token goes out only while the pot forecasts the dish asked for, so a pot the game half filled is never started.</summary>
    private int StartToken(Stove stove)
    {
        var isStarting = _startUntil > 0f && _startStoveId == stove.FurnitureId && _last.Pending is { } pending && ContainsId(_startIds, pending);

        return isStarting ? _startToken : 0;
    }

    private static bool ContainsId(IReadOnlyList<int> ids, int id)
    {
        foreach (var each in ids)
        {
            if (each == id) return true;
        }

        return false;
    }

    /// <summary>A stove's id means nothing in another save, so the queues go when the game is left.</summary>
    private void ForgetQueue()
    {
        _queue = CookQueue.Empty;
        _idleBefore.Clear();
        _toldWaiting.Clear();
    }

    private void SendCatalog(Cookbook book)
    {
        if (ReferenceEquals(book, _sentCatalog)) return;
        if (_page.Catalog(CatalogView.Write(book))) _sentCatalog = book;
    }

    /// <summary>
    /// Runs one press and shows what it did, then reads again so the pot and the odds show the change at once. The food
    /// a press moves shares one refresh of the cooking screen, sent before the read.
    /// </summary>
    private void Press(string name, Func<Pressed> press)
    {
        string message;

        try
        {
            Pressed pressed;

            using (BagRefresh.Hold()) pressed = press();

            message = pressed.Message;
            _log($"{name}: {(pressed.IsDone ? "done" : "refused")} -> {message}");
        }
        catch (Exception ex)
        {
            message = Texts.Failed.Pick(IsChinese);
            _log($"{name} failed: {ex}");
        }

        _page.Status(message);
        Refresh(Clock.Now);
    }

}
