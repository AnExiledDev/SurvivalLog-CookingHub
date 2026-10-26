# Changelog

## 0.6.2

- Fixed: with two stoves cooking the same dish, loading the second one could take the ingredients out of the first and stop it. This also happened with ingredients that have several uses, like frozen pizza. Load, Add and the queue now only take food from your bag and storages, and the Pantry tab no longer lists food sitting in a stove.

## 0.6.1

- Fixed: a queued dish could run out of ingredients if you cooked something else first.
- Queuing a dish now reserves its ingredients. Load, Repeat last and other stoves won't touch them.
- The Queue button only shows up if you have enough ingredients left after everything already queued.
- If a queued dish's ingredients spoil or you use them yourself, it's marked "short of food" and skipped until you have enough.

## 0.6.0

- The cooking queue is back, now on the cooking screen. Click Queue next to Load, on a Recipe Book row or a Cook now card, to add that dish to the stove's queue.
- Once you take the finished dishes out of a stove, it fills itself for the next queued dish and starts cooking, even with its screen closed.
- A queued dish you don't have enough food for waits, and the next one cooks first.
- The Queued tab lists the stove's queue, with a Remove button on each dish.

## 0.5.4

- Load, the Recipe Book and fuel top up no longer use food or fuel you put in the neighbor's basket.
- Put dishes away never puts a dish into the neighbor's basket.

## 0.5.3

- The Health and Vitality sorts and stats are now called Fitness and Life, the names the game uses.

## 0.5.2

- Recipes you learned before your cooking level shows them (the Family Bucket, for example) now show their stats, odds and missing ingredients in the Recipe Book, and can show up in Cook now.

## 0.5.1

- Recipe Book rows show what the dish restores, one icon per stat, the same as Cook now cards.
- With Info Pack installed, every Cook now and Pantry card shows Info Pack's full tooltip.
- Fixed: a dish from a recipe group (a row that says "2 recipes") showed only its name in the tooltip, and other cards showed the game's plain text above Info Pack's details.

## 0.5.0

- The Cooking Hub window, its pot button on the HUD and its K key are gone. Everything now happens on the game's own cooking screen: Cook now lists what the stove can make from your food, and Pantry lists all your food.
- The queue is gone for now: there is no Queued tab and no Load next. You can delete `BepInEx\config\CookingHub.queue.txt`.
- FillSpoilingFirst and ShowOdds are set in the cfg file or on the Mod Manager's Mods screen.

## 0.4.1

- Cook now, Pantry and Queued cards show what the dish or food restores, one icon per stat. Cards in the pot for dishes you know show it too, even with the odds turned off.
- Hovering a Cook now, Pantry or Queued card shows the cooking screen's own tooltip, so Info Pack's details appear there the same as in the Recipe Book.

## 0.4.0

- Fixed: icons were missing on the Cook now, Pantry and Queued tabs.
- Recipe Book and Cook now can be sorted by what the dish restores (Satiety, Stamina, Morale, Health or Vitality) or by its chance of coming out Perfect. Default keeps the game's order.
- Load works with Stacking installed. Food that Stacking made stackable goes in one at a time, and only as many as the recipe needs.
- Loading a dish into a pot that already holds food puts that food back where it came from (or in your bag) first.
- Once your cooking level lets one pot cook several dishes, Load adds the new dish next to what's already in the pot, as long as it fits and won't turn into Dark Food.
- The cooking screen switches to the new layout faster when you open it.

## 0.3.1

- With Exiled's Toolkit installed, each dish that gets put away shows a notice naming the dish and the storage. A dish left in the stove shows a warning notice. Without the Toolkit, the message shows on the cooking screen as before.

## 0.3.0

- New setting PutDishesAway (off by default): a finished dish moves out of the stove into the home storage whose tags want it, the same way Storage Hub puts items away.
- If no tagged storage wants the dish, it goes to a storage that already holds that dish. If none does, it stays in the stove and you get one message about it.

## 0.2.0

- The cooking screen of every stove, juicer, microwave, oven and coffee machine gets tabs: Recipe Book, Cook now, Pantry and Queued.
- The Recipe Book has a search box and filters (All, Can load, New, Locked). Each recipe shows where its food is or what's missing, a bar of its quality odds, and its chance of coming out Perfect.
- When the game says you're short of food for a recipe but your storages have it, a Load button fills the pot from your bag and storages.
- Cook now lists what this stove can make from all your food, with dishes whose food spoils soonest first.
- The Pantry tab puts one of any food into the pot with a click.
- Queue dishes from the Cooking Hub window, then cook them one at a time from a stove's Queued tab with Load next. The queue is kept between game starts.
- Repeat last refills an empty stove with the dish it last cooked and starts it again.
- Top up from storage fills the empty fuel slots with the fuel the stove is already burning, from your bag and storages.
- The stove line shows the stove's speed, its quality bonus, and how long the dish will take.
- The quality odds under the dish preview are shown as bars.

## 0.1.0

- First release.
- The game's cooking screen shows the chance of each quality, the reasons behind it, and one-click buttons to add seasoning or swap spoiled food.
- A list of every dish your food can make.
- Fill a pot from your bag and home storages.
- A pantry with all your cooking food.
