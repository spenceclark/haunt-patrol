# Haunt Patrol — Mini-Games 3 & 4

**Status:** Build specification  
**Platform:** Existing .NET 10 / WPF application  
**Scope:** Ghost Trap and Zombie Invasion  
**Objective:** Add two visually distinctive encounters without rebuilding the existing game or desktop overlay infrastructure.

## 1. Integration requirements

Implement these as two additional encounters within the existing application.

Reuse the current implementations for:

- Encounter scheduling and selection
- Interactive desktop overlays
- Passive, click-through failure effects
- Mouse input and dragging
- Sprite rendering and animation
- Encounter timers and state management
- Monitor selection
- Demo/manual triggering
- Cleanup and cancellation

Adapt to the actual existing interfaces and conventions rather than introducing a parallel framework.

Both games must work with placeholder shapes or PNGs before artwork is available.

Support Primary Only mode for demonstration, with existing multi-monitor support retained.

Neither game should require keyboard focus or global input hooks.

---

# 2. Mini-Game: Ghost Trap

## Concept

A ghost escapes into the user's desktop and floats around, laughing at the player.

A glowing magical pen appears nearby.

The player must pick up the pen and draw a closed magical circle around the ghost before time expires.

If successful, the circle tightens around the ghost, which is sucked into a tiny glowing point and disappears.

If unsuccessful, the ghost releases spectral fog across the screen.

**Primary interaction:** Mouse gesture / freehand drawing  
**Encounter duration:** 20 seconds  
**Difficulty:** Easy to understand, moderately forgiving

## 2.1 Encounter sequence

**Phase A — Arrival (1–2 seconds)**

- A small ghost materialises near the centre of the selected monitor.
- Ghost floats gently up and down.
- A magical pen appears approximately 150 pixels to its left.
- Short instructional text appears: "Draw a circle around the ghost!"
- Countdown begins once the arrival animation completes.

**Phase B — Drawing**

1. User presses and holds the left mouse button on the pen.
2. Pen becomes attached to the cursor.
3. Pointer capture begins.
4. Mouse movement draws a luminous magical trail.
5. Releasing the mouse button completes the drawing attempt.
6. The game evaluates whether a closed loop encloses the ghost.

**Phase C — Resolution**

Success:

- The magical circle flashes.
- It contracts towards the ghost.
- Ghost becomes distressed and shrinks.
- A small implosion/particle burst occurs.
- Encounter completes with Victory.

Failure:

- Incomplete circles disappear after a short animation.
- Player may immediately attempt another circle while time remains.
- If time expires, the ghost laughs and escapes.
- Spectral fog penalty begins.

## 2.2 Ghost movement

Implement a simple floating movement model.

- Spawn around 40–60% across the screen.
- Stay within a bounded region approximately 350 × 250 pixels.
- Float with smooth sinusoidal movement.
- Occasionally change horizontal direction.
- No collision detection with desktop windows required.
- Ghost must remain fully visible.

The ghost should move slowly enough for the player to draw around it.

During drawing, it may continue bobbing, but should not suddenly teleport or accelerate.

Placeholder: a semi-transparent blue ellipse with two eyes.

## 2.3 Freehand drawing system

Record pointer positions while the pen is held.

```text
Stroke:
  Points: List<Point>
  StartPoint
  CurrentPoint
  TotalLength
  IsDrawing
```

Collect samples at a reasonable spatial interval, such as every 3–5 pixels, rather than storing every raw mouse event.

Render the path as a glowing polyline:

- Bright cyan or purple centre
- Wider, faint outer glow
- Rounded joins
- Optional particles following the pen

Initial implementation may use a simple WPF `Polyline` or `Path`.

### Mouse handling

The pen is the only object that initiates drawing.

On mouse down:

- Verify the pointer is inside the pen's hitbox.
- Capture the mouse.
- Start recording positions.

On mouse movement:

- Update the pen position.
- Extend the stroke.
- Update the visual path.

On mouse up:

- Release capture.
- Evaluate the completed stroke.

On timeout, cancellation or exception:

- Release capture.
- Clear any active stroke.
- Restore normal desktop input.

The stroke must continue across transparent desktop regions using the existing drag/capture mechanism.

No full-screen opaque or almost-transparent mouse-catching surface.

## 2.4 Circle validation

A valid capture requires:

1. At least 12 distinct sampled points.
2. A total stroke length of at least 450 pixels, adjusted appropriately for display scaling.
3. The final point is within 45 pixels of the starting point.
4. The ghost's current centre lies inside the closed polygon formed by the stroke.

