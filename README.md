# Klitch's Freefrag

Klitch's Freefrag is a fork of [OpenPrefirePrac](https://github.com/lengran/OpenPrefirePrac) by Lengran: an open-source CounterStrikeSharp powered server-side practicing plugin for CS2. It provides multiple prefire practices on competitive map pool maps and support multiplayer practicing concurrently.

## Get started

### Requirement

- CounterStrikeSharp

### Installation

Download [the latest release files](https://github.com/largepelotas/Freefrag/releases) and extract all files into "**game/csgo/addons/counterstrikesharp/plugins/Freefrag/**".

To install the latest version of CounterStrikeSharp, please refer to this [guide](https://docs.cssharp.dev/docs/guides/getting-started.html).

## How to use

### Tips on operating a practice server

When starting a server, I recommend using these parameters.

```bash
[CS2 Installation Directory]/game/bin/linuxsteamrt64/cs2 -dedicated -insecure +map de_inferno -maxplayers_override 64 +game_alias competitive +sv_hibernate_when_empty 0
```

Note: "**-maxplayers_override 64**" is the most important one. It allows the server to add more than 5 bots on one team, which is crucial to achieve the goal of allowing multiplayer training simultaneously.

### Start prefire practice in game

Send "**!freefrag**" in chatbox or use command "**css_freefrag**" in console. This will bring up the main menu.

There are also some shortcut commands you can use.

  - !freefrag prac [number]: Start practicing on a selected route.
  - !freefrag modes [name]: List the server's game modes in the menu, or switch straight to one (e.g. !freefrag modes prefire). Switching unloads Freefrag.
  - !freefrag map [map name]: Switch to another map.
  - !freefrag df [1-6]: Set the difficulty.
  - !freefrag mode [rand/full]: Set training mode.
  - !freefrag bw [rand/ump/ak/sct/awp/pistol/eco/force]: Set weapons for bots.
  - !freefrag gun [ak/m4/m4a1s/awp/deagle/usp/glock/none]: Set your weapon.
  - !freefrag jitter [0-64]: Set bot spawn offset.
  - !freefrag repeat [n/inf/off]: Repeat the current route.
  - !freefrag autoskip [n/off]: Skip route after deaths.
  - !freefrag hs [on/off]: Headshot-only damage.
  - !freefrag xray [on/off]: Outline bots through walls.
  - !freefrag botsfire [on/off]: Bots shoot back (on, default) or stand unarmed (off).
  - !freefrag bothealth [1-1000]: Set bot health (default 100).
  - !freefrag botarmor [0-100]: Set bot armor (default 100; 0 also removes the helmet).
  - !freefrag countdown [0-5]: Seconds you are held still at the start spot before a route begins (default 3, 0 = off).
  - !freefrag comp [on/off]: Comp mode. Pins bots shoot back, 100 health, 100 armor, 3 s countdown, no repeat and no spawn offset (server-configurable) and locks those settings until you turn comp off, so times are comparable. Comp runs are tagged [comp] on the leaderboard.
  - !freefrag top [1-N] [comp]: Show the top 5 runs for a route; add comp for comp runs only.
  - !freefrag lang [en/pt/zh]: Set language. en for English, pt para português, 中文选择 zh。
  - !freefrag exit: Stop practicing.

You can always use **!freefrag help** to see how to use them.

### Adjust default settings

Now the plugin supports loading default settings of difficulty and training mode from a json file. You can rename *default_cfg.json.example* to *default_cfg.json* and modify the value as you like.

Explanation of values:

- Difficulty
  - 0: No healing.
  - 1: Init hp 500 with no healing.
  - 2: +25hp for each kill.
  - 3: Reheal to 100hp after a kill.
  - 4: +100hp for each kill.
  - 5: +500hp for each kill.
- Training Mode
  - 0: Random mode, randomly spawn some targets.
  - 1: Full mode, all targets.
- Bot Weapon
  - 0: Each bot gets a random preset from the list below (rolled per bot, not per round).
  - 1: Bots use UMP45.
  - 2: Bots use AK47.
  - 3: Bots use Scout.
  - 4: Bots use AWP.
  - 5: Bots use Pistols.
  - 6: Bots use Eco weapons.
  - 7: Bots use Force-buy weapons.
- Aim lock for bots
  - 0: CS2's native bot behavior. It works in a consistant manner but is less powerful.
  - 1: face and track the owning player every tick (plugin aim lock).
  - 2: same tracking plus the hard_mode behaviour tree for firing.
- EquipPlayer:
  - 0: Disabled. To avoid interfering with other weapon related plugins
  - 1: Enabled (default). Players will be equipped with AK47, deagle and nades every time they respawn.
- HeadshotOnly:
  - 0: Off (default).
  - 1: Body shots do no damage to bots.
- Xray:
  - 0: Off (default).
  - 1: Bots are outlined through walls, visible only to you.
- BotsFire:
  - 1: Bots shoot back (default).
  - 0: Bots spawn unarmed.
- BotHealth:
  - 1-1000: Health of spawned bots (default 100).
- BotArmor:
  - 0-100: Armor of spawned bots (default 100). 0 also removes the helmet.
- Countdown:
  - 0-5: Seconds the player is held still before a route begins (default 3). 0 = no countdown.
- SwingTrigger:
  - 100-3000: Distance (units) at which a swinging bot is released when its route line sets no trigger (default 600).
- Comp: the values `!freefrag comp on` applies, as an object with the keys BotsFire (0/1), BotHealth, BotArmor, Countdown, Repeat (0 = off, -1 = infinite, n) and Jitter. Out-of-range values are ignored. Defaults: BotsFire 1, BotHealth 100, BotArmor 100, Countdown 3, Repeat 0, Jitter 0.
- MenuType:
  - 0: Chat menus (type !1-!9 to choose).
  - 1: Screen menus (default), drawn in the centre of the screen. W/S move, D selects, A goes back, Tab closes. Arrow keys, Enter and Backspace work after a one-time bind; type `!freefrag binds` in chat for the line to paste into the console.
- MenuRows:
  - 1-8: Height of a screen menu in rows (default 7). A menu longer than this scrolls, and two of the rows are then used for the "..." scroll markers above and below the entries (a 7-row setting shows 5 entries at a time).
- GameModesConfig:
  - Path to GameModeManager.json, which the Game modes menu reads (default empty: found automatically in configs/plugins/GameModeManager, up to four folders above the plugin).
- GameModesPermission:
  - A CounterStrikeSharp permission flag such as `@css/changemap` that a player needs to switch game modes (default empty: anyone may).

For detailed discussion on bot difficulty, please refer to [this issue](https://github.com/lengran/OpenPrefirePrac/issues/17).

## Development

### Provide translation

First you can make a copy of an existing translation profile from the *lang* folder and start translate the sentences into your language.

Then you might want to create a pull request. Because the plugin uses player's IP to decide the language shown to each player, a mapping from country to language needs to be hard coded into the translator module.

### How to customize a practice profile?

The folder "*maps*" is organized as follows: Each sub-folder in "*maps*" contains practice profiles for the corresponding map. Each text file in that sub-folder is a practice profile.

A practice profile consists of five parts:

The first line contains **the file names of practice profiles** that might interfere with this practice, separated by spaces.

The second line contains **2 numbers**, indicating how many bots are needed in this practice and a spawn ratio between 0 and 1 (how many of them should be spawned in random practice mode) respectively.

The third line instructs the place and facing direction of the player. The first 3 floating numbers are the position and the other 3 are the rotation. This line should only contain **6 numbers**.

```text
pos_x pos_y pos_z ang_x ang_y ang_z
```

The fourth part with an arbitrary number of lines describes spawn positions of bots. Bot will be spawn in the same order as that of the lines. 

There should be **at least 6 numbers and a True or False value** in each line. The first 3 numbers describe position, following 3 numbers indicating the rotation angle. The 7th value is either a *True* or a *False* indicating whether the bot is crouching. You can have comments at the end of the line if needed (optional). 

The positions and rotations can be retrieved from in-game get\_pos command. But please notice that, the height values used in profiles should be the values returned by get\_pos minus 64. I made a python script that does this calculation for you. The usage is explained in [next section](https://github.com/lengran/OpenPrefirePrac?tab=readme-ov-file#how-to-use-the-python-helper-script-to-convert-heights).

```text
pos_x pos_y pos_z ang_x ang_y ang_z is_crouching
```

The fifth part with an arbitrary number of lines describes joint points of a guiding line. The guiding line is used to provide a better narration of how the practice is designed to be played. This also requires a bit calculation on height of joints and it can also be handled by the python script.

Each line contains **3 numbers**.

```text
pos_x pos_y pos_z
```

### How to use the python helper script to convert heights

The script *calculate_height.py* receives output of the *getpos* in-game function and automatically convert them to the proper format of bot spawn positions and joint positions of a guiding line. The steps of how it is used is described as follows.

Step 1: Open the game console and use the *getpos* command while you are standing not crouching.

Step 2: Copy the output to a txt file. 

    For bot spawn points, copy the entire line of output. If you want the bot to crouch, append a *True* at the end of the line. You can also put a *False* if you want the bot to stand. But it's not required since the python script can do that for you. If you want to give a comment to make the practice profile more reader-friendly, feel free to attach your words after the *True* or *False*. 
  
    For joints of a guiding line, only the position part (starting from *"getpos"* to the semicolon in the middle of the line) is needed. Comments are not supported for joints.

Step 3: After finish stacking the lines, you can simply pass the txt file to the python script and the script will automatically print out the formatted lines. Just copy the output and paste them at the end of your customized practice profile.

```bash
python3 calculate_height.py [PATH TO YOUR FILE]
```

Here's an example of the file you put in the text file and the output of the script.

Your text file should look like this:

```text
setpos 1.11111 1.222222 64.333333;setang 1.444444 1.555555 0.000000
setpos 2.11111 2.222222 64.333333;setang 2.444444 2.555555 0.000000 False
setpos 3.11111 3.222222 64.333333;setang 3.444444 3.555555 0.000000 True
setpos 4.11111 4.222222 64.333333;setang 4.444444 4.555555 0.000000 Here's some info about this position.
setpos 5.11111 5.222222 64.333333;setang 5.444444 5.555555 0.000000 False Here's some info about this position.
setpos 6.11111 6.222222 64.333333;setang 6.444444 6.555555 0.000000 True Here's some info about this position.
setpos 1.11111 1.222222 64.333333
setpos 2.11111 2.222222 64.333333
setpos 3.11111 3.222222 64.333333
```

The corresponding output would be:

```text
1.11111 1.222222 0.3333329999999961 1.444444 1.555555 0.000000 False
2.11111 2.222222 0.3333329999999961 2.444444 2.555555 0.000000 False
3.11111 3.222222 0.3333329999999961 3.444444 3.555555 0.000000 True
4.11111 4.222222 0.3333329999999961 4.444444 4.555555 0.000000 False # Here's some info about this position.
5.11111 5.222222 0.3333329999999961 5.444444 5.555555 0.000000 False # Here's some info about this position.
6.11111 6.222222 0.3333329999999961 6.444444 6.555555 0.000000 True # Here's some info about this position.
1.11111 1.222222 9.333332999999996
2.11111 2.222222 9.333332999999996
3.11111 3.222222 9.333332999999996
```

### Current development progress

Currently it's still under active developing.

Finished practices:

- de_inferno
  - A short to A site
  - A long to A site
  - A apartments to A site
  - Banana to B site
  - Retake B from CT spawn
- de_ancient
  - B ramp to B site
  - B house to B site
  - Mid to A site
  - A main to A site
  - Retake A from CT spawn
- de_mirage
  - Attack A site from A ramp (to CT spawn)
  - Attack B site from B apartments
  - Attack A site from A palace (to jungle)
  - Attack B site from mid
  - Attack A site from underpass
  - Retake B site from CT spawn
  - CT aggressively push A Palace
- de_overpass
  - Attack B site from B long
  - Attack B site from B short
  - Clear underpass and go upwards to mid
  - Clear underpass and go towards B short
  - Attack A site from A long
  - Attack A site from A short (mid)
  - Retake B site from CT spawn
- de_anubis
  - Attack B site from B main
  - Attack B site from mid (B palace)
  - Attack B site from water (B connector/E-Box)
  - Attack A site from mid (A connector)
  - Attack A site from A main
  - CT aggressively pushing from A main
  - CT aggressively pushing from B main
- de_dust2
  - Attack A site from A long
  - Attack A site from A short
  - Attack B site from tunnel
  - Attack B site from mid
  - CT aggressively push from lower tunnel
  - CT aggressively push top mid
- de_nuke
  - Attack A site from hut
  - Attack B site from ramp
  - Entrance of lobby (T side)
  - From radio to ramp
  - Attack A site from Ramp/J-Hall
  - From T-side outside to secret
  - Attack B site from secret
  - Fast pace rush MINI from Silo
  - Attack A site from MINI
- de_vertigo
  - Attack B site from stairs
  - From mid to CT spawn
  - Attack A site from A ramp
  - Attack A site from scaffold
  - Retake B site from elevator
- de_train
  - Attack A site from A main / T mid
  - Attack A site from ivy
  - Attack A site from ladder room
  - Retake B site from CT spawn

TODO:

1. Create prefire profiles for all maps.
2. Improve behavior tree to allow each bot only aimlocks its owner.
3. Improve localization support (The supporting framework is done. Submitting translations is warmly welcomed.).
4. Reorganize the files and code structure. Try to put code into submodules to improve readability.
5. Reroute separate logs into one gathered place for better debug experience.

### About the future of this project

Since there are already a lot of awesome prefire maps in the workshop, I might not spend as much energy on this project as I used to do. But if you find any bugs or want some cool features, feel free to leave a comment. Thanks for the support and love of this project for such a long time. <3

## Reference

I have referred these open-source projects during the development.

- [shobhit-pathak/MatchZy: MatchZy is a plugin for CS2 (Counter Strike 2) for running and managing practice/pugs/scrims/matches with easy configuration and Get5 (G5API/G5V) support as well!](https://github.com/shobhit-pathak/MatchZy)
- [B3none/cs2-retakes: CS2 implementation of retakes. Based on the version for CS:GO by Splewis.](https://github.com/B3none/cs2-retakes)
- [aprox2/GeoLocationLanguageManagerPlugin: Language manager plugin for CSSharp that uses users ip for geo location.](https://github.com/aprox2/GeoLocationLanguageManagerPlugin)
- [daffyyyy/CS2-SimpleAdmin: Manage your Counter-Strike 2 server by simple commands :)](https://github.com/daffyyyy/CS2-SimpleAdmin)
- [5e-prac-mirage-prefire](https://steamcommunity.com/sharedfiles/filedetails/?id=3232466864)

This project is inspired by a close-source prefire plugin developed by https://space.bilibili.com/283758782.

Huge thanks.

## Recording guiding lines in game (this fork)

Some routes ship without a guiding line (fewer than two joints). Instead of collecting
`getpos` output by hand, record it while playing:

1. Start the route: `!freefrag prac N`.
2. `!freefrag record` and walk the route the way it is meant to be played. You cannot take
   damage while recording, so the bots will not interrupt. A green line is drawn behind you
   as you go (one joint every 50 units). `!freefrag record` again pauses.
3. `!freefrag record save` writes the joints into `maps/<map>/<route>.txt`, replacing any
   existing guiding line, and redraws the blue line immediately. `!freefrag record clear`
   discards. Leaving the route also discards an unsaved recording.

On a kus/cs2-modded-server install the plugin folder is overwritten on restart, so run
`scripts/pull-routes.sh` afterwards to copy the changed profiles into `custom_files` and
into this repo.

## Placing bots and creating routes in game (this fork)

The same applies to bot positions: stand where a bot should be and place it, with no
`getpos` and no height conversion.

1. `!freefrag newroute <name> [bots]` creates an empty route that starts where you stand
   (`bots` is how many bots are in play at once, 1-10, default 3). Names use letters, digits, `-`
   and `_`. The reply gives the route number N. Skip this step to add bots to an existing
   route.
2. Start the route: `!freefrag prac N`.
3. Stand where a bot should be, look where it should face, and type `!freefrag addbot`.
   It crouches if you are crouching; `addbot crouch` or `addbot stand` overrides that. A green
   post marks the spot, with a short stub pointing the way the bot will look.
   `!freefrag addbot undo` removes the last one and `!freefrag addbot clear` discards all.
4. `!freefrag addbot save` appends the bots to `maps/<map>/<route>.txt`, after the existing
   targets and ahead of the guiding line. Restart the route to spawn them. Leaving the route
   discards unsaved bots.

Run `scripts/pull-routes.sh` afterwards, as above.

## Swinging bots (this fork)

A bot can hide at its spot and run to a second point, the peek point, once you get close. It
runs along the nav mesh and fights from there. A target line gets an optional `peek` spec after
the crouch flag:

```text
pos_x pos_y pos_z ang_x ang_y ang_z is_crouching peek px py pz [trigger d] [# comment]
```

For example `646.37 1180.62 0.84 -0.05 -90.55 0.0 False peek 700.0 1500.0 0.8 trigger 450`.
The line's own position is the hiding spot; `peek` is where the bot runs to (feet coordinates);
`trigger` (optional, 100-3000) is the distance from you to the hiding spot that releases it, and
defaults to `SwingTrigger` from the config. A line without `peek` is a static bot, as before.
A `peek` with fewer than three numbers makes the line a static bot and prints one warning in the
server console.

In game, from a started route:

- `!freefrag addbot swing` places a swinger at your feet, facing your view: its hiding spot.
- `!freefrag addbot peek` sets the peek point of the last draft swinger to where you stand. A
  lime line shows the run from the hiding spot to the peek point.
- `undo`, `clear` and `save` work as for other draft bots; `save` refuses while a draft swinger
  has no peek point.

A released bot runs toward you and is frozen at its peek point, so put the peek point on the
path the bot takes out of cover (the corner's exit), reachable within 2 seconds.

Bots only move on the nav mesh. A bot that never moves after being released is reported in the
server console as `Route <name> target <n> ... never moved`.

Limits of this first version: a swinger does not return to cover, and there is no choice of
swing direction.

## Building (this fork)

CounterStrikeSharp 1.0.3xx targets .NET 10. `scripts/build-on-server.sh` runs tests and
`dotnet publish` inside the official SDK container on the game server, and
`scripts/deploy.sh` copies the plugin files into the install and hot-reloads it.

On a kus/cs2-modded-server install the plugin lives in `plugins/Freefrag` and loads once at
server start, so it stays loaded in every game mode and is dormant outside Prefire: practice
commands answer with a pointer to Prefire, and the main menu shrinks to the game modes list
and a "Switch to Prefire mode" shortcut. The mode cfgs tell it which mode is running through
the console command `css_freefrag_mode <mode|none>`: `deploy.sh` replaces the plugin load line
in `prefire.cfg` with `css_freefrag_mode Prefire` and the unload line in `unload_plugins.cfg`
with `css_freefrag_mode none`. It also reads the current mode from GameModeManager's API at
load and at every map start, so a hot reload during Prefire wakes it up without a cfg.

`!modes` / `/modes` opens Freefrag's own game modes menu (in every mode, active or dormant; switching still follows
`GameModesPermission`). `deploy.sh` sets `Commands.Modes` to `false` in GameModeManager's config so its own `css_modes`
does not run alongside it; `css_mode <name>` stays on. The change applies at the next container restart. It also copies the bundled behavior trees to `addons/scripts/ai/bt/` (where `prefire_settings.cfg` loads `hard_mode.kv3` from), so the old `custom_files/addons/counterstrikesharp/plugins/OpenPrefirePrac` mirror is no longer needed.

### Re-syncing the bot behavior tree after a CS2 update

`resources/bt/hard_mode.kv3` is Freefrag's bot behavior tree; its `config` node points at
`resources/bt/bt_hard_config.kv3`, which must match the engine's own
`addons/scripts/ai/bt/bt_config.kv3` key for key (same keys, same `attack` table shape). When
CS2 changes that format the tree is rejected on every bot spawn and bots silently fall back
to the stock profile. The symptom is this in `docker logs cs2`:

```
[AI BT]: Config node 'attack' must contain 13 entries. ... Unable to load behavior tree '...hard_mode.kv3'
```

To re-sync:

1. Copy the engine's reference files off the server (read-only):
   `scp quinn@192.168.4.20:'~/cs2/install/game/csgo/addons/scripts/ai/bt/bt_{config,very_hard_config}.kv3' /tmp/`
2. Diff the `default` node of `bt_config.kv3` against `resources/bt/bt_hard_config.kv3`: add any
   new keys with the stock value, drop removed ones, and give the `attack` table the same number
   of rows (the row order follows the weapon-type enum; the comments name them). Keep Freefrag's
   own values for the keys that still exist, since they are what makes the bots hard.
3. Check `hard_mode.kv3` still only uses node types the engine knows
   (`strings libserver.so | grep -E '^(decorator_|action_|condition_)'` on the server) and that
   the `scripts/ai/modules/*.kv3` subtrees it references still exist in `pak01_dir.vpk`.
4. Update the sync date in the comment at the top of `bt_hard_config.kv3`, build, deploy, and
   confirm that `docker logs --since 10m cs2 | grep '\[AI BT\]'` prints nothing after a bot spawns.
