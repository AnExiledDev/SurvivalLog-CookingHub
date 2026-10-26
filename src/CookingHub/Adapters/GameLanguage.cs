using System;
using GameCore.HotUpdate;

namespace CookingHub.Adapters;

/// <summary>
/// Asks the game which language it is showing, only once the game has made its settings. Anything that goes wrong reads
/// as English. Asked on every read and every page call, so the settings wrapper is kept while the game keeps the same
/// settings object.
/// </summary>
internal static class GameLanguage
{
    private static IntPtr _settingsPointer;
    private static SettingManager? _settings;

    public static bool IsChinese()
    {
        try
        {
            var pointer = GameSingleton.Pointer<SettingManager>();

            if (pointer == IntPtr.Zero) return false;

            if (pointer != _settingsPointer || _settings == null)
            {
                _settings = GameSingleton.Existing<SettingManager>();
                _settingsPointer = pointer;
            }

            return _settings?.IsChinese() == true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