Use a standard ray-casting point-in-polygon test.

Close the polygon logically by connecting its final point to its first point for validation.

No need to recognise a mathematically perfect circle. A rough, irregular loop is acceptable.

A self-intersecting scribble is allowed to fail.

Use logical WPF coordinates consistently for stroke points and ghost positions.

### Feedback

When a stroke ends:

- Valid loop: flash bright green, trigger capture.
- Open loop: briefly flash red, then dissolve.
- Closed but misses ghost: ghost dodges or laughs.
- Tiny scribble: disappear without a dramatic reaction.

Allow multiple attempts until timeout.

## 2.5 Victory animation

Suggested sequence, approximately 1.5 seconds:

1. The accepted stroke turns bright purple.
2. Path points interpolate towards the ghost's centre.
3. Ghost sprite scales down while rotating.
4. A bright point flashes.
5. Small particles disperse.
6. All encounter visuals disappear.

During this animation, prevent additional input.

## 2.6 Failure effect — Spectral Haunting

Use the existing passive overlay system.

The ghost escapes and leaves a temporary supernatural presence on the monitor.

Visual effects:

- Blue/purple fog rolls in from lower corners.
- Translucent ghostly faces occasionally materialise.
- Small glowing eyes appear in the mist.
- Fog slowly dissipates.

Duration: **10 seconds**, including fade-out.

The effect must be entirely click-through.

Initial placeholder: several translucent blue ellipses drifting slowly across the bottom third of the screen.

Do not dim or obscure the entire desktop with an opaque backdrop.

## 2.7 Assets

```text
Ghost/
  ghost_idle
  ghost_laugh
  ghost_scared
  ghost_captured
  magic_pen
  magic_trail
  capture_burst
  spectral_fog
  ghost_face
```

Suggested production animations:

| Asset | Animation |
|---|---|
| Ghost idle | 6–8 frames, looping |
| Ghost laugh | 4–6 frames |
| Ghost scared | 4 frames |
| Ghost captured | Can use scale/rotation transforms |
| Magic trail | Procedural |
| Spectral fog | Procedural or looping texture |

Prefer a large, expressive ghost with readable facial expressions.

---

# 3. Mini-Game: Zombie Invasion

## Concept

A small army of cartoon zombies begins climbing onto the desktop from the bottom of the monitor.

They are heading towards a glowing brain, which appears floating above the taskbar.

The player must rapidly click zombies to zap them before they reach the brain.

Some zombies require multiple hits.

If enough zombies reach the brain, they overrun the desktop, leaving filthy handprints and spreading a supernatural infection.

**Primary interaction:** Rapid mouse targeting and clicking  
**Encounter duration:** 25 seconds  
**Difficulty:** Faster-paced arcade action

## 3.1 Encounter sequence

**Phase A — Warning**

- A crack appears along the bottom of the desktop.
- Two skeletal hands briefly emerge.
- A glowing brain appears approximately 30% up from the bottom of the screen.
- First zombie climbs out of the ground.
- Brief instruction: "DEFEND THE BRAIN!"

**Phase B — Invasion**

- Zombies emerge from random positions along the bottom edge.
- They advance toward the brain.
- Player clicks zombies to attack.
- Successful hits interrupt or knock back the zombie.
- Defeated zombies collapse or disintegrate.
- Additional zombies arrive in small waves.

**Phase C — Resolution**

Victory:

- All zombies are eliminated.
- Brain performs a brief victory animation.
- Remaining visual effects dissolve.
- Encounter closes.

Failure:

- Brain's health reaches zero, or time expires with zombies remaining.
- Zombies celebrate or swarm the brain.
- Dirty zombie hands slap onto the screen.
- Infection penalty begins.

## 3.2 Enemy waves

Initial encounter contains eight zombies.

Suggested composition:

- Six normal zombies
- Two tougher zombies

Normal zombie:

- One click to defeat
- Average movement speed
- Small to medium sprite

Tough zombie:

- Two clicks to defeat
- Slower movement
- Larger sprite
- Visibly different appearance

Introduce zombies approximately every 1.5–2 seconds, subject to an active enemy cap of four.

Do not spawn all eight simultaneously.

Zombie types should be differentiated visually even with placeholders.

Spawn positions should avoid overlapping characters.

## 3.3 Zombie movement

Each zombie has:

```text
Zombie:
  Position
  Velocity
  Speed
  Health
  Type
  State
  SpawnTime
```

