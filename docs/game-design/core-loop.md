# DungKeeper — Core Game Loop

**Date:** 2026-05-14
**Status:** Pre-MVP / Vertical Slice
**Document type:** Game Design Document

---

## The Fantasy

You are not a villain. You are management. Specifically, you are a mid-tier Extraction Supervisor at Abyssal Holdings Group, a pan-dimensional conglomerate that harvests mortal-world resources through a network of subterranean operations. Your dungeon is a regional office. Your underlings are employees — technically — though the HR policy document they were handed on onboarding was written in a language that hasn't been spoken since the Third Age, and the benefits package consists of "continued existence."

The fantasy is not raw evil for its own sake. It is the fantasy of being the worst boss in any dimension: feared, obeyed, occasionally respected, and perpetually one bad decision away from losing control of an organization that runs entirely on terror. You have power over your workers, but that power has costs. Slap too much, and they break. Don't slap enough, and they slack. Feed them and they get comfortable. Sacrifice them and your labor pool shrinks. Every lever you pull has consequences, and the dungeon remembers.

---

## Emotional Goals

By the end of a play session, the player should feel:

- **Powerful** — direct control over individual units, visible reactions to every action, rooms that grow and change the map
- **Like a genuinely bad boss** — the mechanics reward recognizing that you are the problem; the systems satirize real management dysfunction
- **Amused by systemic consequences** — when a slap-happy strategy causes a labor strike during a hero invasion, that outcome should read as darkly funny, not merely punishing
- **Invested in their specific workers** — named units with visible personality traits create attachment even as the game mechanics encourage treating them as resources

---

## The Core Loop

```
BUILD → SPAWN → ASSIGN → SLAP → CONSEQUENCE → ADAPT → ESCALATE → (repeat)
```

This loop runs at multiple timescales simultaneously. The minute-to-minute loop is tactical (individual slaps, room placement, task assignment). The session-level loop is strategic (wave escalation, resource pressure, unit pool management).

---

## Loop Phase Breakdown

### 1. BUILD

The player places rooms on the dungeon grid. Each room serves a function in the resource economy: Production Rooms generate Gold, Research Chambers generate Essence, Guard Barracks house Fury Grunts, Detention Cells hold imprisoned units. Rooms cost Gold to place and require open adjacent grid cells. The dungeon is spatially constrained — every room placed is a tradeoff in coverage and defense depth.

**Key tension:** Production rooms generate income but attract hero attention. Defense rooms protect the dungeon but consume space and staffing. The player must balance expansion against exposure.

### 2. SPAWN

Units are hired (spawned) for a Gold cost at the Entrance Chamber. Units have procedurally selected names and a personality type (Cowardly, Stubborn, or Eager) that is fixed at spawn and governs how they respond to slaps, threats, and rewards throughout their lives. The player has no control over personality assignment — it is revealed through interaction.

**Key tension:** Spawning costs Gold upfront. A larger unit pool gives more redundancy but increases morale management complexity. Spawning too few units creates a brittle workforce. Personality type is unknown at spawn time; the player learns it by watching responses.

### 3. ASSIGN

Units are assigned to rooms through the Task System. The player can direct this manually or let the automatic assignment system fill open slots by proximity and availability. Different room types prefer different unit types: Hex Interns work best in Research Chambers, Groveling Peons in Production Rooms, Fury Grunts in Guard Barracks.

**Key tension:** Optimal assignment requires attention. Automatic assignment is convenient but rarely optimal. Units in the wrong room type work slower and build frustration faster.

### 4. SLAP

The player's primary direct interaction with units. Click-hold charges the slap; release triggers the swing. Slap force scales with charge duration. A slap can break a unit out of idle, restore flagging output, punish insubordination, or suppress early signs of unrest.

In the short term, slapping works. Units in DISTRESSED or IDLE states snap to attention. Output spikes. Fear rises. This is the seductive part of the mechanic.

**Key tension:** Every slap degrades loyalty and increases anger. High-anger, low-loyalty units are recruiting grounds for the StrikeSystem. The short-term fix is a long-term liability.

### 5. CONSEQUENCE

Consequences arrive with a delay. A unit slapped repeatedly over five minutes doesn't rebel immediately — the system accumulates Slap Accumulation, morale decay, and anger, then crosses thresholds in sequence: first Distressed, then a Strike Warning, then a Strike or Rebellion if left unaddressed. This delay is intentional: the gap between cause and consequence is where management mistakes live.

Consequences include:
- Units entering COWERING state (frozen, useless, but not yet rebellious)
- STRIKING state (passive labor refusal, contagious to adjacent units)
- Active Revolt (units attack rooms, destroy equipment, may join invading heroes)
- Unit death (from excessive slap accumulation + zero morale)
- Resource output collapse (rooms left unstaffed during unrest)

### 6. ADAPT

