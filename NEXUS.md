Name: Cooking Hub
Summary: A better stove screen: odds for every dish, search and filters, Load from your storages, a cooking queue that starts the next dish when you empty the stove, Repeat last and fuel top up. Lists every dish the stove can make from your food, soonest to spoil first, and shows why a dish turns out the way it does, with one-click fixes for better results.

Description (BBCode):
[center][img]https://sldb.core-directive.com/media/save/1-save-safe.png[/img][/center]

Know what you can cook and how good it will turn out before you light the stove. Cooking Hub shows the chance of each quality right on the game's cooking screen, with the reasons and one-press fixes, lists every dish the stove can make from your food, and fills a pot with food from your bag and home storages. Open any stove, juicer, microwave, oven or coffee machine and its cooking screen gains tabs, a searchable recipe book with odds, a Load button that pulls food from storage, a cooking queue, Repeat last and a fuel top up.

[size=5]Main features[/size]
[list]
[*][b]A better cooking screen[/b]: the game's own stove screen gets tabs. Recipe Book adds a search box, filters (All, Can load, New, Locked), where each dish's food is or what is missing, a bar of its odds and its Perfect chance. Cook now lists what the stove can make from all your food, soonest to spoil first. Both can be sorted by Satiety, Stamina, Morale, Fitness, Life or Quality. Cook now and Pantry cards, and the pot cards of known dishes, show what each dish or food restores, one icon per stat. Hovering a card shows the cooking screen's tooltip, Info Pack's details included. Pantry lists all your cooking food and how much you have, and puts one of any food in the pot with a click.[/*]
[*][b]Load from storage[/b]: the game only counts the food in your bag, so a recipe your storages have food for gets a Load button. Food already in the pot goes back where it came from first. Once your cooking level lets one pot cook several dishes, Load adds the new dish next to what's already in the pot, if it fits. Load uses the food closest to spoiling first, or the freshest first if you prefer (setting [font=Courier New]FillSpoilingFirst[/font]).[/*]
[*][b]Cooking queue[/b]: click Queue next to Load, on a Recipe Book row or a Cook now card, to add the dish to that stove's queue (up to 20). Queuing a dish reserves its ingredients, so Load, Repeat last and other stoves won't use them. The Queue button only shows up if you have enough ingredients left after everything already queued. When you take the finished dishes out of a stove, it fills itself for the next queued dish and starts cooking, even with its screen closed. If a queued dish's ingredients spoil or you use them yourself, it's marked "short of food" and the next dish cooks first. The Queued tab lists the queue in order, with a Remove button on each dish. The stove needs fuel, or power if it's electric. The queue lasts until you quit the game. Pairs well with Put dishes away.[/*]
[*][b]Repeat last[/b]: refills an empty stove with the dish it last started and starts it again.[/*]
[*][b]Top up from storage[/b]: fills the free fuel slots with the fuel the stove already burns, from your bag and storages. Put the first fuel in by hand so the mod knows which kind.[/*]
[*][b]Put dishes away[/b] (setting [font=Courier New]PutDishesAway[/font], off by default): a finished dish moves out of the stove into the home storage whose tags want it, the way Storage Hub puts items away. A dish no tagged storage has room for stays in the stove. With Exiled's Toolkit, each dish put away shows a toast.[/*]
[*][b]Not learned tab[/b] (setting [font=Courier New]ShowUnlearnedRecipes[/font], off by default): lists the recipes this stove can cook that your recipe book does not show yet, with the food you have for each, what is missing, and the cooking level a recipe shows up at. Load and Queue work on the ones your food covers. Cooking one teaches it. With the setting off, the mod lists only the recipes the game shows.[/*]
[*][b]Odds on the cooking screen[/b]: under each dish the cooking screen forecasts, the chance of a Failed, Average, Good and Perfect dish, and why: your cooking level's roll, fresh or spoiled food, seasonings, an exact recipe, the stove. It also shows the worst result a roll can give.[/*]
[*][b]What would help[/b]: buttons under the forecast to add a seasoning you have, or to swap a spoiled ingredient in the pot for a fresh one, each with how much it adds to the Perfect chance.[/*]
[/list]
Cooking Hub adds nothing to your save and does not edit game files. You can remove it at any time.

Cooking Hub covers what ProjectCook's dish preview does. Use one or the other, not both.

Works in English and Chinese.

[center][img]https://sldb.core-directive.com/img/mods-banner.jpg[/img]
[size=4][url=https://www.nexusmods.com/survivallog/mods/73]Get Mod Manager[/url] to set up all your mods in game  |  [url=https://sldb.core-directive.com/en/]Open the Survival Log wiki[/url] for items, recipes and maps[/size][/center]

[size=5]Requirements[/size]
[list]
[*]Survival Log 1.1.19611. A game update can break it until the mod is updated.[/*]
[*][url=https://www.nexusmods.com/survivallog/mods/12]BepInEx for Survival Log[/url]. Install it first.[/*]
[*]Optional: [url=https://www.nexusmods.com/survivallog/mods/74]Stacking[/url]. Cooking Hub loads food Stacking made stackable one unit at a time, the way Stacking wants it. Without Stacking, nothing changes.[/*]
[*]Optional: [url=https://www.nexusmods.com/survivallog/mods/61]Exiled's Toolkit[/url] 0.4.0 or newer. With it, settings changed on the Mod Manager's Mods screen take effect without restarting the game, and put-away dishes show as toasts. Without it, they take effect at the next game start. With 0.10.0 or newer, its UI size setting also sizes the pop-up messages, and with 0.11.0 or newer, Cooking Hub's own UiScale setting can set their size for this mod alone.[/*]
[/list]

[size=5]Installation[/size]
[list=1]
[*]Install [url=https://www.nexusmods.com/survivallog/mods/12]BepInEx[/url] [i](if no other mods were installed before, start the game once so BepInEx finishes setup, then quit)[/i].[/*]
[*]If ProjectCook is installed, delete its DLL from the [font=Courier New]BepInEx\plugins[/font] folder.[/*]
[*]Download the zip from the Files tab.[/*]
[*]Extract it into the game folder (the folder with [font=Courier New]SurvivalLog.exe[/font]). The zip holds a BepInEx folder, so the DLL lands in the right place. When asked, merge the folders. The result should be:
[code]C:\Program Files (x86)\Steam\steamapps\common\Survival Log\BepInEx\plugins\CookingHub.dll[/code][/*]
[/list]

[size=5]Uninstall[/size]
Delete [font=Courier New]CookingHub.dll[/font] from the [font=Courier New]BepInEx\plugins[/font] folder.

[size=5]Configuration[/size]
To change which food Load uses first, whether the odds show, or whether dishes are put away, edit [font=Courier New]BepInEx\config\com.dev.survivallog.cookinghub.cfg[/font] while the game is closed, or use the Mod Manager's Mods screen (see [url=https://github.com/AnExiledDev/SurvivalLog-CookingHub/blob/main/CONFIG.md]CONFIG.md[/url] for every setting; the cfg file explains each setting in English and Chinese).

[size=5]Feedback[/size]
Found a bug or have an idea? Come say hi on the [url=https://discord.gg/trTsyxUFqg]Core Directive Discord[/url], or post in this page's Posts tab. For bugs, please include [font=Courier New]BepInEx\LogOutput.log[/font] and, if you turned on [font=Courier New]LogToFile[/font], [font=Courier New]BepInEx\cooking-hub.log[/font].
To reach me directly, message [b]exiled.dev[/b] on Discord; you can find me in the [url=https://discord.gg/MZGg4gQAUe]official Survival Log Discord[/url]. I'm also on X as [url=https://x.com/AnExiledDev]@AnExiledDev[/url].

[size=5]Source and license[/size]
Open source under MIT: [url=https://github.com/AnExiledDev/SurvivalLog-CookingHub]github.com/AnExiledDev/SurvivalLog-CookingHub[/url]

[line]
[size=5]简体中文[/size]

开火之前就知道能做什么菜、能做出什么品质。Cooking Hub 直接在游戏的烹饪界面显示各品质的概率、原因和一键改进办法，列出这个灶具用你的食材能做的每道菜，并能从背包和家中储物里取食材放入锅中。打开任何灶具、榨汁机、微波炉、烤箱或咖啡机，烹饪界面会多出标签页、可搜索并显示概率的菜谱、从储物取食材的「放入」按钮、烹饪队列、重做上一道和补充燃料。

[size=5]主要功能[/size]
[list]
[*][b]更好的烹饪界面[/b]：游戏自己的灶具界面多出标签页。「菜谱」加入搜索框和筛选（全部、可以放入、新菜、未解锁），并显示每道菜的食材在哪里或缺少什么、品质概率条和完美概率。「现在能做」列出这个灶具用你所有食材能做的菜，最快变质的排在前面。两者都可按饱腹、精力、心态、健康、生命或品质排序。「现在能做」「食材库存」的卡片和锅中已知菜品的卡片会在底部用图标显示能恢复的各项数值。鼠标悬停在卡片上会显示烹饪界面自带的提示，包括 Info Pack 的详情。「食材库存」列出所有烹饪食材及数量，点击任意食材即可放一份到锅里。[/*]
[*][b]从储物放入[/b]：游戏只计算背包，因此会把储物里有食材的菜显示为不足；只要储物够用，这些菜会出现「放入」按钮。锅里已有的食材会先放回原处。烹饪等级达到一锅可做多道菜后，只要放得下，「放入」会把新菜加在锅里已有的食材旁边。「放入」默认优先使用快变质的食材，也可改为优先使用最新鲜的（设置 [font=Courier New]FillSpoilingFirst[/font]）。[/*]
[*][b]烹饪队列[/b]：点击「菜谱」各行或「现在能做」卡片上「放入」旁边的「排队」，就能把菜加入这个灶具的队列（最多 20 道）。排队的菜会留好食材，「放入」、重做上一道和其他灶具都不会用掉它们。只有在已排队的菜都留好食材后还够做，才会显示「排队」按钮。取走灶具里做好的菜后，灶具会自动放入队列里下一道菜的食材并开始烹饪，烹饪界面关着也可以。如果排队的菜的食材变质了或被你自己用掉，这道菜会显示「食材不足」，先做下一道。「队列」标签按顺序列出排队的菜，每道菜都有「移除」按钮。灶具里需要有燃料，电器需要有电。离开游戏后队列清空。与自动收菜搭配使用效果最好。[/*]
[*][b]重做上一道[/b]：锅空且没在烹饪时，把这个灶具上次做的菜重新放入并开始烹饪。[/*]
[*][b]从储物补充燃料[/b]：用这个灶具正在烧的燃料，从背包和储物中填满空的燃料格。第一份燃料请手动放入，模组才知道用哪种。[/*]
[*][b]自动收菜[/b]（设置 [font=Courier New]PutDishesAway[/font]，默认关闭）：做好的菜会从灶具移到标签需要它的家中储物里，方式与 Storage Hub 收纳物品相同。没有带相应标签的储物放得下时，菜留在灶具里。安装 Exiled's Toolkit 后，每收好一道菜都会弹出提示。[/*]
[*][b]「未学会」标签页[/b]（设置 [font=Courier New]ShowUnlearnedRecipes[/font]，默认关闭）：列出这个灶具能做、但菜谱里还没显示的菜，以及你有的食材、缺少的食材，以及会在哪个烹饪等级显示。食材够的菜可以「放入」和「排队」。做过一次就会学会。关闭时，模组只列出游戏显示的菜谱。[/*]
[*][b]烹饪界面显示概率[/b]：在烹饪界面预测的每道菜下方，显示做出失败、普通、优良和完美的概率，以及原因：烹饪等级的掷点、食材新鲜或变质、调味料、精确配方、灶具。还会显示掷点最差时的结果。[/*]
[*][b]怎样做会更好[/b]：预测下方的按钮可以加入你拥有的调味料，或把锅里变质的食材换成新鲜的，并标出各自能让完美概率提高多少。[/*]
[/list]
Cooking Hub 不会向存档添加任何内容，也不会修改游戏文件，可以随时移除。

Cooking Hub 包含 ProjectCook 菜品预览的功能，两者请只装一个。安装前如已装有 ProjectCook，请从 [font=Courier New]BepInEx\plugins[/font] 文件夹中删除它的 DLL。

[url=https://www.nexusmods.com/survivallog/mods/73]获取 Mod Manager[/url]，在游戏里设置所有模组。[url=https://sldb.core-directive.com/zh/]打开 Survival Log 维基[/url]，物品、配方、地图一查就有。

[size=5]前置要求[/size]
[list]
[*]Survival Log 1.1.19611。游戏更新后模组可能暂时失效，直到模组更新为止。[/*]
[*][url=https://www.nexusmods.com/survivallog/mods/12]BepInEx for Survival Log[/url]，请先安装。[/*]
[*]可选：[url=https://www.nexusmods.com/survivallog/mods/74]Stacking[/url]。Cooking Hub 会按 Stacking 的规则，把它变成可堆叠的食材一份一份放入锅中。未安装 Stacking 时不受影响。[/*]
[*]可选：[url=https://www.nexusmods.com/survivallog/mods/61]Exiled's Toolkit[/url] 0.4.0 或更新版本。安装后，在 Mod Manager 的模组界面修改的设置无需重启游戏即可生效，自动收菜也会弹出提示；未安装时，在下次启动游戏时生效。0.10.0 或更新版本还能用它的界面大小设置调整弹出提示的大小，0.11.0 或更新版本还可以用 Cooking Hub 自己的 UiScale 设置单独调整本模组弹出提示的大小。[/*]
[/list]

[size=5]安装[/size]
[list=1]
[*]安装 [url=https://www.nexusmods.com/survivallog/mods/12]BepInEx[/url] [i]（如果之前没有装过其他模组，先启动一次游戏，让 BepInEx 完成初始化，然后退出）[/i]。[/*]
[*]在「文件」页下载压缩包。[/*]
[*]把压缩包解压到游戏目录（[font=Courier New]SurvivalLog.exe[/font] 所在的文件夹）。压缩包里带有 BepInEx 文件夹，DLL 会自动放到正确位置。如果提示合并文件夹，选择合并。解压后应为：
[code]C:\Program Files (x86)\Steam\steamapps\common\Survival Log\BepInEx\plugins\CookingHub.dll[/code][/*]
[/list]

[size=5]卸载[/size]
从 [font=Courier New]BepInEx\plugins[/font] 文件夹中删除 [font=Courier New]CookingHub.dll[/font]。

[size=5]配置[/size]
如需修改放入食材的顺序、是否显示概率或是否自动收菜，请在游戏关闭时编辑 [font=Courier New]BepInEx\config\com.dev.survivallog.cookinghub.cfg[/font]。配置文件中每项设置都有中文说明，完整说明见 [url=https://github.com/AnExiledDev/SurvivalLog-CookingHub/blob/main/CONFIG.md]CONFIG.md[/url]（英文）。修改会在下次启动游戏时生效。

[size=5]反馈[/size]
发现 bug 或有好点子？欢迎来 [url=https://discord.gg/trTsyxUFqg]Core Directive Discord[/url] 打个招呼，或在这里留言。报告 bug 时，请附上 [font=Courier New]BepInEx\LogOutput.log[/font] 和 [font=Courier New]BepInEx\cooking-hub.log[/font]。
想直接联系我，可以在 Discord 上给 [b]exiled.dev[/b] 发消息，我在 [url=https://discord.gg/MZGg4gQAUe]Survival Log 官方 Discord[/url] 里。也可以在 X 上找我：[url=https://x.com/AnExiledDev]@AnExiledDev[/url]。

[size=5]源代码与许可[/size]
基于 MIT 许可开源：[url=https://github.com/AnExiledDev/SurvivalLog-CookingHub]github.com/AnExiledDev/SurvivalLog-CookingHub[/url]
