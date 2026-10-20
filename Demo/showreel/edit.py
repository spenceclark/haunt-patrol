"""Assembles the showreel from the shot list at the bottom.

    python edit.py      render any changed shots, mix, and write out/showreel.mp4 (2560x1440 for YouTube)

Everything sits on the beat grid: bar(k, beats) from grid.py, where bar 0 is the drop. A shot runs from its
`at` to the next shot's `at`. Shots render to out/shots/ keyed by everything that affects them, so changing one
shot re-renders only that shot.
"""
import hashlib
import json
import math
import re
import subprocess
import sys
from dataclasses import asdict, dataclass, field
from multiprocessing import Pool
from pathlib import Path

from PIL import Image

import cards
from cards import BONE, GREEN, ORANGE, H, W, FPS, clamp, ease_out, put, slam, text
from grid import BAR, BEAT, DROP, LENGTH, OUT, SPLICE_OUT, bar

DEMO = Path(__file__).resolve().parent.parent
SRC_W, SRC_H = 2560, 1440
SHOTS_DIR = OUT / "shots"
LIGHT = "C:/Windows/Fonts/segoeuil.ttf"


@dataclass(frozen=True)
class Shot:
    at: float                 # output time of the first frame
    src: str = None           # file in Demo/; None = black
    t: float = 0.0            # source time of the first frame
    frame: tuple = ((0, 1280, 720, 1.0),)  # keyframes (secs into shot, centre x, centre y, zoom); 1 = whole screen
    speed: float = 1.0        # 0 freezes on t
    shake: tuple = ()         # impacts, secs into shot
    gain: float = 1.0         # game audio


@dataclass(frozen=True)
class Caption:
    at: float
    dur: float
    text: str
    style: str = "corp"       # corp: quiet office lower-left; punch: big centre slam; label: small caps
    color: tuple = BONE
    y: float = None


@dataclass
class Edit:
    shots: list
    captions: list = field(default_factory=list)
    flashes: list = field(default_factory=list)     # output times
    cards: list = field(default_factory=list)       # (output time, card name)
    sfx: list = field(default_factory=list)         # (output time, wav in HauntPatrol/Assets/Audio, gain)
    vo: list = field(default_factory=list)          # (output time, name in VO_LINES)


# ---- picture ------------------------------------------------------------------------------------

def framing(shot, u):
    """(cx, cy, zoom) at u seconds into the shot, eased between keyframes."""
    keys = shot.frame
    if u <= keys[0][0]:
        return keys[0][1:]
    for a, b in zip(keys, keys[1:]):
        if u <= b[0]:
            p = (u - a[0]) / (b[0] - a[0])
            p = p * p * (3 - 2 * p)
            return tuple(x + (y - x) * p for x, y in zip(a[1:], b[1:]))
    return keys[-1][1:]


def box(shot, u):
    cx, cy, z = framing(shot, u)
    dx, dy = cards.shake(u, shot.shake, 22)
    w, h = SRC_W / z, SRC_H / z
    x0 = clamp(cx - w / 2 - dx * w / W, 0, SRC_W - w)
    y0 = clamp(cy - h / 2 - dy * h / H, 0, SRC_H - h)
    return x0, y0, x0 + w, y0 + h


def draw_caption(c, cap, t):
    u = t - cap.at
    if not 0 <= u < cap.dur:
        return
    out = clamp((cap.dur - u) / 0.2)
    if cap.style == "corp":
        im = text(cap.text, 60, cap.color, path=LIGHT, tracking=1, glow=((0, 0, 0, 255), 14, 2.5))
        put(c, im, 120 + im.width / 2, (cap.y or 930) + 14 * (1 - ease_out(u / 0.35)), 1, min(clamp(u / 0.3), out))
    elif cap.style == "punch":
        im = text(cap.text, 104, cap.color, path=cards.BLACK, glow=(cap.color + (255,), 14, 0.6))
        put(c, im, W / 2, cap.y or H / 2, slam(t, cap.at, big=1.8), out)
    else:
        im = text(cap.text, 34, cap.color, path=cards.LABEL, tracking=8)
        put(c, im, W / 2, cap.y or 120, 1, min(clamp(u / 0.15), out))


