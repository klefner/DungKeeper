# DungKeeper — The Slap System: Design & Specification

**Date:** 2026-05-14
**Status:** Pre-MVP / Vertical Slice
**Document type:** Game Design Document — Mechanic Specification

---

## Design Philosophy

The slap is not cosmetic. It is not a joke button. It is a behavior modification tool with real, tracked, compounding systemic consequences. Every slap is a transaction: the player receives an immediate productivity return and pays a delayed loyalty cost. The exchange rate degrades the more you use it. This is the central mechanical tension of DungKeeper.

The slap must be satisfying to execute. It must also feel wrong — not morally instructive wrong, but systemically consequential wrong. The unit's reaction should read as funny and slightly uncomfortable at the same time. The player should feel the seductive pull of using it again. And then again. And eventually they should feel the consequences arrive and recognize that they built this situation one slap at a time.

---

## Emotional Goal

A player executing a slap should feel, in rapid sequence:

1. **Anticipation** — the charge wind-up creates a moment of intention; the player is committing to the slap
2. **Satisfaction** — the impact hit-stop, sound, and unit reaction deliver visceral feedback
3. **Mild guilt** — the unit's specific reaction (vocalization, stumble, expression) registers as a character, not just a hitbox
4. **Rationalization** — the productivity spike is real; the player tells themselves it was worth it
5. **Recurrence** — the mechanic feels good enough to repeat, which is exactly the trap

---

## Input Specification

| Action | Input | Behavior |
|--------|-------|---------|
| Target a unit | Move cursor over unit | Hover highlight appears on unit; hand cursor icon |
| Begin charge | Left click (hold) | Charge accumulates over time; charge indicator appears |
| Release slap | Release left click | Slap executes at current charge level; hand swings |
| Cancel slap | Right click during charge | Charge cancels; no slap; satisfying cancel whoosh plays |
| Miss | Release over empty space | Slap executes with whoosh; no unit impact; charge wasted |

**Charge accumulation:** Charge fills from 0 to 10 over a 1.2-second hold. Releasing before 0.3 seconds results in a Light slap (force 1–3). Holding beyond 1.2 seconds locks the charge at maximum (force 10) — overcharging is not possible. A visual indicator near the cursor (a glowing hand icon that fills like a loading bar) communicates current charge level in real time.

**Force value:** Force is calculated as `Mathf.Floor(chargeTime / 0.12f)`, clamped to [1, 10]. The charge indicator has three visible threshold markers corresponding to Light, Medium, and Heavy tier boundaries.

---

## Force Tiers

| Tier | Force Range | Description |
|------|------------|-------------|
| Light | 1–3 | Quick tap; a reminder, not a punishment |
| Medium | 4–6 | Deliberate strike; clearly intentional |
| Heavy | 7–10 | Full wind-up impact; visible commitment to violence |

Force tier determines which animation set plays, which audio variant triggers, and which response lookup row is used in `SlapReactionSO`.

---

## Visual Feedback

The slap feedback stack executes in synchronized sequence:

| Element | Trigger | Behavior |
|---------|---------|---------|
| Charge indicator | On hold | Fills near cursor; three threshold glow markers (Light/Medium/Heavy) |
| Hand approach | On release | Hand prefab moves from off-screen toward unit over 0.08 s |
| Hit-stop | On contact | 2-frame game time pause (not real time); heightens impact weight |
| Impact flash | On contact | Brief white flash on unit sprite/mesh; duration scales with force tier |
| Unit stumble | Post-impact | Physics push on unit rigidbody; distance scales with force tier |
| Reaction animation | Post-impact | Animation state machine transitions to tier-appropriate reaction clip |
| Hit effect particle | On contact | Star/impact particle burst; size and count scale with force tier |
| Camera shake | On contact | `CameraShake.Shake()` called with amplitude = `forceTier * 0.04f`, duration = `0.15 + forceTier * 0.02f` |
| Floating feedback text | Post-impact | Message string from `SlapReactionSO` rises from unit and fades over 1.5 s |
| Stat bar update | Post-impact | Morale bar on unit inspect panel updates immediately |