The player must respond to consequences by using the full toolkit: slap less, reward more, feed units to restore morale, imprison instigators to contain strike spread, sacrifice troublemakers to free up Essence while removing the unrest nucleus. Each response has its own cost.

The adapt phase is where the game's satirical core lives — the player is forced to reckon with the consequences of their management style. The systems do not moralize, but they do account.

### 7. ESCALATE

Every five minutes, a new hero invasion wave arrives. Waves escalate in difficulty: more heroes, better equipped, targeting higher-value rooms. A poorly managed dungeon (damaged rooms, low unit count, units in rebellion) is exponentially more vulnerable to invasion. The threat system creates a hard external clock that punishes over-extended internal crises.

Wave completion rewards Gold and Essence bonuses, allowing the player to rebuild and expand before the next wave.

---

## Resource Economy

| Resource | Symbol | Source | Sink | Role |
|----------|--------|--------|------|------|
| Gold | ◈ | Production Rooms (via Peon labor), wave completion bonuses | Room construction, unit hiring, feeding units, rewards | Primary currency; everything costs Gold |
| Essence | ⬡ | Research Chambers (via Hex Intern labor), unit sacrifice | Room upgrades, special abilities (future), ritual mechanics | Upgrade currency; enables advanced options |
| Fear | ☠ | Ambient: high-slap environment, invader presence, executions | Morale decay in nearby units; inverse relationship with loyalty | Systemic pressure metric; high Fear boosts short-term output but accelerates morale collapse |

**Fear** is not spent; it is an ambient field. High Fear in a room section applies a morale decay multiplier to all units in that section. The player can reduce Fear by avoiding slaps, rewarding units, and keeping invader presence low — or they can weaponize it, accepting accelerated morale degradation in exchange for sustained output pressure.

---

## Room Types

| Room | Size | Function | Strategic Role |
|------|------|----------|---------------|
| Production Room | 2×2 | Generates Gold per unit-tick of Peon labor | Core income; must be defended at all costs |
| Research Chamber | 2×2 | Generates Essence per unit-tick of Hex Intern labor | Enables upgrades; secondary priority |
| Guard Barracks | 2×1 | Houses Fury Grunts; reduces invader advance speed | Defense depth |
| Detention Cell | 1×1 | Imprisons one unit; removes from workforce, stops strike contagion | Crisis containment |
| Entrance Chamber | 2×1 | Required for unit spawning; cannot be destroyed | Logistical anchor |
| Corridor | 1×1 | Connects rooms; grants pathfinding routes to units and invaders | Spatial planning |

> Additional room types (Torture Chamber, Ritual Altar, Armory) are post-vertical-slice scope.

---

## Unit Roster (MVP)

### Groveling Peon (Worker)

The backbone of the operation. Low stats, high availability, cheap to hire. Cowardly personality predominates — they respond to slaps with immediate compliance but accumulate fear rapidly. Best suited to Production Rooms. Dies with dignified whimpering.

| Stat | Base Value |
|------|-----------|
| Health | 60 |
| Morale | 70 |
| Loyalty | 50 |
| Work Speed | 1.0× |
| Slap Tolerance | Low |

### Fury Grunt (Guard)

Combat-capable unit stationed in Guard Barracks. Stubborn personality predominates — resistant to slaps, slow to comply, but also slow to break. Engages invading heroes directly. Cannot perform production tasks. More expensive to hire; worth protecting.

| Stat | Base Value |
|------|-----------|
| Health | 120 |
| Morale | 60 |
| Loyalty | 65 |
| Work Speed | 0.5× (non-combat tasks) |
| Slap Tolerance | Medium |

### Hex Intern (Researcher)

Generates Essence in Research Chambers. Eager personality predominates — they respond to positive reinforcement strongly but have surprisingly volatile anger thresholds when slapped. Low health; should not be in combat paths. The most expensive unit to replace.

| Stat | Base Value |
|------|-----------|
| Health | 40 |
| Morale | 80 |
| Loyalty | 70 |
| Work Speed | 1.2× (Research only) |
| Slap Tolerance | Very Low |

---

## Player Actions and Tradeoffs

### Slap

Apply physical discipline to a unit.

| Dimension | Short-term | Long-term |
|-----------|-----------|-----------|
| Productivity | +15–40% output spike (1–2 min) | Net negative if repeated; morale decay cuts baseline output |
| Morale | Immediate drop | Cumulative decay accelerates toward crisis thresholds |
| Loyalty | No immediate change | Slow decay; below 20, unit becomes a strike risk |
| Anger | Rises per hit | High anger + low loyalty = strike nucleus |
| Fear (ambient) | Rises in room section | Pressures all nearby units regardless of target |
| Risk | Low (single slap) | High (pattern of slapping) — escalation to strike/revolt |

**Guidance:** One slap to re-engage a slacking unit is a reasonable tool. Slapping the same unit three times in a minute is how you lose your workforce on wave four.

### Feed

Spend Gold to deliver a meal to a unit (or a room section).

