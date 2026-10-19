# Haunt Patrol — Gameplay Enhancement Specification

**Version:** 1.1  
**Scope:** Skeleton Siege, Ghost Trap, Zombie Invasion  
**Platform:** Existing .NET 10 / WPF application  
**Priority:** Gameplay improvements before final artwork and animation polish

## 1. Objective

Improve three existing mini-games based on playtesting.

Current observations:

- **Skeleton Siege:** The static skeleton is too easy to hit. Aiming requires little anticipation.
- **Ghost Trap:** Drawing one circle immediately completes the encounter, making it too brief.
- **Zombie Invasion:** The mechanics work, but the player doesn't experience enough urgency or difficult target-prioritisation decisions.
- **Witch's Familiar:** The cat-herding mechanic already feels good. Leave it unchanged.

Implement improvements within the existing encounter architecture.

Do not introduce a new game engine, rebuild rendering infrastructure, or change the desktop overlay system.

All proposed numeric values are initial tuning defaults, not fixed design requirements.

---

## 2. Shared Requirements

### 2.1 Preserve existing behaviour

All mini-games must continue to support:

- Normal random scheduling
- Manual encounter triggering
- Demo mode
- Existing victory and failure states
- Existing passive failure overlays
- Primary-monitor and multi-monitor configurations
- Non-activating, transparent desktop overlays
- Full cancellation and cleanup

Game objects should remain interactive only where their visible hit regions require input.

Transparent areas must continue to pass clicks through to underlying Windows applications.

### 2.2 Configurable difficulty parameters

Move the new gameplay tuning values into encounter-specific configuration classes or structures.

Examples:

- Movement speed
- Encounter duration
- Enemy counts
- Projectile limits
- Spawn intervals
- Evasion radius
- Threat thresholds

Hardcoded defaults are acceptable, but values should be centralised and easy to change without modifying gameplay logic.

### 2.3 Visual clarity

The existing characters are relatively small against a full desktop. Any new animations or movement should remain visually clear when the game is recorded at 2560×1440 and viewed at a reduced video size.

Ensure that new gameplay feedback is understandable without reading debug text.

---

# 3. Skeleton Siege — Moving Target and Limited Ammunition

## 3.1 Design goal

Transform Skeleton Siege from shooting a stationary target into a small skill-based aiming challenge.

The player should need to anticipate where the skeleton will be when the projectile arrives.

**Target experience:** 2–3 shots, approximately 10–15 seconds of active gameplay.

## 3.2 Skeleton patrol behaviour

The skeleton should move horizontally through a defined patrol region.

Initial configuration:

| Parameter | Value |
|---|---|
| Patrol speed | 90 pixels/second |
| Patrol width | 300–450 pixels |
| Initial direction | Random left/right |
| Direction changes | At patrol boundaries |
| Encounter duration | 20 seconds |
| Ammunition | 3 shots |

Use the game's existing logical coordinate system and convert nominal pixel values appropriately if it uses WPF device-independent units.

### Movement rules

- Skeleton moves continuously between two horizontal boundaries.
- Reverse direction when reaching either boundary.
- Keep movement within the visible monitor area.
- No teleporting.
- No sudden random direction changes while a projectile is airborne.
- Maintain predictable motion so the player can learn to lead the target.
- If the skeleton animation supports it, face the current movement direction.

The skeleton should start moving as soon as its entrance animation finishes.

### Optional personality

At a patrol boundary, the skeleton may briefly pause, turn around dramatically, or taunt the player.

Limit pauses to approximately 200–350 milliseconds.

Do not make movement excessively random. Difficulty should come from timing, not unpredictable movement.

## 3.3 Projectile interaction

Retain the existing drag-to-aim catapult mechanic.

Add:

- Three-shot ammunition limit.
- Visible shot counter.
- Slight recoil when launching.
- Projectile trail during flight.
- Brief miss feedback.

Suggested HUD:

