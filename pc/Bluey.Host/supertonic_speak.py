"""Local Supertonic 3 worker. One JSON object per line on stdin, one on stdout."""

import json
import os
import sys
import tempfile

os.environ.setdefault("HF_HUB_DISABLE_PROGRESS_BARS", "1")
os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")
os.environ.setdefault("TQDM_DISABLE", "1")


def emit(payload):
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


def main():
    sys.stdin.reconfigure(encoding="utf-8")
    sys.stdout.reconfigure(encoding="utf-8")
    try:
        from supertonic import TTS
    except Exception:
        emit({"ready": False, "error": "Install Supertonic with: py -3 -m pip install supertonic"})
        return 1
    try:
        tts = TTS(model="supertonic-3", auto_download=True)
        styles = {
            "male": tts.get_voice_style(voice_name="M1"),
            "female": tts.get_voice_style(voice_name="F1"),
        }
    except Exception as ex:
        emit({"ready": False, "error": str(ex)[:300]})
        return 1
    emit({"ready": True})
    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            req = json.loads(line)
            text = (req.get("text") or "").strip()
            sex = req.get("sex") if req.get("sex") in ("male", "female") else "male"
            lang = (req.get("lang") or "na").strip().lower() or "na"
            if not text:
                emit({"ok": False, "id": req.get("id"), "error": "empty"})
                continue
            wav, _duration = tts.synthesize(
                text,
                voice_style=styles[sex],
                lang=lang,
                total_steps=5,
                speed=1.05,
            )
            path = os.path.join(tempfile.gettempdir(), f"bluey-tts-{os.getpid()}-{req.get('id', 0)}.wav")
            tts.save_audio(wav, path)
            emit({"ok": True, "id": req.get("id"), "wav": path})
        except Exception as ex:
            emit({"ok": False, "id": None, "error": str(ex)[:300]})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