def frames_of(shot, n):
    """Yield n source frames (RGB Images) for the shot, at FPS, honouring speed and freezes."""
    size = SRC_W * SRC_H * 3
    if shot.src is None:
        black = Image.new("RGB", (SRC_W, SRC_H))
        for _ in range(n):
            yield black
        return
    src = str(DEMO / shot.src)
    if shot.speed == 0:
        args = ["-ss", f"{shot.t:.4f}", "-i", src, "-frames:v", "1"]
    else:
        args = ["-ss", f"{shot.t:.4f}", "-i", src, "-frames:v", str(n),
                "-vf", f"setpts=PTS/{shot.speed},fps={FPS}"]
    p = subprocess.Popen(["ffmpeg", "-v", "error", *args, "-f", "rawvideo", "-pix_fmt", "rgb24", "-"],
                         stdout=subprocess.PIPE)
    last = None
    for _ in range(n):
        buf = p.stdout.read(size)
        if len(buf) == size:
            last = Image.frombuffer("RGB", (SRC_W, SRC_H), buf)
        yield last  # a freeze, or a source that ran short, holds its last frame
    p.stdout.close()
    p.wait()


def shot_span(shots, i):
    a = round(shots[i].at * FPS)
    b = round((shots[i + 1].at if i + 1 < len(shots) else LENGTH) * FPS)
    return a, b


def render_shot(job):
    shot, a, b, caps, flashes, path = job
    n = b - a
    ff = subprocess.Popen(["ffmpeg", "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", f"{W}x{H}",
                           "-r", str(FPS), "-i", "-", "-c:v", "h264_nvenc", "-preset", "p7", "-rc", "constqp",
                           "-qp", "14", "-pix_fmt", "yuv420p", str(path.with_suffix(".mp4"))], stdin=subprocess.PIPE)
    for k, im in enumerate(frames_of(shot, n)):
        u, t = k / FPS, (a + k) / FPS
        c = im.resize((W, H), Image.LANCZOS, box=box(shot, u)).convert("RGBA")
        for cap in caps:
            draw_caption(c, cap, t)
        for f in flashes:
            cards.flash(c, t, f, 0.8, 0.14)
        ff.stdin.write(c.tobytes())
    ff.stdin.close()
    ff.wait()
    # The shot's game audio, exactly n frames long, so the concatenated track lines up with the picture
    samples = n * 48000 // FPS
    if shot.src is None or shot.speed == 0 or shot.gain == 0:
        graph = f"anullsrc=r=48000:cl=stereo,atrim=end_sample={samples}"
        inp = []
    else:
        tempo = f",atempo={shot.speed}" if shot.speed != 1 else ""
        graph = (f"[0:a]atrim={shot.t:.4f}:{shot.t + n / FPS * shot.speed:.4f},asetpts=PTS-STARTPTS{tempo},"
                 f"aresample=48000,volume={shot.gain},afade=t=in:d=0.008,apad,atrim=end_sample={samples},"
                 f"afade=t=out:st={n / FPS - 0.008:.4f}:d=0.008")
        inp = ["-i", str(DEMO / shot.src)]
    subprocess.run(["ffmpeg", "-v", "error", "-y", *inp, "-filter_complex", graph, "-ac", "2",
                    "-c:a", "pcm_s16le", str(path.with_suffix(".wav"))], check=True)
    return path


def render_shots(edit):
    SHOTS_DIR.mkdir(parents=True, exist_ok=True)
    jobs, paths = [], []
    for i, shot in enumerate(edit.shots):
        a, b = shot_span(edit.shots, i)
        t0, t1 = a / FPS, b / FPS
        caps = tuple(c for c in edit.captions if c.at < t1 and c.at + c.dur > t0)
        flashes = tuple(f for f in edit.flashes if t0 - 0.2 < f < t1)
        mtime = (DEMO / shot.src).stat().st_mtime if shot.src else 0
        key = json.dumps([asdict(shot), a, b, [asdict(c) for c in caps], flashes, mtime, VERSION], default=str)
        path = SHOTS_DIR / hashlib.sha1(key.encode()).hexdigest()[:12]
        paths.append(path)
        if not (path.with_suffix(".mp4").exists() and path.with_suffix(".wav").exists()):
            jobs.append((shot, a, b, caps, flashes, path))
    print(f"{len(jobs)} of {len(paths)} shots to render")
    with Pool(6) as pool:
        for p in pool.imap_unordered(render_shot, jobs):
            print("  rendered", p.name)
    return paths


VERSION = 2  # bump to invalidate every cached shot after changing the renderer


