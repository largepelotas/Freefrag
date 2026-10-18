# Swinging bots: design (2026-10-18)

Approved in chat on 2026-10-18. Gives Prefire routes bots that peek the player with the engine's own
running animation and nav-mesh pathing, which is what Refrag's Quickfire, Blitz, Xfire and Clutch are
built on. Positions are exact at the hiding spot and at the peek point; the path between them is the
nav mesh's. Status of the two engine unknowns is tracked under "Spike".

## Why the behaviour tree and not the plugin

A CS2 bot pawn moves only through its own bot-AI movement tick (see the floating-bot investigation of
2026-10-18, memory `bots-off-navmesh-never-simulated`): move types, velocity and pushes from the plugin
do nothing to an idle bot. The plugin can teleport a pawn, which would give a sliding statue; Quinn
chose a visibly running bot. The engine's behaviour trees (extracted from `pak01` to
`.agents/scratch/bt_extract/scripts/ai/`) provide:

- `action_move_to`: runs the bot to `destination`, which is a literal `"x y z"`, a blackboard key holding
  an entity or location, or a named point. Parameters seen: `movement_type = "BT_ACTION_MOVETO_RUN"`,
  `route_type = "BT_ACTION_MOVETO_FASTEST_ROUTE"`, `arrival_epsilon`, `auto_look_adjust`, `threat`.
- `decorator_sensor` with `entity_type_filter = "CLASSNAME"`, `class_name = "<class>"` and an optional
  `shape = { type = "sensor_shape_sphere" radius = N }`: outputs every entity of that class in range, no
  line of sight (Valve uses it for `hostage_entity`, `inferno`, `planted_c4`, `weapon_c4`).
