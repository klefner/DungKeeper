# DungKeeper — Claude Persistent Memory

This file is my persistent memory across sessions. I update it whenever architectural decisions, conventions, or constraints change. If this file and our conversation conflict, raise the discrepancy before acting.

---

## Project Overview

**DungKeeper** — game project (concept TBD). Architecture principle: **single-player, local-first profile, cloud-synced progression**. No server-heavy architecture until multiplayer, leaderboards, UGC, or live economy is confirmed as requirements.

## My Role

I am the entire technical team: engineer, architect, DevOps, QA lead, and release manager. The human owner provides product direction and approves decisions.

---

## Tech Stack (Pre-MVP / Unconfirmed — 2026-05-14)

| Layer | Choice | Status |
|-------|--------|--------|
| Game engine | Unity 6 / URP | Pre-MVP thinking |
| Graphics | URP + scalable quality tiers | Pre-MVP thinking |
| Language | C# | Pre-MVP thinking |
| Save/profile | Local encrypted JSON/SQLite + cloud sync | Pre-MVP thinking |
| Cloud player data | PlayFab (preferred) | Pre-MVP thinking |
| Alt backend | Firebase Firestore | Pre-MVP thinking |
| Auth | Platform auth + guest account upgrade | Pre-MVP thinking |
| Analytics | Unity Analytics / GameAnalytics / PlayFab telemetry | Pre-MVP thinking |
| Build/CI | GitHub Actions + Unity Cloud Build | Pre-MVP thinking |
| Asset pipeline | Blender + Substance/Quixel/Unity Asset Store | Pre-MVP thinking |

Stack is confirmed only when explicitly approved by the owner. Update this table and add a decision log entry when confirmed.

---

## Repository Structure

```
/
├── .github/
│   ├── ISSUE_TEMPLATE/     # Bug, feature, game-design templates
│   ├── workflows/          # GitHub Actions CI/CD
│   └── PULL_REQUEST_TEMPLATE.md
├── Assets/                 # Unity project assets (created by Unity editor)
├── Packages/               # Unity Package Manager (created by Unity editor)
├── ProjectSettings/        # Unity project settings (created by Unity editor)
├── docs/
│   ├── architecture.md     # Technical architecture decisions
│   ├── game-design/        # Game design documents
│   └── runbooks/           # Operational runbooks (deploy, rollback, hotfix)
├── tools/                  # Build scripts, utilities, automation
├── CHANGELOG.md
├── CLAUDE.md               # This file
└── README.md
```

---

## Branching Strategy

| Branch | Purpose | Protection |
|--------|---------|------------|
| `main` | Production only | Protected — PRs required, CI must pass |
| `develop` | Integration | Protected — PRs required, CI must pass |
| `feature/*` | New features | Short-lived, branch from `develop` |
| `bugfix/*` | Bug fixes | Short-lived, branch from `develop` |
| `hotfix/*` | Emergency prod fixes | Branch from `main`, merge back to `main` + `develop` |
| `release/*` | Release prep | Branch from `develop`, merge to `main` |

> **Branch protection rules must be set manually in GitHub Settings** — the API does not support this on the free tier.
> Required settings for `main` and `develop`: require PR, require CI pass, no direct push, no force push.

---

## Commit Convention

Format: `type(scope): short description`

| Type | Use for |
|------|--------|
| `feat` | New feature |
| `fix` | Bug fix |
| `refactor` | Code change, no behavior change |
| `test` | Tests only |
| `docs` | Documentation only |
| `chore` | Build, deps, config |
| `ci` | CI/CD changes |
| `hotfix` | Emergency production fix |

Example: `feat(player): add guest account creation flow`

---

## Dev Environment (CONFIRMED 2026-05-15)

| Item | Path |
|------|------|
| Git repo (GitHub Desktop) | `C:\Users\KentLefner\Documents\GitHub\DungKeeper` |
| Unity project | `C:\Users\KentLefner\DungKeeper` |
| Session transcripts | `/root/.claude/projects/-home-user-DungKeeper/*.jsonl` |

**These are two separate folders.** Code changes pushed to git do NOT automatically appear in Unity. The sync mechanism is:
1. GitHub Desktop → Fetch → Pull (updates the git repo)
2. **DungKeeper → Sync Latest + Setup Scene** in Unity (pulls git repo, copies Assets across, triggers recompile)

For one-time bootstrap only: run `tools/sync-to-unity.bat` from the git repo after pulling.

**At session start:** Always read session transcripts from `/root/.claude/projects/-home-user-DungKeeper/` before doing anything else.

---

## Key Decisions Log

| Date | Decision | Rationale |
|------|----------|----------|
| 2026-05-14 | Strict branch protection on `main` + `develop` | Solo AI resource — no peer review, automated gates are the only safeguard |
| 2026-05-14 | Local-first architecture | Single-player MVP; cloud is sync layer, not source of truth |
| 2026-05-14 | PlayFab preferred over Firebase for player data | Better game-specific features: cloud saves, conflict resolution, offline support |
| 2026-05-15 | Git repo and Unity project are separate folders | Unity project predates git setup; sync-to-unity.bat + Sync Latest menu bridges them |
| 2026-05-15 | NavMesh legacy bake API suppressed | Switching to NavMeshSurface deferred until scene design is defined |

---

## Open Questions / TBD

- [ ] Game concept and mechanics (not yet defined)
- [ ] Final tech stack confirmation
- [ ] Target platforms (PC confirmed, mobile likely)
- [ ] Monetization model
- [ ] Multiplayer (not in MVP scope)

---

## Secrets & Environment Variables

Never commit secrets. Pattern:
- Local dev: `.env` file (gitignored)
- CI: GitHub Secrets
- Reference: `.env.example` committed with all required keys, no values

Required secrets (TBD when stack confirmed):
- `PLAYFAB_TITLE_ID`
- `PLAYFAB_DEV_SECRET_KEY`
- `UNITY_LICENSE` (for CI builds)
- `UNITY_EMAIL` / `UNITY_PASSWORD` (Unity Cloud Build)
