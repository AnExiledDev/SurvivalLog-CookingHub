# Changelog

## 0.5.4

- Load, the recipe book and fuel top up no longer use food or fuel staged in the neighbor's basket, and Put dishes away never puts a dish into it.

## 0.5.3

- The Health and Vitality sorts and stat names are now Fitness and Life, the names the game uses.

## 0.5.2

- Recipes you learned before reaching the cooking level that normally shows them (Family Bucket, for one) now get their stats, odds and missing items in the Recipe Book and can show up in Cook now.

## 0.5.1

- Recipe Book rows show what the dish restores, one icon per stat, as Cook now cards do.
- With Info Pack installed, every Cook now and Pantry card shows Info Pack's own tooltip. Before, a dish from a recipe group (a row with "2 recipes") showed only its name, and the other cards showed the game's plain text above Info Pack's details.

## 0.5.0

- The Cooking Hub window, its pot button on the HUD and the `PanelKey` key (`K`) are gone. Everything the mod does now happens on the game's own cooking screen: Cook now lists what the stove can make from your food, and Pantry lists all your food.
- The queue is gone: no Queued tab on the cooking screen and no Load next. `BepInEx\config\CookingHub.queue.txt` is no longer used and can be deleted.
- `FillSpoilingFirst` and `ShowOdds` are set in the cfg file or on the Mod Manager's Mods screen.

## 0.4.1

- Cook now, Pantry and Queued cards show what the dish or food restores at the bottom, one icon per stat. Pot cards for known dishes show it too, also with the odds turned off.
- Hovering a Cook now, Pantry or Queued card shows the cooking screen's own tooltip, so Info Pack's item details appear there as they do in Recipe Book.

## 0.4.0

- Icons show again on the cooking screen's Cook now, Pantry and Queued tabs.
- Recipe Book and Cook now can be sorted by what the dish restores: Satiety, Stamina, Morale, Health or Vitality, or by Quality (the Perfect chance). Default keeps the game's order.
- Load from storage works with Stacking installed. Food Stacking made stackable goes in one unit at a time, and only as many units as the recipe needs.
- Loading a dish into a pot that already holds food puts that food back where it came from (or in the bag) and fills the pot for the new dish.
- From the cooking level that lets one pot cook several dishes, Load adds the new dish beside what is already in the pot, as long as it fits and the pot is not headed for Dark Food.
- The cooking screen switches to the new view sooner after it opens.

## 0.3.1

- With Exiled's Toolkit installed, each dish put away shows a toast naming the dish and the storage. A dish left in the stove shows a warning toast. Without the Toolkit, the line shows on the cooking screen as before.

## 0.3.0

- New setting `PutDishesAway` (off by default): a finished dish moves out of the stove into the home storage whose tags want it, ranked the way Storage Hub ranks put-away. A storage already holding that dish is the fallback. A dish nothing takes stays in the stove, and a line says so once.

## 0.2.0

- The stove's own cooking screen (stove, juicer, microwave, oven, coffee machine) gets tabs: Recipe Book, Cook now, Pantry and Queued.
- The Recipe Book has a search box and filters (All, Can load, New, Locked). Each recipe row shows where its food is or what is missing, a bar of its quality odds and its Perfect chance.
- A recipe the game marks as short but your storages cover gets a Load button that fills the pot from your bag and storages.
- Cook now lists what this stove can make from all your food, the dish whose food spoils soonest first. The Pantry tab puts one of any food in the pot with a click.
- Queue dishes from the Cooking Hub window (new Queue button and Queued tab), then fill them one at a time from a stove's Queued tab with Load next. The queue is kept between game starts.
- Repeat last refills an empty stove with the dish it last started and starts it again.
- Top up from storage fills the free fuel slots with the fuel the stove already burns, from your bag and storages.
- The stove line shows the stove's speed, quality bonus and the cook time of the forecast dish.
- The odds under the forecast are now drawn as bars.

## 0.1.0

First release. The chance of each quality on the game's cooking screen with the reasons and one-press seasoning and swap buttons, a list of every dish your food can make, filling a pot from your bag and home storages, and a pantry of all your cooking food.
