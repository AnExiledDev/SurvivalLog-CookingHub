# Cooking Hub

Know what you can cook and how good it will turn out before you light the stove. Cooking Hub shows the chance of each quality right on the cooking screen, with the reasons and one-click fixes. It lists every dish the stove can make from your food, and fills the pot with food from your bag and home storages.

Open any stove, juicer, microwave, oven or coffee machine and its cooking screen gets tabs, a searchable recipe book with odds, a Load button that pulls food from storage, a cooking queue, Repeat last and fuel top up.

## Features

- **A better cooking screen**: the stove screen gets tabs. Recipe Book adds a search box and filters (All, Can load, New, Locked), and each dish shows where its food is or what's missing, a bar of its odds and its Perfect chance. Cook now lists what the stove can make from all your food, with the food that spoils soonest first. You can sort both by Satiety, Stamina, Morale, Fitness, Life or Quality. Cook now and Pantry cards, and the cards in the pot for dishes you know, show what each dish or food restores, one icon per stat. Hovering a card shows the cooking screen's tooltip, with Info Pack's details if you have it. Pantry lists all your cooking food and how much you have, and puts one of any food in the pot with a click.
- **Load from storage**: the game only counts the food in your bag, so it calls a recipe short even when your storages have the food. Cooking Hub gives that recipe a Load button. Food already in the pot goes back where it came from first. Once your cooking level lets one pot cook several dishes, Load adds the new dish next to what's already in the pot, if it fits. A dish the pot already makes keeps its Load button too, so three Watermelons go in for three Watermelon Juices. Load uses the food closest to spoiling first, or the freshest first if you prefer (setting `FillSpoilingFirst`).
- **Cooking queue**: click Queue next to Load, on a Recipe Book row or a Cook now card, to add the dish to that stove's queue (up to 20). Queuing a dish reserves its ingredients, so Load, Repeat last and other stoves won't use them. The Queue button only shows up if you have enough ingredients left after everything already queued, and a recipe whose ingredients are all reserved is hidden. When you take the finished dishes out of a stove, it fills itself for the next queued dish and starts cooking, even with its screen closed. Once your cooking level lets one pot cook several dishes, it puts in every queued dish that fits and cooks them together. If a queued dish's ingredients spoil or you use them yourself, it's marked "short of food" and the next dish cooks first. The Queued tab lists the queue in order, with a Remove button on each dish. The stove needs fuel, or power if it's electric. The queue lasts until you quit the game. Pairs well with Put dishes away.
- **Repeat last**: refills an empty stove with the dish it last started and starts it again.
- **Top up from storage**: fills the free fuel slots with the fuel the stove already burns, from your bag and storages. Put the first fuel in by hand so the mod knows which kind.
- **Put dishes away** (setting `PutDishesAway`, off by default): a finished dish moves out of the stove into the home storage whose tags want it, the way Storage Hub puts items away. When a pot cooks several dishes, each one is put away. A dish no storage has room for stays in the stove, and a warning names the full storage. Cooking Hub tries again every 30 seconds, and that stove's queue waits until the dish is out. Each dish put away shows a toast.
- **Not learned tab** (setting `ShowUnlearnedRecipes`, off by default): lists the recipes this stove can cook that your recipe book doesn't show yet, with the food you have for each, what's missing, and the cooking level the recipe shows up at. Load and Queue work on the ones you have the ingredients for. Cooking one teaches it. Cook now marks these dishes "not learned". With the setting off, the mod lists only the recipes the game shows.
- **Odds on the cooking screen**: under each dish in the game's dish preview, Cooking Hub shows the chance of a Failed, Average, Good and Perfect result, and why: your cooking level's roll, fresh or spoiled food, seasonings, an exact recipe and the stove. It also shows the worst result a roll can give.
- **What would help**: buttons under the dish preview add a seasoning you have, or swap a spoiled ingredient in the pot for a fresh one. Each button shows how much it adds to the Perfect chance.

Cooking Hub adds nothing to your save and does not edit game files. You can remove it at any time.

Cooking Hub does everything ProjectCook's dish preview does, so use one or the other.

## Requirements

- Survival Log 1.1.20719. Built for this version. A game update can break it until the mod is updated.
- The [BepInEx Pack for Survival Log](https://www.nexusmods.com/survivallog/mods/12), the BepInEx 6 (IL2CPP) build for the game.
- Optional: [Stacking](https://www.nexusmods.com/survivallog/mods/74). Cooking Hub loads food Stacking made stackable one unit at a time, the way Stacking wants it. Without Stacking, nothing changes.
- Optional: [Machine Speed](https://www.nexusmods.com/survivallog/mods/83). When it makes cooking faster, the cook time Cooking Hub shows on the cooking screen is the faster time. Without Machine Speed, nothing changes.
- [Exiled's Toolkit](https://github.com/AnExiledDev/SurvivalLog-ExiledToolkit) 0.19.0 or newer. Cooking Hub does not load without it. It shows Cooking Hub's messages as toasts and applies settings changed on the Mod Manager's Mods screen without a restart.
- Optional: [Mod Manager](https://www.nexusmods.com/survivallog/mods/73). With it, you can change every setting in game on its Mods screen and see what each one does, without opening the config file.

## Install

1. Install the [BepInEx Pack for Survival Log](https://www.nexusmods.com/survivallog/mods/12) (if no other mods were installed before, start the game once so BepInEx finishes setup, then quit).
2. If ProjectCook is installed, delete its DLL from the `BepInEx\plugins` folder.
3. Install Exiled's Toolkit. Download `ExiledToolkit-x.y.z.zip` from [its latest release](https://github.com/AnExiledDev/SurvivalLog-ExiledToolkit/releases/latest) or from [Nexus Mods](https://www.nexusmods.com/survivallog/mods/61), and extract it into the game folder (the folder with `SurvivalLog.exe`).
4. Download `CookingHub-x.y.z.zip` from the [latest release](https://github.com/AnExiledDev/SurvivalLog-CookingHub/releases/latest) or from [Nexus Mods](https://www.nexusmods.com/survivallog/mods/95).
5. Extract the zip into the game folder. It holds a `BepInEx` folder, so the DLL lands in the right place. When asked, merge the folders. The result should be:
   `C:\Program Files (x86)\Steam\steamapps\common\Survival Log\BepInEx\plugins\CookingHub.dll`

## Uninstall

Delete `CookingHub.dll` from the `BepInEx\plugins` folder. For a clean slate, also delete `BepInEx\config\com.dev.survivallog.cookinghub.cfg` and, if it exists, `BepInEx\cooking-hub.log`.

## Configuration

Settings live in `BepInEx\config\com.dev.survivallog.cookinghub.cfg`, created the first time the game starts with the mod. See [CONFIG.md](CONFIG.md) for every setting.

## Troubleshooting

Built against BepInEx `6.0.0-be.788`.

Check `BepInEx\LogOutput.log` for `===== Cooking Hub 0.9.6 started`. A line starting with `could not` means part of the mod does not work on this game version.

## Feedback and bug reports

Found a bug, have an idea, or something feels off? Come say so on the Core Directive Discord: https://discord.gg/trTsyxUFqg . When reporting a bug, attach `BepInEx\LogOutput.log` and, if you turned on `LogToFile`, `BepInEx\cooking-hub.log`.

To reach me directly, message `exiled.dev` on Discord. You can find me in the [official Survival Log Discord](https://discord.gg/MZGg4gQAUe). I'm also on X as [@AnExiledDev](https://x.com/AnExiledDev).
