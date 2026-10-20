# Showreel shot list (one OBS session, about 45 min)

The existing clips were recorded before the production art and props landed (only `full-run` has final
art, and that's 1080p). So this re-records everything once, at a quality that allows zooms in the edit.

## Setup (once)

- **OBS:** Display Capture, canvas and output **2560×1440, 60 fps** (no downscale). NVENC, CQP 16.
  Container **MKV** (survives crashes). Desktop audio only, no mic.
- **Windows:** Do Not Disturb on. Mouse pointer size **3** (Settings → Accessibility → Mouse pointer), so the
  cursor reads at 1080p. Nothing personal visible (taskbar, tabs, notifications).
- **Background "work":** keep the dark theme (the glass cracks and ghost glow are tuned for it), but make it
  readable: editor font zoomed to ~200%, side panels closed, same layout for every take. A doc or email
  works too. Check the black cat is visible against it.
- **Launch** the Release build so nothing builds on camera, from a terminal you can minimise:
  `HauntPatrol\bin\Release\net10.0-windows\HauntPatrol.exe --demo --trigger <name>`
  `--demo` keeps positions identical between takes, so retakes cut together.
- **Every take:** 2 s of stillness before the encounter and 2 s after the effect clears. I trim the launch.
- **Naming:** `A1.mkv`, `C3-take2.mkv` and so on, or just tell me which file is which.

## A. Normal day (cold open)

| # | Shot | How |
|---|---|---|
| A1 | 30 s of ordinary work: typing, scrolling, clicking. No creatures. | App not running |
| A2 | Typing mid-sentence when the first creature arrives on its own. Keep typing for 2–3 s, then react. | `--demo --start --interval 10` (the skeleton comes first) |

## B. Keep working over it (the headline)

The cursor must visibly click into the app and text must change while the creature is on screen.

| # | Shot | How |
|---|---|---|
| B1 | Skeleton patrols across the editor while you type a full, readable line. Then win it (that's C1). | `--trigger skeleton` |
| B2 | Zombies shamble over the text while you keep typing for ~6 s, then switch to zapping. | `--trigger zombie` |
| B3 | Under each penalty, keep typing, select text, scroll. ~10 s each. | `--trigger slime`, `glass`, `fog`, `infection` (4 takes) |

Type something a viewer can read and smile at, e.g. `// everything is fine` or "Q4 report: all under control".

## C. Wins (2–3 takes each)

| # | Shot |
|---|---|
| C1 | Skeleton: one **MISS!**, then lead the shot and hit. Hold through the crumble. |
| C2 | Cat: herd with the broom, get a close hiss, drive it into the portal. |
| C3 | Ghost: first loop, the split, **CATCH THEM BOTH!**, two loops, **GHOSTS BANISHED**. |
| C4 | Zombies: clear the whole wave, but let the meter reach **CRITICAL** once before you do. |

## D. Losses (play and fail, don't Force Fail, so the attack plays)

| # | Shot |
|---|---|
| D1 | Skeleton: three misses → bone thrown at the screen → glass cracks. |
| D2 | Cat: let it reach the cauldron → slime. |
| D3 | Ghost: let the timer run out → ghosts laugh → fog with faces. |
| D4 | Zombies: let them bite the brain → handprints and mould. |

## Optional

- **E1:** 10 s of the desktop with nothing happening (background for the end card).
- **F1:** 5–10 s of phone video over your shoulder at the desk, flinching when something appears. Real-world
  reaction is the cheapest way to make it feel like an advert.

## Voice-over (ElevenLabs, optional)

A deadpan film-trailer narrator: deep and slow, playing it completely straight. Stability about 35–45, style
high. Generate 2–3 versions of each line and export WAV, one file per line (`vo1-a.wav` and so on).

| # | Line | Lands on |
|---|---|---|
| vo1 | "It was an ordinary afternoon." | The cold open, about 0:03 |
| vo2 | "Your desktop… is haunted." | Just after the title slam, about 0:18 |
| vo3 | "Lose, and your screen pays the price." | The penalty montage, about 1:05 |
| vo4 | "Haunt Patrol. Keep working… if you can." | The end card, about 1:19 |

Music is Option 2, cut to 87.4 s (`showreel/out/music.wav`).
