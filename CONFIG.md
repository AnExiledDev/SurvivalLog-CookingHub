# Cooking Hub settings

Settings live in `BepInEx\config\com.dev.survivallog.cookinghub.cfg`, created the first time the game starts with the mod. Edit it while the game is closed, or change the settings in game on the Mod Manager's Mods screen. Delete the file to get the defaults back. Without Exiled's Toolkit installed, changes made on the Mods screen take effect at the next game start.

## General

| Setting | Default | What it does |
| --- | --- | --- |
| `FillSpoilingFirst` | `true` | Load and Repeat last fill the pot with the food closest to spoiling first. When `false`, they use the freshest food first. |
| `ShowOdds` | `true` | The game's cooking screen shows the chance of each quality under every dish it forecasts, with the reasons and what would raise it. |
| `ShowUnlearnedRecipes` | `false` | The cooking screen gets a Not learned tab listing the recipes this stove can cook that the recipe book does not show yet, with the food you have for each. When `false`, only the recipes the game shows are listed. |
| `PutDishesAway` | `false` | A finished dish moves out of the stove into the home storage whose tags want it, the way Storage Hub puts items away; a storage already holding that dish is the fallback. A dish no storage takes stays in the stove. |

## Interface

| Setting | Default | What it does |
| --- | --- | --- |
| `UiScale` | `0` | How big Cooking Hub's pop-up messages are drawn, in percent, from 75 to 200. At `0` they follow the UI size set in Exiled's Toolkit (its `UiScale`). Needs Exiled's Toolkit 0.11.0 or newer. |

## Diagnostics

| Setting | Default | What it does |
| --- | --- | --- |
| `LogToFile` | `false` | Also writes what Cooking Hub does to `BepInEx\cooking-hub.log`, which is kept between game starts. Turn it on when reporting a problem. |
