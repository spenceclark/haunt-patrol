# Haunt Patrol: Art and Audio Asset Inventory

This is everything the final game needs from an artist or sound designer, and the exact format the game
loads. Every asset listed here already exists as a placeholder in `HauntPatrol/Assets`. Replace each file in
place, keeping its name, and the game picks it up. No code change is needed.

> `tools/MakeAssets.cs` only writes placeholder files that don't exist yet, so it never overwrites production
> art. `--force` regenerates every placeholder.

**Production art so far:** Skeleton (all five clips), Cat (all three), Ghost (all three) and the three zombies
(seven clips), imported from Ludo exports with `tools/ImportSprite.cs` (see §2.7).

---

## 1. Summary

| Kind | Count | Notes |
|---|---:|---|
| Animated characters | 7 | Skeleton, Cat, Ghost, Zombie, Brute, Runner, Brain |
| Animation clips | 21 | 20 are played by the game; the skeleton's `hit` is kept for later. A zombie `emerge` is an optional extra (§3). |
| Static props | 9 | catapult, projectile, bone, broom, portal, cauldron, pen, blaster, bonehand |
| Effect images | 3 | slime splat, zombie handprint, ghost face |
| Sound effects | 17 | Short WAVs |
| Procedural effects | — | Drawn in code, so no art is needed (see §6) |

---

## 2. Format

### 2.1 Images

- **PNG, 32-bit RGBA, with a fully transparent background (alpha = 0).**
- Overlays decide clicks by pixel alpha. On interactive sprites (§2.5), **any pixel with alpha above 0
  catches the mouse**, including faint glows, soft shadows and anti-aliasing halos.
- Use straight (non-premultiplied) alpha, which is the normal PNG export.
- **Characters face right.** The game mirrors them to face left.

### 2.2 Sizes: logical vs pixel

Every sprite has a **logical size in DIPs** (device-independent pixels; 1 DIP = 1 px at 100% Windows
scaling). This is the size it appears on screen and the size its hitbox uses.

- **Characters:** the logical size is set in `animations.json`. Frames of *any* pixel size are stretched to
  it. Author at **2× logical size** so the art stays sharp on 4K / 200% displays and in 2560×1440 recordings.
  For example, the Skeleton is 110×170 logical, so frames are 220×340 px.
- **Props and effect images** (single PNGs in `Props/` and `Effects/`) use **logical size = pixel size**. A
  PNG authored at 2× would show at double size. To use hi-res or animated art for a prop, give it a
  character folder instead (§2.4).

Keep the visible body **filling** the logical frame. Hit tests use the logical rectangle, with a small
padding around it, not the drawn pixels. Empty margin inside the frame makes targets feel inaccurate.

### 2.3 Character animation format

Each character has a folder `Assets/Characters/<Name>/` containing one sprite sheet PNG per clip and an
`animations.json`:

```json
{
  "id": "skeleton",
  "width": 110, "height": 170,
  "pivotX": 55, "pivotY": 170,
  "animations": {
    "idle":   { "source": "idle.png",   "frameWidth": 220, "frameHeight": 340, "frameCount": 6, "fps": 8,  "loop": true  },
    "attack": { "source": "attack.png", "frameWidth": 220, "frameHeight": 340, "frameCount": 4, "fps": 10, "loop": false }
  }
}
```

| Field | Meaning |
|---|---|
| `id` | Lower-case id the code asks for. The folder name matches it, case-insensitively. |
| `width`, `height` | **Logical** display size in DIPs. Keep the values listed in §3. |
| `pivotX`, `pivotY` | Anchor point in **logical** units, measured from the top-left. Position, rotation and scaling are applied around it. Keep the values listed in §3. |
| `source` | Sheet file name, relative to the folder |
| `frameWidth`, `frameHeight` | One frame's size in **pixels** (for example 2× the logical size) |
| `frameCount` | Number of frames in the clip |
| `fps` | Playback rate. A one-shot clip lasts `frameCount / fps` seconds. |
| `loop` | `true` repeats; `false` plays once and holds the last frame |

