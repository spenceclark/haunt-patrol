"""Beat grid of the showreel music, and the cut that brings Option 2 down to ~87 s.

Option 2 runs at 123.046 BPM. Its sections, measured from the track's onsets (bar numbers are downbeats
counted from the drop, which is bar 0):

    0.000 - 15.220  quiet intro (cold open)
   15.220           bass drops                      bar 0
   46.428           hats come in, the main section  bar 16
   77.636           end of that section             bar 32 -> we jump to the track's last phrase
   (cut)            16-bar breakdown and reprise removed; the two sides are the same texture
   83.5             the final hit, then the track's own decay

Run it to write out/music.wav.
"""
import subprocess
from pathlib import Path

BEAT = 60 / 123.046
BAR = 4 * BEAT
DROP = 15.220            # output time of bar 0
SPLICE_OUT = 77.636      # cut leaves the source here (end of bar 31)...
SPLICE_IN = 108.843      # ...and resumes here, on the downbeat of the last phrase
SOURCE_END = 118.6       # the track has decayed to silence by now
LENGTH = SPLICE_OUT + SOURCE_END - SPLICE_IN

HERE = Path(__file__).parent
OUT = HERE / "out"
SOURCE = HERE.parent / "Music Option 2.mp3"


def bar(k, beat=0.0):
    """Output time of bar k (0 = the drop), plus a number of beats."""
    return DROP + k * BAR + beat * BEAT


def main():
    OUT.mkdir(exist_ok=True)
    x = 0.010  # 20 ms crossfade centred on the downbeat
    graph = (
        f"[0:a]atrim=0:{SPLICE_OUT + x},asetpts=PTS-STARTPTS[a];"
        f"[0:a]atrim={SPLICE_IN - x}:{SOURCE_END},asetpts=PTS-STARTPTS[b];"
        f"[a][b]acrossfade=d={2 * x}:c1=qsin:c2=qsin,afade=t=out:st={LENGTH - 0.6}:d=0.6[m]"
    )
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-i", str(SOURCE), "-filter_complex", graph,
                    "-map", "[m]", "-ar", "48000", "-c:a", "pcm_s16le", str(OUT / "music.wav")], check=True)
    print(f"out/music.wav  {LENGTH:.2f} s")


if __name__ == "__main__":
    main()
