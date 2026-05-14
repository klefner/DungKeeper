# Changelog

All notable changes to DungKeeper will be documented here.

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versioning follows [Semantic Versioning](https://semver.org/).

---

## [Unreleased]

### Pre-MVP — Initial Project Setup and Vertical Slice Implementation

#### Added

- Unity project structure under `Assets/_DungKeeper/` with scene, prefab, data, and script directories
- **Core simulation layer** (pure C#, zero Unity dependencies, fully EditMode testable):
  - `UnitData` — runtime state container for all per-unit stats (health, morale, loyalty, anger, fear, slap accumulation, personality flags)
  - `UnitStateMachine` — finite state machine governing IDLE / WORKING / DISTRESSED / COWERING / STRIKING / IMPRISONED / DEAD transitions
  - `SlapSystem` — processes slap inputs, applies force-tier stat deltas, checks accumulation thresholds, publishes `UnitSlappedEvent`
  - `MoraleSystem` — tick-driven morale and loyalty decay/recovery; propagates ambient Fear; checks strike thresholds
  - `TaskSystem` — priority-queue unit-to-room assignment; validates eligibility; fires `TaskAssignedEvent` / `TaskCompleteEvent`
  - `ResourceSystem` — tracks Gold, Essence, Fear; applies per-tick room income; fires `ResourceChangedEvent`
  - `CombatSystem` — resolves hero-vs-dungeon encounters; applies damage to rooms and units; triggers retreat/death outcomes
  - `StrikeSystem` — monitors labor unrest accumulation; escalates through warning → passive strike → active rebellion
  - `EventBus` — typed static publish/subscribe dispatcher decoupling simulation from presentation
  - `GameConstants` — single source of truth for all balance constants
- **Unity presentation layer** (MonoBehaviours, reads simulation state only):
  - `GameManager` — scene coordinator; owns all simulation instances; drives tick loop; routes input to systems
  - `UnitController` — per-unit MonoBehaviour; drives animation, NavMesh pathfinding, and visual reactions via event subscription
  - `SlapController` — reads mouse input; manages charge accumulation; renders charge indicator; calls `GameManager.SlapUnit()`
  - `RoomManager` — manages dungeon grid; validates placement; instantiates `RoomController` prefabs
  - `RoomController` — per-room MonoBehaviour; reflects damage state via material swaps and particle emitters
  - `InvaderController` — controls hero invasion units; reads `CombatSystem` pathfinding decisions; reports hit events
  - `CameraShake` — coroutine-based camera displacement triggered by `UnitSlappedEvent`, scaled by force tier
- **Data layer:**
  - `UnitTypeSO` — ScriptableObject defining base stats, personality, work speed, and audio/animation references per unit type
  - `RoomTypeSO` — ScriptableObject defining grid size, output type, rate, cost, and adjacency requirements per room type
  - `SlapReactionSO` — ScriptableObject defining personality × force tier × unit state → stat deltas and feedback message
  - `SaveData` — serializable snapshot of full game state (resources, all `UnitData`, room grid, wave index, elapsed time)
  - `SaveSystem` — JSON serialization with XOR obfuscation; reads/writes to `Application.persistentDataPath/save.dat`; schema version validation
- **UI system** (`UIManager`):
  - HUD with Gold, Essence, and Fear resource meters
  - Unit inspect panel showing selected unit stats
  - Floating slap feedback text (force-tier and personality-driven messages)
  - Strike warning overlay and threat level escalation notifications
- **Audio system** (`AudioManager`):
  - Layered slap audio: swing whoosh, impact thud, and unit vocalization clips selected by force tier and personality
  - Event-driven one-shot and ambient audio playback
- **Save/load system:** quick save (F5), auto-save every 5 minutes, save-on-quit; full state restoration on load
- **EditMode unit tests** for core simulation systems: `SlapSystem`, `MoraleSystem`, `TaskSystem`, `ResourceSystem`, `StrikeSystem`, `UnitStateMachine`
- **Architecture documentation** (`docs/architecture.md`) — full three-layer architecture specification with EventBus catalog, state machine diagram, task assignment flow, and save/load flow
- **Game design documents:**
  - `docs/game-design/core-loop.md` — core game loop, resource economy, unit roster, player action tradeoffs, threat system, win/loss conditions
  - `docs/game-design/slap-system.md` — slap mechanic specification including input, force tiers, visual/audio feedback, response table, tolerance accumulation, and balance targets
