# DungKeeper — Technical Architecture

**Date:** 2026-05-14
**Status:** Pre-MVP / Vertical Slice
**Supersedes:** Initial stub in this file (2026-05-14)

---

## Overview

DungKeeper is built on a strict three-layer architecture. The central design constraint is that all game logic must be testable without running the Unity engine. This means the Unity API never leaks into simulation code, and simulation state never depends on MonoBehaviour lifecycle methods.

```
┌───────────────────────────────────────────────────────────┐
│                  Unity Presentation Layer                 │
│  MonoBehaviours, coroutines, particle systems, audio,     │
│  NavMesh agents, UI Canvas. READS simulation state.       │
│  NEVER owns game rules or authoritative state.            │
├───────────────────────────────────────────────────────────┤
│                  Core Simulation Layer                    │
│  Pure C# classes and structs. All game rules, state       │
│  machines, economy, event bus. ZERO Unity API deps.       │
│  Fully runnable in NUnit EditMode tests and headless CLI. │
├───────────────────────────────────────────────────────────┤
│                     Data Layer                            │
│  ScriptableObjects (config), SaveData (runtime snapshot), │
│  SaveSystem (serialize/deserialize). Configuration flows  │
│  down into simulation; events flow up to presentation.    │
└───────────────────────────────────────────────────────────┘
```

Communication between layers is strictly one-directional except for the EventBus, which is the only sanctioned upward communication channel from simulation to presentation.

---

## Core Simulation Layer

### Purpose

All game logic lives here. This layer has zero dependencies on `UnityEngine`, `UnityEditor`, or any Unity-specific namespace. It can be compiled and tested in any .NET environment.

### Files and Responsibilities

| File | Responsibility |
|------|---------------|
| `GameConstants.cs` | All tunable numeric constants (slap force tiers, morale decay rates, resource tick rates, threat escalation thresholds). Single source of truth for balance values. |
| `EventBus.cs` | Static publish/subscribe event dispatcher. Typed event objects. Decouples simulation from presentation. |
| `UnitData.cs` | Plain data class representing a single unit's full runtime state: health, morale, loyalty, anger, fear, current task, slap accumulation, personality flags. |
| `UnitStateMachine.cs` | Finite state machine governing unit behavior transitions. Consumes `UnitData`, fires state-change events. |
| `SlapSystem.cs` | Processes slap events: calculates force tier, applies stat deltas to `UnitData`, checks for escalation conditions, publishes `UnitSlappedEvent`. |
| `MoraleSystem.cs` | Tick-driven system that decays/recovers morale and loyalty across all units, propagates ambient fear pressure, checks for strike/revolt thresholds. |
| `TaskSystem.cs` | Assigns units to rooms and tasks based on priority queue; validates unit state before assignment; fires `TaskAssignedEvent` and `TaskCompleteEvent`. |
| `ResourceSystem.cs` | Tracks Gold, Essence, and Fear globally; applies per-tick income from rooms; validates spend operations; fires `ResourceChangedEvent`. |
| `CombatSystem.cs` | Resolves hero-vs-dungeon encounters: calculates damage, applies results to room and unit state, triggers retreat or death outcomes. |
| `StrikeSystem.cs` | Monitors labor unrest across the unit pool; escalates through warning → passive strike → active rebellion; can call back to `TaskSystem` and `MoraleSystem`. |

### Design Goals

- All systems are instantiated and ticked by `GameManager` (presentation layer) — they do not self-tick.
- Systems communicate only through `EventBus` or direct method calls within the same layer.
- No coroutines, no `MonoBehaviour`, no `Time.deltaTime` — time is passed in as a `float dt` parameter.
- All public methods are deterministic given the same inputs (no hidden global state beyond the EventBus subscriber list).

---

## Unity Presentation Layer

### Purpose

MonoBehaviours that read simulation state on each frame or in response to events, and reflect that state visually and aurally. This layer owns rendering, animation, audio, NavMesh pathfinding, input, and UI. It never makes authoritative decisions about game state — it delegates to the simulation layer.

### Core MonoBehaviours

