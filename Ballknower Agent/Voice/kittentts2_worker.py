import contextlib
import json
import os
import sys
import traceback

# stdout is a machine-readable JSON-lines protocol. Keep third-party logs off it.
_protocol_out = sys.stdout

def reply(payload):
    _protocol_out.write(json.dumps(payload, ensure_ascii=False) + "\n")
    _protocol_out.flush()

def main():
    try:
        import soundfile as sf
        from kittenml import KittenTTS
        with contextlib.redirect_stdout(sys.stderr):
            model = KittenTTS("KittenML/kitten-tts-2", weights="emb4")
        reply({"ok": True, "type": "ready", "voices": list(model.available_voices), "sample_rate": model.sample_rate})
    except Exception as exc:
        reply({"ok": False, "type": "fatal", "error": str(exc), "trace": traceback.format_exc()})
        return 1

    for line in sys.stdin:
        try:
            command = json.loads(line)
            action = command.get("action")
            if action == "shutdown":
                reply({"ok": True, "type": "shutdown"})
                return 0
            if action != "speak":
                reply({"ok": False, "error": "Unsupported worker action."})
                continue

            text = str(command.get("text", "")).strip()
            voice = str(command.get("voice", "Luna")).strip()
            output = os.path.abspath(str(command.get("output", "")))
            if not text:
                reply({"ok": False, "error": "There is no text to speak."})
                continue
            if voice not in model.available_voices:
                reply({"ok": False, "error": "Unknown KittenTTS 2 voice: " + voice})
                continue

            os.makedirs(os.path.dirname(output), exist_ok=True)
            with contextlib.redirect_stdout(sys.stderr):
                audio = model.generate(text, voice=voice)
            sf.write(output, audio, model.sample_rate, subtype="PCM_16")
            reply({"ok": True, "type": "audio", "output": output})
        except Exception as exc:
            reply({"ok": False, "error": str(exc), "trace": traceback.format_exc()})

    return 0

if __name__ == "__main__":
    raise SystemExit(main())