# ---- assembly -----------------------------------------------------------------------------------

def concat(paths, suffix, out):
    lst = OUT / f"concat{suffix}.txt"
    lst.write_text("".join(f"file '{p.with_suffix(suffix).as_posix()}'\n" for p in paths))
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "concat", "-safe", "0", "-i", str(lst), "-c", "copy",
                    str(out)], check=True)


def loudness(path):
    err = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", str(path), "-af", "ebur128", "-f", "null",
                          "-"], capture_output=True, text=True).stderr
    return float(re.findall(r"I:\s+(-?[\d.]+) LUFS", err)[-1])


def mix(edit, game):
    """Music + game audio + extra hits + optional VO (music ducks under it), levelled to -14 LUFS."""
    sounds = Path(__file__).resolve().parents[2] / "HauntPatrol" / "Assets" / "Audio"
    inputs = ["-i", str(game), "-i", str(OUT / "music.wav")]
    graph, layers = ["[0:a]volume=1.0[g]", "[1:a]volume=0.55[m]"], ["[g]"]
    for k, (t, wav, gain) in enumerate(edit.sfx):
        inputs += ["-i", str(sounds / wav)]
        graph.append(f"[{len(inputs) // 2 - 1}:a]aresample=48000,volume={gain},adelay={int(t * 1000)}:all=1[s{k}]")
        layers.append(f"[s{k}]")
    vo = edit.vo if (DEMO / VO_TAKE).exists() else []
    if vo:
        inputs += ["-i", str(DEMO / VO_TAKE)]
        graph.append(f"[{len(inputs) // 2 - 1}:a]aresample=48000,volume=1.6,asplit={len(vo)}"
                     + "".join(f"[vs{k}]" for k in range(len(vo))))
        for k, (t, line) in enumerate(vo):
            a, b = VO_LINES[line]
            graph.append(f"[vs{k}]atrim={a}:{b},asetpts=PTS-STARTPTS,afade=t=in:d=0.02,"
                         f"afade=t=out:st={b - a - 0.06:.3f}:d=0.06,adelay={int(t * 1000)}:all=1[v{k}]")
        graph.append("".join(f"[v{k}]" for k in range(len(vo))) + f"amix=inputs={len(vo)}:normalize=0,asplit[vo][key]")
        graph.append("[m][key]sidechaincompress=threshold=0.03:ratio=6:attack=20:release=400[md]")
        layers += ["[md]", "[vo]"]
    else:
        layers.append("[m]")
    graph.append("".join(layers) + f"amix=inputs={len(layers)}:normalize=0:duration=longest,"
                                   f"atrim=0:{LENGTH:.4f}[a]")
    raw = OUT / "mix-raw.wav"
    subprocess.run(["ffmpeg", "-v", "error", "-y", *inputs, "-filter_complex", ";".join(graph), "-map", "[a]",
                    "-c:a", "pcm_s24le", str(raw)], check=True)
    # Static gain to -14 LUFS (YouTube's reference) and a true-peak ceiling; no dynamic loudnorm pumping
    gain = -14 - loudness(raw)
    final = OUT / "mix.wav"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(raw), "-af",
                    f"volume={gain:.2f}dB,alimiter=limit=0.75:attack=1:release=60:level=0",
                    "-c:a", "pcm_s24le", str(final)], check=True)
    print(f"mix: {gain:+.1f} dB to -14 LUFS, now {loudness(final):.1f}")
    return final


def assemble(edit):
    paths = render_shots(edit)
    base, game = OUT / "base.mp4", OUT / "game.wav"
    concat(paths, ".mp4", base)
    concat(paths, ".wav", game)
    audio = mix(edit, game)
    inputs, graph, over = ["-i", str(base)], [], "[0:v]"
    for k, (t, name) in enumerate(edit.cards):
        inputs += ["-i", str(OUT / f"{name}.mov")]
        graph.append(f"[{k + 1}:v]setpts=PTS-STARTPTS+{t:.4f}/TB[c{k}]")
        graph.append(f"{over}[c{k}]overlay=eof_action=pass[o{k}]")
        over = f"[o{k}]"
    graph.append(f"{over}scale=2560:1440:flags=lanczos,format=yuv420p[v]")
    inputs += ["-i", str(audio)]
    out = OUT / "showreel.mp4"
    subprocess.run(["ffmpeg", "-v", "error", "-y", *inputs, "-filter_complex", ";".join(graph), "-map", "[v]",
                    "-map", f"{len(edit.cards) + 1}:a", "-c:v", "h264_nvenc", "-preset", "p7", "-rc", "vbr",
                    "-cq", "17", "-b:v", "40M", "-maxrate", "80M", "-bufsize", "80M", "-profile:v", "high",
                    "-c:a", "aac", "-b:a", "320k", "-movflags", "+faststart", "-t", f"{LENGTH:.4f}", str(out)],
                   check=True)
    print(f"{out}  {LENGTH:.2f} s")


