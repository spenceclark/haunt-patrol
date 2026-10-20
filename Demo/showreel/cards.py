"""Motion-graphics cards for the showreel, drawn from the animation masters in ART-PROOF.

    python cards.py            render every card to out/<name>.mov (ProRes 4444 with alpha), 1080p60
    python cards.py title      render just one
    python cards.py preview    out/cards-preview.mp4: the cards in sequence, with the music
    python cards.py thumbnail  out/thumbnail.jpg: the title card's last frame, 1280x720 for YouTube

Times inside a card are seconds from its first frame, which the edit places on a downbeat.
"""
import io
import math
import subprocess
import sys
import zipfile
from functools import lru_cache
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

from grid import BAR, BEAT, DROP, LENGTH, OUT, SPLICE_OUT

W, H, FPS = 1920, 1080, 60
HERE = Path(__file__).parent
ART = HERE.parents[1] / "ART-PROOF"
CREEPSTER = str(HERE / "fonts" / "Creepster-Regular.ttf")
BLACK = "C:/Windows/Fonts/seguibl.ttf"   # Segoe UI Black
LABEL = "C:/Windows/Fonts/bahnschrift.ttf"

# The game's own palette
NIGHT = (13, 10, 22)
INK = (16, 8, 26)
GREEN = (150, 255, 90)
VIOLET = (200, 140, 255)
CYAN = (190, 240, 255)
BONE = (240, 234, 214)
ORANGE = (255, 150, 40)


# ---- easing -------------------------------------------------------------------------------------

def clamp(v, lo=0.0, hi=1.0):
    return max(lo, min(hi, v))


def ease_out(p):
    return 1 - (1 - clamp(p)) ** 3


def back(p, s=2.2):
    p = clamp(p) - 1
    return 1 + p * p * ((s + 1) * p + s)


def slam(t, t0, dur=0.11, big=2.3):
    """Scale of something slamming in at t0: falls from `big` to 1, then a short wobble. None before t0."""
    if t < t0:
        return None
    p = (t - t0) / dur
    if p < 1:
        return big + (1 - big) * p * p
    u = t - t0 - dur
    return 1 + 0.07 * math.exp(-11 * u) * math.sin(38 * u)


def shake(t, hits, amp=16):
    """Camera offset from impacts at the given times."""
    dx = dy = 0.0
    for t0 in hits:
        u = t - t0
        if u >= 0:
            a = amp * math.exp(-10 * u)
            dx += a * math.sin(u * 83)
            dy += a * math.cos(u * 61)
    return dx, dy


def flash(canvas, t, t0, peak=0.85, dur=0.16):
    a = peak * clamp(1 - (t - t0) / dur) if t >= t0 else 0
    if a > 0:
        canvas.alpha_composite(Image.new("RGBA", canvas.size, (255, 255, 255, int(255 * a))))


# ---- compositing --------------------------------------------------------------------------------

def paste(canvas, im, x, y):
    x, y = int(round(x)), int(round(y))
    l, t = max(0, -x), max(0, -y)
    r, b = min(im.width, canvas.width - x), min(im.height, canvas.height - y)
    if r > l and b > t:
        canvas.alpha_composite(im, (x + l, y + t), (l, t, r, b))


def faded(im, a):
    if a >= 0.999:
        return im
    im = im.copy()
    im.putalpha(im.getchannel("A").point(lambda v: v * clamp(a)))
    return im


def put(canvas, im, cx, cy, scale=1.0, alpha=1.0, rot=0.0):
    """Paste im centred on (cx, cy)."""
    if scale is None or alpha <= 0:
        return
    if abs(scale - 1) > 1e-3:
        im = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.BICUBIC)
    if rot:
        im = im.rotate(rot, Image.BICUBIC, expand=True)
    paste(canvas, faded(im, alpha), cx - im.width / 2, cy - im.height / 2)


def moved(im, dx, dy, fill):
    out = Image.new("RGBA", im.size, fill)
    paste(out, im, dx, dy)
    return out


# ---- text ---------------------------------------------------------------------------------------

@lru_cache(None)
def font(path, size):
    return ImageFont.truetype(path, size)


