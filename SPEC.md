# Haunt Patrol — Application Build Specification

**Version:** 1.0 — Hackathon implementation  
**Platform:** Windows 11  
**Stack:** .NET 10 / C# / WPF  
**Build window:** Monday 19 October – Thursday 22 October 2026  
**Development approach:** Gameplay first, placeholder assets, progressive visual replacement.

## 1. Product concept

Haunt Patrol is a lightweight desktop game that runs alongside ordinary Windows applications.

At random intervals, supernatural creatures appear over the user's actual desktop and initiate short interactive challenges.

The user can play the encounter or ignore it and continue working.

- **Victory:** Defeat or capture the creature. It disappears, leaving the desktop unaffected.
- **Failure:** The creature inflicts a temporary supernatural visual effect on the desktop, such as cracked glass or dripping slime.
- **Ignored encounter:** The timer expires, producing the same failure outcome.

All interactions occur inside transparent desktop overlays. The underlying applications remain usable.

No files, desktop settings, windows or other user data are changed.

### Core design principle

The game is an amusing interruption, not a modal application.

It should feel as though supernatural creatures have invaded the desktop itself.

## 2. Scope and priorities

### P0 — Required

- Native Windows application with a small main control window
- Configurable random encounter scheduler
- Select primary monitor or all monitors
- Transparent interactive desktop overlay
- Mouse interactions without stealing application focus
- Placeholder artwork through the actual sprite-rendering pipeline
- Encounter timer and state machine
- Skeleton Siege encounter
- Glass-shattering failure effect
- Immediate cancellation and cleanup
- Manual encounter triggers for testing and demonstration

### P1 — Target for submission

- Witch's Familiar encounter
- Slime failure effect
- Character animation
- Particle effects
- Sound effects
- Improved game HUD
- Polished transitions
- Deterministic demo mode

### P2 — Only if time remains

- Ghost Trap or Spider Invasion
- Haunting meter and score
- Creature reactions to actual application window boundaries
- Cross-monitor creature movement
- Additional effects or encounters

**Scope rule:** Do not start a third encounter until two complete encounters are playable, visually polished and reliably demonstrated.

## 3. Technical foundation

Reimplement the validated desktop overlay architecture documented in the earlier Desktop Haunting PoC findings.

The existing PoC source must not be copied into the submitted application. All implementation code is written during the permitted hackathon period.

### Overlay architecture

Use two independent window types per participating monitor.

**InteractiveOverlayWindow**

- Borderless, per-pixel transparent WPF window
- Topmost but non-activating
- Invisible in taskbar and Alt+Tab
- Transparent pixels pass input to underlying applications
- Visible interactive game objects receive mouse input
- Used for creatures, weapons, projectiles and interactive objects

**PassiveOverlayWindow**

- Borderless transparent WPF window
- Topmost but non-activating
- Completely click-through using `WS_EX_TRANSPARENT`
- Used for screen damage, magic effects, particles and HUD elements that do not require clicking
- Can remain visible after the interactive overlay closes

These windows must be able to coexist.

### Windows interoperability

Recreate the proven combination:

- `AllowsTransparency=True`
- `ShowActivated=False`
- `WS_EX_NOACTIVATE`
- `WS_EX_TOOLWINDOW`
- `WM_MOUSEACTIVATE` returning `MA_NOACTIVATE`
- `SetWindowPos` with `SWP_NOACTIVATE`
- PerMonitorV2 DPI awareness
- Monitor enumeration using `Screen.AllScreens`

Interactive windows must not use `WS_EX_TRANSPARENT`.

Avoid WPF Button-derived controls in overlay game objects because their focus behaviour may activate the window.

Use non-focusable visual elements with explicit mouse event handlers.

### Multi-monitor behaviour

Preserve multi-monitor support but make it configurable:

- **Primary Only** — default, used for demonstrations
- **All Monitors** — distribute encounters and effects across displays