**Sheet layout:** frames are equal-sized cells read **left to right, then top to bottom**. The number of
columns is `sheet width / frameWidth`. A horizontal strip is simplest.

- No trimming, packing or rotation, and no padding between cells.
- The game reads only its own `animations.json`. Aseprite, TexturePacker and similar JSON atlases are not
  read.
- In Aseprite: *Export Sprite Sheet → Sheet type: Horizontal Strip*, with *Trim* off and padding 0.

**Clip timing drives gameplay.** For one-shot clips, the game waits for the clip to finish before the next
beat. For example, the skeleton's `hit` → `defeat` → fade, or its `attack` → bone throw. Keep one-shot
durations close to the targets in §3, or the pacing shifts.

**Missing clips are safe.** If a clip is missing, the character plays `idle` instead, and any "when
finished" step still runs after 0.35 s. You can deliver idle first and add the rest later.

### 2.4 Turning a prop into an animated or hi-res sprite

The loader checks `Characters/<id>/animations.json` **before** `Props/<id>.png`. To give a prop several
frames or 2× art, create `Assets/Characters/<Name>/animations.json` with the prop's id and an `idle` clip.

**Keep the pivot at the centre** (`pivotX = width/2`, `pivotY = height/2`). The code positions props by their
centre.

This is recommended for the **bone** (scaled up 10× as it flies at the screen) and the **portal**.

### 2.5 Interactive vs click-through layers

Each encounter draws on two overlay windows:

- **Interactive:** visible pixels receive the mouse. Keep these sprites free of low-alpha glows and
  shadows, or their clickable area grows.
- **Click-through:** never blocks the desktop. Glows, haze and semi-transparency are fine here.

Each asset's layer is listed in §3–§4.

### 2.6 What the code animates for you

Don't bake these into the artwork. The game applies them on top of whichever frame is showing:

- facing (horizontal flip)
- idle bobbing
- shakes, recoil and jitter
- squash and stretch on entrances and splits
- rotation: the zombies' shamble, the runner's lean, the broom's tilt, the portal's spin, the cauldron
  tipping over, spinning projectiles
- scale pulses: critical zombies, the brain's danger pulse
- fades, and shrinking for captures and deaths
- the rise out of the ground (a vertical scale from the feet)

### 2.7 Importing exported frames (Ludo and similar)

`tools/ImportSprite.cs` turns exported frames into a character folder in this format. Each clip's frames can
come from a ZIP or a folder of PNGs, played in file-name order. The sources are only read, never changed.

```bash
dotnet run tools/ImportSprite.cs -- --out HauntPatrol/Assets/Characters/Skeleton --id skeleton --frame 220x340 --logical 165x255 --clip idle=ART-PROOF/Skeleton/Idle.zip:15:loop --clip walk=ART-PROOF/Skeleton/walk.zip:24:loop --clip attack=ART-PROOF/Skeleton/attack.zip:20:once --clip hit=ART-PROOF/Skeleton/hit.zip:24:once --clip defeat=ART-PROOF/Skeleton/defeat.zip:24:once
```

Each `--clip` is `name=source:fps:loop|once`. A source can end in `#first-last` to take a contiguous range of its
frames, for example `zombie_brute_hit.zip#0-7`.

`--anchor` decides what lines up between clips:

- `feet` (the default) aligns the feet. The pivot goes at the feet, as low in the frame as the clips allow.
  Use it for upright characters such as the skeleton and zombies.
- `centre` aligns the paws and the middle of the body. The pivot is the frame centre. Use it for characters
  the game positions by their middle, such as the cat. Lining up on the paws, rather than the bounding box,
  stops a swishing tail from shifting the body, and lets the game mirror the sprite in place.
- `stance` aligns the feet, like `feet`, but centres on the feet themselves rather than the bounding box. A
  clip whose first frame is mid-stride can make that jump, which is why the zombies use `feet`.
