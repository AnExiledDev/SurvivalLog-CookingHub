using System;
using System.IO;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace CookingHub.Adapters;

/// <summary>
/// The mod's own UI size (Interface &gt; UiScale), handed to Exiled's Toolkit 0.11.0 or newer, which then draws the
/// panels the page script hands it with <c>mod: "CookingHub"</c> at that size instead of the Toolkit's UiScale. 0 has them
/// follow the Toolkit's. Without the Toolkit, or with an older one, the setting does nothing. Only <see cref="SetWithToolkit"/> names a Toolkit type, so the mod loads without it.
/// </summary>
internal static class OptionalUiSize
{
    /// <summary>The name the page script hands its panels over with.</summary>
    public const string Mod = "CookingHub";

    private static readonly Lazy<ManualLogSource> Log = new(() => Logger.CreateLogSource(typeof(OptionalUiSize).Assembly.GetName().Name ?? "UiSize"));
    private static bool _isUnavailable;
    private static bool _hasSent;

    public static void Bind(ConfigFile config)
    {
        var entry = config.Bind("Interface", "UiScale", 0, new ConfigDescription(
            "How big Cooking Hub's tabs and the recipe book are drawn on the cooking screen, in percent, from 75 to 200. At 0 they follow the UI size set in Exiled's Toolkit (its UiScale). When the cooking screen would no longer fit the window, the book is drawn as big as still fits.\n" +
            "烹饪界面上 Cooking Hub 的页签和菜谱书的显示大小（百分比），范围 75 到 200。设为 0 时跟随 Exiled's Toolkit 的界面大小设置（它的 UiScale）。烹饪界面放不进窗口时，菜谱书按仍能放下的最大尺寸显示。",
            new AcceptableValueRange<int>(0, 200)));

        OptionalLive.Watch(entry, Set);
    }

    // 0 at start is what the Toolkit has anyway, so nothing is sent until there is a size to send.
    private static void Set(int percent)
    {
        if (_isUnavailable || !OptionalLive.IsToolkitInstalled || (percent <= 0 && !_hasSent)) return;

        try
        {
            SetWithToolkit(percent);
            _hasSent = true;
        }
        catch (Exception ex) when (ex is TypeLoadException or MissingMethodException or FileNotFoundException or FileLoadException or InvalidOperationException)
        {
            _isUnavailable = true;
            Log.Value.LogWarning("UiScale under Interface needs Exiled's Toolkit 0.11.0 or newer, so it does nothing: " + ex.Message);
        }
    }

    // Kept in its own method so ModUiSize is only looked up when there is a size to send.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetWithToolkit(int percent) => ExiledToolkit.ModUiSize.Set(Mod, percent);
}
