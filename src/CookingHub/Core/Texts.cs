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
    public static readonly Phrase EmptyRefused = new("The food in the pot could not be put back, so nothing was loaded.", "锅里的食材放不回去，所以没有放入新食材。");
    public static readonly Phrase AddedDish = new("Added {0} to the pot as one more dish.", "已把{0}作为又一道菜放进锅里。");
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

    public static readonly Phrase NoFuelStove = new("This stove does not burn fuel.", "这个灶具不用燃料。");
    public static readonly Phrase FuelFull = new("The fuel slots are full.", "燃料格已满。");
    public static readonly Phrase FuelByHand = new("Put one fuel in by hand first; topping up adds more of the same.", "请先手动放入一份燃料，补充时会加入同样的燃料。");
    public static readonly Phrase NoFuelLeft = new("No more of this stove's fuel in your bag or storage.", "背包和储物中没有这个灶具用的燃料了。");
    public static readonly Phrase FuelAdded = new("Added {0} fuel.", "已加入 {0} 份燃料。");
    public static readonly Phrase FuelRefused = new("The game refused the fuel.", "游戏拒绝了这份燃料。");
    public static readonly Phrase NothingQueued = new("Nothing queued for this stove.", "没有为这个灶具排队的菜。");
    public static readonly Phrase Queued = new("Queued {0}.", "已将{0}加入队列。");
    public static readonly Phrase QueueFull = new("The queue is full.", "队列已满。");
    public static readonly Phrase Unqueued = new("Removed {0} from the queue.", "已将{0}移出队列。");
    public static readonly Phrase NoLast = new("Nothing has been cooked on this stove since the game started.", "本次启动游戏后，这个灶具还没有做过菜。");
    public static readonly Phrase ToastSource = new("Cooking Hub", "烹饪中心");
    public static readonly Phrase DishStored = new("Put {0} away in {1}.", "已把{0}放进{1}。");
    public static readonly Phrase DishStays = new("{0} stays in the stove: no storage tagged for it has room.", "{0}留在灶具里：没有带相应标签的储物放得下。");
    public static readonly Phrase Starting = new("The pot is filled for {0}. Starting it.", "已为{0}放好食材，正在开始烹饪。");

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
        ("sortBy", new("Sort:", "排序：")),
        ("sNone", new("Default", "默认")),
        ("sSatiety", new("Satiety", "饱腹")),
        ("sStamina", new("Stamina", "精力")),
        ("sMorale", new("Morale", "心态")),
        ("sHealth", new("Health", "健康")),
        ("sVitality", new("Vitality", "生命")),
        ("sQuality", new("Quality", "品质")),
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
        ("partFresh", new("All ingredients fresh", "食材全部新鲜")),
        ("partRotten", new("Spoiled ingredient", "有变质食材")),
        ("partSeason1", new("1 seasoning", "1 种调味料")),
        ("partSeasonN", new("{0} seasonings", "{0} 种调味料")),
        ("partExact", new("Exact recipe", "精确配方")),
        ("partStove", new("Stove quality", "灶具品质")),
        ("nSeason", new("Add {0}", "加入{0}")),
        ("nSwap", new("Swap the spoiled {0} for fresh", "把变质的{0}换成新鲜的")),
        ("nGain", new("about +{0} Perfect", "完美约 +{0}")),
        ("hub", new("Cooking Hub", "烹饪中心")),
        ("tabBook", new("Recipe Book", "菜谱")),
        ("tabNow", new("Cook now", "现在能做")),
        ("tabQueue", new("Queued", "队列")),
        ("searchFood", new("Search dishes or food", "搜索菜品或食材")),
        ("fCanLoad", new("Can load", "可以放入")),
        ("fNew", new("New", "新菜")),
        ("missing", new("Missing: {0}", "缺少：{0}")),
        ("load", new("Load", "放入")),
        ("queue", new("Queue", "排队")),
        ("unqueue", new("Remove", "移除")),
        ("loadNext", new("Load next", "放入下一道")),
        ("waiting", new("waiting", "等待中")),
        ("spoiledWord", new("spoiled", "已变质")),
        ("nowNote", new("From your bag and every storage. Food that spoils soonest first.", "来自背包和所有储物。最快变质的食材排在前面。")),
        ("nowNone", new("Nothing this stove can make from your food right now.", "现在你的食材做不了这个灶具能做的菜。")),
        ("pantryNote", new("Click a food to put one in this pot from where it is stored.", "点击食材，从存放处放一份到锅里。")),
        ("queueNote", new("Queued from the Cooking Hub window. Load next fills the first one, putting back any food already in the pot.", "在烹饪中心窗口中排队。「放入下一道」会放入第一道，并把锅里已有的食材放回原处。")),
        ("queueNone", new("Nothing queued for this stove. Queue dishes from the Cooking Hub window.", "没有为这个灶具排队的菜。可以在烹饪中心窗口中排队。")),
        ("queueKNote", new("Queued dishes wait for a stove: open one and its Queued tab fills them one at a time.", "排队的菜会等待灶具：打开灶具后，在其「队列」标签中逐道放入。")),
        ("queueKNone", new("Nothing queued. Press Queue on a dish to add it.", "队列为空。点击菜品的「排队」即可加入。")),
        ("speed", new("x{0} speed", "{0} 倍速度")),
        ("quality", new("+{0} quality", "品质 +{0}")),
        ("cookTime", new("Cook time {0}", "烹饪时间 {0}")),
        ("hm", new("{0}h {1}m", "{0}小时{1}分")),
        ("mins", new("{0}m", "{0}分")),
        ("repeat", new("Repeat last: {0}", "重做上一道：{0}")),
        ("topUp", new("Top up from storage ({0})", "从储物补充（{0}）")),
        ("whyLevel", new("Cooking level {0}: rolls {1}-100", "烹饪等级 {0}：掷点 {1}-100")),
    };
}
