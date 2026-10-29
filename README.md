# Cooking Hub

Know what you can cook and how good it will turn out before you light the stove. Cooking Hub shows the chance of each quality right on the game's cooking screen, with the reasons and one-press fixes, lists every dish the stove can make from your food, and fills a pot with food from your bag and home storages.

Open any stove, juicer, microwave, oven or coffee machine and its cooking screen gains tabs, a searchable recipe book with odds, a Load button that pulls food from storage, a cooking queue, Repeat last and a fuel top up.

## Features

- **A better cooking screen**: the game's own stove screen gets tabs. Recipe Book adds a search box, filters (All, Can load, New, Locked), where each dish's food is or what is missing, a bar of its odds and its Perfect chance. Cook now lists what the stove can make from all your food, soonest to spoil first. Both can be sorted by Satiety, Stamina, Morale, Fitness, Life or Quality. Cook now and Pantry cards, and the pot cards of known dishes, show what each dish or food restores, one icon per stat. Hovering a card shows the cooking screen's tooltip, Info Pack's details included. Pantry lists all your cooking food with how much you have and puts one of any food in the pot with a click.
- **Load from storage**: a recipe the game calls short because it only counts your bag gets a Load button when your storages cover it. Food already in the pot goes back where it came from first. Once your cooking level lets one pot cook several dishes, Load adds the dish beside what is there when it fits. Load uses the food closest to spoiling first, or the freshest first if you prefer (setting `FillSpoilingFirst`).
- **Cooking queue**: a Queue button beside Load, on Recipe Book rows and Cook now cards, adds the dish to that stove's queue (up to 20). Queue shows only on dishes your food covers after every dish already queued, on any stove, and that food is set aside: Load, Repeat last and other queues leave it alone, and a dish only set-aside food covers says "set aside for the queue". Once the stove's finished dishes are taken out and its pot is empty, the stove is filled for the first queued dish with its set-aside food and started, from your bag and storages, with its screen open or closed. Food that spoils or that you use by hand can still leave a dish short; it shows "short of food" and waits while the next one cooks. The Queued tab lists the queue in order, with Remove on each dish. The stove needs fuel in it, or power for an electric one. Queues last until you leave the game. Pairs well with Put dishes away.
- **Repeat last**: refills an empty stove with the dish it last started and starts it again.
- **Top up from storage**: fills the free fuel slots with the fuel the stove already burns, from your bag and storages. Put the first fuel in by hand so the mod knows which kind.
- **Put dishes away** (setting `PutDishesAway`, off by default): a finished dish moves out of the stove into the home storage whose tags want it, the way Storage Hub puts items away. A dish no tagged storage has room for stays in the stove. With Exiled's Toolkit, each dish put away shows a toast.
- **Not learned tab** (setting `ShowUnlearnedRecipes`, off by default): lists the recipes this stove can cook that your recipe book does not show yet, with the food you have for each, what is missing, and the cooking level a recipe shows up at. Load and Queue work on the ones your food covers. Cooking one teaches it. Cook now marks these dishes "not learned". With the setting off, the mod lists only the recipes the game shows.
- **Odds on the cooking screen**: under each dish the cooking screen forecasts, the chance of a Failed, Average, Good and Perfect dish, and why: your cooking level's roll, fresh or spoiled food, seasonings, an exact recipe, the stove. It also shows the worst result a roll can give.
- **What would help**: buttons under the forecast to add a seasoning you have, or to swap a spoiled ingredient in the pot for a fresh one, each with how much it adds to the Perfect chance.

Cooking Hub adds nothing to your save and does not edit game files. You can remove it at any time.

Cooking Hub covers what ProjectCook's dish preview does. Use one or the other, not both.

## Requirements

- Survival Log 1.1.19611. Built for this version; a game update can break it until the mod is updated.
- The [BepInEx Pack for Survival Log](https://www.nexusmods.com/survivallog/mods/12), the BepInEx 6 (IL2CPP) build for the game.
- Optional: [Stacking](https://www.nexusmods.com/survivallog/mods/74). Cooking Hub loads food Stacking made stackable one unit at a time, the way Stacking wants it. Without Stacking, nothing changes.
- Optional: [Exiled's Toolkit](https://github.com/AnExiledDev/SurvivalLog-ExiledToolkit) 0.4.0 or newer. With it, settings changed on the Mod Manager's Mods screen take effect without restarting the game, and put-away dishes show as toasts. Without it, they take effect at the next game start. With 0.10.0 or newer, its UI size setting also sizes the pop-up messages, and with 0.11.0 or newer, Cooking Hub's own `UiScale` under Interface sets their size for this mod alone (0, the default, follows the Toolkit's).

## Install

1. Install the [BepInEx Pack for Survival Log](https://www.nexusmods.com/survivallog/mods/12) (if no other mods were installed before, start the game once so BepInEx finishes setup, then quit).
2. If ProjectCook is installed, delete its DLL from the `BepInEx\plugins` folder.
3. Optional, to change settings without a restart: install Exiled's Toolkit. Download `ExiledToolkit-x.y.z.zip` from [its latest release](https://github.com/AnExiledDev/SurvivalLog-ExiledToolkit/releases/latest) or from [Nexus Mods](https://www.nexusmods.com/survivallog/mods/61), and extract it into the game folder (the folder with `SurvivalLog.exe`).
4. Download `CookingHub-x.y.z.zip` from the [latest release](https://github.com/AnExiledDev/SurvivalLog-CookingHub/releases/latest) or from [Nexus Mods](https://www.nexusmods.com/survivallog/mods/95).
5. Extract the zip into the game folder. It holds a `BepInEx` folder, so the DLL lands in the right place. When asked, merge the folders. The result should be:
   `C:\Program Files (x86)\Steam\steamapps\common\Survival Log\BepInEx\plugins\CookingHub.dll`

## Uninstall

Delete `CookingHub.dll` from the `BepInEx\plugins` folder. For a clean slate, also delete `BepInEx\config\com.dev.survivallog.cookinghub.cfg` and, if it exists, `BepInEx\cooking-hub.log`.

## Configuration

Settings live in `BepInEx\config\com.dev.survivallog.cookinghub.cfg`, created the first time the game starts with the mod. See [CONFIG.md](CONFIG.md) for every setting.

## Troubleshooting

Built against BepInEx `6.0.0-be.788`.

Check `BepInEx\LogOutput.log` for `===== Cooking Hub 0.6.9 started`, followed by `listening for panel messages`. A line starting with `could not` means part of the mod does not work on this game version.

## Feedback and bug reports

Found a bug, have an idea, or something feels off? Come say so on the Core Directive Discord: https://discord.gg/trTsyxUFqg . When reporting a bug, attach `BepInEx\LogOutput.log` and, if you turned on `LogToFile`, `BepInEx\cooking-hub.log`.

To reach me directly, message `exiled.dev` on Discord; you can find me in the [official Survival Log Discord](https://discord.gg/MZGg4gQAUe). I'm also on X as [@AnExiledDev](https://x.com/AnExiledDev).
