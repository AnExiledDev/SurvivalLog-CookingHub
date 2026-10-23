using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using CookingHub.Adapters;
using CookingHub.Core;
using HarmonyLib;
using UnityEngine;

namespace CookingHub;

[BepInPlugin(Guid, "Cooking Hub", Version)]
[BepInDependency(OptionalLive.ToolkitGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(StackingGuid, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BasePlugin
{
    private const string Guid = "com.dev.survivallog.cookinghub";
    private const string Version = "0.4.1";
    private const string StackingGuid = "com.dev.survivallog.stacking";

    public override void Load()
    {
        if (!OptionalLive.IsToolkitInstalled) Log.LogInfo(OptionalLive.MissingNote);

        var panelKey = Config.Bind("General", "PanelKey", KeyCode.K,
            "Key that opens and closes the Cooking Hub window, K by default. None turns the key off; the Cooking Hub button in the HUD's tool column still opens the window.\n" +
            "打开和关闭 Cooking Hub 窗口的按键，默认 K。设为 None 则停用该按键，HUD 工具栏中的 Cooking Hub 按钮仍可打开窗口。");
        var isSpoilingFirst = Config.Bind("General", "FillSpoilingFirst", true,
            "When true, filling a pot from the window uses the food closest to spoiling first. When false, it uses the freshest food first.\n" +
            "设为 true 时，从窗口放入食材会优先使用快变质的食材。设为 false 时，优先使用最新鲜的食材。");
        var isShowingOdds = Config.Bind("General", "ShowOdds", true,
            "When true, the game's cooking screen shows the chance of each quality under every dish it forecasts, with the reasons and what would raise it.\n" +
            "设为 true 时，游戏的烹饪界面会在每道预测菜品下方显示各品质的概率、原因，以及怎样提高。");
        var isPuttingDishesAway = Config.Bind("General", "PutDishesAway", false,
            "When true, a finished dish is moved out of the stove into the home storage whose tags want it, the way Storage Hub puts items away. A dish no storage is tagged for, or that a storage already holding it has no room for, stays in the stove.\n" +
            "设为 true 时，做好的菜会从灶具移到标签需要它的家中储物里，方式与 Storage Hub 收纳物品相同。没有储物标签收它，或已有这道菜的储物放不下时，菜会留在灶具里。");
        var isLoggingToFile = Config.Bind("Diagnostics", "LogToFile", false, new BepInEx.Configuration.ConfigDescription(
            "When true, what Cooking Hub does is also written to BepInEx\\cooking-hub.log, which is kept between game starts. Turn it on when reporting a problem.\n" +
            "设为 true 时，Cooking Hub 的操作也会写入 BepInEx\\cooking-hub.log，该文件在重启游戏后保留。报告问题时再打开。", null, "ModManager.Advanced"));
        var log = new CookLog(Log, Path.Combine(Paths.BepInExRootPath, "cooking-hub.log"));
        var harmony = new Harmony(Guid);
        var queueFile = Path.Combine(Paths.ConfigPath, "CookingHub.queue.txt");
        var game = new GameCooking(log.Write);
        var toasts = new DishToasts(log.Write);
        var session = new CookSession(
            game,
            game,
            new WebPage(log.Write),
            note => toasts.Show(note, GameLanguage.IsChinese()),
            game.IsCookingScreenOpen,
            isOn => isSpoilingFirst.Value = isOn,
            isOn => isShowingOdds.Value = isOn,
            CookQueue.Read(File.Exists(queueFile) ? File.ReadAllText(queueFile) : null),
            text => SaveQueue(queueFile, text, log.Write),
            log.Write);

        OptionalLive.Watch(isLoggingToFile, isOn => log.IsFileOn = isOn);
        log.Write($"===== Cooking Hub {Version} started");
        InputGate.Apply(harmony, log.Write);
        PageMessages.Apply(harmony, log.Write);

        OptionalLive.Watch(() =>
        {
            session.UseSettings(panelKey.Value, isSpoilingFirst.Value, isShowingOdds.Value, isPuttingDishesAway.Value);
            log.Write($"key {panelKey.Value}, spoiling first {isSpoilingFirst.Value}, odds {isShowingOdds.Value}, put dishes away {isPuttingDishesAway.Value}");
        }, panelKey, isSpoilingFirst, isShowingOdds, isPuttingDishesAway);

        CookTicker.Session = session;
        CookTicker.Log = log.Write;
        AddComponent<CookTicker>();
    }

    /// <summary>The queue is one line of recipe ids beside the config, shared by every save. A failed write is logged, not thrown.</summary>
    private static void SaveQueue(string path, string text, System.Action<string> log)
    {
        try
        {
            File.WriteAllText(path, text);
        }
        catch (System.Exception ex) when (ex is IOException or System.UnauthorizedAccessException)
        {
            log($"queue not saved: {ex.Message}");
        }
    }
}