---

## Audio Specification

Audio is layered and variant-selected based on force tier and unit personality:

| Layer | Trigger | Variants |
|-------|---------|---------|
| Swing whoosh | Always on release (hit or miss) | 3 variants per tier (Light/Medium/Heavy); selected randomly |
| Impact thud | On contact only | 3 variants per tier; selected randomly |
| Unit vocalization | On contact | Pool of 4–6 clips per personality type (Cowardly/Stubborn/Eager) × force tier; selected randomly |
| Reaction ambience | Post-impact (Heavy only) | Room-level reaction murmur from other units (short ambient oneshot) |

**Vocalization examples by personality × tier:**

| Personality | Light | Medium | Heavy |
|-------------|-------|--------|-------|
| Cowardly | Startled squeak | Pained whimper | Full sob + cringe |
| Stubborn | Grunt + glare sound | Angry muttered syllable | Teeth-clenched roar |
| Eager | Surprised yelp + immediate apology | Confused whine | Devastated wail |

All audio is played through `AudioManager` via event subscription to `UnitSlappedEvent`. `AudioManager` selects the appropriate clip pool based on `forceTier` and unit personality field from `UnitData`.

---

## Response Type Table

`SlapSystem` performs a lookup in all registered `SlapReactionSO` assets matching the unit's personality, the incoming force tier, and the unit's current state. The matched entry defines the response type and all stat deltas.

| Personality | Force Tier | Unit State | Response Type | Fear Gain | Anger Gain | Loyalty Loss | Morale Loss | Feedback Message |
|-------------|-----------|-----------|--------------|----------|-----------|-------------|------------|-----------------|
| Cowardly | Light | IDLE | Compliance | +5 | +2 | 1 | 5 | "Y-yes sir, right away!" |
| Cowardly | Light | WORKING | Flinch | +3 | +1 | 1 | 3 | "I'm working! I swear!" |
| Cowardly | Light | DISTRESSED | Collapse | +8 | +5 | 3 | 10 | "Please, not again..." |
| Cowardly | Medium | IDLE | Cowering | +15 | +8 | 5 | 15 | "I'll do anything!" |
| Cowardly | Medium | WORKING | Compliance | +10 | +5 | 3 | 10 | "Faster! Yes! Faster!" |
| Cowardly | Medium | DISTRESSED | Breakdown | +20 | +15 | 8 | 20 | "I can't take anymore..." |
| Cowardly | Heavy | IDLE | COWERING state | +25 | +10 | 10 | 25 | "PLEASE I HAVE DEBTS" |
| Cowardly | Heavy | WORKING | DISTRESSED state | +20 | +12 | 8 | 20 | "I—I'll work harder..." |
| Cowardly | Heavy | DISTRESSED | COWERING state | +30 | +20 | 15 | 35 | "..." (silent terror) |
| Stubborn | Light | IDLE | Glare | +2 | +8 | 2 | 3 | "Watch it, boss." |
| Stubborn | Light | WORKING | Grunt | +1 | +5 | 1 | 2 | "(grumbles)" |
| Stubborn | Light | DISTRESSED | Defiance | +3 | +12 | 4 | 5 | "Fine. FINE." |
| Stubborn | Medium | IDLE | Compliance | +8 | +12 | 5 | 8 | "I heard you the first time." |
| Stubborn | Medium | WORKING | Slowdown | +5 | +15 | 6 | 8 | "You're making me slower, genius." |
| Stubborn | Medium | DISTRESSED | STRIKING risk | +10 | +20 | 10 | 15 | "I've had enough of this." |
| Stubborn | Heavy | IDLE | DISTRESSED state | +15 | +25 | 12 | 18 | "You'll regret that." |
| Stubborn | Heavy | WORKING | STRIKING state | +12 | +30 | 15 | 20 | "That's it. I'm done." |
| Stubborn | Heavy | DISTRESSED | STRIKING state | +15 | +35 | 20 | 25 | "We're ALL done." |
| Eager | Light | IDLE | Overcorrection | +8 | +3 | 1 | 5 | "So sorry! What did I do wrong?!" |
| Eager | Light | WORKING | Speed spike | +5 | +2 | 1 | 4 | "More output, got it, more output!" |
| Eager | Light | DISTRESSED | Anxiety spiral | +10 | +8 | 3 | 8 | "I'm trying! I don't know what I'm doing wrong!" |
| Eager | Medium | IDLE | Compliance + confusion | +12 | +8 | 4 | 10 | "I — was I not doing it right?" |
| Eager | Medium | WORKING | Frantic burst | +8 | +10 | 5 | 10 | "MORE PRODUCTIVITY. I AM A PRODUCTIVITY MACHINE." |
| Eager | Medium | DISTRESSED | Loyalty crack | +15 | +15 | 10 | 15 | "I've been trying so hard..." |
| Eager | Heavy | IDLE | DISTRESSED state | +20 | +15 | 12 | 20 | "I don't understand what you want from me." |
| Eager | Heavy | WORKING | COWERING state | +18 | +18 | 10 | 20 | "I quit. I mean — I can't quit — but..." |
| Eager | Heavy | DISTRESSED | STRIKING state | +22 | +25 | 18 | 28 | "We deserve better than this." |

