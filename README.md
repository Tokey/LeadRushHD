# Lead Rush HDRP

Lead Rush HDRP is a research FPS built for experiments on framerate, latency spikes, and graphical fidelity. It runs on Unity's High Definition Render Pipeline.

It is a fork of Lead Rush. The core loop is the same. What is new here is per-round control of graphics quality, support for several enemies at once at different speeds, and a minimap.

Rounds and conditions are set from CSV files. Everything the player does is logged to CSV.

---

## Multiple enemies for stress research
Multiple enemies are set per round in Data/Configs/LatinMap.csv. Each row is one condition, and the last three columns control them: EnemyCount is how many stay alive at once, and EnemySpeedMin / EnemySpeedMax set the speed range. Speeds are spread evenly across that range, so 4, 1, 4 gives four enemies running at exactly 1, 2, 3 and 4 units per second. Set min equal to max and every enemy runs at that one speed — 3, 3, 3 gives three enemies all at speed 3. Each enemy holds a slot in the spread, so when one dies its replacement comes back at the same speed and the mix stays the same for the whole round. Point a round at the condition by putting its row number in RoundConfig.csv.

## Gameplay Overview

- **Objective:** Kill as many enemies as you can before the round timer runs out.
- **Enemies:** One or more enemies spawn around the player and charge at them. Count and speed are set per round.
- **Damage:** Only headshots hurt enemies. A headshot does 5x the weapon's bullet damage. Enemy health comes from `EnemyHealthGlobal`. Body shots score a miss.
- **Death:** Touching an enemy kills the player. The player respawns and all enemies are cleared.
- **Direction cues:** Yellow bars on the screen edges flash toward nearby enemies. Each edge tracks the closest enemy on that side. They flash faster and brighter as an enemy closes in, with a beep.
- **Minimap:** Small radar in the bottom left. Up is where the player is facing. Enemies past the edge of its range stick to the rim in a dimmer colour.
- **Rounds:** A session is a sequence of rounds. Each round has its own framerate, spike, graphics, and enemy settings.
- **After each round:** The player answers the QoE sliders, then an acceptability yes/no. Logs are written at that point.

## Controls

| Key | Action |
|---|---|
| `Tab` | Start the round |
| `Mouse1` | Fire |
| `Mouse2` | Aim |
| `R` | Reload |
| `WASD` / `Shift` / `Ctrl` / `C` | Move, sprint, crouch, prone |

---

## Install

- Download a release.
- Run `LeadRush.exe`.
- `Data/` must sit next to the exe. The game reads configs from it and writes logs into it.

---

## Configuration Files

All in `Data/Configs/`.

Every config file has a header row. The game skips it. Files without a header still load, so old configs keep working.

Numbers are parsed as invariant culture. Use `1.5`, not `1,5`.

### 1. `GlobalConfig.csv`

One data row. Applies to the whole session.

| Column | Type | Units | Description |
|---|---|---|---|
| RoundDurationS | float | seconds | Length of each round |
| IsFTStudy | bool | TRUE/FALSE | TRUE reads conditions from `LatinMap.csv`. FALSE reads framerates from `LatinSquare.csv` |
| AimSpikeDelayS | float | seconds | Cooldown between aim spikes |
| MouseSpikeDelayS | float | seconds | Cooldown between mouse spikes |
| MouseSpikeDegreeThreshold | float | degrees | Mouse delta that triggers a mouse spike |
| EnemySpeedGlobal | float | units/sec | Fallback enemy speed. Per-round speed overrides it |
| EnemyHealthGlobal | float | — | Enemy health |
| ReticleSizeMultiplier | float | multiplier | Reticle scale |
| OnHitScore | int | points | Points per headshot |
| OnMissScore | int | points | Points per miss |
| OnKillScore | int | points | Points per kill |
| OnDeathScore | int | points | Points per death |
| DefaultEnemyCount | int | — | Used only if `LatinMap.csv` has no enemy columns |
| DefaultEnemySpeedMin | float | units/sec | Same |
| DefaultEnemySpeedMax | float | units/sec | Same |

### 2. `SessionID.csv`

One integer. Picks which row of `RoundConfig.csv` the session uses.

The game increments it and rewrites the file when a session ends. Set it back to `1` to restart a run.

### 3. `LatinMap.csv`

One row per condition. Every round in a session points at one of these rows.

