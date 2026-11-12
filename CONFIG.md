# Cooking Hub settings

Settings live in `BepInEx\config\com.dev.survivallog.cookinghub.cfg`, created the first time the game starts with the mod. Edit it while the game is closed, or change the settings in game on the Mod Manager's Mods screen. Delete the file to get the defaults back.

## General

| Setting | Default | What it does |
| --- | --- | --- |
| `FillSpoilingFirst` | `true` | Load and Repeat last fill the pot with the food closest to spoiling first. When `false`, they use the freshest food first. |
| `ShowOdds` | `true` | The cooking screen shows the chance of each quality under every dish it says the pot will make, with the reasons and what would raise it. |
| `ShowUnlearnedRecipes` | `false` | The cooking screen gets a Not learned tab listing the recipes this stove can cook that the recipe book does not show yet, with the food you have for each. When `false`, only the recipes the game shows are listed. |
| `PutDishesAway` | `false` | A finished dish moves out of the stove into the home storage whose tags want it, the way Storage Hub puts items away. If no tagged storage takes it, it goes to a storage that already holds that dish. If no storage has room, the dish stays in the stove. |

## Interface

| Setting | Default | What it does |
| --- | --- | --- |
| `UiScale` | `0` | The size of Cooking Hub's tabs and recipe book on the cooking screen, in percent, from 75 to 200. At `0` they use the UI size set in Exiled's Toolkit (its `UiScale`). If the cooking screen wouldn't fit in the window at this size, the book shrinks until it does. |

## Diagnostics

| Setting | Default | What it does |
| --- | --- | --- |
| `LogToFile` | `false` | Also writes what Cooking Hub does to `BepInEx\cooking-hub.log`, which is kept between game starts. Turn it on when reporting a problem. |