> **States not listed above:** COWERING, STRIKING, IMPRISONED, DEAD — see Special Cases section.

---

## Tolerance Accumulation

Each unit tracks a `CurrentSlapAccumulation` value (float, 0–100). This value represents the cumulative burden of being slapped, separate from morale or anger.

**Accumulation gain:** On each slap, `CurrentSlapAccumulation += forceTier * toleranceMultiplier` where `toleranceMultiplier` is defined by personality (Cowardly: 1.5, Eager: 1.2, Stubborn: 0.8).

**Accumulation decay:** Decays at a rate of `1.0 per second` of real simulation time during which the unit is not slapped. If a unit goes 60 continuous seconds without being slapped and has morale above 50, decay rate doubles.

**Threshold effects:**

| Accumulation Level | Effect |
|-------------------|--------|
| 0–30 | Normal operation; slaps resolve normally per table above |
| 31–60 | Elevated sensitivity: all Anger Gain values in the response table are multiplied by 1.5× |
| 61–85 | Breaking point: all slap responses skip to the DISTRESSED or COWERING outcome regardless of force tier; Loyalty Loss is doubled |
| 86–100 | Crisis: next slap of any force tier triggers STRIKING state (Stubborn), COWERING state (Cowardly), or STRIKING state (Eager); no further accumulation possible — unit is saturated |

---

## Short-term vs. Long-term Tradeoffs

This is the core tension the slap mechanic exists to create.

**Short-term:** A slap on an IDLE or WORKING unit delivers an immediate productivity spike. The unit moves faster, completes tasks quicker, and visually snaps to attention. The game rewards this with an immediate resource tick bonus.

**Long-term:** Every slap deposits into two invisible accounts — `CurrentSlapAccumulation` and the unit's Anger stat. Both decay slowly. If slapping outpaces decay, the unit climbs the accumulation thresholds and the return on each slap diminishes. The same slap that doubled output on turn one produces COWERING on turn five and a STRIKING state on turn eight.

The optimal play pattern (which players should discover through failure, not instruction) is: use slaps as punctuation, not prose. One slap to re-engage. A period of non-slap productivity. A reward to restore loyalty. Another slap if necessary. Then rest.

The player who treats slapping as their primary management tool will consistently hit a crisis between minutes 5 and 10 — just in time for a wave three invasion.

---

## Special Cases

### Slapping a COWERING unit

The unit is already in a fear state. They are rooted, unresponsive to normal task assignment, and at high accumulation.

- **Light slap:** No response. The unit is beyond responding to light stimulation. Charge wasted. Audio: hollow thud, no vocalization.
- **Medium slap:** Slap lands; unit jolts; Feedback: "Please... I'll do anything..."; extends COWERING duration by 15 seconds; no productivity benefit.
- **Heavy slap:** COWERING unit enters a micro-revolt state: briefly attempts to flee (pathfinds away from player cursor), which may interfere with corridor traffic. Resolves to DEAD if health drops to zero from additional slaps.