- `box` aligns the centre of the first frame's bounding box. The pivot is the frame centre. Use it for
  floating characters with no feet or paws, such as the ghost; on the ghost, the "paws" would be its swaying
  tail.

The cat import:

```bash
dotnet run tools/ImportSprite.cs -- --out HauntPatrol/Assets/Characters/Cat --id cat --frame 360x240 --logical 240x160 --anchor centre --clip idle=ART-PROOF/Cat/cat_idle.zip:14:loop --clip run=ART-PROOF/Cat/cat_run.zip:36:loop --clip hiss=ART-PROOF/Cat/cat_hiss.zip:15:loop
```

Exports often differ in canvas size and character scale. The tool handles that as follows:

- It treats each clip's **first frame** as the neutral standing pose. It scales the clip so the figure in that
  frame has the same height in every clip, and anchors it by that frame's feet and body centre.
- It applies one transform per clip, so movement within a clip (bobs, recoil, falls) is preserved.
- It picks the largest standing height at which **every frame of every clip** fits the output frame without
  clipping, and sets the pivot to match.
- It prints the chosen height and pivot, and writes the strips plus `animations.json`.

Wide poses limit the size. For the skeleton, the defeat clip's scattered bones are 0.79× its height wide, so
the figure stands 270 px tall in a 220 × 340 frame. The skeleton is shown at logical 165 × 255 (1.5× the
original 110 × 170), so it stands about 200 DIP tall on screen. `--logical` only sets the display size and
pivot; it doesn't change the rendered frames.

---

## 3. Characters

Suggested frame counts come from the specs. "Targets" are durations that keep the current gameplay timing.

### 3.1 Skeleton (Skeleton Siege)

`Characters/Skeleton/` · id `skeleton` · logical **165 × 255** (scaled up 1.5× from 110 × 170) · pivot **(82.5, 211.5)** (the feet, with room
below for the defeat bone pile) · **interactive** layer · faces right · **production art: done**

| Clip | Loop | Delivered | Used for |
|---|---|---|---|
| `idle` | yes | 36 @ 15 fps (2.4 s) | Before it starts walking; the pause at each end of its patrol; just after the throw |
| `walk` | yes | 36 @ 24 fps (1.5 s) | Patrolling at 90 DIP/s |
| `attack` | no | 16 @ 20 fps (0.8 s) | Wind-up and throw when you fail. On the last frame the flying bone is spawned **at the hand** (`SkeletonSiegeOptions.BoneRelease`: +56, −158 DIP from the feet, held upright), and the skeleton switches to `idle`. |
| `hit` | no | 16 @ 24 fps (0.67 s) | **Not played yet.** Kept for a future non-lethal hit. It ends crouched, so it doesn't lead into `defeat`. |
| `defeat` | no | 25 @ 24 fps (1.04 s) | The killing shot: its own impact reaction, then crumbling to a bone pile. The code then fades it out. |

> If the throw is re-exported, re-measure `BoneRelease`.

### 3.2 Cat (Witch's Familiar)

`Characters/Cat/` · id `cat` · logical **240 × 160** (2.5× the original 96 × 64) · pivot **(120, 80)** at
the centre · **interactive** layer · faces right · **production art: done**, imported with `--anchor centre`
into 360 × 240 frames (1.5 px per DIP). The cat stands about 142 DIP tall, including its raised tail. The game
keeps the whole sprite on screen, at least half its size from each edge.

| Clip | Loop | Delivered | Used for |
|---|---|---|---|
| `idle` | yes | 42 @ 14 fps (3.0 s) | Nearly stopped (below 40 DIP/s) |
| `run` | yes | 24 @ 36 fps (0.67 s, about 3 strides) | Any real movement (above 60 DIP/s): a slow trot while wandering, a sprint when fleeing. Playback rate = speed ÷ 540 DIP/s, clamped to 0.25×–2.4×, so the paws keep pace with the ground. |
| `hiss` | yes | 25 @ 15 fps (1.67 s) | Broom within 220 DIP of the cat's centre (about 100 DIP from its body), until it's back beyond 260 DIP. Takes priority over idle and run. |

