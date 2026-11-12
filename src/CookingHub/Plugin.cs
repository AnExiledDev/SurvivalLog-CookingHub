using BepInEx;
using BepInEx.Unity.IL2CPP;
using CookingHub.Adapters;
using ExiledToolkit;
using ExiledToolkit.Game;
using ExiledToolkit.Kit;
using HarmonyLib;

namespace CookingHub;

[BepInPlugin(Guid, "Cooking Hub", Version)]
[BepInDependency(ToolkitGuid, ToolkitVersion)]
[BepInDependency(StackingGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(MachineSpeedCooks.Guid, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BasePlugin
{
    private const string Guid = "com.dev.survivallog.cookinghub";
    private const string Version = "0.9.6";

    /// <summary>Exiled's Toolkit: its shared helpers, toasts, live settings and UI size all come from it.</summary>
    private const string ToolkitGuid = "com.dev.survivallog.exiledtoolkit";
    private const string ToolkitVersion = ">=0.19.0";

    private const string StackingGuid = "com.dev.survivallog.stacking";
    private const string LogFileName = "cooking-hub.log";

    /// <summary>The name the page script hands its panels to the Toolkit's UI size with.</summary>
    private const string UiSizeName = "CookingHub";

    public override void Load()
    {
        ModUiSize.Bind(Config, UiSizeName, description:
            "The size of Cooking Hub's tabs and recipe book on the cooking screen, in percent, from 75 to 200. At 0 they use the UI size set in Exiled's Toolkit (its UiScale). If the cooking screen wouldn't fit in the window at this size, the book shrinks until it does.\n" +
            "烹饪界面上 Cooking Hub 的标签页和菜谱的大小（百分比），范围 75 到 200。设为 0 时跟随 Exiled's Toolkit 的界面大小（它的 UiScale）。按这个大小烹饪界面放不进窗口时，菜谱会缩小到刚好放得下。");

        var isSpoilingFirst = Config.Bind("General", "FillSpoilingFirst", true,
            "When true, Load and Repeat last fill the pot with the food closest to spoiling first. When false, they use the freshest food first.\n" +
            "设为 true 时，「放入」和「重做上一道」会优先使用快变质的食材。设为 false 时，优先使用最新鲜的食材。");
        var isShowingOdds = Config.Bind("General", "ShowOdds", true,
            "When true, the cooking screen shows the chance of each quality under every dish it says the pot will make, with the reasons and what would raise it.\n" +
            "设为 true 时，烹饪界面会在锅里将做出的每道菜下方显示各品质的概率、原因，以及怎样提高。");
        var isPuttingDishesAway = Config.Bind("General", "PutDishesAway", false,
            "When true, a finished dish is moved out of the stove into the home storage whose tags want it, the way Storage Hub puts items away. If no tagged storage takes it, it goes to a storage that already holds that dish. If no storage has room, the dish stays in the stove.\n" +
            "设为 true 时，做好的菜会从灶具移到标签需要它的家中储物里，方式与 Storage Hub 收纳物品相同。没有带标签的储物能放时，会放进已有这道菜的储物。都放不下时，菜会留在灶具里。");
        var isShowingUnlearned = Config.Bind("General", "ShowUnlearnedRecipes", false,
            "When true, the cooking screen gets a Not learned tab listing the recipes this stove can cook that the recipe book does not show yet, with the food you have for each. When false, only the recipes the game shows are listed.\n" +
            "设为 true 时，烹饪界面会多一个「未学会」标签页，列出这个灶具能做、但菜谱里还没显示的菜，以及你有的食材。设为 false 时，只列出游戏显示的菜谱。");
        var isLoggingToFile = Config.Bind("Diagnostics", "LogToFile", false, new BepInEx.Configuration.ConfigDescription(
            "When true, what Cooking Hub does is also written to BepInEx\\cooking-hub.log, which is kept between game starts. Turn it on when reporting a problem.\n" +
            "设为 true 时，Cooking Hub 的操作也会写入 BepInEx\\cooking-hub.log，该文件在重启游戏后保留。报告问题时再打开。", null, "ModManager.Advanced"));
        var log = new ModLog(Log, LogFileName);

        // Before the first line, since the file copy is on until the setting says otherwise.
        LiveSettings.Watch(isLoggingToFile, isOn => log.IsFileOn = isOn);

        var hooks = new PatchKit(new Harmony(Guid), log.Write);
        var game = new GameCooking(log.Write);
        var session = new CookSession(
            game,
            game,
            game,
            new CookPage(log.Write),
            log.Write,
            () => MachineSpeedCooks.Speedup(log.Write));

        log.Write($"===== Cooking Hub {Version} started");
        BagRefresh.Apply(hooks, game.IsCookingScreenOpen, log.Write);

        LiveSettings.Watch(() =>
        {
            session.UseSettings(isSpoilingFirst.Value, isShowingOdds.Value, isPuttingDishesAway.Value, isShowingUnlearned.Value);
            log.Write($"spoiling first {isSpoilingFirst.Value}, odds {isShowingOdds.Value}, put dishes away {isPuttingDishesAway.Value}, unlearned {isShowingUnlearned.Value}");
        }, isSpoilingFirst, isShowingOdds, isPuttingDishesAway, isShowingUnlearned);

        CookTicker.Session = session;
        CookTicker.Log = log.Write;
        AddComponent<CookTicker>();
        StoveWarmUp.Start(log.WriteFileOnly);
    }
}