Possible states:

```text
Spawning
Walking
Hit
Dying
Removed
```

Use simple steering toward the brain's position.

There is no requirement for terrain navigation or pathfinding.

Zombies can walk across the visual appearance of existing desktop windows. They do not interact with those windows.

Add a slight side-to-side wobble or head bob to create personality.

When hit:

- Briefly flash.
- Recoil away from the brain.
- Pause movement momentarily.
- Lose one health point.
- Play hit or death animation as appropriate.

Prevent repeated clicks on an enemy while it is in a non-interactive death animation.

## 3.4 Attack interaction

The player attacks by clicking directly on a zombie sprite.

No weapon pickup is required.

The cursor can display an optional crosshair, but gameplay should not depend on continuously tracking it over transparent desktop regions.

On valid zombie click:

1. Register one attack.
2. Display a short magical projectile, lightning bolt or impact flash.
3. Apply damage.
4. Trigger the zombie's hit reaction.
5. Remove the zombie after its death animation if health reaches zero.

Suggested effect: a tiny glowing bolt shoots from a stationary supernatural blaster at the screen edge towards the clicked zombie.

This gives the attack a clear visual origin without adding another control mechanic.

### Important input requirement

Only visible enemy hit regions should intercept clicks.

Clicking an empty area of the desktop should continue interacting with the underlying application.

Do not create an invisible full-screen shooting surface.

Use the existing non-activating mouse event approach.

## 3.5 Brain defence

The glowing brain is a temporary game object displayed above the real desktop.

It has two health points.

Each zombie that reaches the brain:

- Performs a quick attack animation.
- Removes one brain health point.
- Disappears from active gameplay.
- Produces a short visible impact.

At zero health, immediately trigger failure.

Show brain health using two small glowing heart or brain icons.

The brain is not a draggable or clickable control.

It should not intercept unrelated desktop input unnecessarily.

Position it far enough above the taskbar that it remains visible in screen recordings.

## 3.6 Win conditions

The player wins when:

- All eight zombies have spawned.
- All eight have been defeated.
- The brain still has at least one health point.
- The encounter timer has not expired.

Fail if:

- Brain health reaches zero.
- Timer expires before all zombies are defeated.

Once success or failure has been determined, stop enemy spawning and prevent further attacks.

## 3.7 Victory animation

Suggested sequence:

1. Last zombie disintegrates.
2. Brain bounces happily.
3. Small green magical particles scatter.
4. Brief "INVASION REPELLED" text appears.
5. All game objects disappear.

Target duration: approximately 1 second.

## 3.8 Failure effect — Zombie Infection

Display a temporary collection of large dirty zombie handprints over the desktop.

Visual sequence:

1. Several handprints slap onto the display one after another.
2. Dark green mould or infection spreads around the edges.
3. Small insects or bubbles may move within the infection.
4. After a short hold, the entire effect dissolves.

Duration: **10 seconds**.

Make the handprints exaggerated and cartoonish rather than photorealistic gore.

Use the passive click-through overlay.

Placeholder: large semi-transparent green hand-shaped polygons or simple circles, plus a translucent green border around the monitor.

Do not permanently modify the screen, wallpaper or any files.

## 3.9 Assets

```text
Zombies/
  zombie_normal_idle
  zombie_normal_walk
  zombie_normal_hit
  zombie_normal_die

  zombie_brute_idle
  zombie_brute_walk
  zombie_brute_hit
  zombie_brute_die

  brain_idle
  brain_hit
  brain_victory

  zombie_bolt
  zombie_impact
  ground_crack
  zombie_handprint
  infection_overlay
```

Suggested animations:

| Asset | Animation |
|---|---|
| Normal zombie walk | 6–8 frames, looping |
| Brute zombie walk | 6–8 frames, looping |
| Hit reaction | 2–3 frames or transform |
| Death | 4–6 frames |
| Brain idle | Scale/bobbing transforms |
| Handprints | Static art with scale/opacity animation |
| Infection | Procedural paths or animated texture |

Aim for amusing, expressive zombies rather than realistic horror.

---

# 4. Shared implementation notes

## 4.1 Coordinate system

Reuse the existing overlay coordinate system.

All gameplay positions must be monitor-relative and correctly converted into WPF drawing coordinates.

Pointer input, hit-testing, sprite positions and gesture validation must use consistent units.

## 4.2 Rendering

Both encounters should use the existing sprite and animation services.

Support placeholders without requiring any final production assets.

