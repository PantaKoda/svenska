"""
Generate the audio for every Swedish line the app plays.

GOAL (in plain words): ask the validator which lines need audio, speak each one with
the offline Piper voice, and save it as content/audio/<hash>.ogg. Files that already
exist are skipped, so running it again only makes what is new.

SETUP (once)
  python -m venv tools/.venv
  tools/.venv/Scripts/pip install piper-tts soundfile numpy      (Linux: tools/.venv/bin/pip)
  tools/.venv/Scripts/python -m piper.download_voices sv_SE-nst-medium --data-dir data/voices

USAGE (from the repository root)
  tools/.venv/Scripts/python tools/gen_audio.py            make missing files
  tools/.venv/Scripts/python tools/gen_audio.py --prune    also delete files no line uses

The list of lines and their file names comes from `Svenska.Validator --audio-list`
(Svenska.Core/Audio/AudioFiles.cs), so the app and this script always agree.
"""
import pathlib
import subprocess
import sys

import numpy as np
import soundfile as sf
from piper import PiperVoice, SynthesisConfig

ROOT = pathlib.Path(__file__).resolve().parent.parent
AUDIO_DIR = ROOT / "content" / "audio"
VOICE = "sv_SE-nst-medium"          # must match AudioFiles.Voice
VOICE_FILE = ROOT / "data" / "voices" / f"{VOICE}.onnx"

# Piper's length_scale: 1.0 is the voice's normal speed, larger is slower.
LENGTH_SCALE = {"normal": 1.0, "slow": 1.45}
SILENCE_SECONDS = 0.15              # a short pause before and after, so nothing is clipped


def audio_lines():
    """[(file name, speed, text)] from the validator."""
    result = subprocess.run(
        ["dotnet", "run", "--project", str(ROOT / "src" / "Svenska.Validator"), "--",
         str(ROOT / "content"), "--audio-list"],
        capture_output=True, encoding="utf-8", check=True)
    return [tuple(line.split("\t", 2)) for line in result.stdout.splitlines() if line.count("\t") == 2]


def main():
    prune = "--prune" in sys.argv[1:]
    lines = audio_lines()
    if not VOICE_FILE.exists():
        sys.exit(f"Voice not found: {VOICE_FILE} (see SETUP at the top of this file)")

    AUDIO_DIR.mkdir(parents=True, exist_ok=True)
    voice = PiperVoice.load(VOICE_FILE)
    rate = voice.config.sample_rate
    silence = np.zeros(int(rate * SILENCE_SECONDS), dtype=np.int16)

    made = 0
    for name, speed, text in lines:
        target = AUDIO_DIR / name
        if target.exists():
            continue
        config = SynthesisConfig(length_scale=LENGTH_SCALE[speed])
        chunks = [chunk.audio_int16_array for chunk in voice.synthesize(text, syn_config=config)]
        sf.write(target, np.concatenate([silence, *chunks, silence]), rate, format="OGG", subtype="VORBIS")
        made += 1
        print(f"{name}  {speed:6}  {text}")

    removed = 0
    if prune:
        wanted = {name for name, _, _ in lines}
        for file in AUDIO_DIR.glob("*.ogg"):
            if file.name not in wanted:
                file.unlink()
                removed += 1

    print(f"{len(lines)} lines, {made} files made, {removed} removed")


if __name__ == "__main__":
    main()