@lru_cache(None)
def text(s, size, fill, path=CREEPSTER, stroke=0, glow=None, tracking=0, shadow=True, gradient=False):
    """A text sprite: optional drop shadow, coloured glow, dark outline and top-lit gradient fill."""
    f = font(path, size)
    widths = [f.getlength(c) + tracking for c in s] if tracking else [f.getlength(s)]
    l, t, r, b = f.getbbox(s)
    pad = stroke + (glow[1] * 2 if glow else 0) + size // 4
    w, h = int(sum(widths)) + 2 * pad, (b - t) + 2 * pad

    def mask(sw):
        m = Image.new("L", (w, h))
        d = ImageDraw.Draw(m)
        if tracking:
            x = pad
            for c, cw in zip(s, widths):
                d.text((x, pad - t), c, font=f, fill=255, stroke_width=sw, stroke_fill=255)
                x += cw
        else:
            d.text((pad, pad - t), s, font=f, fill=255, stroke_width=sw, stroke_fill=255)
        return m

    body, outline = mask(0), mask(stroke)
    out = Image.new("RGBA", (w, h))
    if shadow:
        sh = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        sh.putalpha(outline.filter(ImageFilter.GaussianBlur(size / 18)).point(lambda v: v * 0.75))
        paste(out, sh, 0, size / 16)
    if glow:
        g = Image.new("RGBA", (w, h), glow[0])
        g.putalpha(outline.filter(ImageFilter.GaussianBlur(glow[1])).point(lambda v: min(255, v * glow[2])))
        out.alpha_composite(g)
    if stroke:
        o = Image.new("RGBA", (w, h), INK)
        o.putalpha(outline)
        out.alpha_composite(o)
    if gradient:  # lighter at the top, deeper at the bottom
        y = np.linspace(0, 1, h)[:, None, None]
        top = np.array([min(255, c + 70) for c in fill])
        bot = np.array([c * 0.62 for c in fill])
        rgb = (top * (1 - y) + bot * y).repeat(w, 1).clip(0, 255).astype("uint8")
        fl = Image.fromarray(rgb, "RGB").convert("RGBA")
    else:
        fl = Image.new("RGBA", (w, h), fill)
    fl.putalpha(body)
    out.alpha_composite(fl)
    return out


def fit(s, width, path=CREEPSTER, start=400):
    """Largest font size that sets s no wider than `width`."""
    size = start
    while font(path, size).getlength(s) > width:
        size -= 4
    return size


# ---- characters ---------------------------------------------------------------------------------

CLIPS = {  # name: (master zip, fps, anchor, loop)
    "skeleton.idle": ("Skeleton/Idle.zip", 15, "feet", True),
    "skeleton.walk": ("Skeleton/walk.zip", 24, "feet", True),
    "skeleton.attack": ("Skeleton/attack.zip", 20, "feet", False),
    "cat.idle": ("Cat/cat_idle.zip", 14, "centre", True),
    "cat.run": ("Cat/cat_run.zip", 36, "centre", True),
    "cat.hiss": ("Cat/cat_hiss.zip", 15, "centre", True),
    "ghost.idle": ("Ghost/ghost_idle.zip", 12, "centre", True),
    "ghost.laugh": ("Ghost/ghost_laugh.zip", 26, "centre", False),
    "zombie.walk": ("Zombies/zombie_normal_walk.zip", 24, "feet", True),
    "brute.walk": ("Zombies/zombie_brute_walk.zip", 20, "feet", True),
    "runner.walk": ("Zombies/zombie_runner_walk.zip", 36, "feet", True),
    "brain.idle": ("Brain/brain_idle.zip", 12, "centre", True),
    "brain.victory": ("Brain/brain_victory.zip", 20, "centre", True),
}


