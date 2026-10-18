# Swinging Bots Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prefire routes can mark bots that, once the player comes within a trigger distance, run along the nav mesh from their hiding spot to a recorded peek point and fight from there.

**Architecture:** A `peek px py pz [trigger d]` spec on a route line marks a swinger. The plugin spawns the bot frozen at the hiding spot as today, and on release spawns an invisible marker entity at the peek point and unfreezes the bot. One new branch in `resources/bt/hard_mode.kv3` makes any walking bot run to the nearest marker; the existing attack subtree takes over once it sees the player.

**Tech Stack:** C# / CounterStrikeSharp (net10, built on the OptiPlex), xunit, CS2 behaviour-tree kv3.

**Spec:** `docs/swinging-bots-design.md`

## Global Constraints

- Tests and builds run only through `bash scripts/build-on-server.sh test|all` (Bash tool, 400000 ms timeout, output to a log under `.agents/scratch/`); never `dotnet` locally, never `scripts/deploy.sh`. Check `ssh quinn@192.168.4.20 "docker ps --filter ancestor=mcr.microsoft.com/dotnet/sdk:10.0 -q"` prints nothing before each build.
- `Freefrag.cs` is stored with CRLF line endings (`git ls-files --eol Freefrag.cs` must read `i/crlf w/crlf` before every commit); every other source file is LF. Edit with tools that preserve the file's endings.
- Every route file without a `peek` token must parse to exactly the same targets as before (the parser test in Task 1 pins this on a copy of `maps/de_dust2/a_long.txt`).
- Chat strings go in `lang/en.json` only (pt-BR and zh fall back), through `PrintEditorChat(player, key, args)` in `Freefrag.cs:2018`.
- Marker entity class name is one constant, `SwingMarkerClass`, used by the plugin and asserted against the tree file; its value is decided by the spike (Task 0).
- No commit may include the spike command (Task 0's `swingtest`) once Task 5 lands; Task 5 removes it.

## Review Focus

- A `peek` spec with only two numbers (`peek 1 2 True`): the line must become a static bot with one console warning, never a crash or a half-parsed swinger (Task 1 test `MalformedPeekFallsBackToStaticWithWarning`).
- Owner dies or exits the route after releasing a swinger: the marker must be removed and the bot kicked like any other, no marker left for the next route (Task 4 test `ClearRemovesMarkersAndStates` plus the in-game check in Task 6).
- Two swingers whose hiding spots are closer to each other's peek points than their own: both run to the same marker; the README states the rule and the editor does not try to prevent it (Task 3 README line).
- A swinger spawned while the owner is already inside the trigger distance: it must still land and freeze first, then release on the next tick after the landing watch finishes, not run from mid-air (Task 4 test `ReleaseWaitsForLanding`).
- `trigger` below 100 or above 3000 on a route line: clamp and keep going rather than reject the route (Task 1 test `TriggerIsClamped`); a config `SwingTrigger` out of range keeps the default (Task 3 test `SwingTriggerOutOfRangeKeepsDefault`).

---

### Task 0: Spike — can the tree see a plugin-spawned marker and move an unfrozen bot?

**Files:**
- Modify: `resources/bt/hard_mode.kv3` (second `parallel`, the `Attack` subtree entry at the end of the file)
- Modify: `Freefrag.cs` (new `case "swingtest":` next to `case "newroute":` in the `!freefrag` switch at 2950; handler next to `HandleNewRouteCommand` at 2169)
- No tests: throwaway, verified in game by Quinn.

**Interfaces:**
- Produces: the decision `SwingMarkerClass` (string) and the arrival rule for Task 4 and Task 5; the exact tree branch text that Task 5 keeps.

- [ ] **Step 1: Add the swing branch to `hard_mode.kv3`.** Replace the second `parallel`'s two children (`decorator_sensor HUMAN_PLAYERS -> action_aim` and `subtree bt_attack.kv3`) with: the same sensor child first, then a `selector` whose children are the unchanged `bt_attack.kv3` subtree followed by

```text
{
    type = "decorator_sensor"
    shape = { type = "sensor_shape_sphere"  radius = 2000 }
    entity_type_filter = "CLASSNAME"
    class_name = "info_target"
    output = "SwingMarkers"
    child =
    {
        type = "decorator_picker_nearby"
        input = "SwingMarkers"
        cutoff_distance = 2000
        child =
        {
            type = "condition_is_empty"
            input = "SwingMarkers"
            negated = 1
            child =
            {
                type = "action_move_to"
                destination = "SwingMarkers"
                movement_type = "BT_ACTION_MOVETO_RUN"
                route_type = "BT_ACTION_MOVETO_FASTEST_ROUTE"
                arrival_epsilon = 20
                auto_look_adjust = 1
            }
        }
    }
}
```

Keep the kv3 header line and the tab indentation of the file.

- [ ] **Step 2: Add `!freefrag swingtest [class]` in `Freefrag.cs`.** Handler `HandleSwingTestCommand(CCSPlayerController player, string className)`: requires `_modeGate.Active` and a route in progress; spawns an entity of `className` (default `info_target`) with `Utilities.CreateEntityByName<CBaseEntity>(className)`, teleports it to the player's aim point (feet position plus 300 units along the player's yaw is enough; use `EyeAngles.Y`), calls `DispatchSpawn()`; then finds the nearest managed bot of this owner (`_playerStatuses[slot].Bots`) and unfreezes it with the mechanism of `SetPlayerFrozen(player, false)` (`Freefrag.cs:1598`): `MoveType = MOVETYPE_WALK`, `m_nActualMoveType = 2`, `SetStateChanged`. Prints one chat line with the entity index and bot name, and logs the same to the console. A second call with a different class spawns a second marker; nothing is cleaned up (spike).