| Class | Responsibility |
|-------|---------------|
| `GameManager.cs` | Scene coordinator. Owns instances of all simulation systems. Drives the simulation tick each `Update`. Routes input events from controllers to the appropriate simulation system. Handles scene lifecycle (init, pause, quit). |
| `UnitController.cs` | Attached to each unit GameObject. Subscribes to `UnitSlappedEvent`, `UnitStateChangedEvent`, and `TaskAssignedEvent` for its unit. Drives animation state machine, NavMesh destination, and visual reaction overlays. |
| `SlapController.cs` | Reads mouse input. Manages charge accumulation (hold duration → force value). On release, calls `GameManager.SlapUnit()`. Renders the charge indicator UI element near the cursor. |
| `RoomManager.cs` | Manages the room grid: tracks which cells are occupied, validates placement, instantiates `RoomController` prefabs. Maintains the `RoomData` registry used by `TaskSystem`. |
| `RoomController.cs` | Attached to each room GameObject. Reflects room state (active, damaged, on fire) through material swaps and particle emitters. Subscribes to room-specific events. |
| `InvaderController.cs` | Controls hero invasion units. Reads pathfinding targets from `CombatSystem` decisions. Plays attack and death animations. Reports collision and damage events back to `CombatSystem`. |

### UI

| Class | Responsibility |
|-------|---------------|
| `UIManager.cs` | Drives the HUD (Gold, Essence, Fear meters), the unit inspect panel (stats readout for selected unit), slap feedback labels (floating text on hit), and the strike/threat warning overlays. Subscribes to `ResourceChangedEvent`, `UnitSlappedEvent`, `StrikeWarningEvent`, `ThreatLevelChangedEvent`. |
| `CameraShake.cs` | Triggered by `UnitSlappedEvent`. Shakes the camera by a displacement proportional to slap force tier. Implemented as a timed decay coroutine. |

### Audio

| Class | Responsibility |
|-------|---------------|
| `AudioManager.cs` | Singleton. Plays one-shot and looping audio clips by key. Subscribes to `UnitSlappedEvent` (plays whoosh + impact + unit vocalization), `UnitDiedEvent` (death sound), `ThreatLevelChangedEvent` (escalation sting). Clip selection considers force tier and unit type. |

---

## Data Layer

### ScriptableObjects

ScriptableObjects define the configuration for all authored content. They are referenced by `UnitController`, `RoomController`, and the simulation systems through wrappers that extract plain-data structs before passing into the simulation layer.

| Asset Type | Fields | Used By |
|-----------|--------|---------|
| `UnitTypeSO` | Display name, base health, base morale, base loyalty, base anger, personality flags (Cowardly / Stubborn / Eager), work speed multiplier, vocalization clip references, animation controller | `GameManager` (unit spawn), `UnitController` |
| `RoomTypeSO` | Display name, size (width × height in grid cells), resource output type, output rate per tick, max occupancy, construction cost, required room adjacency | `RoomManager` (placement validation), `ResourceSystem` |
| `SlapReactionSO` | Personality type, force tier, unit state at time of slap → response enum, fear gain, anger gain, loyalty loss delta, feedback message string | `SlapSystem` (reaction lookup) |

### Save System

| Class | Responsibility |
|-------|---------------|
| `SaveData.cs` | Plain serializable class. Captures: resource totals (Gold, Essence, Fear), all `UnitData` instances, room grid state (type, position, health per room), current wave index, elapsed play time, player settings. |
| `SaveSystem.cs` | Serializes `SaveData` to JSON using `JsonUtility`. Applies XOR obfuscation with a fixed key before writing to disk (not encryption — obfuscation to deter casual hex editing). Writes to `Application.persistentDataPath/save.dat`. Reads and deobfuscates on load. Validates schema version field; on mismatch, attempts migration or falls back to new game. |

**Save trigger points:** F5 quick save, on pause menu "Save & Quit", auto-save every 5 minutes (configurable in `GameConstants`), on wave completion.

**What is NOT saved:** audio settings (stored separately in `PlayerPrefs`), transient animation and particle state, in-flight hero positions during active combat (combat always restarts cleanly on load).

---

## EventBus Pattern

`EventBus` is a static class with a typed publish/subscribe interface. Subscribers register with a callback; publishers fire events by type. There is no dependency on Unity's messaging system.

```csharp
// Publishing (from simulation layer)
EventBus.Publish(new UnitSlappedEvent { UnitId = id, ForceTier = tier, ResponseType = response });

// Subscribing (from presentation layer)
EventBus.Subscribe<UnitSlappedEvent>(OnUnitSlapped);

// Unsubscribing (on MonoBehaviour OnDestroy)
EventBus.Unsubscribe<UnitSlappedEvent>(OnUnitSlapped);
```

### Event Catalog