class Clip:
    def __init__(self, name):
        path, self.fps, anchor, self.loop = CLIPS[name]
        with zipfile.ZipFile(ART / path) as z:
            names = sorted(n for n in z.namelist() if n.endswith(".png"))
            self.frames = [Image.open(io.BytesIO(z.read(n))).convert("RGBA") for n in names]
        # Like tools/ImportSprite: the first frame is the neutral pose; scale and anchor by its figure
        l, t, r, b = self.frames[0].getchannel("A").point(lambda a: 255 if a > 24 else 0).getbbox()
        self.figure = b - t
        self.anchor = ((l + r) / 2, b if anchor == "feet" else (t + b) / 2)

    def index(self, t):
        i = int(max(0.0, t) * self.fps)
        return i % len(self.frames) if self.loop else min(i, len(self.frames) - 1)


@lru_cache(None)
def clip(name):
    return Clip(name)


@lru_cache(maxsize=400)
def scaled(name, i, h, flip):
    c = clip(name)
    s = h / c.figure
    im = c.frames[i]
    im = im.resize((max(1, round(im.width * s)), max(1, round(im.height * s))), Image.BICUBIC)
    ax, ay = c.anchor
    if flip:
        im, ax = im.transpose(Image.Transpose.FLIP_LEFT_RIGHT), c.frames[i].width - ax
    return im, (ax * s, ay * s)


def actor(canvas, name, t, x, y, h, flip=False, alpha=1.0):
    """Draw clip `name` at local time t with its anchor (feet or centre) on (x, y), figure h px tall."""
    if h < 2 or alpha <= 0:
        return
    im, (ax, ay) = scaled(name, clip(name).index(t), round(h), flip)
    paste(canvas, faded(im, alpha), x - ax, y - ay)


# ---- background ---------------------------------------------------------------------------------

@lru_cache(None)
def night_base():
    yy, xx = np.mgrid[0:H, 0:W]
    d = np.sqrt(((xx - W / 2) / W) ** 2 + ((yy - H * 0.42) / H) ** 2)
    v = np.clip(1 - d * 1.5, 0, 1)[..., None]
    glow = np.exp(-(((xx - W / 2) / (W * 0.32)) ** 2 + ((yy - H * 0.3) / (H * 0.28)) ** 2))[..., None]
    rgb = np.array(NIGHT) * (0.35 + 0.9 * v) + np.array([70, 30, 120]) * glow * 0.45
    return Image.fromarray(rgb.clip(0, 255).astype("uint8"), "RGB").convert("RGBA")