- [ ] **Step 3: Build and publish.** Run: `bash scripts/build-on-server.sh all > .agents/scratch/swing-spike-build.log 2>&1`; expected: `Passed!` with all existing tests and exit 0. Commit as `Swinging bots spike: marker sensor branch and swingtest command (throwaway)` and push; give Quinn `bash -c "FORCE_RELOAD=1 scripts/deploy.sh"`.

- [ ] **Step 4: Quinn runs the spike** on any route: start the route, look at a spot on walkable ground 200-400 units from a bot, type `!freefrag swingtest`. Record one of: (a) the nearest bot runs to the spot and stops; (b) the bot stands still; (c) the bot runs somewhere else. If (b), repeat with `!freefrag swingtest prop_dynamic`. If (b) again, the fallback applies: change `class_name`/sensor to `entity_type_filter = "HUMAN_PLAYERS"`, `output = "SwingTarget"`, `destination = "SwingTarget"` and record whether the bot runs at the player. Note also whether the console shows any `[AI BT]` error after the reload.

- [ ] **Step 5: Record the outcome** at the top of `docs/swinging-bots-design.md` under "Spike" (one line: date, class that worked or fallback) and set `SwingMarkerClass` accordingly in Task 4. Commit.

---

### Task 1: Peek spec in the route format

**Files:**
- Create: `SwingSpec.cs`
- Modify: `Target.cs:5-17` (`TargetBot`), `Target.cs:161-162` (target line parsing)
- Test: `tests/Freefrag.Tests/SwingSpecTests.cs`

**Interfaces:**
- Produces: `public readonly record struct SwingSpec(float X, float Y, float Z, float Trigger)` with `public const float NoTrigger = 0f`, `MinTrigger = 100f`, `MaxTrigger = 3000f`; `public static bool TryParse(string[] tokens, int start, out SwingSpec spec, out string? error)` scanning tokens from index `start` (the token after the crouch flag) for `peek x y z` and optional `trigger d`, returning false with `error == null` when there is no `peek` token, false with a message when `peek` is present but malformed; `public static string Format(SwingSpec spec)` → `"peek x y z"` plus `" trigger d"` when `Trigger != NoTrigger`, F6/invariant for coordinates, F0 for trigger; `TargetBot.Peek` (`SwingSpec?`, null for static) and `TargetBot.IsSwinger`.

- [ ] **Step 1: Write the failing tests** in `SwingSpecTests.cs`:

```csharp
[Fact] public void ParsesPeekWithoutTrigger()   // tokens of "1 2 3 4 5 6 False peek 10.5 -20 30" from index 7 -> (10.5,-20,30, NoTrigger)
[Fact] public void ParsesPeekWithTrigger()      // "... True peek 1 2 3 trigger 450" -> Trigger 450
[Fact] public void TriggerIsClamped()           // trigger 50 -> 100; trigger 9000 -> 3000
[Fact] public void NoPeekTokenIsNotAnError()    // "... False # comment" -> false, error null
[Fact] public void MalformedPeekIsAnError()     // "... False peek 1 2" -> false, error mentions "peek"
[Fact] public void CommentAfterPeekIsIgnored()  // "... False peek 1 2 3 # left side" -> true
[Fact] public void FormatRoundTrips()           // Format then TryParse gives the same spec; invariant culture under de-DE
[Fact] public void ParserKeepsExistingRoutesIdentical() // copy maps/de_dust2/a_long.txt to a temp maps/<map>/ dir, construct PrefirePractice, assert 39 targets, none IsSwinger, first/last positions unchanged
[Fact] public void ParserReadsSwingerLine()     // temp profile with one static and one peek line -> second target IsSwinger with the spec
[Fact] public void MalformedPeekFallsBackToStaticWithWarning() // temp profile with "peek 1 2" -> target is static; (warning goes to Console, not asserted)
```

- [ ] **Step 2: Run to verify they fail.** `bash scripts/build-on-server.sh test > .agents/scratch/swing-t1a.log 2>&1`; expected: build error `SwingSpec` not found.

- [ ] **Step 3: Implement `SwingSpec.cs`** with the interface above. Tokens are compared case-insensitively; parsing uses `NumberStyles.Float` and `CultureInfo.InvariantCulture`.

- [ ] **Step 4: Extend `TargetBot`** with a second constructor taking `SwingSpec? peek` (keep the 7-argument one, which passes null), `public readonly SwingSpec? Peek;`, `public bool IsSwinger => Peek != null;`. In `PrefirePractice`'s loop, after reading the seven base tokens call `SwingSpec.TryParse(w, 7, out var spec, out var error)`; on `error != null` write `[Freefrag] {map}/{practice} line {n}: {error}; treating the bot as static.` and use null.

- [ ] **Step 5: Run tests.** Same command; expected `Passed!`, 0 failed.

- [ ] **Step 6: Commit** `Route format: optional peek spec marks a swinging bot` (SwingSpec.cs, Target.cs, SwingSpecTests.cs).

---

### Task 2: Route editor places swingers

**Files:**
- Modify: `RouteEditor.cs:9` (`DraftBot`), `RouteEditor.cs:16` (`FormatBotLine`)
- Modify: `Freefrag.cs:2028-2097` (`HandleAddBotCommand`), `Freefrag.cs:2099` (`DrawDraftBotMarker`), `Freefrag.cs:2128` (`SaveDraftBots`)
- Modify: `PlayerStatus.cs` (`DraftBotBeams` stays; no new field needed: the peek lives on the `DraftBot`)
- Modify: `lang/en.json` (keys `addbot.swing_added`, `addbot.peek_set`, `addbot.peek_nothing`, `addbot.peek_missing`; extend `addbot.help` and the `addbot` line in `mainmenu.help` with `swing`/`peek`)
- Test: `tests/Freefrag.Tests/RouteEditorTests.cs`

**Interfaces:**
- Consumes: `SwingSpec`, `SwingSpec.Format` (Task 1).
- Produces: `DraftBot(float X, float Y, float Z, float Pitch, float Yaw, bool Crouch, bool Swing = false, SwingSpec? Peek = null)`; `RouteEditor.WithPeek(DraftBot bot, float x, float y, float z)` returning a copy with `Peek` set and `Trigger = SwingSpec.NoTrigger`; `RouteEditor.MissingPeeks(IReadOnlyList<DraftBot>)` → count of swingers without a peek.

- [ ] **Step 1: Write the failing tests** (append to `RouteEditorTests.cs`):

```csharp
[Fact] public void FormatBotLineAppendsPeekSpec()        // swinger with peek -> line ends "True peek 1.000000 2.000000 3.000000"
[Fact] public void FormatBotLineOmitsPeekForStaticBots()  // unchanged 7-token line
[Fact] public void WithPeekSetsThePointOnly()             // position/angles/crouch untouched, Trigger NoTrigger
[Fact] public void MissingPeeksCountsSwingersWithoutPoint() // [static, swing w/o peek, swing w/ peek] -> 1
[Fact] public void InsertedSwingerLineParsesBack()        // NewProfile + InsertBots with a swinger -> PrefirePractice target IsSwinger
```