# ---- the edit -----------------------------------------------------------------------------------
# Takes, with the event times events.py found in them (source seconds).

A1 = "working-normally.mp4"            # 2 min of ordinary coding; control panel on screen around 35-45 s
SKT = "2026-10-20 23-42-39.mp4"        # skeleton while typing: encounter 2.21, launch 13.34, impact 14.41
CAT = "test-run-1440.mp4"              # cat win: encounter 2.45, near the cauldron ~20, into the portal 26.55
RUN = "2026-10-20 23-48-21.mp4"        # all four wins; ghost: encounter 49.74, loop 56.06, final 68.36
ZOM = "2026-10-20 23-45-33.mp4"        # zombie loss: panel gone by 5.0, bite 20.05, handprints 22.09
PEN = "2026-10-20 23-46-32.mp4"        # working under penalties: glass 6.31, slime 19.0, fog 34.26
CATL = "2026-10-20 23-43-26.mp4"       # cat loss: cauldron tips 3.89, slime 4.37
FAILS = "2026-10-21 00-15-05.mp4"      # bone thrown 21.08, glass 21.77 (chat sidebar at x<700: keep it out
                                       # of frame); ghosts escape 60.79, fog 61.95

# The voice-over is one take; lines are cut at its pauses. Each starts 80 ms before its first word.
VO_TAKE = "VoiceOVer.mp3"
VO_LINES = {  # source seconds
    "ordinary": (0.02, 2.20),          # "It was an ordinary afternoon."  first word at 0.10
    "your-desktop": (2.42, 3.66),      # "Your desktop…"                  first word at 2.50
    "is-haunted": (3.84, 5.06),        # "…is haunted."                   "haunted" at 4.32
    "pays": (5.32, 8.52),              # "Lose, and your screen pays the price."
    "haunt-patrol": (8.64, 9.86),      # "Haunt Patrol."
    "if-you-can": (10.02, 12.51),      # "Keep working… if you can."      ends at 12.45
}

