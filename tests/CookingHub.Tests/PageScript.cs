using CookingHub.Core;
using ExiledToolkit.Web;
using Jint;

namespace CookingHub.Tests;

/// <summary>
/// page.js exactly as the plugin embeds it, run in Jint with Exiled's Toolkit's page API (api.js, from the Toolkit's
/// source) on top of FakePage.js (Root.html, the game's HUD page, its cooking screen and the vuplex bridge). The
/// English words go first, as CookPage hands them over before its first call. The calls into the script are the ones
/// CookPage makes, and what the script posts is parsed with the real PageCommand, so a test sees the commands the
/// plugin would act on.
/// </summary>
internal sealed class PageScript
{
    private readonly Engine _engine = new();

    /// <param name="isToolkitFirst">
    /// The game runs the Toolkit's script first, or with false Cooking Hub's first; then the Toolkit's runs only at
    /// <see cref="LoadToolkit"/>.
    /// </param>
    public PageScript(bool isToolkitFirst = true)
    {
        _engine.Execute(Resource("CookingHub.Tests.FakePage.js"));
        _engine.Execute(PageWords.Script(isChinese: false));

        if (isToolkitFirst) _engine.Execute(Resource("ExiledToolkit.api.js"));

        _engine.Execute(Resource("CookingHub.page.js"));
        Hear();
    }

    /// <summary>The Toolkit's page API arriving after Cooking Hub's script.</summary>
    public void LoadToolkit() => Run(Resource("ExiledToolkit.api.js"));

    /// <summary>Every command the script sent the plugin, its ready message and diag notes aside.</summary>
    public List<PageCommand> Sent { get; } = new();

    /// <summary>How many times the script asked the plugin for everything again.</summary>
    public int Readies { get; private set; }

    /// <summary>The script's "diag|" notes, as the plugin logs them.</summary>
    public List<string> Notes { get; } = new();

    public void Run(string statements)
    {
        _engine.Execute(statements);
        Hear();
    }

    public string Text(string expression) => _engine.Evaluate($"String({expression})").AsString();

    public bool Is(string expression) => _engine.Evaluate($"!!({expression})").AsBoolean();

    /// <summary>One run of the Toolkit's 250 ms page check, which hands the cooking screen to the script.</summary>
    public void Poll() => Run("poll()");

    public void OpenHud() => Run("openHud()");

    public void Click(string expression) => Run($"click({expression})");

    public void Catalog(string catalogJson) => Call($"catalog({catalogJson})");

    /// <summary>The overlay JSON the plugin sends for the game's cooking screen.</summary>
    public void Cooking(string overlayJson) => Call($"cooking({overlayJson})");

    /// <summary>The player opens a stove's cooking screen, which forecasts the recipes in this order.</summary>
    public void OpenCooking(params int[] recipeIds) => Run($"var cookingFrame = openCooking([{string.Join(",", recipeIds)}])");

    /// <summary>The cooking screen forecasts again after the pot changed, rebuilding every card.</summary>
    public void Forecast(params int[] recipeIds) => Run($"forecast(cookingFrame, [{string.Join(",", recipeIds)}])");

    public void Status(string text) => Call($"status({JsText.Literal(text)})");

    private void Call(string expression) => Run("window.__slcookinghub." + expression);

    /// <summary>Takes what the script posted since the last look.</summary>
    private void Hear()
    {
        var count = (int)_engine.Evaluate("posted.length").AsNumber();
        var messages = Enumerable.Range(0, count).Select(i => _engine.Evaluate($"posted[{i}]").AsString()).ToList();

        _engine.Execute("posted.length = 0");

        foreach (var message in messages) Take(PageCommand.Parse(message) ?? throw new InvalidOperationException("the page sent a message the plugin cannot read: " + message));
    }

    private void Take(PageCommand command)
    {
        switch (command)
        {
            case PageCommand.Ready:
                Readies++;
                break;
            case PageCommand.Note note:
                Notes.Add(note.Text);
                break;
            default:
                Sent.Add(command);
                break;
        }
    }

    private static string Resource(string name)
    {
        using var stream = typeof(PageScript).Assembly.GetManifestResourceStream(name) ?? throw new InvalidOperationException(name + " is not embedded in the test assembly");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