- **Frame rates are starting values.** The Ludo exports carry no timing metadata, so I chose these rates. They
  live in `animations.json`.
- **540 DIP/s is how much ground one run cycle covers at 1× playback.** It was measured from the planted paws.
  If the feet look like they lag behind the ground, raise `RunStrideSpeed` in `WitchsFamiliarEncounter`; if
  they outrun it, lower it.
- **Clips switch with a gap between "on" and "off" thresholds.** Without the gap, a speed or distance hovering
  near a line would keep flipping clips and restarting them.
- **Known art issues:**
  - `idle` frames 23–24 have a tail glitch: a flat black blob, then a differently drawn swoosh.
  - The hiss is now 2.5× larger. Check by eye that the open mouth reads clearly.
- **Violet edge glow, drawn by the game.** The glow is drawn in code, so don't bake it into the art.
  - It's built from each frame's alpha: a 2.5 DIP rim at 50% opacity, plus an 8 DIP soft glow at 30%, in
    `#A779E8`.
  - Each glow frame is built once and cached.
  - It's drawn on the click-through layer with the sprite's own pixels cut out, so it never covers the cat or
    enlarges its hitbox, and it follows every move, flip and the portal capture.
  - Any character can use it: `Sprite.AttachGlow(layer, new GlowOptions { ... })`. It's currently enabled only
    for the cat.

The game plays no separate capture or escape clips. It shrinks and spins the cat into the portal, and slides
it off-screen when it escapes.

### 3.3 Ghost (Ghost Trap)

`Characters/Ghost/` · id `ghost` · logical **240 × 280** (2× the original 120 × 140) · pivot **(120, 140)**
at the centre · **click-through** layer, so semi-transparency is welcome · faces right · **production art:
done**, imported with `--anchor box` into 360 × 420 frames (1.5 px per DIP). The ghost stands about 260 DIP
tall. The art has its own soft blue edge, so no extra glow is added.

| Clip | Loop | Delivered | Used for |
|---|---|---|---|
| `idle` | yes | 36 @ 12 fps (3.0 s float cycle) | Floating |
| `laugh` | no | 16 @ 46 fps (0.35 s) | A missed loop, and escaping on timeout. The small ghosts play the same clip once, slowed to 0.6 s (`GhostTrapOptions.SmallLaughSeconds`). |
| `scared` | yes | 42 @ 18 fps (2.33 s) | When the pen is near, during the split squash, and when the other ghost is caught |

- **Frame rates are starting values.** The exports carry no timing metadata.
- **The art floats on its own,** about 7 DIP of drift per cycle at this size. So the procedural bob is
  reduced to 2 DIP for the big ghost and 1.5 for the small ones (`GhostTrapOptions.Bob` and `SmallBob`).
- The same sprite is used for the **two small ghosts at 68% scale** (about 163 × 190 DIP). Their faces read
  clearly at that size. The capture effect (shrink and spin) is done in code.
- **Placement scales with the ghost:** the pen starts outside ink range, and the small ghosts' area keeps them
  fully on screen and at least about a body apart.
- **Known art issues:**
  - `laugh` frames 9 and 11 have dark grey smudges by the hands.
  - `scared` reads as a nervous grin (wide eyes, gritted teeth) more than real fright.
  - At 46 fps the full-size laugh is a quick cackle. If it feels rushed, about 0.6 s (26 fps) would show the
    full laugh more clearly.

### 3.4 Zombies (Zombie Invasion): three variants

All three are on the **interactive** layer: every visible pixel is clickable, which is what you want. They
face right. **Production art: done**, imported with `--anchor feet`. Feet sit on the baseline and each clip is
centred on the first frame's whole figure, so the body doesn't jump between walk, hit and die.

