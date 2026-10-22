using System.Collections.Generic;
using System.Globalization;

namespace CookingHub.Core;

/// <summary>One piece of on-screen text in the two languages the game ships. {0}, {1} are filled in by Say.</summary>
public readonly record struct Phrase(string En, string Zh)
{
    public string Pick(bool isChinese) => isChinese ? Zh : En;

    public string Say(bool isChinese, params object[] args) => string.Format(CultureInfo.InvariantCulture, Pick(isChinese), args);
}

/// <summary>The words the mod puts on screen: press results, reasons, and the page's own labels.</summary>
public static class Texts
{
    private const string Log = "BepInEx/LogOutput.log";

    public static readonly Phrase NoGame = new("Load a game first.", "请先载入游戏。");
    public static readonly Phrase NotReady = new("Can't cook right now: {0}.", "现在无法烹饪：{0}。");
    public static readonly Phrase NoWorld = new("the game world is not loaded", "游戏世界尚未载入");
    public static readonly Phrase Away = new("you are away from home", "你不在家中");
    public static readonly Phrase ReadFailed = new("your food could not be read, see " + Log, "无法读取食材，详见 " + Log);
    public static readonly Phrase Failed = new("Cooking Hub failed, see " + Log + ".", "Cooking Hub 出错，详见 " + Log + "。");

    public static readonly Phrase NoStove = new("Open a stove's cooking screen first.", "请先打开灶具的烹饪界面。");
    public static readonly Phrase Cooking = new("This stove is already cooking.", "这个灶具正在烹饪。");
    public static readonly Phrase NoRecipe = new("That recipe is not in the game's tables.", "游戏数据中没有这个菜谱。");
    public static readonly Phrase PotNotEmpty = new("Empty the pot first.", "请先清空锅里的食材。");
    public static readonly Phrase PotFull = new("The pot is full.", "锅已经满了。");
    public static readonly Phrase StoveRefuses = new("This stove can't cook {0}.", "这个灶具不能做{0}。");
    public static readonly Phrase TooBig = new("{0} needs {1} slots and this stove has {2}.", "{0}需要 {1} 格，这个灶具只有 {2} 格。");
    public static readonly Phrase NotEnough = new("Not enough fresh food for {0}.", "新鲜食材不够做{0}。");
    public static readonly Phrase Filled = new("The pot is filled for {0}.", "已为{0}放好食材。");
    public static readonly Phrase MovedSome = new("Moved {0} of {1}; the game refused the rest.", "已放入 {0}/{1} 份，其余被游戏拒绝。");
    public static readonly Phrase NoneLeft = new("No fresh {0} left.", "没有新鲜的{0}了。");
    public static readonly Phrase Added = new("Added {0}.", "已加入{0}。");
    public static readonly Phrase MoveRefused = new("The game refused to move that.", "游戏拒绝了这次移动。");
    public static readonly Phrase NothingSpoiled = new("No spoiled {0} in the pot.", "锅里没有变质的{0}。");
    public static readonly Phrase Swapped = new("Swapped the spoiled {0} for fresh.", "已把变质的{0}换成新鲜的。");
    public static readonly Phrase SpoilingFirstOn = new("Filling now uses the food closest to spoiling first.", "放入食材时现在优先使用快变质的。");
    public static readonly Phrase SpoilingFirstOff = new("Filling now uses the freshest food first.", "放入食材时现在优先使用最新鲜的。");
    public static readonly Phrase OddsOn = new("Odds now show on the cooking screen.", "烹饪界面现在会显示品质概率。");
    public static readonly Phrase OddsOff = new("Odds no longer show on the cooking screen.", "烹饪界面不再显示品质概率。");

    public static readonly Phrase Bag = new("Bag", "背包");
    public static readonly Phrase Storage = new("Storage", "储物");
    public static readonly Phrase Pot = new("Pot", "锅中");