`SKELETON SIEGE — 3 SHOTS LEFT`

### Firing rules

1. Player drags and releases the projectile.
2. One ammunition unit is consumed.
3. The projectile follows its existing ballistic trajectory.
4. Collision is evaluated against the skeleton's current position every simulation step.
5. The skeleton continues moving while the projectile is airborne.
6. If the projectile misses, another shot becomes available when it finishes or exits the active play area.
7. If all three shots are exhausted and no projectile remains active, the encounter fails.

The timer may also cause failure before ammunition runs out.

Prevent launching another shot while the previous shot remains active, unless the existing mechanic already intentionally supports multiple projectiles.

## 3.4 Collision detection

Ensure collisions use the skeleton's current moving hitbox, not its original spawn position.

Prefer a forgiving collision shape.

For example:

- Main body and skull included.
- Small padding around the visible character.
- No requirement for pixel-perfect collision.

If the projectile moves rapidly enough to skip across the skeleton between frames, use swept collision detection or sufficiently small simulation steps.

A visually convincing hit should always count.

## 3.5 Optional reactive behaviour

Only if the moving-target mechanic feels too simple after testing:

The skeleton occasionally ducks or reacts when a projectile passes nearby.

Constraints:

- Reaction must not happen every shot.
- It must not make a previously accurate shot feel unfair.
- Don't implement reactive dodging before evaluating ordinary patrol movement.

This is a stretch enhancement, not required.

## 3.6 Victory

On projectile collision:

1. Stop skeleton movement.
2. Trigger impact flash and recoil.
3. Play defeat animation.
4. Optionally scatter bones.
5. Resolve victory through existing encounter completion logic.

## 3.7 Failure

Failure occurs when:

- The timer expires, or
- Three shots have been used and all active projectiles have missed.

Trigger the existing skeleton attack and glass-fracture failure effect.

## 3.8 Acceptance tests

- [ ] Skeleton patrols continuously within its configured region.
- [ ] Skeleton reverses direction smoothly at boundaries.
- [ ] Player must lead shots against the moving target.
- [ ] Hit detection follows the moving skeleton.
- [ ] Ammunition decreases only when a shot launches.
- [ ] Three missed shots cause failure.
- [ ] A successful hit ends the encounter immediately.
- [ ] Skeleton movement and projectile updates stop on completion.
- [ ] Existing glass penalty remains unchanged.

---

# 4. Ghost Trap — Split into Two

## 4.1 Design goal

Introduce a surprise second stage after the first successful capture.

The player believes they have captured the ghost, but it splits into two smaller spirits which must then be captured independently.

**Target experience:** Approximately 10–15 seconds of active gameplay.

## 4.2 Encounter stages

Introduce explicit stages:

```text
InitialGhost
    |
    | First successful circle
    v
Splitting
    |
    | Split animation completes
    v
TwoGhosts
    |
    | Capture first small ghost
    v
OneGhostRemaining
    |
    | Capture final ghost
    v
Victory
```

Timeout from any active stage causes failure.

### Stage 1 — Initial ghost

Preserve existing behaviour:

- One large ghost appears.
- Magical pen can be picked up.
- Player draws a circle around the ghost.
- Existing loop validation determines capture.

The initial ghost should move gently as before.

### Stage 2 — Surprise split

Instead of immediately winning when the first ghost is captured:

1. The valid magical circle flashes.
2. The large ghost becomes compressed or distorted.
3. Ghost briefly freezes.
4. A bright magical burst occurs.
5. The ghost splits into two smaller ghosts.
6. Both move apart in opposing directions.
7. The instruction changes to: `CATCH THEM BOTH!`

Suggested duration: 0.6–1.0 seconds.

Do not allow drawing input during the split transition.

Do not stop the encounter timer.

### Stage 3 — Capture both smaller ghosts

Two smaller ghosts now float independently around the screen.

Each must be enclosed by a valid drawn loop.

When one is captured:

- It shrinks and disappears.
- A small magical burst plays.
- The remaining ghost continues moving.
- Encounter does not end.

When the second is captured:

- Play final victory effect.
- Resolve the encounter successfully.

## 4.3 Smaller ghost behaviour

Suggested configuration:

| Parameter | Value |
|---|---|
| Size | 65–70% of original |
| Normal movement speed | 45–65 px/second |
| Pen avoidance radius | 120 px |
| Avoidance speed multiplier | 1.4× |
| Movement boundaries | Defined region within monitor |
| Encounter duration | 25 seconds total |

### Movement rules

Each ghost:

- Has independent position and velocity.
- Drifts smoothly rather than changing direction abruptly.
- Bounces or turns gently near movement boundaries.
- Avoids overlapping the other ghost when practical.
- Remains entirely within the visible monitor.

Avoid having both ghosts follow identical movement patterns.

## 4.4 Pen avoidance

The smaller ghosts should react to the magical pen.

**Important:** Only enable avoidance while the pen is actively picked up and being dragged.

Do not require passive global cursor tracking.

When the active pen approaches within approximately 120 pixels:

- Ghost gains a repulsive steering force away from the pen.
- Movement accelerates gradually.
- Speed remains capped.
- Ghost continues to stay inside its allowed movement region.

The ghost should appear nervous, not impossible to capture.

### Fairness constraints

- Do not teleport.
- Do not reverse direction instantly.
- Do not move faster than the player can reasonably draw.
- Do not allow a ghost to escape beyond the screen.
- Reduce avoidance if the ghost is trapped near a screen boundary.
- Once a capture stroke is completed, evaluate it against the ghost position at that moment.

Use smooth acceleration and damping.

If the mechanic proves frustrating, reduce avoidance speed before increasing encounter time.

## 4.5 Circle validation changes

Reuse existing closed-loop and point-in-polygon validation.

However, the current stroke minimum may be too large for smaller ghosts.

Make validation thresholds appropriate to the current target size.

Suggested defaults:

| Validation | Large ghost | Small ghost |
|---|---|---|
| Minimum stroke length | 450 px | 220–280 px |
| Closure distance | 45 px | 35–40 px |
| Minimum samples | 12 | 12 |
| Ghost centre inside polygon | Required | Required |

Retain existing safeguards against tiny scribbles and malformed strokes.

### Multiple targets in one loop

For predictable difficulty, each valid completed stroke should capture at most one ghost.

If the user encloses both ghosts in a single circle, capture one and leave the other active.

This prevents accidentally completing the entire second stage with one giant circle.

## 4.6 Ghost capture feedback

Differentiate partial and complete victory.

Capturing the first small ghost:

- Brief sparkle or implosion.
- Small `1 REMAINING` message.
- Remaining ghost visibly reacts, perhaps looking frightened.

Capturing the final ghost:

- Larger burst.
- Magical circle collapse.
- Brief victory text.
- Encounter ends.

Do not replay the full initial split animation for smaller ghost captures.

## 4.7 Timer and failure

Set the entire encounter duration to approximately 25 seconds.

The timer starts when the first ghost becomes playable and continues through all stages.

Do not reset the timer after splitting.

At timeout:

- Any remaining ghosts escape.
- Trigger existing spectral fog penalty.
- Clean up any active stroke and mouse capture.

## 4.8 Acceptance tests

- [ ] Initial ghost can be enclosed normally.
- [ ] First capture triggers split rather than victory.
- [ ] Exactly two smaller ghosts appear.
- [ ] Smaller ghosts move independently.
- [ ] Pen proximity causes controlled avoidance.
- [ ] Smaller ghosts remain capturable.
- [ ] Each successful small-ghost capture removes one ghost.
- [ ] First small-ghost capture does not end encounter.
- [ ] Capturing both triggers victory.
- [ ] Timeout at any stage triggers spectral fog.
- [ ] Mouse capture and drawing state are cleaned up properly.
- [ ] One loop cannot accidentally finish both smaller ghosts.

