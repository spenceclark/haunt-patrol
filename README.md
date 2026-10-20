# Haunt Patrol

A small Windows desktop game that haunts your real desktop. Every so often a creature appears on top of
whatever you're working on and starts a short mouse mini-game. Win it and the creature disappears. Lose it,
or ignore it, and your screen gets a temporary supernatural effect: cracked glass or dripping slime.

Your applications stay usable the whole time. Transparent areas click through to the windows underneath,
the overlays never take keyboard focus, and penalty effects are entirely click-through. Nothing on your
system is changed.

## Encounters

| Encounter | How to win | If you fail or ignore it |
|---|---|---|
| **Skeleton Siege** (20 s) | The skeleton walks back and forth. Grab the catapult's projectile, drag back to aim (part of the arc is shown), and release to fire. Aim ahead of the skeleton. You have 3 shots. | It throws a bone at the screen and the glass cracks. |
| **Witch's Familiar** (25 s) | Drag the broom near the cat to make it run away, and herd it into the portal. | The cat knocks over the cauldron and slime gets all over the screen. |
| **Ghost Trap** (25 s) | Pick up the magic pen and carry it to the floating ghost. It starts drawing once it's close; then draw a loop around the ghost. It splits into two smaller ghosts that edge away from the pen, and each needs its own loop. Failed loops fade, and you can try again. | The ghosts escape laughing and spectral fog rolls in, with faces and glowing eyes. |
| **Zombie Invasion** (25 s) | Zombies come in waves. Click them to zap them before they reach the brain. Brutes take two hits, and runners are fast. A threat meter and the brain's glow show how close the nearest zombie is. The brain survives one bite. | Zombie handprints slap onto the screen and green mould spreads in from the edges. |

Gameplay tuning (speeds, ammo, waves, loop sizes) lives in `SkeletonSiegeOptions`, `GhostTrapOptions` and
`ZombieInvasionOptions`, next to each encounter.

## Build and run

Requirements: Windows 10/11 and the .NET 10 SDK.

```bash
dotnet run --project HauntPatrol
```

When the app starts, automatic hauntings are **off**. Click **Start Haunting** to turn them on, or use the
demo triggers to start a specific encounter or penalty straight away.

Command-line options, for recording demos and smoke tests:

```bash
HauntPatrol.exe --demo --trigger skeleton
```

- `--demo` makes positions and timing deterministic.
- `--trigger skeleton|cat|ghost|zombie|glass|slime|fog|infection` starts that event immediately.
- `--start`, `--interval <seconds>`, `--all` turn on automatic hauntings, set the average interval, and use all monitors.
- `--selftest` runs the game-rule checks (physics, steering, exactly-once resolution, assets) without
  showing any windows. It exits with code 0 if they pass and writes `selftest.log` next to the exe.

The app writes a diagnostic log to `haunt.log` next to the exe. It records DPI awareness, monitor layout,
the actual rectangle of each overlay window and the foreground window before and after each encounter.

## Controls

- **Start / Stop Haunting**: turns automatic encounters on or off. Stop also removes every overlay immediately.
- **Trigger …**: start any of the four encounters or four penalties straight away. Ignored if another event is already running.
- **Force Win / Force Fail**: end the running encounter immediately with that outcome. Force Fail is followed by the encounter's penalty.
- **Primary only / All monitors**: in All monitors mode, encounters pick a random monitor and penalties cover every monitor.
- **Encounter interval**: the average wait between automatic encounters. The actual wait is between 0.5× and 1.5× this value (exactly 1× in demo mode).
- **Sound enabled**, **Demo mode**, **Exit**.

## Artwork and sound

All the images in `HauntPatrol/Assets` are production art. They're imported from high-resolution masters
in `ART-PROOF/`, which is kept out of git. `tools/ImportSprite.cs` builds the character sheets and
`tools/ImportProp.cs` sizes the props and effects. The sounds are production too: `tools/process-audio.sh`
trims and balances the ElevenLabs masters. `ART-ASSETS.md` has the commands. `tools/MakeAssets.cs` only fills
in missing placeholder files. Never run it with `--force` on production assets:

```bash
dotnet run tools/MakeAssets.cs
```

- **Characters** (`Assets/Characters/<Id>/animations.json`) define the logical size in DIPs, the pivot, and
  one entry per animation clip. Each clip has a source sheet, frame size, frame count, fps and a loop flag.
  Frames are read left to right, then top to bottom, and are stretched to the logical size, so
  higher-resolution art works without code changes. Character art faces right. If a clip is missing, the
  idle clip plays instead.
- **Props and effects** (`Assets/Props/*.png`, `Assets/Effects/*.png`) are single images. Their logical size
  is their pixel size and their pivot is the centre.
- **Sounds** (`Assets/Audio/*.wav`, uncompressed PCM): encounter, pickup, launch, impact, hiss, success, failure, glass,
  slime, laugh, zap, groan, slap, fog, screech, thud, warning. Sounds play through `waveOut` on a worker thread, preloaded, so several can
  overlap (repeats too, up to 16 voices). WPF's `MediaPlayer` was dropped because it stalled rendering by 100–500 ms per sound.

## How it works

Each participating monitor gets two borderless, topmost WPF windows that never activate:

- An **interactive** per-pixel-alpha window for creatures and props. Windows hit-tests it by pixel alpha,
  so transparent pixels pass clicks through to the app underneath. Pressing on a visible sprite starts a
  drag, and WPF mouse capture keeps it going while the cursor moves over transparent areas.
- A **passive** `WS_EX_TRANSPARENT` window for the HUD, particles, aim arc and penalties. It is completely
  click-through.

Focus is never taken: `ShowActivated=False`, `WS_EX_NOACTIVATE`, `WM_MOUSEACTIVATE` returns `MA_NOACTIVATE`,
the overlays contain no `Button` controls, and there are no `Focus()` or `Activate()` calls. Windows are
placed in physical pixels with `SetWindowPos` (the app is PerMonitorV2), and each window converts to DIPs
using its own DPI.

`HauntingController` owns the state machine (Stopped → Idle → EncounterStarting → EncounterActive →
EncounterEnding / PenaltyActive → Idle), the scheduler, a frame loop that runs once per rendered WPF frame (`CompositionTarget.Rendering`), with `Stopwatch` delta time, and
every window. If a tick or input handler throws, the encounter is cancelled and every overlay is closed.
Encounters implement `IEncounter` and use only what `EncounterContext` gives them: two canvases, sprites,
tweens, particles, audio, the remaining time, and `Resolve`/`Complete`. `Resolve` decides the outcome
exactly once.

```
HauntPatrol/
  Core/        controller, encounter contract and context
  Platform/    Win32 interop, monitor enumeration
  Overlays/    overlay window and manager
  Rendering/   sprite asset loader, sprite + animation player, particles
  Encounters/  SkeletonSiege, WitchsFamiliar, GhostTrap, ZombieInvasion
  Effects/     glass, slime, spectral fog and zombie infection penalties
  Audio/       WAV playback
tools/MakeAssets.cs   placeholder art and sound generator
tools/ImportSprite.cs character sheets from exported frames
tools/ImportProp.cs   props and effects from single high-resolution images
tools/process-audio.sh sound effects from the ElevenLabs masters (ffmpeg)
```

## Known limits

- **CPU:** WPF layered windows are redrawn in software. On a 2560×1440 monitor an active encounter uses
  about one CPU core. The frame loop runs on `CompositionTarget.Rendering`, and `haunt.log` records frame
  statistics for each encounter, plus any single frame that takes longer than 100 ms.
- **Not tested:** mixed-DPI monitor setups, monitors at negative coordinates, touch and pen input.
- **Display changes:** any display settings change (resolution, scaling, plugging in a monitor) cancels the
  current event.
- **Z-order:** other topmost windows can cover creatures.
