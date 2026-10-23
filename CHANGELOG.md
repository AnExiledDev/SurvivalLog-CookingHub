# Changelog

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
