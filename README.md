# Cooking Hub

Know what you can cook and how good it will turn out before you light the stove. Cooking Hub shows the chance of each quality right on the game's cooking screen, with the reasons and one-press fixes, lists every dish your food can make, and fills a pot with food from your bag and home storages.

Press `K`, or the pot button in the tool column on the left of the screen, to open the window. The button shows how much of your food spoils within a day.

## Features

- **Odds on the cooking screen**: under each dish the cooking screen forecasts, the chance of a Failed, Average, Good and Perfect dish, and why: your cooking level's roll, fresh or spoiled food, seasonings, an exact recipe, the stove. It also shows the worst result a roll can give.
- **What would help**: buttons under the forecast to add a seasoning you have, or to swap a spoiled ingredient in the pot for a fresh one, each with how much it adds to the Perfect chance.
- **What can I cook**: every recipe you can see at your cooking level, checked against all the fresh food in your bag and home storages. Filter by ready, missing one, missing more or locked, and search by name. Dishes that use up food spoiling soon are marked and listed first, as are dishes you have never cooked.
- **Fill pot**: with a stove's cooking screen open, one press puts everything a dish needs into the pot, taken from your bag and your storages. It uses the food closest to spoiling first, or the freshest first if you prefer.
- **Pantry**: all your cooking food in one list, with how much you have, how much has spoiled, how long until it spoils and where it is kept.

Cooking Hub adds nothing to your save and does not edit game files. You can remove it at any time.

Cooking Hub covers what ProjectCook's dish preview does. Use one or the other, not both.

## Requirements

- Survival Log 1.1.19131. Built for this version; a game update can break it until the mod is updated.
- The [BepInEx Pack for Survival Log](https://www.nexusmods.com/survivallog/mods/12), the BepInEx 6 (IL2CPP) build for the game.
- Optional: [Exiled's Toolkit](https://github.com/AnExiledDev/SurvivalLog-ExiledToolkit) 0.4.0 or newer. With it, settings changed on the Mod Manager's Mods screen take effect without restarting the game. Without it, they take effect at the next game start.

## Install

1. Install the [BepInEx Pack for Survival Log](https://www.nexusmods.com/survivallog/mods/12) (if no other mods were installed before, start the game once so BepInEx finishes setup, then quit).
2. If ProjectCook is installed, delete its DLL from the `BepInEx\plugins` folder.
3. Optional, to change settings without a restart: install Exiled's Toolkit. Download `ExiledToolkit-x.y.z.zip` from [its latest release](https://github.com/AnExiledDev/SurvivalLog-ExiledToolkit/releases/latest) or from [Nexus Mods](https://www.nexusmods.com/survivallog/mods/61), and extract it into the game folder (the folder with `SurvivalLog.exe`).
4. Download `CookingHub-x.y.z.zip` from the [latest release](https://github.com/AnExiledDev/SurvivalLog-CookingHub/releases/latest).
5. Extract the zip into the game folder. It holds a `BepInEx` folder, so the DLL lands in the right place. When asked, merge the folders. The result should be:
   `C:\Program Files (x86)\Steam\steamapps\common\Survival Log\BepInEx\plugins\CookingHub.dll`

## Uninstall

Delete `CookingHub.dll` from the `BepInEx\plugins` folder. For a clean slate, also delete `BepInEx\config\com.dev.survivallog.cookinghub.cfg` and, if it exists, `BepInEx\cooking-hub.log`.

## Configuration

Settings live in `BepInEx\config\com.dev.survivallog.cookinghub.cfg`, created the first time the game starts with the mod. Which food a fill uses first and whether the odds show can also be set in the window. See [CONFIG.md](CONFIG.md) for every setting.

## Troubleshooting

Built against BepInEx `6.0.0-be.788`.

Check `BepInEx\LogOutput.log` for `===== Cooking Hub 0.1.0 started`, followed by `listening for panel messages`. A line starting with `could not` means part of the mod does not work on this game version.

## Known issues

None known.

## Feedback and bug reports

Found a bug, have an idea, or something feels off? Come say so on the Core Directive Discord: https://discord.gg/trTsyxUFqg . When reporting a bug, attach `BepInEx\LogOutput.log` and, if you turned on `LogToFile`, `BepInEx\cooking-hub.log`.
