# Haunt Patrol: Art and Audio Asset Inventory

This is everything the final game needs from an artist or sound designer, and the exact format the game
loads. Every asset listed here already exists as a placeholder in `HauntPatrol/Assets`. Replace each file in
place, keeping its name, and the game picks it up. No code change is needed.

> `tools/MakeAssets.cs` only writes placeholder files that don't exist yet, so it never overwrites production
> art. `--force` regenerates every placeholder.

**Production art so far:** Skeleton (all five clips, imported from Ludo exports with `tools/ImportSprite.cs`;
see §2.7).

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

Each `--clip` is `name=source:fps:loop|once`.

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

`Characters/Cat/` · id `cat` · logical **96 × 64** · pivot **(48, 32)** at the centre · **interactive**
layer · faces right

| Clip | Loop | Suggested frames | Used for |
|---|---|---|---|
| `idle` | yes | 4 @ 6 fps | Ambling slowly |
| `run` | yes | 6 @ 12 fps | Fleeing. Playback speed scales with the cat's speed (0.5×–2.4×). |
| `hiss` | yes | 3–4 @ 8 fps | When the broom is within ~150 DIP |

The game plays no separate capture or escape clips. It shrinks and spins the cat into the portal, and slides
it off-screen when it escapes.

### 3.3 Ghost (Ghost Trap)

`Characters/Ghost/` · id `ghost` · logical **120 × 140** · pivot **(60, 70)** at the centre ·
**click-through** layer, so semi-transparency is welcome · faces right

| Clip | Loop | Suggested frames | Target | Used for |
|---|---|---|---|---|
| `idle` | yes | 6–8 @ 8 fps | — | Floating |
| `laugh` | no | 4–6 | **~0.35 s** | A missed loop, and escaping on timeout. Small ghosts replay it for ~0.6 s. |
| `scared` | yes | 4 @ 8 fps | — | When the pen is near, during the split squash, and when the other ghost is caught |

The same sprite is used for the **two small ghosts at 68% scale** (about 82 × 95 DIP). Make sure the face
still reads at that size. The capture effect (shrink and spin) is done in code.

### 3.4 Zombies (Zombie Invasion): three variants

All three are on the **interactive** layer: every visible pixel is clickable, which is what you want. They
face right and pivot at the feet.

| Variant | Folder / id | Logical size | Pivot | Look |
|---|---|---|---|---|
| Normal | `Characters/Zombie/` · `zombie` | **70 × 100** | (35, 100) | Classic cartoon zombie |
| Brute | `Characters/Brute/` · `brute` | **100 × 130** | (50, 130) | Bigger, heavier, armoured or helmeted. Takes 2 hits. |
| Runner | `Characters/Runner/` · `runner` | **62 × 92** | (31, 92) | Skinny and fast, in a loud colour. The code leans it forward 12°. |

Clips:

| Clip | Needed by | Loop | Suggested frames | Target | Used for |
|---|---|---|---|---|---|
| `walk` | all three | yes | 6–8 @ 8–10 fps | — | Advancing on the brain. The code adds a ±7° shamble. |
| `die` | all three | no | 4–6 | **~0.4 s** | Disintegrating. Afterwards the code fades it, in 0.12 s for runners and 0.3 s for the others. Keep the runner's short. |
| `hit` | **Brute only** | no | 2–3 | **~0.2 s** | Surviving the first zap. Movement pauses for 0.25 s. Normal zombies and runners die from one zap, so they never play it. |

No `idle` is needed: if a clip is missing, the first clip in the file plays instead.

Optional extra: an `emerge` clip for climbing out of the ground. The game doesn't play it yet; it currently
scales the zombie up from its feet.

### 3.5 Brain (Zombie Invasion)

`Characters/Brain/` · id `brain` · logical **110 × 90** · pivot **(55, 45)** at the centre ·
**click-through** layer

| Clip | Loop | Suggested frames | Target | Used for |
|---|---|---|---|---|
| `idle` | yes | 4–6 @ 6 fps | — | Floating and glowing |
| `hit` | no | 2–3 | **~0.33 s** | Being bitten |
| `victory` | yes | 2–4 @ 6 fps | — | Happy bounce when you win |

The danger glow behind the brain, its pulse and the health hearts are drawn in code. Draw the brain without a
baked glow, or with a neutral one; the code tints a glow ellipse from purple to orange to red.

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
| `Props/pen.png` | `pen` | 28 × 110 | interactive | Ghost | Vertical, **writing tip at the bottom centre**. It's held tilted 25° and the trail starts at the tip. |
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