The boxes are **wider than the original placeholders** so the zombies keep their height. Their death and run
poses are about as wide as the zombie is tall. Clicks still reach the desktop through every transparent pixel;
the wider box only means clicks on wide poses (lying down, arms out) aren't lost.

| Variant | Folder / id | Logical size | Pivot (feet) | Frames | Stands about | Look |
|---|---|---|---|---|---|---|
| Normal | `Characters/Zombie/` · `zombie` | **228 × 175** | (114, 168.6) | 285 × 219 | 152 DIP | Ragged office zombie |
| Brute | `Characters/Brute/` · `brute` | **392 × 228** | (196, 212) | 490 × 285 | 182 DIP | Armoured, spiked helmet. Takes 2 hits. |
| Runner | `Characters/Runner/` · `runner` | **210 × 161** | (105, 147.4) | 263 × 201 | 126 DIP | Pink mohawk, trainers. The code leans it forward 12°. |

These are 1.75× the first production boxes (Normal 130 × 100, Brute 224 × 130, Runner 120 × 92), which were
themselves widened from the placeholders' 70 × 100, 100 × 130 and 62 × 92. The frames are 1.25 px per DIP
rather than 2, which keeps all three at about 66 MB once loaded (at 2× the brute alone would take about
100 MB). That's still sharper than 1:1 at 100% scaling. The hitboxes grew with the sprites, so zombies are a
little easier to click.

The pivots sit a few DIP above the bottom of the box, which leaves room for the death poses that sprawl just
below the feet line.

Delivered clips (no timing metadata in the exports, so these rates are starting values):

| Character | Clip | Frames | FPS | Duration | Loop | Notes |
|---|---|---|---|---|---|---|
| Normal | `walk` | 35 | 24 | 1.46 s | yes | Shamble; the code adds a ±7° sway |
| Normal | `die` | 25 | 40 | 0.63 s | no | Recoil, fall back, lie flat; then a 0.3 s fade |
| Brute | `walk` | 36 | 20 | 1.80 s | yes | Heavy stomp |
| Brute | `hit` | 8 (frames 0–7 of 16) | 32 | 0.25 s | no | Only the recoil; the rest of the export turns to face the camera, which would pop back to the side-on walk |
| Brute | `die` | 25 | 40 | 0.63 s | no | Rear up, topple back, dust; then a 0.3 s fade |
| Runner | `walk` | 42 | 36 | 1.17 s | yes | Sprint in place; the code adds the lean |
| Runner | `die` | 16 | 40 | 0.40 s | no | Stumble and sprawl forward; then a fast 0.12 s fade |

- **Dying zombies:** a dying zombie can't be clicked and no longer counts towards the threat, so the slightly
  longer deaths (0.63 s, against the ~0.4 s target) don't change gameplay. The last zombie's death only delays
  the victory banner by a fraction of a second.
- **Known art issues:**
  - Some exports reach the very edge of their canvas (normal and brute `die`, runner `die`, brute `hit`), so
    a few pixels of a sprawled limb may have been cut off in the source.
  - The death clips include a soft ground shadow, which is clickable on the interactive layer.

Optional extra: an `emerge` clip for climbing out of the ground. The game doesn't play it yet; it currently
scales the zombie up from its feet.

### 3.5 Brain (Zombie Invasion)

`Characters/Brain/` · id `brain` · logical **220 × 180** · frames **275 × 225** (1.25 px per DIP) ·
pivot **(110, 90)** at the centre · **click-through** layer · imported with `--anchor box`

Twice the brief's 110 × 90, so the brain (body about 120 DIP across) stays in proportion with the 1.75×
zombies. The frames are 1.25 px per DIP, like the zombies, which comes to about 18 MB once loaded.