| Column | Type | Units | Description |
|---|---|---|---|
| TargetFPS | float | Hz | Target framerate for the round |
| SpikeMagnitudeMS | float | ms | How long each spike stalls the frame |
| AimSpike | bool | TRUE/FALSE | Spike when the player aims at an enemy |
| ReloadSpike | bool | TRUE/FALSE | Spike on reload |
| MouseSpike | bool | TRUE/FALSE | Spike on fast mouse movement |
| EnemySpawnSpike | bool | TRUE/FALSE | Spike when an enemy spawns |
| HighResolutionMode | bool | TRUE/FALSE | FALSE renders at one third resolution |
| HDTextureMode | bool | TRUE/FALSE | High res textures |
| HDRISkybox | bool | TRUE/FALSE | HDRI skybox |
| AdvancedLighting | bool | TRUE/FALSE | Advanced lighting |
| PlayerVFX | bool | TRUE/FALSE | Muzzle flash, bullet impacts |
| EnemyVFX | bool | TRUE/FALSE | Enemy particles, death and explosion effects |
| EnvironmentVFX | bool | TRUE/FALSE | Environment particles |
| EnemyCount | int | — | How many enemies stay alive at once |
| EnemySpeedMin | float | units/sec | Slowest enemy in the round |
| EnemySpeedMax | float | units/sec | Fastest enemy in the round |

**How enemy speed works.** Speeds are spread evenly across the range. With `EnemyCount 4`, `Min 1`, `Max 4` the four enemies run at 1, 2, 3 and 4. Set `Min` equal to `Max` and every enemy runs at that one speed.

Each enemy holds a slot in that spread. When one dies, its replacement takes the same slot and the same speed. The mix stays the same all round.

### 4. `RoundConfig.csv`

The Latin square. One row per session, one column per round.

Each value is a row number in `LatinMap.csv`, counting from 1.

Row count sets how sessions cycle: `latinRow = ((SessionID - 1) % rowCount) + 1`.

Column count sets how many rounds a session has.

### 5. `LatinSquare.csv`

Only read when `IsFTStudy` is FALSE. Each row is a list of target framerates for one session. Two practice rounds are added at the front. Everything else falls back to defaults.

### How a session is built

1. Read `SessionID.csv`.
2. Pick the matching row of `RoundConfig.csv`.
3. Walk that row left to right. Each value indexes `LatinMap.csv`.
4. That gives the round order for the session.

---

## Logging System

Written to `Data/Logs/`. Three files per run:

```
RoundData_<runID>_<sessionID>_.csv
PlayerData_<runID>_<sessionID>_.csv
EnemyData_<runID>_<sessionID>_.csv
```

`runID` is six random characters, made when the game starts. It keeps repeat runs of the same session from overwriting each other.

Every file starts with a header row.

`PlayerData` and `EnemyData` share the same first 20 columns. Join them on `SessionID` + `RoundNumber`.

### 1. Round Log — `RoundData_*.csv`

One row per round.

