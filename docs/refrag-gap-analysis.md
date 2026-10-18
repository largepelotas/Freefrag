# Refrag vs. the OptiPlex CS2 server

Snapshot taken 2026-10-18 from refrag.gg, wiki.refrag.gg, the kus/cs2-modded-server README
and the live install at 192.168.4.20 (`~/cs2`). Prices are Refrag's annual-billing rates.

## 1. What Refrag sells

| Tier | Price | Adds |
|---|---|---|
| Player | $5.40/mo ($7 monthly) | Coach, all in-game modes, Creator Studio, Bootcamp, Routines, on-demand server |
| Competitor | $11.50/mo ($15 monthly) | + Scrim, Restrat, Utility Hub, Detailed Aim Stats, 2D Demo Viewer, Academy |
| Team | $60/mo ($79 monthly) | + 7 seats, one server per player |

Every mode runs on a Refrag-hosted 128-tick server in 35 regions; you switch mode and map
with `.mode map` chat commands without reconnecting. Most arena modes share one engine
(Creator Studio) with three run modes: `.prac` (tweak settings), `.comp` (creator defaults,
leaderboard), `.smart` (per-mode Elo, bots scale reaction/accuracy as you improve).

### In-game modes (22 on the wiki)

| Group | Mode | What it is |
|---|---|---|
| Static-bot aim | **Prefire** | Clear a set path of bots in common spots; randomised placement; `.xray`, `.hs`, weapon presets (pistol/save/eco/force/default/AWP), `.endless`, `.autoskip`, Elo |
| | **Angle Trainer** | Prefire without knowing which spots are filled |
| | **Aimbotz** | Static bots that don't shoot, time-challenge in `.comp`, 1–6 spawns, jiggle toggle |
| | **Pop** | Gridshot/microflick spheres, pure aim trainer |
| Swinging-bot aim | **Quickfire** | Bots peek you with realistic movement; solo/duo; `.reaction`, `.repeekreaction`, `.aimoffset`, `.swingers`, `.repeat` |
| | **Blitz** | Five bots swing in quick succession |
| | **Xfire** | Prefire path plus some bots that peek you |
| | **Clutch** | Multiple fights from one tight spot |
| | **Repeek** | One randomised target; you peek it |
| | **Surge** / **Waves** | Bots rush you (Waves = knife rushers, endless respawn, 5 difficulties) |
| | **Rapid by Wooting** | Checkpoint run with swingers and holders |
| | **Spray Transfer** | Three bots in two spots; arena resets if you stop spraying |
| AWP | **AWP Hold**, **AWP Flick**, **Quickscope** | Same swinging-bot engine, AWP-specific arenas |
| Site play | **Defender** | Hold a site vs bots that execute with utility, varied swings |
| | **VPL** | Hold a site vs a recorded full execute (bots replay recorded POVs; playlists shareable) |
| | **Challenger** | Retake vs bots, solo or with friends |
| Multiplayer | **Retakes**, **Executes**, **Duels** (1v1), **Deathmatch**, **Scrim** (5v5 with demos) |
| Utility | **NADR** | Save/load/rethrow nades, map-placed lineups with ghost replay, grenade *finder* (search a lineup that lands in a drawn box), strategy recorder, timer, `.observeme` |
| Mechanics | **Recoil Trainer** | Live spray-pattern overlay, distance-aware, `.slowmo`, `.nospread` |
| | **Momentum**, **Bunnyhop** | Skill jumps with bot replay; bhop maps from real-map assets |
| Review | **Restrat** | Watch a demo *inside* the server with teammates in separate POVs |
| Authoring | **Creator Studio** | Build arenas: player/bot spawns, swing direction (left/right/wackamole/recording/KZ), activation (seen/zone/defuse/point/chain/kill), duck/jiggle/jump/rush/turtle chances, placed smokes/infernos/bot utility, random offset, seed, publish to Community Hub |

### Web platform (not server features)
Coach (auto-imports Premier/FACEIT matches, prescribes modes), Bootcamp (map courses with exam),
Academy (pro videos + linked scenarios), Routines (shareable warmups, `.nexttask`), Utility Hub
(2D lineup browser), 2D Demo Viewer, Detailed Aim Stats, Community Hub (sets, nade groups,
VPL playlists).

## 2. What the OptiPlex already has

Hardware is not a constraint: i7-10700 (16 threads), 14 GB RAM with a 6 GB container cap,
128 tick, 12 slots, one map at a time.

**Live now:** OpenPrefirePrac fork (0.1.48) with `!freefrag record`, 9 maps of routes,
6 difficulty levels, random/full target mode, bot weapon choice, behaviour-tree hard bots,
multiplayer concurrent routes. GameModeManager, SimpleAdmin, CustomVotes, InventorySimulator,
MapConfigurator, RCON.

**Bundled and one `!modes` (or `exec x.cfg`) away** — the kus image keeps these in
`plugins/disabled/` and each mode cfg loads what it needs:

| Refrag mode | Already on the box | Caveat |
|---|---|---|
| Retakes | CS2 Retakes + RetakesAllocator + Instadefuse (`!retake`) | bots fill empty slots |
| Executes | CS2 Executes (`!executes`) | human-vs-human; needs players |
| Deathmatch | CS2 Deathmatch (`!dm`): custom spawns, gun menu, HS-only, spawn protection | |
| Scrim / PUG | MatchZy (`!comp`, `!wingman`): ready-up, knife, pause, demo recording | |
| NADR core | MatchZy practice (`!prac`): save/load/rethrow/last/back, `.noflash`, `.break`, `.god`, `.dryrun`, `.timer`, bot/crouchbot/boost, `.watchme`, grenade trajectory + preview | no finder, no map-placed lineups, no ghost replay |
| Duels | K4-Arenas (`!1v1`) | needs a MySQL/MariaDB container |
| Bunnyhop / Momentum (partial) | SharpTimer bhop/kz/surf (`!bhop`, `!kz`, `!surf`) | workshop maps, needs API_KEY |
| Aimbotz / Pop | `!aim` map group (workshop aim maps) | workshop, not real maps |
| Aim stats (partial) | K4-DamageInfo | per-round damage only |

## 3. Gaps, by how to close them

### A. Build into OpenPrefirePrac (the real Refrag-only stuff)
These are what the subscription buys that no free plugin provides. Ranked by value ÷ effort.

1. **Quality-of-life parity for Prefire** — small, high-impact, all server-side cvars or
   existing OPP state: `!freefrag hs` (`mp_damage_headshot_only`), `!freefrag xray` (glow on
   targets, prac only), weapon presets (pistol/eco/force/awp instead of one `bw`), `noprimary`,
   `endless`/`repeat N` on one route, `autoskip` after N deaths, random spawn offset
   (`.offangle`: jitter each bot a few units so the spot is learned, not the pixel).
2. **Scoring and a leaderboard** — time-to-clear, deaths, headshot %, per route per player,
   persisted to SQLite (CSS has SQLite built in). This is the `.comp` loop and the single
   biggest reason people keep paying. OPP currently records nothing.
3. **Smart difficulty** — map the behaviour-tree knobs already in `resources/bt`
   (reaction delay, aim offset) to a per-player Elo that moves with route results. Refrag's
   `.easy/.normal/.hard/.expert/.nightmare` is the same knob set with presets.
4. **Swinging bots (Quickfire / Blitz / Xfire / Clutch / Surge)** — the big one. Needs a
   route format extension: per-bot swing direction, trigger (on-seen / zone / chain / on-kill)
   and a short scripted move (strafe out, jiggle, rush). CS2 behaviour trees can drive a
   walk-to-point; OPP already ships custom `.kv3` trees, so the mechanism exists. Xfire falls
   out for free once Prefire routes can mark some bots as swingers.
5. **Waves / Surge** — knife bots that rush the player and respawn; simplest "dynamic bot"
   mode because it needs no scripted positions, just `bot_zombie 0`, knife-only and respawn.
6. **Spray Transfer** — three bots, two spots, reset on fire release; a Prefire route variant
   plus a "did the player stop shooting" check.
7. **Creator Studio lite** — `!freefrag record` already captures guiding lines; extend it to
   place bots (`!freefrag addbot [left|right|static]`) and save a route from in game, so
   routes stop needing `getpos` + the Python script.

### B. Add an existing plugin
- **NadeLauncher** (StefanKunde) — saved lineups with throw-type detection, in-game browser,
  ghost replay, sharing. Closest thing to NADR's map-placed lineups. Needs vetting; small repo.
- **CS2-Bot-Improver** (ed0ard) — bots that throw situational utility, flick, spray, better
  pathing; `bot_nades max`. Pairs with Executes/Retakes to get a crude **Defender**: bots
  executing onto a site with smokes/flashes. Linux needs `-insecure` and manual file copy;
  it is licensed AGPL and explicitly not for public servers.
- **demotracer** (unicbm) — plays a demo back inside a CS2 server, i.e. **Restrat**. Public
  playback plugin, AGPL; GUI closed.
- **MariaDB container** — unlocks K4-Arenas for Duels/1v1 ladders.
- **Workshop maps via MultiAddonManager** — aim_botz (3070923343), recoil master, gridshot-style
  maps cover Aimbotz, Pop and most of Recoil Trainer. Needs `API_KEY` set; already in `.env`.

### C. Off-server, free equivalents
- **2D demo viewer + aim stats**: CS Demo Manager (free, desktop) does 2D playback, per-match
  stats and heat maps from MatchZy's demos.
- **Coach / Routines / Academy / Bootcamp / Utility Hub**: content and web product; nothing to
  host. A `routines.json` that chains `!freefrag prac N` steps with timers would be the one
  server-side piece worth imitating.

### D. Not realistic to reproduce
- **Grenade Finder** (search the throw space for a lineup that lands in a box) — needs
  server-side trajectory simulation at scale; CS2 exposes prediction only to the client.
- **VPL** as Refrag does it (recorded multi-bot POV playback with utility) — bot "ghost"
  playback through the behaviour-tree API is unproven; treat as research, not a feature.
- **Recoil Trainer overlay** — live spray-line rendering needs client-side draw; a server can
  only approximate with beams.

## 4. Suggested order

1. A (1) QoL flags — a few hours, no new systems.
2. A (2) SQLite scoring + `!freefrag top` — makes every later mode measurable.
3. A (5) Waves — proves dynamic bots on the cheap.
4. A (3) smart difficulty on top of the scores.
5. A (4) swingers in the route format, then Xfire/Blitz/Clutch as route presets.
6. B items as the appetite for Retakes/Executes/Duels nights appears; they are config, not code.