### Slapping a STRIKING unit

Slapping a striking unit is the highest-risk player action in the game.

- **Light slap:** 50% chance of suppression (unit returns to DISTRESSED, strike paused); 50% chance of escalation (strike spreads to nearest non-striking unit).
- **Medium slap:** 25% suppression, 75% escalation. Nearby units' Anger rises regardless of outcome.
- **Heavy slap:** Suppression chance 0%. Heavy slap on a STRIKING unit always escalates: the struck unit transitions to Active Revolt (attacks nearby rooms), and all units within two grid cells of the event gain +20 Anger immediately.

> The lesson: do not slap strikers. Use Imprison to contain them.

### Slapping an IMPRISONED unit

Available if the player clicks on the Detention Cell with a unit inside. The slap mechanics apply normally in terms of stat changes, but output is zero regardless (imprisoned units cannot work). This interaction exists as a torture hook for future mechanics and is visually distinct (more confined animation space, muffled audio).

Slapping an imprisoned Stubborn unit repeatedly will cause them to become an Active Revolt leader the moment they are released. This is always the player's fault and should feel like it.

### Slapping a DEAD unit

The slap connects. A hit effect plays. The corpse ragdolls slightly.

Nothing else happens.

Feedback message: *"(no response)"*

On landing this slap, the achievement **"Overkill"** unlocks. The post-match statistics screen will note "X posthumous disciplinary actions taken." This is presented without comment.

---

## Balance Targets

| Scenario | Target Outcome |
|----------|---------------|
| Player slaps every idle unit once | No crisis; minor morale reduction; recovered naturally within 2 minutes |
| Player slaps same unit 3× in 1 minute | Unit reaches accumulation 30–50; Elevated Sensitivity active; player sees visible morale bar drop |
| Player relies primarily on slapping for 5 minutes | Accumulation crisis on multiple units; first Strike Warning fires; loyalty below 20 on 2+ units |
| Player slaps exclusively for 10 minutes | Active strike on 30%+ of workforce; Anger-driven revolt risk on Stubborn units; labor crisis during wave 3 or 4 |
| Player never slaps, relies on rewards only | Higher Gold expenditure on feeding/rewards; lower Fear ambient; morale stable; less short-term productivity spike; viable but tight on wave 4+ without economic efficiency elsewhere |
| Optimal alternating play | Morale stays 50–75 across workforce; accumulation stays below 30; Gold surplus allows rewards when needed; dungeon survives all five waves |

---

## The Miss

Releasing the charge over empty space (no unit targeted) triggers the swing animation and the whoosh audio, but no impact sound and no stat changes. The charge is spent. The hand swings at air.

The miss is not punished mechanically beyond the wasted charge time. It is punished tonally: the whiff animation and sound are slightly more drawn-out than the impact version, and the unit nearest to the miss point plays a brief "that was close" idle animation variant. This reinforces the physical reality of the mechanic without adding mechanical penalty.

---

## Future Extensions

The slap mechanic is designed with the following extensions explicitly planned but out of vertical slice scope:

| Extension | Description |
|-----------|-------------|
| **Possession Slap** | Player temporarily possesses a unit, slaps from inside their perspective (first-person variant). Feels completely different — the slapped unit is now your peer. Morale consequences are amplified. |
| **Group Slap** | Charged area-of-effect slap covering a 2×2 grid area. Hits all units in range at reduced force. Efficient but chaotic — personality variance means some units comply while others escalate simultaneously. |
| **Ritual Slap** | Boss-tier ability unlocked via Research. Imbues the next slap with Essence energy. Slapped unit enters a temporary Fervor state (2× output, 3× morale decay, guaranteed collapse afterward). A sacrifice of the unit's future for immediate output. |
| **Cross-unit Slap** | Slapping a unit into an adjacent unit. Both receive damage/stat effects; the secondary unit is startled even if they were otherwise stable. Comic potential; requires physics tuning. |