EDIT = Edit(
    shots=[
        # Cold open over the quiet intro: an ordinary afternoon, slowly pushing in on the typing
        Shot(0, A1, 18.0, ((0, 1280, 720, 1.0), (bar(-1), 1150, 560, 1.45))),
        Shot(bar(-1), SKT, 2.21 - BEAT, ((0, 1280, 800, 1.2),)),   # something arrives: the sting on beat 2
        Shot(bar(-1, 3), None),                                   # one beat of black before the drop
        Shot(bar(0), None),                                       # under the title card

        # Skeleton Siege: it patrols while the typing carries on, then one shot, hit on the downbeat
        Shot(bar(2), SKT, 3.6, ((0, 1240, 800, 1.3),)),
        Shot(bar(4), SKT, 7.5, ((0, 1350, 780, 1.5), (2 * BAR, 1350, 760, 1.6))),
        Shot(bar(6), SKT, 14.41 - BAR, ((0, 1130, 900, 1.45),)),
        Shot(bar(7), SKT, 14.41, ((0, 1680, 980, 2.0), (BAR, 1680, 960, 1.7)), shake=(0.0,)),

        # Witch's Familiar: arrival, a near miss at the cauldron, into the portal on the downbeat
        Shot(bar(8), CAT, 2.3, ((0, 1400, 760, 1.15),)),
        Shot(bar(9, 2), CAT, 19.3, ((0, 1060, 960, 1.7),)),
        Shot(bar(10, 2), CAT, 26.55 - 2 * BEAT, ((0, 1950, 720, 1.55),)),

        # Ghost Trap: the loop closes, freeze, then it splits as the hats come in at bar 16
        Shot(bar(12), RUN, 49.6, ((0, 800, 640, 1.4),)),  # ghost right of the intro card's panel
        Shot(bar(14), RUN, 56.06 - 1.5 * BAR, ((0, 1340, 640, 1.6),)),
        Shot(bar(15, 2), RUN, 56.10, ((0, 1380, 660, 1.7), (2 * BEAT, 1380, 660, 1.9)), speed=0),
        Shot(bar(16), RUN, 56.10, ((0, 1320, 560, 1.45),), shake=(0.0,)),
        Shot(bar(17, 2), RUN, 68.36 - 2 * BEAT, ((0, 1100, 600, 1.35),)),

        # Zombie Invasion: waves, zaps, CRITICAL, and the brute gets the brain on the downbeat
        Shot(bar(19), ZOM, 5.0, ((0, 1000, 740, 1.25),)),
        Shot(bar(21), ZOM, 11.0, ((0, 1150, 960, 1.45),)),
        Shot(bar(22), ZOM, 16.0, ((0, 1280, 800, 1.3),)),
        Shot(bar(23), ZOM, 20.05 - BAR, ((0, 1330, 760, 1.6), (BAR, 1350, 740, 1.8))),
        Shot(bar(24), ZOM, 20.05, ((0, 1330, 740, 2.0), (BAR, 1280, 720, 1.4)), shake=(0.0,)),

        # Penalty montage: one per downbeat, then still typing under the slime
        Shot(bar(25), ZOM, 22.0, ((0, 1280, 720, 1.0), (BAR, 1280, 720, 1.1))),       # handprints
        Shot(bar(26), FAILS, 21.77 - 2 * BEAT, ((0, 1560, 760, 1.5),), shake=(2 * BEAT,)),  # bone, glass
        Shot(bar(27), CATL, 3.88, ((0, 1150, 1000, 1.6), (1.2, 1280, 720, 1.0))),     # cauldron, slime
        Shot(bar(28), FAILS, 60.6, ((0, 1300, 720, 1.3), (1.5 * BAR, 1280, 820, 1.1))),  # ghosts escape, fog
        Shot(bar(29, 2), PEN, 21.5, ((0, 1150, 620, 1.5), (2.5 * BAR, 1000, 580, 1.75))),  # typing under slime
        Shot(SPLICE_OUT, None),                                   # under the end card
    ],
    captions=[
        Caption(bar(-7), bar(-5) - bar(-7) - 0.2, "Tuesday, 4:47 PM."),
        Caption(bar(-5), bar(-3) - bar(-5) - 0.2, "The report is due at five."),
        Caption(bar(-3), bar(-1) - bar(-3) - 0.2, "Nothing could possibly go wrong."),
        Caption(bar(4), BAR - 0.1, "Keep working.", "punch", BONE, 150),
        Caption(bar(5), BAR - 0.1, "It never steals focus.", "punch", GREEN, 150),
        Caption(bar(15, 2), 2 * BEAT, "…wait.", "punch", BONE, 200),
        Caption(bar(25), 2 * BAR - 0.1, "Lose, and your screen pays.", "punch", ORANGE, 960),  # under the bone
        Caption(bar(30), 2 * BAR - 0.1, "Still click-through. Still working.", "punch", GREEN, 150),
    ],
    flashes=[bar(7), bar(16), bar(24), bar(26, 2)],
    cards=[(bar(0), "title"), (bar(2), "bump-skeleton"), (bar(8), "bump-cat"), (bar(12), "bump-ghost"),
           (bar(19), "bump-zombie"), (SPLICE_OUT, "end")],
    sfx=[(bar(0), "impact.wav", 1.0), (SPLICE_OUT, "impact.wav", 0.8),
         (SPLICE_OUT + 3 * BAR, "laugh.wav", 0.9), (SPLICE_OUT + 3 * BAR + 0.1, "hiss.wav", 0.6)],
    vo=[(bar(-6) - 0.08, "ordinary"),
        (bar(1) - 0.08, "your-desktop"),                 # with YOUR DESKTOP on the title card
        (bar(1, 3) - (4.32 - 3.84), "is-haunted"),       # "haunted" on the HAUNTED. slam
        (bar(25) - 0.03, "pays"),                        # with the caption
        (SPLICE_OUT + 0.02, "haunt-patrol"),             # on the end card's logo slam
        (SPLICE_OUT + 3 * BAR - 0.1 - (12.45 - 10.02), "if-you-can")],  # ends just before the cackle
)

if __name__ == "__main__":
    assemble(EDIT)