- [ ] **Step 2: Run to verify they fail** (build error on the new `DraftBot` members).

- [ ] **Step 3: Implement** the `RouteEditor` changes. `FormatBotLine` appends `" " + SwingSpec.Format(Peek.Value)` when `Peek != null`.

- [ ] **Step 4: Editor commands** in `HandleAddBotCommand`: `case "swing":` places a draft bot like the default case with `Swing = true` and prints `addbot.swing_added` (count); `case "peek":` requires a last draft bot with `Swing == true` (else `addbot.peek_nothing`), replaces it with `WithPeek(last, feet.X, feet.Y, feet.Z)`, redraws its marker adding a lime beam from the hiding spot (feet + 8 Z) to the peek point, prints `addbot.peek_set`. `case "save":` first checks `MissingPeeks(status.DraftBots) > 0` → `addbot.peek_missing` and returns. `DrawDraftBotMarker` draws the extra beam when `Peek != null`; the beam list per draft bot (`DraftBotBeams[^1]`) holds it so undo/clear remove it.

- [ ] **Step 5: Lang strings** (exact copy):
  - `addbot.swing_added`: `Draft swinger {0} placed at its hiding spot. Walk to where it should peek and type [PURPLE]!freefrag addbot peek[NORMAL].`
  - `addbot.peek_set`: `Peek point set for draft bot {0}.`
  - `addbot.peek_nothing`: `No draft swinger to set a peek point for. Place one with [PURPLE]!freefrag addbot swing[NORMAL].`
  - `addbot.peek_missing`: `{0} draft swinger(s) have no peek point yet. Stand where each should peek and type [PURPLE]!freefrag addbot peek[NORMAL], or undo them.`
  - `addbot.help`: `Usage: [PURPLE]!freefrag addbot[NORMAL] [crouch/stand] | [PURPLE]addbot swing[NORMAL] | [PURPLE]addbot peek[NORMAL] | [PURPLE]addbot undo[NORMAL] | [PURPLE]addbot save[NORMAL] | [PURPLE]addbot clear[NORMAL]`
  - `mainmenu.help` addbot line gains `swing/peek` in the bracket list.
  Validate with `python -c "import json;json.load(open('lang/en.json',encoding='utf-8'))"`.

- [ ] **Step 6: Run tests**; expected `Passed!`.

- [ ] **Step 7: Commit** `Route editor: addbot swing and addbot peek`.

---

### Task 3: Config and README

**Files:**
- Create: `SwingTriggerSetting.cs` (same shape as `CountdownSetting` in `RangeSetting.cs`)
- Modify: `ServerConfig.cs:36,221` (next to `Countdown`), `ServerConfig.cs:162-165` (range check, same `if` shape), the `Using default settings` console line at ~202
- Modify: `default_cfg.json.example`, `README.md` (new section after "Placing bots and creating routes in game")
- Test: `tests/Freefrag.Tests/CheapSettingsTests.cs`

**Interfaces:**
- Produces: `public static class SwingTriggerSetting { public const int Min = 100, Max = 3000, Default = 600; public static bool InRange(int v); public static float Clamp(float v); }` and `DefaultConfig.SwingTrigger` (int, default 600; a config value outside `Min..Max` keeps the default, like `Countdown`). Task 4's `EffectiveTrigger` uses `Clamp`.

- [ ] **Step 1: Write the failing test** `SwingTriggerOutOfRangeKeepsDefault` in `CheapSettingsTests.cs`, next to the countdown test: `InRange(99)` false, `InRange(100)` and `InRange(3000)` true, `InRange(3001)` false, `Clamp(50f) == 100f`, `Clamp(9000f) == 3000f`, `Clamp(450f) == 450f`, `Default == 600`. (The file-reading constructor of `DefaultConfig` is not unit-tested today; the range check in it reuses `InRange`.)

- [ ] **Step 2: Run to verify it fails.**

