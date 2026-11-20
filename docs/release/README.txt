Champions of the Sons
=====================
An RPG overhaul for Sons of the Forest: enemy levels and elite affixes, loot with eight rarities and
rolled stats, sets and unique items, a perk tree, 27 spells, crafting, a kill-streak XP bonus and eleven
difficulty tiers from Easy to Hell. Works alone and in co-op (host, client and dedicated server).

Requirements
- Sons of the Forest (Steam, current version)
- RedLoader 0.8.6 (https://github.com/ToniMacaroni/RedLoader)

Install
- With RedManager: install "Champions of the Sons" from SOTF Mods.
- By hand: install RedLoader, start the game once, then extract this zip into the game folder
  (the one with SonsOfTheForest.exe). You should end up with Mods\ChampionsOfTheSons.dll and the
  folder Mods\ChampionsOfTheSons\.

Multiplayer
- Every player and the dedicated server need the mod, all on the same version. The game refuses
  to mix versions and says so.
- The host (or server) decides difficulty, enemies and drops. Each player keeps their own character.
- Dedicated server: put the same files into the server's Mods folder.

Keys (change them in the RedLoader mod settings, file ChampionsOfTheSons.cfg)
  I  inventory and character      P  perk tree        K  crafting
  O  host settings (difficulty)   H  ping              Z X V B N M  spell slots 1-6
  Gamepad: LB + D-pad opens the panels.

Admin and cheat commands (host or server admin only)
- Single player and co-op host: open the game's debug console and type "cots help". Co-op clients
  cannot use them. Dedicated server: players with the Admin role type "/cots ..." in chat.
- Switch them off in ChampionsOfTheSonsAdmin.cfg (commands_enabled).
  cots help                          the list below
  cots players                       player ids for [player] (0 = host, all = everyone)
  cots xp <amount> [player]          add XP
  cots level <n> [player]            set the level        cots addlevels <n> [player]
  cots points <n> [player]           bonus perk points    cots respec [player]
  cots give <item id|random[:rarity]> [level] [player]   roll an item into the bag
  cots heal [player]                 full health and energy
  cots god [on|off] [player]         no damage taken      cots nocooldowns [on|off] [player]
  cots spawn <normal|elite|miniboss|boss> [actor type] [count]
  cots difficulty <id|0-10>          cots list <items|perks|spells|stats|enemies|difficulties> [filter]

Saves
- Your level, items and perks are saved with the game's own save (through RedLoader). Each player's
  character is kept in their own save in multiplayer.

Uninstall
- Delete Mods\ChampionsOfTheSons.dll and the Mods\ChampionsOfTheSons folder.

Credits: see CREDITS.md and CREDITS-icons.md. The mechanics follow the original Champions of the
Forest mod for The Forest; the code, names and text here are new.
