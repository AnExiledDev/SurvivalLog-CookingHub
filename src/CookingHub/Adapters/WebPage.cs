using System;
using System.IO;
using CookingHub.Core;
using ExiledToolkit;
using GameCore.HotUpdate;
using GameCore.HotUpdate.ReduxUI;
using Vuplex.WebView;

namespace CookingHub.Adapters;

/// <summary>
/// The mod's half that lives in the game's web UI: page.js, run in the root document the game's own pages sit in. It
/// draws the window and puts the Cooking Hub button in the HUD's tool column. The web view can reload at any time and
/// lose the script, so it is checked for every couple of seconds and put back when the page says it is missing.
/// </summary>
internal sealed class WebPage
{
    private const float CheckSeconds = 2f;
    private const string ScriptResource = "CookingHub.page.js";
    private const string NoScriptReply = "window.vuplex&&window.vuplex.postMessage('" + PageCommand.Prefix + "noscript')";

    private readonly Action<string> _log;
    private string? _script;
    private float _nextCheckAt;
    private bool _hasReportedRunFailure;
    private bool? _wordsAreChinese;

    public WebPage(Action<string> log)
    {
        _log = log;
    }

    public void KeepInstalled(float now)
    {
        if (now < _nextCheckAt) return;

        _nextCheckAt = now + CheckSeconds;
        Run("window.__slcookinghub||" + NoScriptReply);
    }

    public void Install()
    {
        _wordsAreChinese = null;

        if (Run(Script())) _log("page script sent to the web UI");
    }

    /// <summary>The page lost its script, or loaded it afresh, so its words go with the next call.</summary>
    public void ForgetWords() => _wordsAreChinese = null;

    /// <summary>What a press did, as an Exiled's Toolkit toast. The page hears it too, as it frees the pressed button.</summary>
    public void Status(string text, ToastKind kind)
    {
        Call($"status({JsText.Literal(text)})");
        Toast(text, kind);
    }

    /// <summary>A message as an Exiled's Toolkit toast, without touching the page.</summary>
    public void Toast(string text, ToastKind kind)
    {
        if (string.IsNullOrEmpty(text)) return;

        if (Toasts.Show(new ToastRequest(Texts.ToastSource.Pick(GameLanguage.IsChinese()), text) { Kind = kind }).IsNone) _log("a toast was not shown: " + text);
    }

    /// <summary>Names and icons of food types, items and recipes; the views refer to them by id only.</summary>
    public bool Catalog(string catalogJson) => Call($"catalog({catalogJson})");

    /// <summary>The odds and nudges drawn on the game's own cooking screen, or a view with show false to clear them.</summary>
    public bool Cooking(string overlayJson) => Call($"cooking({overlayJson})");

    /// <summary>
    /// The page's words go with the first call after a load and again whenever the game's language changes; resending
    /// the whole table with every call was most of what each refresh cost the web view.
    /// </summary>
    private bool Call(string expression)
    {
        var isChinese = GameLanguage.IsChinese();
        var words = _wordsAreChinese == isChinese ? "" : PageWords.Script(isChinese);
        var isRun = Run($"{words}window.__slcookinghub?window.__slcookinghub.{expression}:{NoScriptReply}");

        if (isRun) _wordsAreChinese = isChinese;

        return isRun;
    }

    private bool Run(string javaScript)
    {
        var webView = GameSingleton.Existing<ReduxUISystem>()?.GetWebUILayer()?.canvasWebViewPrefab?.WebView;

        if (webView == null) return false;

        try
        {
            webView.ExecuteJavaScript(javaScript, null);

            return true;
        }
        catch (Exception ex)
        {
            if (!_hasReportedRunFailure) _log("could not run script in the web UI (reported once): " + ex.Message);

            _hasReportedRunFailure = true;

            return false;
        }
    }

    private string Script()
    {
        if (_script != null) return _script;

        using var stream = typeof(WebPage).Assembly.GetManifestResourceStream(ScriptResource) ?? throw new InvalidOperationException(ScriptResource + " is not embedded");
        using var reader = new StreamReader(stream);

        return _script = reader.ReadToEnd();
    }
}