---

# 5. Zombie Invasion — Escalation and Urgency

## 5.1 Design goal

Make Zombie Invasion feel like an escalating threat instead of a collection of clickable targets.

The player should face increasingly difficult choices about which zombie to attack first.

A zombie close to reaching the brain should feel dangerous, even if other enemies are still spawning.

**Target experience:** Increasing tension with a frantic final 5–8 seconds.

## 5.2 Enemy roster

Retain the existing eight-enemy encounter initially, but introduce a third enemy type.

Suggested composition:

| Type | Count | Health | Behaviour |
|---|---:|---:|---|
| Normal zombie | 4 | 1 | Average movement speed |
| Brute zombie | 2 | 2 | Slow, requires two hits |
| Runner zombie | 2 | 1 | Fast, dangerous if ignored |

Total: 8 zombies.

Avoid requiring new production artwork immediately. Use existing sprites with scale, tint or simple visual markers for initial implementation.

### Suggested relative speeds

| Type | Speed multiplier |
|---|---:|
| Normal | 1.0× |
| Brute | 0.65× |
| Runner | 1.65× |

Use the existing normal zombie movement speed as the baseline.

## 5.3 Spawn schedule

Replace uniformly spaced spawns with small, escalating waves.

Suggested schedule:

| Time | Spawn |
|---|---|
| 0s | 2 normal zombies |
| 3s | 1 normal + 1 brute |
| 6s | 1 runner |
| 9s | 1 normal + 1 brute |
| 12s | 1 runner |

Do not spawn new zombies after the full roster has entered.

Maximum concurrent enemies: 6.

If a wave would exceed the active enemy cap, defer the excess enemies until capacity becomes available.

Use the configured spawn schedule relative to encounter start, not separate independent timers per enemy.

### Escalating speed

Optionally increase movement speeds gradually over the encounter.

Suggested multiplier:

- First third: 1.0×
- Middle third: 1.1×
- Final third: 1.2×

Apply to each zombie's base movement speed.

Avoid making the final wave impossible to defeat.

## 5.4 Threat-based target prioritisation

The main source of difficulty should be enemies reaching different levels of danger.

For example:

- A brute is far from the brain but needs two hits.
- A runner is much closer and needs only one hit.
- Several normal zombies are approaching together.

The player must choose where to spend their clicks.

Do not automatically highlight the correct target.

The game should communicate danger through visual feedback.

## 5.5 Threat meter

Add a compact visual threat indicator.

Suggested HUD:

```text
DEFEND THE BRAIN!

[████████░░░░░░░░░░░░]  DANGER
```

Threat should reflect the actual proximity and number of approaching zombies.

It must not simply increase with elapsed time.

### Suggested threat model

For each active zombie, estimate its time to reach the brain using remaining distance and current movement speed.

Derive an individual threat contribution:

- More than 6 seconds away: Low threat
- Between 3 and 6 seconds: Moderate threat
- Under 3 seconds: High threat
- Under 1.5 seconds: Critical threat

Combine the highest individual threat with a smaller contribution from other approaching zombies.

Clamp the resulting value to 0–100.

Recalculate continuously or at roughly 10 Hz.

Smooth the displayed value so it does not flicker wildly.

### Threat colours

| Threat | Display |
|---|---|
| 0–29 | Purple/green — Safe |
| 30–69 | Orange — Danger |
| 70–100 | Red — Critical |

Only show `CRITICAL!` when a zombie is genuinely close to reaching the brain.

Threat should decrease if the player eliminates dangerous zombies.

## 5.6 Near-brain warnings

When a zombie approaches within its critical range:

- Zombie starts flashing or pulsing.
- Brain briefly pulses red.
- A short warning sound may play.
- Zombie movement remains unchanged.

Use actual distance or estimated time-to-impact rather than arbitrary elapsed encounter time.

