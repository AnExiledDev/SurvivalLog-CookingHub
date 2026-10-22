# Cooking Hub settings

Settings live in `BepInEx\config\com.dev.survivallog.cookinghub.cfg`, created the first time the game starts with the mod. Edit it while the game is closed, or change the settings in game on the Mod Manager's Mods screen. Delete the file to get the defaults back. Without Exiled's Toolkit installed, changes made on the Mods screen take effect at the next game start.

## General

| Setting | Default | What it does |
| --- | --- | --- |
| `PanelKey` | `K` | Opens and closes the Cooking Hub window. `None` turns the key off; the pot button in the tool column on the left of the screen still opens it. |
| `FillSpoilingFirst` | `true` | Filling a pot from the window uses the food closest to spoiling first. When `false`, it uses the freshest food first. Also in the window's Settings tab. |
| `ShowOdds` | `true` | The game's cooking screen shows the chance of each quality under every dish it forecasts, with the reasons and what would raise it. Also in the window's Settings tab. |
| `PutDishesAway` | `false` | A finished dish moves out of the stove into the home storage whose tags want it, the way Storage Hub puts items away; a storage already holding that dish is the fallback. A dish no storage takes stays in the stove. |

## Diagnostics

| Setting | Default | What it does |
| --- | --- | --- |
| `LogToFile` | `false` | Also writes what Cooking Hub does to `BepInEx\cooking-hub.log`, which is kept between game starts. Turn it on when reporting a problem. |