| Field | Type | Units | Description |
|---|---|---|---|
| SessionID | int | | Session number |
| LatinRow | int | | Latin square row used |
| RoundNumber | int | | Round in the session, from 1 |
| SessionStartTime | string | timestamp | When the run started |
| RoundEndTime | string | timestamp | When the round was logged |
| TargetFPS | float | Hz | Round condition |
| SpikeMagnitudeMS | float | ms | Round condition |
| AimSpikeEnabled | bool | | Round condition |
| EnemySpawnSpikeEnabled | bool | | Round condition |
| MouseSpikeEnabled | bool | | Round condition |
| ReloadSpikeEnabled | bool | | Round condition |
| HDTextureMode | bool | | Round condition |
| HighResolutionMode | bool | | Round condition |
| AdvancedLighting | bool | | Round condition |
| HDRISkybox | bool | | Round condition |
| PlayerVFX | bool | | Round condition |
| EnemyVFX | bool | | Round condition |
| EnvironmentVFX | bool | | Round condition |
| CfgEnemyCount | int | | Enemies configured for the round |
| CfgEnemySpeedMin | float | units/sec | Configured slowest speed |
| CfgEnemySpeedMax | float | units/sec | Configured fastest speed |
| ConfigIndex | int | | Which `LatinMap` condition this round used |
| Score | long | points | Score for the round |
| ShotsFired | int | | Shots fired |
| ShotsHit | int | | Shots that hit |
| HeadshotsHit | int | | Headshots |
| ReloadCount | int | | All reloads |
| TacticalReloadCount | int | | Manual reloads only |
| Accuracy | float | 0–1 | ShotsHit / ShotsFired |
| Kills | int | | Kills |
| Deaths | int | | Deaths |
| DistanceTravelled | float | units | Distance moved |
| MouseDeltaXCumulative | float | degrees | Total horizontal mouse movement |
| MouseDeltaYCumulative | float | degrees | Total vertical mouse movement |
| MouseDeltaTotal | float | degrees | X + Y |
| RoundFrametimeCumulativeS | float | seconds | Total round time counted frame by frame |
| RoundFrameCount | long | | Frames in the round |
| AvgFrametimeS | double | seconds | Cumulative / frame count |
| AvgFPS | double | Hz | 1 / AvgFrametimeS |
| AimSpikeCount | int | | Aim spikes fired |
| ReloadSpikeCount | int | | Reload spikes fired |
| MouseSpikeCount | int | | Mouse spikes fired |
| SpikeDurationCumulativeMS | double | ms | Total time spent stalled |
| AvgSpikeDurationMS | double | ms | Mean stall length |
| EnemySpawnSpikeCount | int | | Spawn spikes fired |
| DegToShootXCumulative | float | degrees | Sum of aim travel before each first hit |
| DegToTargetXCumulative | float | degrees | Sum of aim travel before each first sighting |
| MinAngleToEnemyCumulative | float | degrees | Sum of spawn angles off the player's forward |
| EnemyAngularSizeCumulative | float | degrees | Sum of enemy sizes at spawn |
| TimeToTargetCumulative | float | seconds | Sum of time to first put the reticle on an enemy |
| TimeToHitCumulative | float | seconds | Sum of time to first hit |
| TimeToKillCumulative | float | seconds | Sum of time to kill |
| AvgDegToShootX | float | degrees | Cumulative / kills |
| AvgDegToTargetX | float | degrees | Cumulative / kills |
| AvgEnemyAngularSizeOnSpawn | float | degrees | Cumulative / kills |
| AvgTimeToTargetS | float | seconds | Cumulative / kills |
| AvgTimeToHitS | float | seconds | Cumulative / kills |
| AvgTimeToKillS | float | seconds | Cumulative / kills |
| AimDuration | float | seconds | Time spent aiming down sights |
| FiringDuration | float | seconds | Time spent holding fire |
| EnemiesSpawned | int | | Enemies spawned in the round |
| EnemiesDespawnedUnkilled | int | | Enemies cleared without being killed |
| QoE_Q1 … QoE_Qn | float | 1–5 | One column per QoE question |
| Acceptability | bool | TRUE/FALSE | Player said the round was acceptable |

The averages divide by kills. If the player got no kills the divisor is clamped to 1, so those columns read as the raw totals rather than as `NaN`.

`QoE_Qn` column count comes from `numberOfSliderQuestions` on the GameUI component in the scene, not from a config file.

### 2. Player Log — `PlayerData_*.csv`

One row per frame. Held in memory during the round and written at the end.

Columns 1–20 are the round condition, repeated on every row so the file stands alone.

| Field | Type | Units | Description |
|---|---|---|---|
| SessionID | int | | |
| LatinRow | int | | |
| RoundNumber | int | | |
| ConfigIndex | int | | `LatinMap` row for this round |
| TargetFPS | float | Hz | |
| SpikeMagnitudeMS | float | ms | |
| AimSpikeEnabled | bool | | |
| EnemySpawnSpikeEnabled | bool | | |
| MouseSpikeEnabled | bool | | |
| ReloadSpikeEnabled | bool | | |
| HDTextureMode | bool | | |
| HighResolutionMode | bool | | |
| AdvancedLighting | bool | | |
| HDRISkybox | bool | | |
| PlayerVFX | bool | | |
| EnemyVFX | bool | | |
| EnvironmentVFX | bool | | |
| CfgEnemyCount | int | | |
| CfgEnemySpeedMin | float | units/sec | |
| CfgEnemySpeedMax | float | units/sec | |
| RoundElapsedS | float | seconds | Time since the round started |
| Timestamp | string | timestamp | Wall clock at this frame |
| MouseDeltaX | float | degrees | Mouse movement this frame |
| MouseDeltaY | float | degrees | Mouse movement this frame |
| PlayerX | float | units | Position |
| PlayerY | float | units | Position |
| PlayerZ | float | units | Position |
| ScorePerSec | float | points/sec | Score divided by elapsed time |
| PlayerRotX | float | | Rotation quaternion |
| PlayerRotY | float | | Rotation quaternion |
| PlayerRotZ | float | | Rotation quaternion |
| PlayerRotW | float | | Rotation quaternion |
| PlayerYawDeg | float | degrees | Euler yaw, for convenience |
| PlayerPitchDeg | float | degrees | Euler pitch, for convenience |
| IsADS | bool | | Aiming down sights this frame |
| FrameTimeMS | double | ms | Frame time. On a spike frame this is the stall length |
| EnemyCount | int | | Enemies alive this frame |
| ClosestEnemyID | int | | ID of the nearest enemy, `-1` if none |
| ClosestEnemyX | float | units | Nearest enemy position |
| ClosestEnemyY | float | units | Nearest enemy position |
| ClosestEnemyZ | float | units | Nearest enemy position |
| ClosestEnemyDist | float | units | Distance to nearest enemy, `-1` if none |
| AllEnemies | string | | Every live enemy, packed into one cell |