Do not flash the entire desktop repeatedly.

A warning should be visible without becoming visually overwhelming.

## 5.7 Brain feedback

The brain should feel like something worth protecting.

Add:

**Idle state**

- Gentle floating or bobbing.
- Subtle glow.

**Danger state**

- Slightly increased pulsing.
- Glow shifts toward orange/red.

**Damaged state**

- Brief recoil.
- Visible hit flash.
- Reduced health indicator.
- Small particle burst.

**Critical state**

- Faster pulsing.
- Stronger red glow.
- Optional short distress sound.

**Victory**

- Bright happy bounce.
- Restored calm colour.
- Brief celebratory effect.

Prefer procedural WPF transforms over requiring new animation sheets.

## 5.8 Hit feedback

Give attacks more impact.

For every zombie hit:

1. Display a short hit flash.
2. Apply visible recoil.
3. Briefly interrupt movement.
4. Play a short impact sound if audio enabled.
5. Update health.
6. Resume movement or trigger death animation.

Suggested recoil duration: 100–180 milliseconds.

For a brute, the first hit should produce an obvious reaction without killing it.

For runners, use quick and snappy death animations so they do not obscure the remaining enemies.

## 5.9 Victory and failure

Preserve the current core win condition:

Victory requires:

- Every zombie has spawned.
- All zombies are defeated.
- Brain health remains above zero.
- Encounter timer has not expired.

Failure occurs when:

- Brain health reaches zero, or
- Timer expires before all zombies are defeated.

Keep the existing zombie infection/handprint penalty.

Suggested total encounter duration: 25 seconds.

If the encounter routinely finishes before the final wave becomes threatening, adjust spawn spacing and travel speed rather than just reducing the total timer.

## 5.10 Audio feedback

If an audio system already exists, add:

- Short arrival cue for each wave.
- Brute hit sound.
- Runner entrance cue.
- Brain danger sound.
- Brain damage sound.
- Victory and failure sounds.

Avoid continuously looping alarms.

Rate-limit warnings so several near-brain zombies do not produce an unpleasant barrage of overlapping sounds.

Respect the existing mute setting.

## 5.11 Acceptance tests

- [ ] Initial two zombies spawn together.
- [ ] Later waves follow the configured schedule.
- [ ] Normal, brute and runner types behave differently.
- [ ] Runner zombies create meaningful urgency.
- [ ] Brutes require two hits.
- [ ] Maximum active enemy limit is respected.
- [ ] Threat meter reflects actual enemy proximity.
- [ ] Threat decreases after dangerous enemies are killed.
- [ ] Brain visibly reacts to danger.
- [ ] Critical zombies receive visual warnings.
- [ ] Hit feedback is clear for all zombie types.
- [ ] All zombies remain clickable without stealing desktop focus.
- [ ] Victory and failure conditions remain correct.
- [ ] Existing infection penalty remains unchanged.

---

# 6. UI and Feedback Consistency

All three encounters should use a consistent HUD language.

Keep the UI simple and readable.

Suggested components:

| Encounter | Primary indicator | Secondary indicator |
|---|---|---|
| Skeleton | Time remaining | Shots remaining |
| Ghost | Time remaining | Ghosts remaining |
| Zombies | Brain health | Threat level |

Avoid large permanent UI panels or opaque backdrops.

Game HUD elements should ideally be rendered in the existing passive overlay layer, so they do not block normal desktop clicks.

Use clear visual feedback for:

- Encounter arrival
- Successful interaction
- Failed interaction
- Increasing danger
- Victory
- Failure

Avoid introducing extra confirmation clicks or menus.

---

# 7. Tuning and Demo Requirements

## 7.1 Centralised tuning

Create or extend configuration for each encounter.

Example conceptual structure:

```csharp
public sealed class SkeletonSiegeOptions
{
    public double PatrolSpeed { get; init; } = 90;
    public int MaxShots { get; init; } = 3;
    public double EncounterSeconds { get; init; } = 20;
}

public sealed class GhostTrapOptions
{
    public int SplitGhostCount { get; init; } = 2;
    public double SmallGhostScale { get; init; } = 0.7;
    public double AvoidanceRadius { get; init; } = 120;
    public double EncounterSeconds { get; init; } = 25;
}

public sealed class ZombieInvasionOptions
{
    public int TotalZombies { get; init; } = 8;
    public int MaxConcurrentZombies { get; init; } = 6;
    public double RunnerSpeedMultiplier { get; init; } = 1.65;
    public double EncounterSeconds { get; init; } = 25;
}
```

Adjust to existing project patterns and types.

Do not introduce a new configuration framework solely for these values.

## 7.2 Deterministic demo behaviour

Maintain repeatable encounters for video capture and testing.

Allow fixed random seeds or predetermined spawn configurations where useful.

Demo mode should:

- Use real gameplay logic.
- Keep movement and timings predictable between runs.
- Produce the same wave sequence for Zombie Invasion.
- Spawn the skeleton in a useful visible location.
- Place the split ghosts where they can be captured.
- Allow the player to win or fail through actual interaction.

No canned footage.

## 7.3 Difficulty targets

These are soft targets to evaluate through manual playtesting:

| Encounter | Intended experience |
|---|---|
| Cat | Playful and relatively forgiving |
| Skeleton | Skill-based; typically 2–3 shots |
| Ghost | Surprising two-stage capture; 10–15 seconds |
| Zombies | Escalating pressure; challenging final seconds |

Do not optimise all games for equal difficulty.

A varied experience is desirable.

---

# 8. Implementation Order

## Phase 1 — Skeleton Siege

Implement:

1. Patrol boundaries and horizontal movement.
2. Moving hitbox.
3. Three-shot ammunition limit.
4. HUD shot counter.
5. Victory/failure cleanup.

Test before proceeding.

## Phase 2 — Ghost Trap

Implement:

1. Split state.
2. Two smaller ghosts.
3. Independent movement.
4. Pen avoidance.
5. Size-adjusted circle validation.
6. Remaining ghost counter.
7. Final capture resolution.

Test first-stage and second-stage timeouts separately.

## Phase 3 — Zombie Invasion

Implement:

1. Runner type.
2. Wave schedule.
3. Escalating enemy speed.
4. Threat calculation.
5. Critical warnings.
6. Brain danger animations.
7. Stronger hit feedback.

## Phase 4 — Playtesting and balance

Run each encounter repeatedly.

Check:

- Are successful attempts satisfying?
- Are failures understandable?
- Is any mechanic unfair or frustrating?
- Does the encounter overstay its welcome?
- Does the player have sufficient time to react?
- Do the new mechanics remain readable at demo-video size?

Tune configuration before adding extra gameplay complexity.

---

# 9. Out of Scope

Do not implement:

- New mini-games
- New desktop overlay architecture
- Advanced physics engines
- Full pathfinding
- Complex AI decision trees
- Multiplayer
- Global mouse or keyboard hooks
- Persistent progression
- Difficulty selection menus
- Leaderboards
- Extensive new artwork requirements

All enhancements should be compatible with current placeholder assets.

---

# 10. Definition of Done

The gameplay enhancement pass is complete when:

1. Skeleton Siege requires aiming at a moving target with limited ammunition.
2. Ghost Trap includes a surprise split into two smaller, independently capturable spirits.
3. Zombie Invasion escalates through enemy waves, fast zombies and visible threat feedback.
4. Witch's Familiar remains unchanged.
5. All four games still work on the real Windows desktop without blocking underlying applications.
6. All existing failure effects and cleanup behaviour continue working.
7. Gameplay remains compatible with the production sprite and animation pipeline.

**Final objective:** Make the four encounters mechanically distinct, immediately understandable and entertaining to watch, without sacrificing the lightweight desktop-haunting experience.