| Event | Produced By | Consumed By |
|-------|-------------|-------------|
| `UnitSlappedEvent` | `SlapSystem` | `UnitController`, `AudioManager`, `UIManager`, `CameraShake` |
| `UnitStateChangedEvent` | `UnitStateMachine` | `UnitController`, `UIManager` |
| `UnitDiedEvent` | `UnitStateMachine` | `UnitController`, `AudioManager`, `UIManager`, `TaskSystem` |
| `TaskAssignedEvent` | `TaskSystem` | `UnitController` |
| `TaskCompleteEvent` | `TaskSystem` | `ResourceSystem`, `UIManager` |
| `ResourceChangedEvent` | `ResourceSystem` | `UIManager` |
| `RoomDamagedEvent` | `CombatSystem` | `RoomController`, `UIManager` |
| `RoomDestroyedEvent` | `CombatSystem` | `RoomManager`, `TaskSystem`, `UIManager` |
| `StrikeWarningEvent` | `StrikeSystem` | `UIManager`, `AudioManager` |
| `StrikeStartedEvent` | `StrikeSystem` | `UIManager`, `TaskSystem`, `AudioManager` |
| `ThreatLevelChangedEvent` | `GameManager` (wave controller) | `UIManager`, `AudioManager` |
| `InvasionStartedEvent` | `GameManager` (wave controller) | `InvaderController`, `UIManager`, `AudioManager` |

---

## Slap Mechanic Architecture

The slap mechanic crosses all three layers with a clear ownership boundary at each step.

```
[SlapController]          ← Input layer: reads mouse, manages charge timer
       │
       │  calls GameManager.SlapUnit(unitId, forceValue)
       ▼
[GameManager]             ← Routes to correct simulation system
       │
       │  calls SlapSystem.ProcessSlap(unitData, forceValue)
       ▼
[SlapSystem]              ← Simulation: computes force tier, looks up
       │                    SlapReactionSO, applies stat deltas to UnitData,
       │                    updates CurrentSlapAccumulation, checks escalation
       │
       │  publishes UnitSlappedEvent { unitId, forceTier, responseType, message }
       ▼
[EventBus]
       │
       ├──► [UnitController]   plays reaction animation, stumble physics
       ├──► [AudioManager]     plays whoosh + impact + vocalization clip
       ├──► [UIManager]        shows floating feedback message, updates stat bars
       └──► [CameraShake]      shakes camera proportional to forceTier
```

---

## Unit State Machine

All state transitions are managed by `UnitStateMachine`. The presentation layer (`UnitController`) listens for `UnitStateChangedEvent` and mirrors the state into the animation controller.

```
                    ┌─────────────────────────────┐
                    │                             │
          ┌─────────▼──────┐             ┌────────▼────────┐
          │      IDLE      │◄────────────│    WORKING      │
          └────────┬───────┘  task done  └────────┬────────┘
                   │                              │
          task     │                              │  slapped (medium+)
          assigned │                              │  OR morale < threshold
                   │                              ▼
                   │                    ┌─────────────────┐
                   └───────────────────►│   DISTRESSED    │
                                        └────────┬────────┘
                                                 │
                                    morale       │        morale
                                    recovers     │        collapses
                                                 │
                              ┌──────────────────┼──────────────────┐
                              │                  │                  │
                    ┌─────────▼──────┐  ┌────────▼────────┐  ┌─────▼──────────┐
                    │    COWERING    │  │    STRIKING     │  │   IMPRISONED   │
                    │  (fear spike)  │  │  (labor unrest) │  │  (player jails)│
                    └────────┬───────┘  └────────┬────────┘  └────────┬───────┘
                             │                   │                    │
                    fear     │         strike     │          released  │
                    decays   │         resolved   │          or dies   │
                             │         or crushed │                   │
                             └──────────┬─────────┘                   │
                                        │                             │
                                        ▼                             │
                              ┌─────────────────┐                     │
                              │      DEAD       │◄────────────────────┘
                              └─────────────────┘
                                   (terminal)

```

**State definitions:**

| State | Unit behavior | Task assignment allowed |
|-------|--------------|------------------------|
| IDLE | Standing, idle animation plays | Yes |
| WORKING | Moving to room, performing task animation | No (already assigned) |
| DISTRESSED | Slow movement, distress animation overlay | Yes, but output penalty applies |
| COWERING | Rooted in place, cowering animation | No |
| STRIKING | Refuses tasks, paces or sits, can influence nearby units | No |
| IMPRISONED | Locked in Detention Cell, cannot be assigned | No |
| DEAD | Ragdoll/corpse, removed from unit pool next tick | No |