- [ ] **Step 3: Implement** the property in both config classes and the range check; add `SwingTrigger = {SwingTrigger}` to the console line; add `"SwingTrigger": 600,` to `default_cfg.json.example` with a README bullet under the config list: `SwingTrigger: 100-3000: Distance (units) at which a swinging bot is released when its route line sets no trigger (default 600).`

- [ ] **Step 4: README section** "Swinging bots (this fork)": the `peek px py pz [trigger d]` format with one example line; the editor commands from Task 2; "a released bot runs to the nearest marker, so keep hiding spots farther apart than their peek points"; "bots only move on the nav mesh; a bot that never moves is reported in the server console as `Route <name> target <n> ... never moved`"; v1 limits (no return to cover, no swing direction).

- [ ] **Step 5: Run tests**; `Passed!`. **Commit** `Config: SwingTrigger; README: swinging bots`.

---

### Task 4: Release loop, markers and cleanup

**Files:**
- Create: `SwingRelease.cs`
- Modify: `PlayerStatus.cs` (add `public readonly Dictionary<int, SwingState> Swingers = new();`)
- Modify: `Freefrag.cs` OnPlayerSpawn bot branch (~640-660: where `MovePlayer` and the `LandingWatch` are created), `OnTickHandler` after the landing pass (~4090) and the aim-lock loop (~4100-4130), `KickBot` (3910), `UnsetPrefireMode` (~3360), `OnMapStartHandler` status loop (~560), `Unload` (~419)
- Test: `tests/Freefrag.Tests/SwingReleaseTests.cs`

**Interfaces:**
- Consumes: `TargetBot.Peek`, `SwingSpec` (Task 1); `DefaultConfig.SwingTrigger` (Task 3); `SwingMarkerClass` value (Task 0).
- Produces: `public sealed class SwingState { public SwingSpec Peek; public float Trigger; public bool Released; public int Marker = -1; public bool Landed; }`; static `SwingRelease` with `public const float ArriveDistance = 30f;` `public static float EffectiveTrigger(float lineTrigger, int configDefault)` (line value when not `NoTrigger`, else the config, both through `SwingTriggerSetting.Clamp`); `public static bool ShouldRelease(SwingState s, float ownerX, float ownerY, float ownerZ, float hideX, float hideY, float hideZ)` (false unless `Landed && !Released` and 3D distance ≤ `Trigger`); `public static bool Arrived(SwingState s, float botX, float botY, float botZ)` (`Released && Marker != -1` and distance to `Peek` ≤ `ArriveDistance`). Plugin-side: `private const string SwingMarkerClass = "<from spike>";`, `ReleaseSwinger(int ownerSlot, int botSlot)`, `RemoveSwingMarker(SwingState s)`, `ClearSwingers(int ownerSlot)`.

- [ ] **Step 1: Write the failing tests** in `SwingReleaseTests.cs`:

```csharp
[Fact] public void EffectiveTriggerPrefersTheLineAndClamps()   // (NoTrigger, 600) -> 600; (450, 600) -> 450; (50, 600) -> 100; (NoTrigger, 9000) -> 3000
[Fact] public void ReleaseWaitsForLanding()                    // Landed=false inside trigger -> false; Landed=true -> true
[Fact] public void ReleaseUsesThreeDimensionalDistance()        // owner 600 above the spot with trigger 500 -> false
[Fact] public void ReleasedOnceStaysReleased()                 // Released=true -> ShouldRelease false
[Fact] public void ArrivedOnlyWithAMarkerWithinThirtyUnits()   // Marker=-1 -> false; 29 units -> true; 31 -> false
[Fact] public void ClearRemovesMarkersAndStates()              // pure part: a helper that collects marker indices to remove from a status and empties the dictionary
```

- [ ] **Step 2: Run to verify they fail.**

- [ ] **Step 3: Implement `SwingRelease.cs`** and the `SwingState` class (same file), plus `public static List<int> TakeMarkers(Dictionary<int, SwingState> swingers)` which returns every `Marker != -1` and clears the dictionary (the pure half of `ClearSwingers`).