@lru_cache(None)
def fog():
    rng = np.random.default_rng(7)
    layers = []
    for cell, weight in ((180, 0.65), (60, 0.35)):
        n = rng.random((H // cell + 2, 3 * W // cell + 2)).astype("float32")
        im = Image.fromarray((n * 255).astype("uint8")).resize((3 * W, H), Image.BICUBIC)
        layers.append(np.asarray(im.filter(ImageFilter.GaussianBlur(cell / 3)), "float32") / 255 * weight)
    n = sum(layers)
    n = np.clip((n - n.mean()) * 3.2 + 0.35, 0, 1)
    rgba = np.zeros((H, 3 * W, 4), "uint8")
    rgba[..., :3] = (120, 90, 190)
    rgba[..., 3] = (n * 70).astype("uint8")
    return Image.fromarray(rgba, "RGBA")


def night(t):
    bg = night_base().copy()
    x = int(200 + t * 45)
    bg.alpha_composite(fog().crop((x, 0, x + W, H)))
    return bg


# ---- cards --------------------------------------------------------------------------------------

def lineup(c, t, t0, step, feet=1000, swap_at=None, swap=()):
    """The cast across the bottom, popping in one per `step` from t0, each playing its own loop.
    From `swap_at`, clips named in `swap` play their replacement instead (the end card's final hit)."""
    swap = dict(swap)
    cast = [  # clip, x, height, flip, floating y (None = stands on `feet`)
        ("runner.walk", 175, 235, False, None),
        ("zombie.walk", 420, 275, False, None),
        ("brute.walk", 700, 300, False, None),
        ("ghost.idle", 960, 330, False, feet - 205),
        ("skeleton.idle", 1215, 360, True, None),
        ("cat.idle", 1480, 205, True, feet - 95),
        ("brain.idle", 1740, 200, True, feet - 120),
    ]
    order = [3, 2, 4, 1, 5, 0, 6]  # from the middle outwards
    for k, i in enumerate(order):
        name, x, h, flip, fy = cast[i]
        u = t - (t0 + k * step)
        if u < 0:
            continue
        pop = back(u / 0.28)
        if swap_at is not None and t >= swap_at and name in swap:
            name, u = swap[name], t - swap_at
            pop = 1 + 0.15 * math.exp(-6 * u)
        actor(c, name, u, x, fy if fy is not None else feet, h * pop, flip)


def logo(c, t, t0, cx, cy, width):
    size = fit("HAUNT PATROL", width)
    im = text("HAUNT PATROL", size, GREEN, stroke=max(6, size // 24), glow=(GREEN + (255,), size // 9, 1.1), gradient=True)
    breathe = 1 + 0.012 * math.sin((t - t0) * 2.4)
    put(c, im, cx, cy, None if slam(t, t0) is None else slam(t, t0) * breathe)


def title_card(t):
    """At the drop: logo slam, the cast, then YOUR DESKTOP IS HAUNTED one word per beat. Two bars."""
    c = night(t)
    lineup(c, t, 0.12, BEAT / 2)
    logo(c, t, 0.0, W / 2, 290, 1380)
    words = [("YOUR", BONE), ("DESKTOP", BONE), ("IS", BONE), ("HAUNTED.", GREEN)]
    size, track = 66, 6
    f = font(BLACK, size)
    wide = [f.getlength(w) + track * len(w) for w, _ in words]
    space = f.getlength(" ") * 1.4
    x = W / 2 - (sum(wide) + space * (len(words) - 1)) / 2
    for k, ((w, col), ww) in enumerate(zip(words, wide)):
        t0 = BAR + k * BEAT
        im = text(w, size, col, path=BLACK, tracking=track, glow=(col + (255,), 10, 0.5) if col == GREEN else None)
        s = slam(t, t0, big=1.8) if col == GREEN else (None if t < t0 else 1)
        a = clamp((t - t0) / 0.08)
        put(c, im, x + ww / 2, 505 - 18 * (1 - ease_out((t - t0) / 0.2)), s, a)
        x += ww + space
    flash(c, t, 0.0)
    flash(c, t, BAR + 3 * BEAT, 0.35, 0.12)
    dx, dy = shake(t, [0.11, BAR + 3 * BEAT + 0.11], 18)
    return moved(c, dx, dy, NIGHT + (255,))


GAMES = {  # n, title lines, colour, how to win
    "skeleton": (1, ("SKELETON", "SIEGE"), BONE, "Aim ahead. You get three shots."),
    "cat": (2, ("WITCH'S", "FAMILIAR"), VIOLET, "Herd the cat into the portal."),
    "ghost": (3, ("GHOST", "TRAP"), CYAN, "Draw a loop to trap the ghost."),
    "zombie": (4, ("ZOMBIE", "INVASION"), GREEN, "Zap them before they reach the brain."),
}
BUMP_OUT = 5.5 * BEAT  # the panel starts leaving here
BUMP_LEN = 6.5 * BEAT


def bump_actor(game, c, t, dx):
    """Each game's character entering the panel. dx is the panel's slide offset."""
    if game == "skeleton":
        x = -260 + 560 * ease_out(t / 0.9)
        if t < 2 * BEAT:
            actor(c, "skeleton.walk", t, x + dx, 1050, 600)
        else:
            actor(c, "skeleton.attack", t - 2 * BEAT, x + dx, 1050, 600)
    elif game == "cat":
        if t < 1.5 * BEAT:
            actor(c, "cat.run", t, -260 + 600 * ease_out(t / (1.5 * BEAT)) + dx, 820, 330)
        else:
            actor(c, "cat.hiss", t - 1.5 * BEAT, 340 + dx, 820, 330)
    elif game == "ghost":
        rise = ease_out(t / 0.6)
        y = 640 + 260 * (1 - rise) + 14 * math.sin(t * 3)
        name, u = ("ghost.idle", t) if t < 2 * BEAT else ("ghost.laugh", t - 2 * BEAT)
        actor(c, name, u, 320 + dx, y, 560, alpha=rise)
    else:
        actor(c, "brute.walk", t, -200 + 330 * ease_out(t / 1.4) + dx, 1060, 380)
        actor(c, "zombie.walk", t + 0.4, -120 + 520 * ease_out(t / 1.1) + dx, 1080, 420)
        u = t - 2.5 * BEAT
        if u > 0:
            actor(c, "runner.walk", u, -200 + 2400 * u, 1075, 330)


def bump_card(game, t):
    """Over the footage at the start of each game: a dark panel from the left, character, name, how to win."""
    n, lines, col, how = GAMES[game]
    c = Image.new("RGBA", (W, H))
    slide = (1 - ease_out(t / 0.22)) + ease_out((t - BUMP_OUT) / 0.25) if t >= 0 else 1
    dx = -1500 * slide
    panel = band()
    paste(c, panel, dx, 0)
    bump_actor(game, c, t, dx)
    x0 = 690 + dx
    lab = text(f"ENCOUNTER {n} OF 4", 30, BONE, path=LABEL, tracking=9, shadow=False)
    paste(c, faded(lab, clamp((t - 0.15) / 0.15)), x0, 285)
    size = 150
    for k, line in enumerate(lines):
        im = text(line, size, col, stroke=8, glow=(col + (255,), 16, 0.9), gradient=True)
        s = slam(t, 0.5 * BEAT + k * BEAT / 2, big=1.9)
        if s is not None:
            put(c, im, x0 + im.width / 2 - 34, 400 + k * 150, s)
    sub = text(how, 44, BONE, path=BLACK)
    u = t - 2 * BEAT
    put(c, sub, x0 + sub.width / 2 - 30, 735 - 14 * (1 - ease_out(u / 0.2)), 1 if u >= 0 else None, clamp(u / 0.12))
    dx_, dy_ = shake(t, [0.5 * BEAT + 0.11], 10)
    return moved(c, dx_, dy_, (0, 0, 0, 0))


@lru_cache(None)
def band():
    """Dark panel, solid on the left and fading out to the right, so the footage stays visible."""
    x = np.linspace(0, 1, 1500)
    a = np.clip(1 - (x - 0.55) / 0.45, 0, 1) ** 1.6 * 0.88
    y = np.linspace(-1, 1, H)[:, None]
    alpha = (a[None, :] * (1 - 0.25 * y ** 2) * 255).astype("uint8")
    rgba = np.zeros((H, 1500, 4), "uint8")
    rgba[..., :3] = NIGHT
    rgba[..., 3] = alpha
    return Image.fromarray(rgba, "RGBA")


END_LEN = LENGTH - SPLICE_OUT


def end_card(t):
    """From the last phrase to the end: logo, three claims, the cast, the final hit, the credit, fade."""
    c = night(t + 20)
    hit = 3 * BAR  # the track's final hit: the ghost cackles, the cat hisses, the brain cheers
    lineup(c, t, 0.05, BEAT / 4, feet=990, swap_at=hit,
           swap={"ghost.idle": "ghost.laugh", "cat.idle": "cat.hiss", "brain.idle": "brain.victory"})
    logo(c, t, 0.0, W / 2, 175, 1120)
    claims = [("4 creatures.", BONE, BAR), ("4 ways to lose.", ORANGE, BAR + 2 * BEAT), ("Zero stolen focus.", GREEN, 2 * BAR)]
    for k, (s, col, t0) in enumerate(claims):
        big = k == 2
        im = text(s, 88 if big else 64, col, path=BLACK, glow=(col + (255,), 12, 0.6) if big else None)
        sc = slam(t, t0, big=1.7) if big else (None if t < t0 else 1)
        put(c, im, W / 2, 360 + k * 92 - 16 * (1 - ease_out((t - t0) / 0.2)), sc, clamp((t - t0) / 0.1))
    cr = text("A solo build for Hackyard #5: Haunted", 36, BONE, path=LABEL, tracking=3, shadow=False)
    put(c, cr, W / 2, 1048, 1, clamp((t - hit - 0.4) / 0.4))
    flash(c, t, 0.0)
    flash(c, t, hit, 0.6, 0.2)
    dx, dy = shake(t, [0.11, hit, 2 * BAR + 0.11], 14)
    c = moved(c, dx, dy, NIGHT + (255,))
    out = clamp((t - (END_LEN - 1.2)) / 1.0)
    if out > 0:
        c.alpha_composite(Image.new("RGBA", c.size, (0, 0, 0, int(255 * out))))
    return c


CARDS = {
    "title": (2 * BAR, title_card),
    **{f"bump-{g}": (BUMP_LEN, (lambda g: lambda t: bump_card(g, t))(g)) for g in GAMES},
    "end": (END_LEN, end_card),
}


def render(name):
    dur, draw = CARDS[name]
    OUT.mkdir(exist_ok=True)
    n = round(dur * FPS)
    ff = subprocess.Popen(["ffmpeg", "-v", "error", "-y", "-f", "rawvideo", "-pix_fmt", "rgba", "-s", f"{W}x{H}",
                           "-r", str(FPS), "-i", "-", "-c:v", "prores_ks", "-profile:v", "4444",
                           "-pix_fmt", "yuva444p10le", "-vendor", "apl0", str(OUT / f"{name}.mov")],
                          stdin=subprocess.PIPE)
    for i in range(n):
        ff.stdin.write(draw(i / FPS).tobytes())
    ff.stdin.close()
    if ff.wait():
        sys.exit(f"ffmpeg failed on {name}")
    print(f"out/{name}.mov  {dur:.2f} s")


def preview():
    """Cards in edit order with the real music: one bar of intro, the title, the four game intros
    over the test take, then a jump to the end card."""
    footage = HERE.parent / "test-run-1440.mp4"
    games = list(GAMES)
    a0 = DROP - BAR
    gap = 2 * BAR  # one game intro every two bars
    norm = "format=yuv420p,setsar=1"
    graph = [f"color=c=black:s={W}x{H}:r={FPS}:d={BAR},{norm}[k]",
             f"[1:v]setpts=PTS-STARTPTS,{norm}[ti]",
             f"[0:v]trim=start=3:duration={4 * gap},setpts=PTS-STARTPTS,scale={W}:{H}:flags=lanczos,fps={FPS}[fg]"]
    over = "[fg]"
    for k in range(4):
        graph.append(f"[{2 + k}:v]setpts=PTS-STARTPTS+{k * gap}/TB[b{k}]")
        graph.append(f"{over}[b{k}]overlay=eof_action=pass[o{k}]")
        over = f"[o{k}]"
    graph += [f"{over}{norm}[fo]",
              f"[6:v]setpts=PTS-STARTPTS,{norm}[en]",
              f"[k][ti][fo][en]concat=n=4:v=1:a=0[v]",
              f"[7:a]atrim={a0}:{DROP + 2 * BAR + 4 * gap},asetpts=PTS-STARTPTS[m1]",
              f"[7:a]atrim={SPLICE_OUT},asetpts=PTS-STARTPTS[m2]",
              f"[m1][m2]concat=n=2:v=0:a=1[a]"]
    inputs = ["-i", str(footage)]
    for name in ["title"] + [f"bump-{g}" for g in games] + ["end"]:
        inputs += ["-i", str(OUT / f"{name}.mov")]
    inputs += ["-i", str(OUT / "music.wav")]
    subprocess.run(["ffmpeg", "-v", "error", "-y", *inputs, "-filter_complex", ";".join(graph), "-map", "[v]",
                    "-map", "[a]", "-c:v", "h264_nvenc", "-cq", "19", "-c:a", "aac", "-b:a", "256k",
                    str(OUT / "cards-preview.mp4")], check=True)
    print("out/cards-preview.mp4")


if __name__ == "__main__":
    names = sys.argv[1:] or list(CARDS)
    for name in names:
        if name == "thumbnail":
            im = title_card(2 * BAR - 1 / FPS).convert("RGB").resize((1280, 720), Image.LANCZOS)
            im.save(OUT / "thumbnail.jpg", quality=92)
            print("out/thumbnail.jpg")
        else:
            preview() if name == "preview" else render(name)