- `decorator_picker_nearby input cutoff_distance`: keeps the closest entry.
- `bt_attack.kv3` (already in Freefrag's `hard_mode.kv3`): aims, fires and does `action_combat_positioning`
  (strafing) as soon as the bot has the player in short-term memory. It sits first in the selector, so it
  takes over the moment a peek exposes the bot.
- Per-bot identity exists (`decorator_token_service` hands out tokens such as `ct_1`), but the plugin
  cannot learn which bot got which token, so per-bot literal destinations are not usable. The marker
  approach below gives each bot its own destination without identity.

## Route format

A target line may carry a peek spec after the crouch flag:

```text
pos_x pos_y pos_z ang_x ang_y ang_z is_crouching peek px py pz [trigger d] [# comment]
```

- The line's own position and angles are the hiding spot, exactly as for a static bot.
- `peek px py pz` is where the bot runs to when released (feet coordinates, same convention as the line).
- `trigger d` (optional) is the owner-to-hiding-spot distance in units that releases the bot; default
  `SwingTrigger` from the config (600).
- A line without `peek` is a static bot. Every existing route parses unchanged. Tokens after the crouch
  flag that are not `peek`/`trigger` are a comment, as before; `#` still starts one.
- Parsing lives in `Target.cs`: `TargetBot` gains `Peek` (nullable `Vector`) and `Trigger` (float, 0 = default).
  A pure `SwingSpec.TryParse(string[] tokens, out ...)` is the single parser, used by both the file reader
  and tests. Malformed peek specs (fewer than three numbers) make the whole line a static bot and print
  one console warning naming the route and line.

Route presets: Xfire is any Prefire route with some `peek` lines. Quickfire/Blitz/Clutch are routes whose
lines are all swingers, differing in trigger distances and where the player starts. They are content,
not code.

## Route editor

- `!freefrag addbot swing`: places a draft bot at your feet, facing your view, flagged as a swinger with no
  peek point yet.
- `!freefrag addbot peek`: sets the peek point of the last draft bot to where you stand now. Refused with
  `addbot.peek_nothing` when there is no draft bot or the last one is not a swinger.
- `!freefrag addbot save` refuses (`addbot.peek_missing`) while any draft swinger has no peek point.
- Draft markers: the existing lime post plus a lime line from the hiding spot to the peek point once set.
- `RouteEditor.FormatBotLine` emits the peek spec; `InsertBots` is unchanged.

## Runtime

### State

- `PlayerStatus.Swingers`: `Dictionary<int botSlot, SwingState>` with `Released` (bool), `Marker`
  (entity index or -1), `Peek` (Vector), `Trigger` (float). Filled when a bot is moved to a target that has
  a peek spec (OnPlayerSpawn, the same place the landing watch is created). Removed on bot kick, death,
  route exit, disconnect, unload and map change, always removing the marker entity first.

### Release

- `OnTickHandler`, after the landing pass: for each owner alive and practicing, for each unreleased
  swinger whose landing watch has finished, if the owner's feet are within `Trigger` of the hiding spot,
  release it. Pure helper `SwingRelease.ShouldRelease(ownerPos, hidingPos, trigger)`.
- Release does three things, in order: spawn the marker at `Peek`; set the bot's move type back to
  walking (the same mechanism `SetPlayerFrozen(player, false)` uses: `MOVETYPE_WALK` plus
  `m_nActualMoveType = 2` plus `SetStateChanged`); mark `Released`. Released swingers are skipped by the
  per-tick aim lock loop so the tree's own look control is not fought.
- Marker: superseded by the spike result (see "Runtime changes that follow from the spike"): no entity.
- Arrival: once the released bot's feet are within 30 units of `Peek` (`SwingRelease.Arrived`) or 2 s have
  passed, freeze it there and let the aim lock resume. No return to cover in v1.

### Behaviour tree

`resources/bt/hard_mode.kv3` gains one branch. The second `parallel` currently holds the aim-acquire
sensor and the `Attack` subtree side by side. It becomes:

```text
parallel
  decorator_sensor HUMAN_PLAYERS -> Target -> action_aim acquire_only   (unchanged)
  selector
    subtree bt_attack.kv3                                               (unchanged, first)
    decorator_sensor CLASSNAME class_name=<SwingMarkerClass> shape sphere radius 2000 output=SwingMarkers
      decorator_picker_nearby input=SwingMarkers cutoff_distance=2000
        condition_is_empty input=SwingMarkers negated=1
          action_move_to destination=SwingMarkers RUN FASTEST arrival_epsilon=20 auto_look_adjust=1
```

Static bots are frozen (`MOVETYPE_VPHYSICS`), so the branch cannot move them. Two swingers released at
once each run to the marker nearest their own hiding spot, which is the intended one whenever hiding
spots are farther apart than their peek points are from each other; document this in the README.

`tests/Freefrag.Tests/BehaviorTreeConfigTests.cs` already guards the config table; add a test that
`hard_mode.kv3` contains exactly one `action_move_to` whose `destination` is the marker key and that
`class_name` equals `SwingMarkerClass`.

### Config

- `SwingTrigger` in `default_cfg.json` (int, 100..3000, default 600): default release distance.
- README: a section after the route-format section describing `peek`/`trigger`, the editor commands,
  the marker/nearest-marker rule, and the "bots only run on the nav mesh" limitation with the
  `never moved` log line as the diagnostic.

## Spike (before any of the above is built)

**Result, 2026-10-18 22:50.** Three runs on the live server (commits e02f735, ec3d1b3 and a patched build):
(1) `decorator_sensor` with `CLASSNAME` does not see a plugin-spawned `info_target` or `prop_dynamic`: the
bot was released and stood still, no `[AI BT]` error. (2) With `entity_type_filter = "HUMAN_PLAYERS"` and
`action_move_to destination` on that key, the released bot only sidestepped, because the per-tick aim lock
teleports its angles every tick and cancels the move. (3) With the released bot excluded from the aim lock
and the player behind cover, the bot ran around the corner to the player. So the sensor sees the human
through walls and the tree moves an unfrozen bot. **Decision:** no marker entity. The branch runs the bot
at the human; the peek point is the stop line (below). Spike 3's branch is the final one.

### Runtime changes that follow from the spike

- Release: unfreeze the bot and exclude it from the aim lock. No entity is spawned.
- Arrival: the first tick the bot's feet are within 30 units of its peek point, `FreezeBot` runs and the
  aim lock resumes for it, so it fights from the recorded spot. If 2 seconds pass without that (the nav
  path never touched the peek point), freeze it where it is and log
  `[Freefrag] Swinger <bot> on <route> target <n> never reached its peek point; frozen after 2 s.` once per
  route target per map load. Either way the bot is a static bot from then on.
- The tree branch: `decorator_sensor HUMAN_PLAYERS -> SwingTarget`, `decorator_picker_nearby`,
  `condition_is_empty negated`, `action_move_to destination = "SwingTarget"` RUN / FASTEST,
  `arrival_epsilon 20`, `auto_look_adjust 1`, placed after `Attack` in a selector. Static bots cannot move,
  so the branch is inert for them.
- Multiple owners: the bot runs at the nearest human, which is its owner in practice; the stop line
  makes the difference moot.

Two things only the live server can answer:

1. Does `decorator_sensor` with `CLASSNAME` see an entity the plugin spawned (`info_target`, else
   `prop_dynamic`)?
2. Does a bot that was frozen with `MOVETYPE_VPHYSICS` and switched back to walking run under the tree?

Spike build: the tree branch above plus a console/chat command `!freefrag swingtest` (admin only, removed
afterwards) that spawns a marker where the player aims and unfreezes the nearest managed bot. Quinn runs
it on a route and reports: bot runs to the marker / bot stands still / bot runs elsewhere. Each outcome
maps to: proceed; try the next marker class; or fall back to `destination = "Target"` (run at the
player), which keeps the format and release logic and changes only the tree branch and the arrival rule
(arrive = sees the player).

## Out of scope for v1

Return to cover and re-peek, left/right swing styles, chain and on-kill triggers, Waves/Surge, duck or
jiggle chances, per-bot reaction settings. Each is a later increment on this format (more tokens after
`peek`) and this release loop.

## Testing

xunit, run on the OptiPlex: `SwingSpec.TryParse` (valid, missing trigger, malformed, comment after it,
static line), `FormatBotLine` round trip through `PrefirePractice`, `SwingRelease.ShouldRelease` and
`Arrived` boundaries, `PlayerStatus` bookkeeping helpers, the tree-shape test, config range clamping.
Movement, sensing and animation are verified in game by Quinn, first in the spike, then on one Xfire
route (an existing route with two `peek` lines added through the editor).