    /// <summary>The words page.js draws the window, the cooking screen's odds and the HUD button with, by the key it looks them up by.</summary>
    public static readonly IReadOnlyList<(string Key, Phrase Phrase)> Page = new (string, Phrase)[]
    {
        ("title", new("Cooking Hub", "烹饪中心")),
        ("tip", new("Cooking Hub", "烹饪中心")),
        ("tabCook", new("What can I cook", "能做什么菜")),
        ("tabPantry", new("Pantry", "食材库存")),
        ("tabSettings", new("Settings", "设置")),
        ("keyOpen", new("{0} opens and closes this window.", "按 {0} 打开或关闭此窗口。")),
        ("escClose", new("Esc closes it.", "按 Esc 关闭。")),
        ("level", new("Cooking level {0}, rolls start at {1}", "烹饪等级 {0}，掷点从 {1} 起")),
        ("stoveOpen", new("Stove open: {0} of {1} slots free", "已打开灶具：{1} 格中空 {0} 格")),
        ("stoveCooking", new("The open stove is cooking", "打开的灶具正在烹饪")),
        ("stoveNone", new("Open a stove's cooking screen to fill its pot from here.", "打开灶具的烹饪界面后，可以在这里直接放入食材。")),
        ("search", new("Search dishes", "搜索菜品")),
        ("fAll", new("All", "全部")),
        ("fReady", new("Ready", "可以做")),
        ("fOne", new("Missing one", "差一样")),
        ("fShort", new("Missing more", "差几样")),
        ("fLocked", new("Locked", "未解锁")),
        ("bNew", new("Never cooked", "从未做过")),
        ("bSpoiling", new("Uses food spoiling soon", "能用掉快变质的食材")),
        ("bVaries", new("Dish varies", "成品不定")),
        ("lockLevel", new("Cooking level {0}", "烹饪等级 {0}")),
        ("lockLearn", new("Not learned yet", "尚未学会")),
        ("anyType", new("any {0}", "任意{0}")),
        ("perfect", new("Perfect {0}", "完美 {0}")),
        ("fill", new("Fill pot", "放入锅中")),
        ("nothing", new("Nothing matches.", "没有符合的菜品。")),
        ("pFood", new("Food", "食材")),
        ("pCount", new("Count", "数量")),
        ("pLeft", new("Spoils in", "剩余保质")),
        ("pWhere", new("Kept in", "存放位置")),
        ("pSpoiled", new("{0} spoiled", "{0} 份已变质")),
        ("pKeeps", new("Keeps", "不会变质")),
        ("pEmpty", new("You have no food to cook with.", "你没有可用于烹饪的食材。")),
        ("hours", new("{0} h", "{0} 小时")),
        ("days", new("{0} d", "{0} 天")),
        ("sSpoilFirst", new("Fill with the food closest to spoiling first", "放入食材时优先使用快变质的")),
        ("sSpoilFirstNote", new("Off uses the freshest food first.", "关闭时优先使用最新鲜的。")),
        ("sOdds", new("Show odds on the cooking screen", "在烹饪界面显示品质概率")),
        ("sOddsNote", new("Under each dish the cooking screen forecasts: the chance of each quality, why, and what would help.", "在烹饪界面预测的每道菜下方显示：各品质的概率、原因，以及怎样做会更好。")),
        ("sKey", new("Hotkey", "快捷键")),
        ("sKeyNote", new("Change it in the mod's settings (PanelKey).", "可在模组设置中更改（PanelKey）。")),
        ("sKeyNone", new("None", "无")),
        ("tPerfect", new("Perfect", "完美")),
        ("tGood", new("Good", "优良")),
        ("tNormal", new("Average", "普通")),
        ("tFail", new("Failed", "失败")),
        ("why", new("Why", "原因")),
        ("rollFloor", new("Cooking level floor (roll {0}-100)", "烹饪等级下限（掷出 {0}-100）")),
        ("partFresh", new("All ingredients fresh", "食材全部新鲜")),
        ("partRotten", new("Spoiled ingredient", "有变质食材")),
        ("partSeason1", new("1 seasoning", "1 种调味料")),
        ("partSeasonN", new("{0} seasonings", "{0} 种调味料")),
        ("partExact", new("Exact recipe", "精确配方")),
        ("partStove", new("Stove quality", "灶具品质")),
        ("lowest", new("Worst roll lands on", "最差结果")),
        ("nSeason", new("Add {0}", "加入{0}")),
        ("nSwap", new("Swap the spoiled {0} for fresh", "把变质的{0}换成新鲜的")),
        ("nGain", new("about +{0} Perfect", "完美约 +{0}")),
        ("hub", new("Cooking Hub", "烹饪中心")),
    };
}
