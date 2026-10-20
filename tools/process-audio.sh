#!/usr/bin/env bash
# Builds the game's sound effects from the ElevenLabs masters: trims, fades, balances the level and writes
# 16-bit PCM WAVs at 44.1 kHz. The masters are only read.
#
#   tools/process-audio.sh [masters dir] [output dir]
#
# Defaults: ART-PROOF/Sound -> HauntPatrol/Assets/Audio. Needs ffmpeg on the PATH.
set -euo pipefail
src="${1:-ART-PROOF/Sound}"
out="${2:-HauntPatrol/Assets/Audio}"
mkdir -p "$out"

# name     start  end   fade  gain(dB)  channels
# start/end: the part of the master kept (s). fade: fade-out at the end (s). Channels: 1 where the master is
# effectively mono (side/mid below -24 dB), 2 where its stereo width is part of the sound.
# Gains balance the loudest 400 ms (K-weighted, measured with ffmpeg ebur128 on the trimmed clip) to:
#   prominent -12 LUFS (success, failure, glass, slime, screech, warning), normal -15 (encounter, launch,
#   impact, hiss, groan, zap, thud, laugh), fog -16, slap -19 (seven in a row), pickup -21,
# capped so true peaks stay under -1 dBTP (prominent) or -3 dBTP (the rest, which overlap). The capped ones
# (glass, slime, impact, launch, thud, laugh) sit a little under their target rather than being compressed.
while read -r name start end fade gain ch; do
  [[ -z "$name" || "$name" == \#* ]] && continue
  [[ -f "$src/$name.wav" ]] || { echo "missing master: $src/$name.wav" >&2; exit 1; }
  [[ "$(realpath "$src/$name.wav")" == "$(realpath -m "$out/$name.wav")" ]] && { echo "refusing to overwrite a master" >&2; exit 1; }
  len=$(awk "BEGIN{print $end-$start}")
  filters="atrim=start=$start:end=$end,asetpts=PTS-STARTPTS"
  # A 2 ms fade-in only where the cut lands inside the sound; untouched starts keep their full transient.
  [[ "$start" != "0" ]] && filters+=",afade=t=in:d=0.002"
  filters+=",afade=t=out:st=$(awk "BEGIN{print $len-$fade}"):d=$fade,volume=${gain}dB"
  [[ "$ch" == 1 ]] && filters+=",pan=mono|c0=0.5*c0+0.5*c1"
  filters+=",aresample=44100:dither_method=triangular"
  ffmpeg -v error -y -i "$src/$name.wav" -af "$filters" -c:a pcm_s16le -map_metadata -1 -fflags +bitexact -flags:a +bitexact "$out/$name.wav"
  echo "$name: ${len}s, ${gain} dB, ${ch} ch"
done <<'EOF'
encounter  0     0.90  0.12  -11.0 2
pickup     0     0.12  0.05  -0.9  1
launch     0     0.40  0.10  -3.8  1
impact     0     0.40  0.12  -3.6  2
hiss       0     0.55  0.08  -9.7  2
success    0     0.70  0.15  -2.2  2
failure    0     0.85  0.15  -6.7  1
glass      0     0.90  0.20  -2.0  1
slime      0     0.70  0.15  -1.1  1
laugh      0.04  0.94  0.10  +3.1  1
zap        0     0.22  0.07  -7.7  2
groan      0     0.92  0.12  -5.1  1
screech    0     0.45  0.06  -5.0  1
thud       0     0.25  0.08  -3.4  2
warning    0     0.37  0.02  -0.9  2
slap       0     0.18  0.06  -3.8  2
fog        0     1.25  0.25  -4.3  1
EOF