Prefer simple transform-based effects over complex sprite-sheet animations for:

- Bobbing
- Recoil
- Shrinking
- Rotation
- Impact flashes
- Fade-outs

Do not build an additional rendering engine.

## 4.3 Passive effects

Both new penalties must use the existing click-through effect infrastructure.

Effects must:

- Remain visible above ordinary applications.
- Allow mouse and keyboard activity underneath.
- Automatically expire.
- Disappear immediately when the application stops.
- Never steal focus.

Avoid unnecessarily redrawing a complete full-screen bitmap at 60 FPS. Use the existing rendering approach and reduce effect update frequency if profiling identifies a problem.

## 4.4 Game selection

Add the new encounters to the existing scheduler.

Normal mode randomly selects from all available games.

Add manual controls for:

- Trigger Ghost Trap
- Trigger Zombie Invasion
- Trigger Spectral Fog
- Trigger Zombie Infection

Preserve existing game selection and scheduling behaviour.

## 4.5 Demo mode

Demo mode must use real gameplay and real desktop overlays.

For recording:

- Spawn the ghost in a predictable position.
- Use repeatable ghost movement.
- Spawn zombies in a repeatable wave pattern.
- Provide configurable encounter duration if already supported.
- Allow manual success or failure testing through the main application's diagnostics controls.

No pre-rendered gameplay videos.

A fixed random seed is acceptable for reproducibility.

## 4.6 Cleanup

Each encounter must cancel safely during:

- Normal completion
- Timeout
- Application shutdown
- Stop Haunting
- Monitor reconfiguration
- Unexpected exceptions

Specifically ensure that Ghost Trap always releases mouse capture and clears any incomplete drawing.

Zombie Invasion must stop its spawn scheduler and remove all enemy objects on completion.

Neither game should leave behind visual overlays or input handlers.

---

# 5. Development sequence

## Stage 1 — Ghost Trap placeholders

1. Add floating ghost placeholder.
2. Add draggable magical pen.
3. Draw freehand trail.
4. Implement closed-loop validation.
5. Implement point-in-polygon test.
6. Trigger success/failure.
7. Add placeholder fog penalty.

**Done when:** The player can draw a rough circle around a moving ghost, successfully trap it, or ignore it and receive the fog effect.

## Stage 2 — Zombie Invasion placeholders

1. Add brain placeholder.
2. Implement zombie spawning and movement.
3. Implement click-to-attack.
4. Add health and hit reactions.
5. Implement victory and failure.
6. Add placeholder handprint penalty.

**Done when:** Eight zombies can be defeated through direct clicks, or can reach the brain and trigger failure.

## Stage 3 — Visual polish

Replace placeholders with production art.

Prioritise:

1. Ghost personality and capture animation.
2. Zombie movement and satisfying hit effects.
3. Spectral fog.
4. Zombie handprints and infection.
5. Sound effects and transitions.

Avoid changing gameplay rules while replacing artwork.

---

# 6. Acceptance criteria

## Ghost Trap

- [ ] Ghost floats above the real desktop.
- [ ] Magic pen can be picked up without stealing focus.
- [ ] Drawing continues across transparent regions.
- [ ] A closed loop containing the ghost succeeds.
- [ ] Open or missed loops do not accidentally succeed.
- [ ] Additional attempts are possible before timeout.
- [ ] Success plays a capture animation.
- [ ] Timeout produces click-through spectral fog.
- [ ] Mouse capture is always released.

## Zombie Invasion

- [ ] Zombies spawn in small waves.
- [ ] Zombies advance toward a visible brain.
- [ ] Clicking zombies damages them.
- [ ] Clicking empty desktop areas passes input through.
- [ ] Zombie health and death animations work.
- [ ] Reaching the brain reduces its health.
- [ ] Defeating all zombies triggers victory.
- [ ] Brain destruction or timeout triggers failure.
- [ ] Handprint/infection penalty is fully click-through.
- [ ] Spawning and movement stop cleanly after completion.

## Integration

- [ ] Both games work with placeholders.
- [ ] Both use existing scheduling and overlay services.
- [ ] No changes are necessary to the first two games.
- [ ] All four encounters remain independently triggerable.
- [ ] Automatic selection works across all four.
- [ ] Normal desktop operation is preserved throughout.
- [ ] All temporary effects are reversible.

**Definition of done:** Four distinct, fully playable mini-games share one reliable desktop haunting runtime, with each game delivering a unique failure effect and supporting straightforward replacement of placeholder artwork.
