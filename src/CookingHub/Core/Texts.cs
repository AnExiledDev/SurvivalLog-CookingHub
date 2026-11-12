using System.Collections.Generic;
using ExiledToolkit.Kit;

namespace CookingHub.Core;

/// <summary>The words the mod puts on screen: press results, reasons, and the page's own labels.</summary>
public static class Texts
{
    private const string Log = "BepInEx/LogOutput.log";

    public static readonly Phrase NoGame = new("Load a game first.", "请先载入游戏。");
    public static readonly Phrase NotReady = new("Can't cook right now: {0}.", "现在无法烹饪：{0}。");
    public static readonly Phrase Away = new("you are away from home", "你不在家中");
    public static readonly Phrase ReadFailed = new("your food could not be read, see " + Log, "无法读取食材，详见 " + Log);
    public static readonly Phrase Loading = new("the recipes are still loading", "菜谱还在加载");
    public static readonly Phrase Failed = new("Something went wrong in Cooking Hub. See " + Log + ".", "Cooking Hub 出错了，详见 " + Log + "。");

    public static readonly Phrase NoStove = new("Open a stove's cooking screen first.", "请先打开灶具的烹饪界面。");
    public static readonly Phrase Cooking = new("This stove is already cooking.", "这个灶具正在烹饪。");
    public static readonly Phrase NoRecipe = new("Cooking Hub can't find that recipe.", "找不到这个菜谱。");
    public static readonly Phrase EmptyRefused = new("The food in the pot could not be put back, so nothing was loaded.", "锅里的食材放不回去，所以没有放入新食材。");
    public static readonly Phrase AddedDish = new("Added {0} to the pot as one more dish.", "已把{0}作为又一道菜放进锅里。");
    public static readonly Phrase PotFull = new("The pot is full.", "锅已经满了。");
    public static readonly Phrase StoveRefuses = new("This stove can't cook {0}.", "这个灶具不能做{0}。");
    public static readonly Phrase TooBig = new("{0} needs {1} slots and this stove has {2}.", "{0}需要 {1} 格，这个灶具只有 {2} 格。");
    public static readonly Phrase NotEnough = new("Not enough fresh food for {0}.", "新鲜食材不够做{0}。");
    public static readonly Phrase Filled = new("Loaded the ingredients for {0}.", "已为{0}放好食材。");
    public static readonly Phrase MovedSome = new("Moved {0} of {1}. The game wouldn't take the rest.", "已放入 {0}/{1} 份，其余的放不进去。");
    public static readonly Phrase NoneLeft = new("No fresh {0} left.", "没有新鲜的{0}了。");
    public static readonly Phrase Added = new("Added {0}.", "已加入{0}。");
    public static readonly Phrase MoveRefused = new("The game wouldn't move that.", "没能移动这份食材。");
    public static readonly Phrase NothingSpoiled = new("No spoiled {0} in the pot.", "锅里没有变质的{0}。");
    public static readonly Phrase Swapped = new("Swapped the spoiled {0} for a fresh one.", "已把变质的{0}换成新鲜的。");

    public static readonly Phrase NoFuelStove = new("This stove does not burn fuel.", "这个灶具不用燃料。");
    public static readonly Phrase FuelFull = new("The fuel slots are full.", "燃料格已满。");
    public static readonly Phrase FuelByHand = new("Put one fuel in by hand first. Top up adds more of the same kind.", "请先手动放入一份燃料，补充时会加入同一种燃料。");
    public static readonly Phrase NoFuelLeft = new("No more of this stove's fuel in your bag or storage.", "背包和储物中没有这个灶具用的燃料了。");
    public static readonly Phrase FuelAdded = new("Added {0} fuel.", "已加入 {0} 份燃料。");
    public static readonly Phrase FuelRefused = new("The game wouldn't take the fuel.", "燃料放不进去。");
    public static readonly Phrase NoLast = new("Nothing has been cooked on this stove since the game started.", "本次启动游戏后，这个灶具还没有做过菜。");
    public static readonly Phrase ToastSource = new("Cooking Hub", "烹饪中心");
    public static readonly Phrase DishStored = new("Put {0} away in {1}.", "已把{0}放进{1}。");
    public static readonly Phrase DishStays = new("{0} stays in the stove because no storage is tagged for it.", "{0}留在灶具里：没有带相应标签的储物。");
    public static readonly Phrase DishStaysFull = new("{0} stays in the stove because {1} is full.", "{0}留在灶具里：{1}已满。");
    public static readonly Phrase DishStaysAllFull = new("{0} stays in the stove because {1} and every other storage that takes it are full.", "{0}留在灶具里：{1}和其他能放它的储物都满了。");
    public static readonly Phrase DishStaysQueue = new(" The stove's queue waits until the dish is out. Cooking Hub tries again every {0} seconds.", "这个灶具的队列要等这道菜取出后才继续，每 {0} 秒会再试一次。");
    public static readonly Phrase Queued = new("Queued {0}.", "已将{0}加入队列。");
    public static readonly Phrase QueueFull = new("The queue is full.", "队列已满。");
    public static readonly Phrase SetAside = new("The ingredients for {0} are reserved for queued dishes.", "做{0}的食材已留给排队的菜。");
    public static readonly Phrase Unqueued = new("Removed {0} from the queue.", "已将{0}移出队列。");
    public static readonly Phrase QueueStarted = new("Started {0} from the queue on {1}.", "已从队列开始在{1}做{0}。");
    public static readonly Phrase QueueNotStarted = new("Loaded {1} for {0} from the queue, but it didn't start cooking. Press Start Cooking yourself.", "已从队列为{0}在{1}放好食材，但没有开始烹饪。请手动点击「开始烹饪」。");
    public static readonly Phrase QueueNoHeat = new("{0} has dishes queued but no fuel or power.", "{0}有排队的菜，但没有燃料或电力。");
    public static readonly Phrase QueueShort = new("{0} has dishes queued but not enough fresh food for any of them.", "{0}有排队的菜，但新鲜食材不够做其中任何一道。");
    public static readonly Phrase Starting = new("Loaded the ingredients for {0}. Starting to cook.", "已为{0}放好食材，正在开始烹饪。");