Use physical pixel coordinates for native window positioning. Convert into the appropriate WPF coordinates for each window.

Do not spend significant hackathon time supporting unusual monitor layouts beyond the already-established architecture.

## 4. Rendering and asset architecture

**All gameplay must be functional before final artwork is available.**

The application must support placeholder sprites and production sprites through the same rendering interfaces.

### Rendering approach

Use a WPF `Canvas` containing:

- `Image` elements for sprites
- `Path`, `Line` and `Ellipse` for temporary geometry, trajectories and particles
- `RenderTransform` for translation, rotation and scaling
- Sprite frame selection for animated sequences

Use a central update loop for active gameplay, targeting 60 FPS where practical, falling back to 30 FPS if layered-window performance is inadequate.

Use `Stopwatch`-derived delta time for physics and movement.

Avoid an unnecessary game engine dependency.

### Sprite asset contract

Every game character should be represented by an asset definition with:

- Character identifier
- Logical display dimensions
- Pivot or anchor point
- Animation identifier
- Sprite source path
- Frame size and frame count
- Frame rate
- Looping behaviour

Supported animation states should include, as appropriate:

- Idle
- Move
- Attack / Action
- Hit
- Defeat
- Victory

Not every animation needs artwork initially.

An animation may consist of a single static image.

### Placeholder strategy

Create simple transparent PNGs at the intended sprite dimensions.

For example:

- Skeleton: white rectangle with a skull symbol
- Cat: black ellipse with green eyes
- Catapult: brown triangle
- Broom: narrow rectangle
- Portal: purple circle
- Projectile: yellow circle

Keep the exact same asset identifiers and anchor points when replacing these assets with production artwork.

The graphics loader must not contain game-specific assumptions about final artwork.

### Asset organisation

```text
Assets/
  Characters/
    Skeleton/
      idle.png
      attack.png
      hit.png
      defeat.png
      animations.json
    Cat/
      idle.png
      run.png
      hiss.png
      animations.json
  Props/
    catapult.png
    broom.png
    portal.png
    projectile.png
  Effects/
    glass.png
    slime.png
    particles/
  Audio/
    encounter.wav
    launch.wav
    impact.wav
    success.wav
    failure.wav
```

Sprite sheets and frame metadata may replace individual PNGs where appropriate.

Final assets should support transparent backgrounds and consistent character scale.

No Unity, Blender or AI generation runtime integration is needed. Those are external asset-production tools.

## 5. Game runtime

### State machine

```text
Stopped
   |
   v
Idle
   |
   | Scheduled or manual trigger
   v
EncounterStarting
   |
   v
EncounterActive
   |
   +--- Victory ---> EncounterEnding ---> Idle
   |
   +--- Timeout ---> PenaltyActive ---> Idle
```

The controller is responsible for:

- Scheduling encounters
- Selecting encounter types
- Creating overlays
- Starting and stopping gameplay
- Tracking encounter time
- Determining success or failure
- Triggering penalties
- Cleaning up all temporary windows and resources

Only one active encounter is permitted.

An encounter must not begin while a previous encounter or penalty is active.

### Encounter interface

Each mini-game implements a common contract covering:

- `Initialize(context)`
- `Start()`
- `Update(deltaTime)`
- `OnPointerDown(position)`
- `OnPointerMove(position)`
- `OnPointerUp(position)`
- `End()`

The encounter reports completion through a result of `Victory`, `Failure` or `Cancelled`.

The encounter context provides access to:

- Current monitor dimensions
- Sprite and animation services
- Pointer input
- Audio playback
- Particle effects
- Time remaining
- Completion callbacks

Individual games must not manage desktop overlay windows themselves.

### Input handling

**Important technical risk:** The PoC established click-through behaviour, but the finished games need mouse dragging.

A transparent overlay will not automatically receive mouse movement over alpha-zero regions.

For dragging interactions:

1. The player presses the left mouse button on a visible interactive object.
2. The object attempts WPF mouse capture.
3. Movement is tracked for the duration of the drag.
4. Mouse capture is released on pointer-up, cancellation, timeout or error.

Test this behaviour before implementing game-specific dragging.

If capture behaves unreliably in non-activating overlay windows, investigate cursor-position polling during the active drag rather than installing global input hooks.

Do not require continuous passive tracking of the mouse over the entire desktop.

## 6. Encounter One — Skeleton Siege

**Priority:** P0  
**Suggested duration:** 20 seconds

### Premise

A skeleton appears on the desktop and begins preparing to attack.

A catapult materialises some distance away.

The player must launch a projectile and hit the skeleton before the timer expires.

### Gameplay

1. Skeleton appears with an entrance animation.
2. Catapult appears.
3. Player clicks the catapult projectile.
4. While holding the mouse button, drags backwards to aim.
5. An aiming trajectory is displayed.
6. Releasing the button fires the projectile.
7. Projectile follows a ballistic arc.
8. Collision with skeleton triggers victory.
9. Missing allows another shot if sufficient time remains.

### Physics

Implement simple 2D projectile physics.

- Position
- Initial velocity derived from drag vector
- Gravity
- Optional velocity damping
- Screen bounds
- Circle or rectangle collision detection

Use fixed gameplay values configured in code; no need for a physics engine.

Make the hitbox generous enough to be enjoyable.

### Placeholder implementation

Initially:

- Skeleton is a rectangle.
- Catapult is a triangle.
- Projectile is a circle.
- Projectile arc is a WPF path.
- Collision produces a simple expanding circle.

Do not wait for skeleton animations before making the encounter playable.

### Victory

- Skeleton plays hit/defeat animation.
- Brief particle burst.
- Creature and game props disappear.
- Overlay closes.

### Failure

- Skeleton plays a short attack animation.
- A projectile or bone flies toward the viewer.
- Screen impact occurs.
- Glass fracture penalty appears.

## 7. Encounter Two — Witch's Familiar

**Priority:** P1  
**Suggested duration:** 25 seconds

### Premise

A witch's black cat escapes onto the desktop.

A magical portal appears at one edge of the screen.

The player must herd the cat back into the portal using a broom.

### Gameplay

1. Cat emerges from the portal.
2. Cat moves around the screen.
3. Broom becomes available.
4. Player clicks and holds the broom to drag it around.
5. Moving the broom near the cat causes the cat to flee away from it.
6. Player guides the cat toward the portal.
7. Cat entering the portal triggers victory.

### Cat movement

Implement a simple 2D steering behaviour:

- Cat has position and velocity.
- Broom proximity creates a repulsive force.
- Movement is bounded to the selected monitor.
- Cat decelerates when not being chased.
- A small amount of wandering gives it personality.
- Portal has a generous capture region.

Prioritise predictability and satisfying control over realistic animal behaviour.

Avoid complex pathfinding.

Use deterministic movement parameters in demo mode.

### Placeholder implementation

- Cat is a black circle.
- Broom is a coloured rectangle.
- Portal is a purple circle.
- Direction of movement is shown by an optional small arrow.

### Victory

- Cat is pulled into the portal.
- Portal contracts and vanishes.
- No penalty.

### Failure

- Cat knocks over a magical cauldron.
- Green slime splashes onto the desktop.
- Cat escapes.

## 8. Penalty and visual effects system

Penalties must be independent of encounter gameplay.

A penalty should expose:

- `Start()`
- `Update(deltaTime)`
- `Stop()`
- Duration
- Participating monitors

### Glass fracture

Initial implementation:

- Red X from the existing PoC design, rebuilt from scratch

Final implementation:

- Impact point
- Expanding fracture lines
- Radial cracking
- Optional small falling glass fragments
- Automatic fade-out

Duration: approximately 10 seconds.

### Slime

Initial implementation:

- Large green shapes overlaying the desktop

Final implementation:

- Green splashes spreading outward
- Large droplets
- Slime dripping downward from the upper screen edge
- Optional bubbles
- Gradual fade or dissolve

Duration: approximately 10 seconds.

### Required behaviour

All penalty windows are completely click-through.

The user must be able to:

- Type normally
- Click through the effect
- Scroll underlying windows
- Switch applications
- Ignore the effect until it disappears

No changes to the screen resolution, desktop wallpaper or existing application windows are permitted.

Prefer visually animated overlays over manipulating the actual desktop.

## 9. Animation system

Implement a small reusable animation player supporting:

- Named animation clips
- Looping and one-shot playback
- Frame timing
- Playback speed
- Animation completion notification
- Replacing one animation with another

Support simple transform animations independently of sprite sheets:

- Bobbing
- Rotation
- Scale / squash and stretch
- Translation
- Fade
- Brief recoil or shake

Use these transforms to add life to static placeholders while artwork is being produced.

Avoid building a general-purpose animation editor.

## 10. Audio

Audio is optional during the first playable implementation but important for the final demonstration.

Use local sound effects for:

- Encounter arrival
- Weapon pickup
- Projectile release
- Hit
- Creature reaction
- Victory
- Failure
- Glass breaking
- Slime impact

Audio should be short and responsive.

Provide a mute option in the main application.

Never play sounds when the application is stopped.

## 11. Scheduling and user controls

### Main window

The application starts as a normal Windows taskbar application.

Main controls:

- Start Haunting
- Stop Haunting
- Trigger Skeleton
- Trigger Cat
- Trigger Glass Penalty
- Trigger Slime Penalty
- Monitor selection: Primary Only / All Monitors
- Encounter interval
- Sound enabled/disabled
- Current state
- Recent event log
- Exit

Automatic encounters should be disabled on startup.

### Scheduler

Normal mode:

- Wait a random configurable interval.
- Select an available encounter.
- Start the encounter.
- Wait for completion.
- Schedule the next event.

Default interval may be several minutes, configurable to shorter periods during testing.

### Demo mode

Provide manual controls that immediately launch specific encounters.

Also allow the developer to trigger each penalty directly.

Use deterministic character positions and behaviour where practical so the demo can be recorded reliably.

Demo mode must exercise the same real overlay and rendering implementation as normal mode.

No canned video or simulated desktop integration.

## 12. Reliability and safety

The application must be harmless and fully reversible.

Requirements:

- Explicit opt-in before automatic events
- No global keyboard hooks
- No modification of external windows
- No file deletion or user-data modification
- No network access required
- No persistent effects
- No startup registration
- All mouse capture released during cleanup
- All overlay windows closed when stopping or exiting
- Automatic cleanup after unexpected exceptions
- No overlapping encounters

If a game throws an exception, immediately cancel the encounter and remove all overlays.

Use the existing PoC findings as the reference for focus behaviour, per-pixel hit-testing and shutdown safety.

## 13. Suggested code organisation

```text
HauntPatrol/
  App.xaml
  MainWindow.xaml

  Core/
    HauntingController.cs
    EncounterScheduler.cs
    EncounterContext.cs
    EncounterResult.cs
    IEncounter.cs

  Platform/
    MonitorService.cs
    NativeMethods.cs
    WindowStyleHelper.cs

  Overlays/
    OverlayManager.cs
    InteractiveOverlayWindow.xaml
    PassiveOverlayWindow.xaml

  Rendering/
    Sprite.cs
    SpritePlayer.cs
    SpriteAssetLoader.cs
    AnimationClip.cs
    ParticleSystem.cs

  Input/
    PointerController.cs

  Encounters/
    SkeletonSiege/
      SkeletonSiegeEncounter.cs
      Projectile.cs
      Catapult.cs
    WitchsFamiliar/
      WitchsFamiliarEncounter.cs
      CatController.cs
      BroomController.cs

  Effects/
    IPenaltyEffect.cs
    GlassEffect.cs
    SlimeEffect.cs

  Audio/
    AudioService.cs

  Assets/
    Characters/
    Props/
    Effects/
    Audio/
```