| Clip | Loop | Frames | Duration | Used for |
|---|---|---|---|---|
| `idle` | yes | 36 @ 12 fps | 3.0 s | Floating; plays during the wave and again after each hit |
| `hit` | no | 12 @ 30 fps | 0.4 s | Being bitten (all frames; 36 fps for 0.33 s felt rushed) |
| `victory` | yes | 25 @ 20 fps | 1.25 s | Happy bounce when you win (the win sequence lasts about 0.95 s) |

The ZIPs had no timing metadata, so these frame rates are my choice. The body lines up within 1 px across
the first frames of the three clips. The idle clip only drifts about 1.5 DIP, so the code's 5 DIP bob stays.

The art has its own cyan aura. The coded danger glow behind it (purple to orange to red) is therefore
fainter than the placeholder's: opacity 0.3–0.7, down from 0.45–0.9. It's a 300 × 250 ellipse centred on the
brain body, above the wispy tail.

Known issues: some exports touch their canvas edge (2 px), so a few aura pixels may be cut off in the
source. The victory face reads as mild, not overjoyed.

---

## 4. Props (single PNG, logical size = pixel size, pivot at the centre)

| File | id | Size | Layer | Used in | Notes for the artist |
|---|---|---|---|---|---|
| `Props/catapult.png` | `catapult` | 150 × 110 | interactive | Skeleton | Base sits on the bottom edge. The projectile rests about 8 px below the **top centre** (the cup). The game mirrors it and rocks it 10° when firing. |
| `Props/projectile.png` | `projectile` | 36 × 36 | interactive | Skeleton | Spins in flight, so keep it roughly round. |
| `Props/bone.png` | `bone` | 70 × 26 | click-through | Skeleton | Spins and is **scaled up to 10×** as it flies at the viewer. Provide it hi-res via §2.4. |
| `Props/broom.png` | `broom` | 44 × 180 | interactive | Cat | Vertical, bristles at the bottom. The cat flees from a point 35% below the centre. It tilts ±35° while dragged. |
| `Props/portal.png` | `portal` | 200 × 200 | interactive | Cat | Spins continuously at 90°/s, so make it roughly radially symmetric. The capture radius is ~100 DIP. Good candidate for animated frames (§2.4). |
| `Props/cauldron.png` | `cauldron` | 120 × 96 | interactive | Cat | Tips over 100° about its centre on failure, and slime bursts from its rim. |
| `Props/pen.png` | `pen` | 28 × 110 | interactive | Ghost | Vertical, **writing tip at the bottom centre**. It's held tilted 25° and the trail starts at the tip. It's drawn at 125% (`GhostTrapOptions.PenScale`), with a pulsing glow on the tip added in code. |
| `Props/blaster.png` | `blaster` | 100 × 64 | click-through | Zombies | Sits bottom-left. Bolts leave from a point 30 px right and 22 px above its centre, so put the muzzle at the upper right. |
| `Props/bonehand.png` | `bonehand` | 46 × 70 | click-through | Zombies | Skeletal hand, fingers up. Two of them poke up from the bottom edge as a warning. |

## 5. Effect images (single PNG, click-through)

| File | id | Size | Used in | Notes |
|---|---|---|---|---|
| `Effects/slime.png` | `slime` | 260 × 200 | Slime penalty | Splat. 9 per screen, randomly rotated, scaled 0.5–1.7×, oozing slowly down. Semi-transparent edges are fine. |
| `Effects/handprint.png` | `handprint` | 180 × 210 | Infection penalty | Cartoon grimy zombie hand, fingers up. 7 per screen, randomly flipped and rotated ±30°, scaled 0.8–1.3×, shown at 88% opacity. |
| `Effects/ghostface.png` | `ghostface` | 160 × 180 | Spectral fog | Translucent ghostly face. Fades in and out at about 55% opacity while drifting up. |

---

## 6. Procedural effects (no art needed)

These are drawn in code and work at any resolution. Replacing any of them with artwork would need a code
change.