    public static readonly Phrase Bag = new("Bag", "背包");
    public static readonly Phrase Storage = new("Storage", "储物");
    public static readonly Phrase Pot = new("Pot", "锅中");

    /// <summary>The words page.js draws on the cooking screen with, by the key it looks them up by.</summary>
    public static readonly IReadOnlyList<(string Key, Phrase Phrase)> Page = new (string, Phrase)[]
    {
        ("tabPantry", new("Pantry", "食材库存")),
        ("fAll", new("All", "全部")),
        ("fLocked", new("Locked", "未解锁")),
        ("sortBy", new("Sort:", "排序：")),
        ("sNone", new("Default", "默认")),
        ("sSatiety", new("Satiety", "饱腹")),
        ("sStamina", new("Stamina", "精力")),
        ("sMorale", new("Morale", "心态")),
        ("sHealth", new("Fitness", "健康")),
        ("sVitality", new("Life", "生命")),
        ("sQuality", new("Quality", "品质")),
        ("anyType", new("any {0}", "任意{0}")),
        ("pSpoiled", new("{0} spoiled", "{0} 份已变质")),
        ("pKeeps", new("Keeps", "不会变质")),
        ("pEmpty", new("You have no food to cook with.", "你没有可用于烹饪的食材。")),
        ("hours", new("{0} h", "{0} 小时")),
        ("days", new("{0} d", "{0} 天")),
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
        ("tabBook", new("Recipe Book", "菜谱")),
        ("tabNow", new("Cook now", "现在能做")),
        ("searchFood", new("Search dishes or food", "搜索菜品或食材")),
        ("fCanLoad", new("Can load", "可以放入")),
        ("fNew", new("New", "新菜")),
        ("missing", new("Missing: {0}", "缺少：{0}")),
        ("load", new("Load", "放入")),
        ("queue", new("Queue", "排队")),
        ("unqueue", new("Remove", "移除")),
        ("tabQueue", new("Queued", "队列")),
        ("waiting", new("waiting", "等待中")),
        ("queueShortChip", new("short of food", "食材不足")),
        ("queueNote", new("This stove cooks these in order once you take its finished dishes out. Each dish's ingredients are reserved, so Load and other stoves won't use them. A dish that's short of food waits while the next one cooks.", "取走这个灶具里做好的菜后，它会按顺序做这些菜。每道菜的食材都已留好，「放入」和其他灶具不会用掉。食材不够的菜会等着，先做下一道。")),
        ("queueNone", new("Nothing is queued for this stove. Press Queue on a dish to add it. If the stove is empty and not cooking, it starts right away.", "这个灶具没有排队的菜。点击菜品的「排队」即可加入。灶具空着又没在烹饪时，会马上开始做。")),
        ("spoiledWord", new("spoiled", "已变质")),
        ("nowNote", new("Made from your bag and every storage. Dishes whose food spoils soonest come first.", "用背包和所有储物里的食材。食材最快变质的菜排在前面。")),
        ("nowNone", new("Your food can't make anything on this stove right now.", "你现在的食材在这个灶具上什么都做不了。")),
        ("tabUnlearned", new("Not learned", "未学会")),
        ("unlearnedNote", new("Recipes this stove can cook that your recipe book does not show yet. Cooking one teaches it.", "这个灶具能做、但菜谱里还没显示的菜。做过一次就会学会。")),
        ("unlearnedNone", new("This stove has no recipes left to learn.", "这个灶具没有要学的菜谱了。")),
        ("unlearnedChip", new("not learned", "未学会")),
        ("showsAt", new("cooking level {0}", "烹饪等级 {0}")),
        ("pantryNote", new("Click a food to put one in the pot from your bag or storage.", "点击食材，从背包或储物放一份到锅里。")),
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
