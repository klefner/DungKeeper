# DungKeeper

**Run your empire with fear, efficiency, and the occasional slap.**

---

## What Is DungKeeper?

DungKeeper is an evil dungeon management sim set inside a sprawling interdimensional dark corporation — think malevolent middle management meets bureaucratic nightmare, with tentacles. You are the Overlord: a mid-tier executive of an ancient evil conglomerate that harvests dimensional resources by running extraction dungeons staffed by an assortment of terrified, barely-functional underlings. Your dungeon is both factory floor and fortress. You build rooms, assign labor, manage morale through a finely calibrated cocktail of incentives and physical violence, and defend your operation against do-gooder hero squads who keep filing breach-of-evil-code complaints in person — with swords.

This is not a Dungeon Keeper clone. The tone is satirical dark comedy grounded in corporate dysfunction: performance reviews delivered at the end of a gauntlet, HR memos written in blood, workers clocking overtime under threat of sacrifice. The mechanical core is about systems in tension — productivity vs. morale, fear vs. loyalty, short-term output vs. long-term stability. Every decision compounds or degrades. The dungeon is alive, anxious, and one bad slap away from a full labor revolt.

---

## Current Status

**Pre-MVP / Vertical Slice in development**

Core systems are being implemented and tested. The project is not yet in a playable end-to-end state. See MVP Scope below for what is targeted for the vertical slice.

---

## Getting Started

### Requirements

| Tool | Version |
|------|---------|
| Unity Hub | Latest |
| Unity Editor | 6000.0.x or later (Unity 6) |
| Universal Render Pipeline (URP) | Included via Package Manager |
| NavMesh Components | Included via Package Manager |
| TextMeshPro | Included via Package Manager |

> All package dependencies are declared in `Packages/manifest.json` and are resolved automatically by Unity Package Manager on first open.

### Setup Steps

1. **Clone the repository**
   ```bash
   git clone https://github.com/your-org/DungKeeper.git
   cd DungKeeper
   ```

2. **Open Unity Hub** and click **Add project from disk**.

3. **Select the repository root** (`DungKeeper/`). Unity Hub will detect the correct editor version from `ProjectSettings/ProjectVersion.txt` and prompt you to install it if needed.

4. **Open the project** in Unity 6. Allow Unity to import all assets and compile scripts on first launch — this may take several minutes.

5. **Open the main scene**: `Assets/_DungKeeper/Scenes/MainScene.unity`

6. **Press Play** in the Unity Editor to run the vertical slice.

> **Note:** If you see errors about missing packages on first open, go to **Window → Package Manager** and allow all packages to resolve before entering Play mode.

---

## MVP Scope (Vertical Slice)

The vertical slice targets one complete playable loop demonstrating all core systems.

| System | Status |
|--------|--------|
| Production Room (resource generation) | In development |
| Worker units — Groveling Peon | In development |
| Guard units — Fury Grunt | In development |
| Researcher units — Hex Intern | In development |
| Slap mechanic (click-hold to charge, release to swing) | In development |
| Morale and loyalty tracking per unit | In development |
| Basic threat system (escalating hero invasion waves) | In development |
| Save / Load (JSON + XOR obfuscation) | In development |
| EditMode unit tests for core simulation systems | In development |

Features explicitly **outside** vertical slice scope: multiplayer, leaderboards, cloud sync, meta-progression, procedural dungeon generation.

---

## Architecture Summary

DungKeeper uses a strict three-layer architecture to keep game logic testable and Unity-decoupled.

```
┌───────────────────────────────────────────────────┐
│           Unity Presentation Layer                │
│   MonoBehaviours — reads sim state, drives VFX,   │
│   audio, UI, input. No game logic lives here.     │
├───────────────────────────────────────────────────┤
│           Core Simulation Layer                   │
│   Pure C# — all game rules, state machines,       │
│   resource economy, event bus. Zero Unity deps.   │
│   Fully testable in EditMode / headless.          │
├───────────────────────────────────────────────────┤
│              Data Layer                           │
│   ScriptableObjects define all unit/room/slap     │
│   reaction config. SaveData/SaveSystem handles    │
│   persistence. Data flows down; events flow up.   │
└───────────────────────────────────────────────────┘
```

See `docs/architecture.md` for the full technical specification.

---

## Key Controls

| Input | Action |
|-------|--------|
| Left Click (hold) | Charge slap — charge indicator appears near cursor |
| Left Click (release) | Release slap — force scales with charge duration |
| Right Mouse Button | Enter build mode / place rooms |
| ESC | Pause / open pause menu |
| F5 | Quick save |

---

## Branch Strategy

| Branch | Purpose |
|--------|---------|
| `main` | Production only — PRs required, CI must pass |
| `develop` | Integration branch — PRs required |
| `feature/*` | New features — branch from `develop` |
| `bugfix/*` | Bug fixes — branch from `develop` |
| `hotfix/*` | Emergency prod fixes — branch from `main`, merge back to both |
| `release/*` | Release prep — branch from `develop`, merge to `main` |

Commit format: `type(scope): short description`
Example: `feat(slap): add charge hold visual indicator`

See `CLAUDE.md` for the full branching strategy, commit types, and decisions log.

---

## Contributing

This project is currently in closed pre-MVP development. Branch protection rules on `main` and `develop` enforce PR requirements and CI gates — no direct pushes are permitted to either branch.

---

## License

TBD — License to be determined before public release.