- **Glass penalty:** cracks, impact flash, falling shards
- **Slime penalty:** top-edge slime band, drips and falling drops, bubbles (the splats use `slime.png`)
- **Spectral fog:** fog puffs, glowing eyes (the faces use `ghostface.png`)
- **Zombie infection:** mould creeping in from the edges, crawling bugs (the hands use `handprint.png`)
- **Encounter effects:** aim arc and catapult band, projectile trail, magic pen trail, lightning bolts,
  ground crack, particles and rings, the brain's danger glow, health hearts
- **Character glow:** `Rendering/SpriteGlow.cs` builds silhouette edge-lights from sprite frames (§3.2).
- **Interface:** HUD, banners, popups ("MISS!", "1 REMAINING"), threat meter. These are WPF text and shapes.
  Fonts and colours can be changed in code.

---

## 7. Sound effects

**Format: uncompressed PCM WAV, 16-bit, mono or stereo, 22.05 / 44.1 / 48 kHz.**

- MP3, OGG and compressed WAV are **not** supported.
- Some audio tools save 24-bit or 32-bit float files with a "WAVE_FORMAT_EXTENSIBLE" header. The game skips
  those files without an error. **Export 16-bit PCM.**
- Keep sounds short and tight at the start. Several can play at once, and replaying a sound restarts it.
- Mute is handled in the app.

| File | Length | Plays when |
|---|---|---|
| `encounter.wav` | ~0.9 s | Any encounter appears |
| `pickup.wav` | ~0.1 s | Grabbing the projectile, broom or pen |
| `launch.wav` | ~0.35 s | Catapult fires; the bone is thrown at the screen |
| `impact.wav` | ~0.35 s | The skeleton is hit; a zombie bites the brain |
| `hiss.wav` | ~0.5 s | The cat appears; the broom gets close (at most every 2 s) |
| `success.wav` | ~0.7 s | Any victory |
| `failure.wav` | ~0.8 s | Any failure |
| `glass.wav` | ~0.9 s | The glass cracks |
| `slime.wav` | ~0.7 s | The cauldron spills; the slime penalty starts |
| `laugh.wav` | ~0.9 s | The ghost laughs (a missed loop, the split, escaping) |
| `zap.wav` | ~0.2 s | Zapping a zombie; a loop captures a ghost |
| `groan.wav` | ~0.9 s | Zombie warning; each zombie wave arrives |
| `screech.wav` | ~0.5 s | A wave with a runner arrives |
| `thud.wav` | ~0.2 s | A brute survives a hit |
| `warning.wav` | ~0.3 s | A zombie is about to reach the brain (at most every 1.5 s) |
| `slap.wav` | ~0.15 s | Each handprint hits the screen (7 in quick succession) |
| `fog.wav` | ~1.2 s | The spectral fog rolls in |

---

## 8. Delivery checklist

1. Keep the file and folder names exactly as listed. Ids are lower-case; folder names are case-insensitive.
2. Keep the **logical sizes and pivots** in §3–§5. Hitboxes, positions and tuning depend on them. Changing a
   character's `width`/`height` resizes it but also changes its hitbox.
3. Characters face right, sheets are uncropped grids, backgrounds are fully transparent, and interactive
   sprites have no low-alpha halos.
4. One-shot clips stay close to their target durations.
5. Check the result:
   - `HauntPatrol.exe --selftest` confirms every id loads and every WAV parses. The result goes to
     `selftest.log`.
   - `HauntPatrol.exe --demo --trigger skeleton|cat|ghost|zombie|glass|slime|fog|infection` shows each
     encounter or penalty with the real overlays.

### Suggested priority

1. **Skeleton, Cat, Ghost, Zombies (all three), Brain.** These are on screen the most, and the ghost and
   zombies matter most for the demo.
2. **Catapult, broom, portal, pen, cauldron, projectile:** what the player touches.
3. **Bone (hi-res), handprint, slime splat, ghost face:** the failure moments.
4. **Blaster and bone hands:** small background props.
5. **Sounds.** Even placeholder-quality real recordings beat the synthesized ones.