Keep the architecture intentionally lightweight.

The goal is reusable encounter infrastructure, not a generic 2D game engine.

## 14. Build plan

### Monday evening — Foundation

**Goal:** A working desktop game runtime using only placeholders.

- Create new .NET 10 WPF project.
- Reimplement overlay infrastructure from the PoC findings.
- Add primary-monitor-only mode.
- Establish the sprite-loading and rendering system.
- Display placeholder sprites over the desktop.
- Test mouse capture and dragging without stealing focus.
- Implement the common encounter interface and controller.
- Add manual trigger controls.

**Monday acceptance:** A draggable placeholder object appears above another application, moves correctly across transparent desktop space, and disappears cleanly without disrupting the original application.

### Tuesday — First complete game

**Goal:** Skeleton Siege works end-to-end.

- Catapult input
- Projectile physics
- Aiming trajectory
- Skeleton collision
- Encounter timer
- Victory and failure states
- Placeholder glass effect
- Automatic scheduling

**Tuesday acceptance:** Play, win, deliberately lose or ignore the skeleton encounter, with all states completing cleanly.

### Wednesday — Second game and artwork

**Goal:** Two playable games and initial production assets.

- Cat movement and steering
- Broom dragging
- Portal capture
- Slime penalty
- Replace character placeholders with production artwork
- Add character animations
- Introduce basic sound effects

**Wednesday acceptance:** Both encounters are genuinely playable on the user's desktop and both penalties are animated, click-through and automatically reversible.

### Thursday — Polish and submission

**Goal:** Finished visual experience and recorded demonstration.

- Finalise artwork and visual consistency
- Add creature personality and reactions
- Polish transitions and particles
- Profile rendering performance
- Fix input and lifecycle bugs
- Prepare README and screenshots
- Record demonstration video
- Make repository public
- Submit before Thursday evening

Do not begin significant new features on Thursday.

## 15. Acceptance tests

### Desktop integration

- [ ] Encounters appear over real Windows applications.
- [ ] Transparent desktop areas remain interactive.
- [ ] Clicking and dragging game objects works.
- [ ] Existing application focus is preserved.
- [ ] Penalty effects are entirely click-through.
- [ ] The selected monitor is covered correctly.
- [ ] Stopping or exiting removes every overlay.

### Gameplay

- [ ] Skeleton can be hit with a projectile.
- [ ] Missing or ignoring Skeleton Siege produces the glass penalty.
- [ ] Cat responds to broom movement.
- [ ] Cat can be guided into the portal.
- [ ] Ignoring or failing the cat encounter produces slime.
- [ ] Encounters cannot overlap.
- [ ] Success and failure resolve exactly once.

### Graphics

- [ ] All gameplay works with placeholder images.
- [ ] Production sprites can replace placeholders without changing game logic.
- [ ] Character scale and placement remain consistent.
- [ ] Transparent sprites do not block unrelated desktop clicks.
- [ ] Animations remain responsive on the demonstration machine.

### Submission

- [ ] Source code created during build week.
- [ ] Repository is open-source and contains build instructions.
- [ ] No PoC implementation code is reused.
- [ ] App has been tested on the real desktop.
- [ ] Demonstration video shows real gameplay and actual desktop overlays.
- [ ] User can stop the application and restore an ordinary desktop immediately.

## 16. Definition of done

Haunt Patrol is complete when a user can:

1. Launch the application.
2. Enable hauntings.
3. Continue using Windows normally.
4. Encounter a skeleton or escaping witch's cat.
5. Attempt the corresponding mouse-controlled mini-game.
6. Win and return to normal, or fail and experience a temporary supernatural effect.
7. Ignore an encounter without losing access to their desktop.
8. Stop the application and remove all effects immediately.

**Success means an entertaining, visually convincing desktop experience — not the number of monsters or features implemented.**