---

## Task Assignment Flow

```
[Player places or upgrades room]
       │
       ▼
[RoomManager] registers room as open task slot in RoomData registry
       │
       ▼
[TaskSystem.Tick()] runs priority evaluation each simulation tick
       │
       ├─ For each open slot: find idle units eligible to fill it
       ├─ Score candidates: proximity, skill match, morale ≥ threshold, not COWERING/STRIKING
       ├─ Assign highest-scoring unit
       │
       ▼
[TaskSystem] calls UnitStateMachine.TransitionTo(WORKING)
       │
       ▼
[EventBus] publishes TaskAssignedEvent { unitId, roomId, taskType }
       │
       ▼
[UnitController] receives event, sets NavMesh destination to room position
       │
       ▼
[Unit arrives at room] → UnitController notifies TaskSystem
       │
       ▼
[TaskSystem] begins task timer; on completion publishes TaskCompleteEvent
       │
       ▼
[ResourceSystem] receives TaskCompleteEvent, applies resource output
       │
       ▼
[TaskSystem] returns unit to IDLE, process repeats
```

---

## Save / Load Flow

```
[Player triggers save: F5, menu, or auto-save timer]
       │
       ▼
[GameManager.Save()]
       │  collects current state from all simulation systems
       ▼
[SaveData] snapshot constructed (resources, all UnitData[], room grid, wave index)
       │
       ▼
[SaveSystem.Write(saveData)]
       │  JsonUtility.ToJson → string
       │  XOR obfuscate bytes with fixed key
       │  Write to Application.persistentDataPath/save.dat
       ▼
[Done]

[Player loads game]
       │
       ▼
[SaveSystem.Read()]
       │  Read bytes from disk
       │  XOR deobfuscate
       │  JsonUtility.FromJson → SaveData
       │  Validate schema version
       ▼
[GameManager.Load(saveData)]
       │  Reconstruct simulation system state
       │  Spawn unit and room GameObjects to match saved state
       │  Restore resource totals
       ▼
[Game resumes]
```

---

## Extension Points

### Adding a New Unit Type

1. Create a new `UnitTypeSO` asset under `Assets/_DungKeeper/Data/Units/`.
2. Set personality flags, stats, and clip references. No code changes required.
3. Add `SlapReactionSO` entries for the new personality type if it needs unique slap responses.
4. Add any state-specific animations to a new Animator Controller and reference it from the SO.

### Adding a New Room Type

1. Create a new `RoomTypeSO` asset under `Assets/_DungKeeper/Data/Rooms/`.
2. Set size, output type, rate, cost. No code changes required for basic resource-producing rooms.
3. For rooms with unique mechanics (e.g., Torture Chamber, Ritual Altar), subclass `RoomController` and override the relevant virtual methods.

### Adding a New Slap Reaction

1. Create a new `SlapReactionSO` asset under `Assets/_DungKeeper/Data/SlapReactions/`.
2. Set personality type, force tier, unit state, and all stat delta fields.
3. `SlapSystem` queries all `SlapReactionSO` assets at init via `Resources.LoadAll`; the new reaction is picked up automatically.

### Adding a New Event Type

1. Define a new plain struct or class in `EventBus.cs` (or a dedicated `Events/` folder).
2. Any simulation system can publish it; any presentation class can subscribe. No registration step required.

---

## Performance Notes

| Target | Value |
|--------|-------|
| Max simultaneous units | 50 |
| Grid dimensions | 20 × 20 cells |
| Simulation tick rate | 10 Hz (every 0.1 s, configurable in `GameConstants`) |
| Rendering frame target | 60 fps on mid-tier hardware |
| Thread model | Single-threaded; all simulation runs on main thread |

The simulation tick is deliberately decoupled from the render frame rate. `GameManager.Update()` accumulates delta time and fires simulation ticks at the configured rate. This allows the simulation to be deterministic across different frame rates and simplifies save/load state reconstruction.

NavMesh pathfinding uses Unity's built-in async `NavMeshAgent`. Pathfinding results are consumed in `UnitController.Update()` and do not feed back into simulation state directly.

At 50 units, all O(n²) operations (e.g., proximity scoring in task assignment) remain well within budget. If unit counts exceed 100, task assignment should be moved to a spatial grid lookup.