- [ ] **Step 4: Wire the plugin.**
  - OnPlayerSpawn: when `target.IsSwinger`, after creating the landing watch add `status.Swingers[botSlot] = new SwingState { Peek = target.Peek.Value, Trigger = SwingRelease.EffectiveTrigger(target.Peek.Value.Trigger, _defaultPlayerSettings!.SwingTrigger) }`.
  - Landing pass: when a watch ends (any verdict) and the bot has a `SwingState`, set `Landed = true` (FreezeBot still runs).
  - New block after the landing pass: for each practicing owner with a live pawn, for each `(botSlot, state)` in `Swingers`: if `ShouldRelease(...)` with the hiding spot = `_practices[idx].Targets[...]` position stored on the state at creation (`add public float HideX, HideY, HideZ`), call `ReleaseSwinger`; else if `Arrived(state, botFeet)` call `RemoveSwingMarker(state)`.
  - `ReleaseSwinger`: spawn `Utilities.CreateEntityByName<CBaseEntity>(SwingMarkerClass)`, `Teleport(peek, QAngle.Zero, Vector.Zero)`, `DispatchSpawn()`, store `Marker = entity.Index`; unfreeze the bot exactly as `SetPlayerFrozen(bot, false)` does; `Released = true`; console line `[Freefrag] Released swinger {name} for slot {owner} towards ({peek}).`
  - Aim-lock loop: `continue` for bots whose `SwingState.Released` is true.
  - `RemoveSwingMarker`: `Utilities.GetEntityFromIndex<CBaseEntity>(Marker)?.Remove()` when valid, `Marker = -1`.
  - `ClearSwingers(ownerSlot)`: remove every marker from `TakeMarkers`, called from `UnsetPrefireMode`, `Unload` (per owner), and on bot death/kick for that bot only (`KickBot`: remove the one state after removing its marker; look up the owner through `_ownerOfBots`). Map start: clear the dictionaries without touching entity indices, like `DraftBotBeams`.

- [ ] **Step 5: Run tests**; `Passed!`. **Commit** `Swinging bots: release loop, markers and cleanup`.

---

### Task 5: Final tree branch and shape test; remove the spike command

**Files:**
- Modify: `resources/bt/hard_mode.kv3` (set `class_name` to the spike's result; keep the Task 0 structure)
- Modify: `Freefrag.cs` (delete `case "swingtest":` and `HandleSwingTestCommand`)
- Test: `tests/Freefrag.Tests/BehaviorTreeConfigTests.cs`

**Interfaces:**
- Consumes: `SwingMarkerClass` (Task 4) — the test reads the constant through reflection or a `public const` on a small `SwingMarker` static class moved out of `Freefrag.cs` (`SwingMarker.ClassName`); prefer the static class so the test needs no engine types.

- [ ] **Step 1: Write the failing test** `HardModeTreeRunsToTheSwingMarker`: read `resources/bt/hard_mode.kv3` through the existing `RepoFile` helper; assert exactly one `action_move_to`, its `destination = "SwingMarkers"`, one `class_name = "<SwingMarker.ClassName>"`, and that `bt_attack.kv3` appears before the sensor in the file (attack wins the selector).

- [ ] **Step 2: Run to verify it fails** (if the spike already set the right class, the test passes immediately: then make it fail once by editing the expected value, confirm, and restore it).

- [ ] **Step 3: Implement**: move the constant to `SwingMarker.cs`, delete the spike command, align the tree.

- [ ] **Step 4: Run tests**; `Passed!`. **Commit** `Swinging bots: final tree branch, spike command removed`.

---

### Task 6: In-game verification on one Xfire route

**Files:** none in code; one route file under `maps/<map>/` gains two `peek` lines through the editor.

- [ ] **Step 1: Build and publish** (`all`), commit nothing yet, hand Quinn `bash -c "FORCE_RELOAD=1 scripts/deploy.sh"`.
- [ ] **Step 2: Quinn** starts an existing route, uses `!freefrag addbot swing` behind a corner and `!freefrag addbot peek` at the exposed spot twice, `addbot save`, restarts the route, and reports: both bots stay hidden until approached, run to their points, shoot; kills count; leaving the route leaves no marker (check with `!freefrag prac` again and the console).
- [ ] **Step 3: Read the server log** for `Released swinger` lines and any `[AI BT]` error; `scripts/pull-routes.sh`; commit the route file as `Routes: two swingers on <map>/<route>`.
- [ ] **Step 4: Update** `docs/swinging-bots-design.md` status line and the roadmap memory.
