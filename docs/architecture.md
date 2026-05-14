# Architecture

> This document evolves as decisions are confirmed. See `CLAUDE.md` decisions log for history.

## Core Principle

Single-player game, local-first profile, cloud-synced progression. The local device is the source of truth. Cloud is a sync and backup layer — not required to play.

## Data Flow

```
Player Action
    │
    ▼
Local Game State (RAM)
    │
    ▼
Local Persistence (SQLite / encrypted JSON)
    │
    ▼  (on sync trigger: session end, milestone, manual save)
Cloud Sync (PlayFab)
```

## Layer Responsibilities

| Layer | Responsibility |
|-------|---------------|
| Game engine (Unity 6 / URP) | Rendering, input, physics, scene management |
| Game logic (C#) | Rules, progression, economy, creature behavior |
| Local persistence | Save/load, encryption, schema migration |
| Cloud sync | Cross-device profile, conflict resolution |
| Auth | Guest → platform account upgrade path |
| Analytics | Session, economy, progression, retention events |

## Not in Scope (MVP)

- Real-time multiplayer
- Server-authoritative anti-cheat
- Live economy balancing
- User-generated content
- Competitive leaderboards

Adding any of these requires a dedicated architecture review before implementation.
