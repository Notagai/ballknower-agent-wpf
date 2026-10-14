import contextlib
import json
import os
import subprocess
import sys
import traceback

_protocol_out = sys.stdout

def reply(payload):
    _protocol_out.write(json.dumps(payload, ensure_ascii=False) + "\n")
    _protocol_out.flush()

def main():
    try:
        with contextlib.redirect_stdout(sys.stderr):
            import imageio_ffmpeg
            from f5_tts.api import F5TTS
            model = F5TTS()
        reply({"ok": True, "type": "ready"})
    except Exception as exc:
        reply({"ok": False, "type": "fatal", "error": str(exc), "trace": traceback.format_exc()})
        return 1

    app_data = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~")
    profile_dir = os.path.join(app_data, "Ballknower", "VoiceClones")
    os.makedirs(profile_dir, exist_ok=True)
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    normalized_reference = os.path.join(profile_dir, "reference-normalized.wav")

    for line in sys.stdin:
        try:
            command = json.loads(line)
            action = command.get("action")
            if action == "shutdown":
                reply({"ok": True, "type": "shutdown"})
                return 0
            if action != "speak":
                reply({"ok": False, "error": "Unsupported voice-clone action."})
                continue

            text = str(command.get("text", "")).strip()
            reference = os.path.abspath(str(command.get("reference", "")))
            reference_text = str(command.get("reference_text", "")).strip()
            consent = command.get("consent") is True
            output = os.path.abspath(str(command.get("output", "")))
            if not consent:
                reply({"ok": False, "error": "Confirm that you own this voice or have the speaker's explicit permission."})
                continue
            if not os.path.isfile(reference):
                reply({"ok": False, "error": "Upload a reference audio or video clip in Speech settings first."})
                continue
            if not reference_text:
                reply({"ok": False, "error": "Enter the exact words spoken in the reference clip."})
                continue
            if not text:
                reply({"ok": False, "error": "There is no text to speak."})
                continue

            os.makedirs(os.path.dirname(output), exist_ok=True)
            conversion = subprocess.run(
                [ffmpeg, "-y", "-i", reference, "-vn", "-ac", "1", "-ar", "24000",
                 "-t", "15", normalized_reference],
                stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True, timeout=120
            )
            if conversion.returncode != 0 or not os.path.isfile(normalized_reference):
                reply({"ok": False, "error": "Could not extract audio from that clip. Try a clear audio/video file with audible speech."})
                continue

            with contextlib.redirect_stdout(sys.stderr):
                model.infer(
                    ref_file=normalized_reference,
                    ref_text=reference_text,
                    gen_text=text,
                    file_wave=output
                )
            if not os.path.isfile(output):
                reply({"ok": False, "error": "The voice model did not produce an audio file."})
                continue
            reply({"ok": True, "type": "audio", "output": output})
        except subprocess.TimeoutExpired:
            reply({"ok": False, "error": "Timed out extracting audio from the reference clip."})
        except Exception as exc:
            reply({"ok": False, "error": str(exc), "trace": traceback.format_exc()})

    return 0

if __name__ == "__main__":
    raise SystemExit(main())