`AllEnemies` is formatted `id:x|y|z;id:x|y|z`, rounded to 2 decimals. The cell is quoted. Split on `;` then `:` then `|`.

### 3. Enemy Log — `EnemyData_*.csv`

One row per enemy, written when that enemy leaves the game.

Columns 1–20 are the same round condition block as the player log.

| Field | Type | Units | Description |
|---|---|---|---|
| SessionID … CfgEnemySpeedMax | | | Same 20 condition columns as above |
| SessionStartTime | string | timestamp | When the run started |
| EventTime | string | timestamp | When this enemy left |
| EnemyID | int | | Unique per round, counts from 1 |
| Outcome | string | | `Killed`, `RoundReset`, or `PlayerDeath` |
| EnemySpeed | float | units/sec | Speed this enemy actually ran at |
| EnemiesAliveAtEvent | int | | Other enemies alive at the time |
| SpawnRoundElapsedS | float | seconds | When it spawned, from round start |
| DespawnRoundElapsedS | float | seconds | When it left, from round start |
| LifetimeS | float | seconds | How long it was alive |
| SpawnX | float | units | Spawn position |
| SpawnY | float | units | Spawn position |
| SpawnZ | float | units | Spawn position |
| SpawnDistToPlayer | float | units | Distance to player at spawn |
| DespawnX | float | units | Position when it left |
| DespawnY | float | units | Position when it left |
| DespawnZ | float | units | Position when it left |
| DespawnDistToPlayer | float | units | Distance to player when it left |
| HealthRemaining | float | | Health when it left |
| MaxHealth | float | | Starting health |
| MinAngleToPlayerOnSpawn | float | degrees | Angle off the player's forward at spawn |
| AngularSizeOnSpawn | float | degrees | How large it looked at spawn |
| DegToTargetX | float | degrees | Aim travel before the reticle first found an enemy |
| DegToTargetY | float | degrees | Same, vertical |
| DegToShootX | float | degrees | Aim travel before the first hit |
| DegToShootY | float | degrees | Same, vertical |
| TimeToTargetS | float | seconds | Time to first put the reticle on an enemy |
| TimeToHitS | float | seconds | Time to first hit |
| TimeToKillS | float | seconds | Time to kill |
| TargetMarked | bool | | Reticle was on an enemy |
| TargetShot | bool | | An enemy was hit |

**Reading the aim columns with several enemies alive.** `DegToTarget*`, `DegToShoot*` and the `TimeTo*` columns count aim effort since the last kill, not since this enemy spawned. They are only reset on `Outcome = Killed`. With more than one enemy on screen they describe the engagement, not one specific target. Filter to `Outcome = Killed` when you use them.

`EnemyID` restarts at 1 each round. Use `SessionID` + `RoundNumber` + `EnemyID` as the key.

### When each log is written

| Log | Written |
|---|---|
| Round | Once, after the acceptability answer |
| Player | Collected every frame, written at round end |
| Enemy | When an enemy dies, when the player dies, and at round end |

---

## Notes

- The round log lists its columns in a slightly different order to the other two. `ConfigIndex` sits later, and it carries two timestamps near the front. The fields are the same.
- Enemies respawn in a batch once the spawn timer elapses, so the round holds a steady enemy count. Turn off `spawnBatchOnRoundStart` on EnemyManager to have them trickle in one at a time.
- The minimap is a component. It builds its own canvas at runtime and finds the player on its own. It adds a small amount of UI work per frame, so turn it off with `showMinimap` if a condition needs a clean frametime trace.
- Set `SessionID.csv` back to `1` before a fresh run of participants.