| Dimension | Effect |
|-----------|--------|
| Morale | +20 (immediate) |
| Loyalty | +5 |
| Anger | -10 |
| Gold cost | 15 per unit |
| Output | No immediate spike; prevents decay |

Feeding is the primary morale maintenance tool. Optimal play feeds units proactively before morale drops below 50, not reactively after a crisis.

### Imprison

Spend Gold to drag a unit to the Detention Cell.

| Dimension | Effect |
|-----------|--------|
| Strike contagion | Stops immediately for this unit |
| Unit productivity | Zero (unit is locked up) |
| Morale of other units | -5 to nearby witnesses (intimidation/fear effect) |
| Anger of imprisoned unit | +30 on release |
| Gold cost | 10 to imprison |
| Duration | Until player releases or sacrifices |

Imprisonment is a surgical tool for containing an instigator during a spreading strike. The side effect is a workforce member removed from production and an angrier unit on release.

### Sacrifice

Permanently remove a unit in exchange for Essence.

| Dimension | Effect |
|-----------|--------|
| Essence | +50 (base; multiplied by unit level in future) |
| Unit pool | -1 permanently |
| Morale of witnesses | -25 within room section |
| Loyalty of witnesses | -15 within room section |
| Fear (ambient) | +20 spike in room section |

Sacrifice is an emergency Essence injection that cannibalizes labor capacity and devastates nearby unit morale. It should feel costly even when it solves the Essence problem.

### Reward

Give a unit a bonus (trophy, commendation, small bribe).

| Dimension | Effect |
|-----------|--------|
| Morale | +30 |
| Loyalty | +20 |
| Anger | -20 |
| Gold cost | 25 per unit |
| Nearby units | +5 morale (visible equity effect; others want reward too) |

The most expensive per-unit morale restoration. Unlike Feed, rewards also restore loyalty significantly. Optimal use: targeted rewards for highest-anger, lowest-loyalty units to pull them back from the strike threshold.

---

## Threat System

Hero invasions arrive on a fixed escalating timer regardless of dungeon state. The player cannot defer waves; they can only prepare for them.

| Wave | Composition | Primary Target |
|------|------------|---------------|
| 1 | 1 Novice Hero | Entrance Chamber |
| 2 | 2 Novice Heroes | Production Rooms |
| 3 | 1 Veteran Hero + 1 Novice | Research Chamber |
| 4 | 2 Veterans | Guard Barracks, then inner rooms |
| 5+ | Scaling composition | Deep dungeon, high-value rooms |

Heroes pathfind through corridors, engage Fury Grunts in Guard Barracks, and attempt to destroy Production and Research Chambers. A room reduced to zero health is destroyed and removed from the income stream permanently until rebuilt. Rebuilding costs Gold and time — two things in short supply during an active invasion.

A dungeon in active labor unrest during an invasion is dramatically more vulnerable: striking units do not fight, COWERING units flee, and an Active Revolt may cause units to open doors for heroes rather than defend against them.

---

## Win / Loss Conditions (MVP Vertical Slice)

### Win Conditions (any one met)

| Condition | Description |
|-----------|-------------|
| Wave survival | Survive all five scheduled invasion waves with at least one unit and one room intact |
| Gold target | Accumulate 2,000 Gold total (banked, not net) before wave 5 |

### Loss Conditions (any one triggers)

| Condition | Description |
|-----------|-------------|
| Unit wipeout | All units dead — no workforce, dungeon collapses |
| Room wipeout | All rooms destroyed — no income or defense, dungeon falls |
| Active Revolt + invasion | Internal revolt meets active invasion simultaneously; if heroes reach Entrance Chamber while revolt is active, scenario ends |

On loss, the player receives a post-mortem screen summarizing: cause of death (statistical), most-slapped unit, total slaps landed, total Gold earned, wave reached. The tone is a bureaucratic termination notice from Abyssal Holdings Group.

---

## Tone Notes

**Dark comedy, not grimdark.** The dungeon should feel like a dysfunctional workplace, not a horror show. Units have names. They whimper. The floating feedback text when you slap someone reads like a passive-aggressive HR memo. The Strike Warning notification is formatted like an internal email chain that has gone off the rails.

**Satirical evil corporate culture.** Every mechanic should echo recognizable management dysfunction. Sacrifice is downsizing. Imprisonment is a PIP. Rewards are the annual holiday bonus that nobody thinks is enough. The hero invasions are regulatory audits that occasionally feature axes.

**Systemic consequences, not punishment.** The game does not moralize when the player slaps their way into a crisis. The systems simply account for it, and the consequences unfold with darkly comedic inevitability. The player should laugh at what they've done, then immediately start figuring out how to fix it.

**Slapstick physicality.** The slap mechanic is the primary tactile pleasure of the game. It must feel satisfying, ridiculous, and consequential at the same time. The visual and audio feedback of a Heavy slap hitting a Cowardly Peon should make the player simultaneously wince and laugh.
