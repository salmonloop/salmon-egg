#!/usr/bin/env python3
"""Drive two native navigation cases on the one PID owned by the calling gate."""
import argparse
import os
from pathlib import Path
import re
import subprocess
import time


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--display", required=True)
    parser.add_argument("--pid", required=True, type=int)
    parser.add_argument("--window", required=True)
    parser.add_argument("--boot-log", required=True, type=Path)
    parser.add_argument("--exchange", required=True, type=Path)
    parser.add_argument("--artifacts", required=True, type=Path)
    args = parser.parse_args()
    environment = dict(os.environ, DISPLAY=args.display)
    args.exchange.mkdir(parents=True, exist_ok=True)
    deadline = time.monotonic() + 45

    def command(*arguments):
        return subprocess.check_output(arguments, env=environment, text=True, timeout=5).strip()

    def wait_for(pattern):
        while time.monotonic() < deadline:
            os.kill(args.pid, 0)
            text = args.boot_log.read_text(errors="replace") if args.boot_log.exists() else ""
            match = re.search(pattern, text)
            if match:
                return match
            if "NavMaskProbe: status run faulted" in text:
                raise RuntimeError("Native diagnostic driver failed; inspect boot.log")
            time.sleep(0.05)
        raise TimeoutError(f"Missing native marker: {pattern}")

    window = args.window
    def click_marker(phase):
        match = wait_for(rf"NavInteractionProbe ready={phase} x=(-?\d+) y=(-?\d+)")
        screenshot(f"interaction-{phase}-before.png")
        print(f"[probe] phase={phase} client-target=({match[1]},{match[2]}) window={window}", flush=True)
        command("xdotool", "mousemove", "--window", window, match[1], match[2], "click", "1")

    def screenshot(name):
        subprocess.run(["ffmpeg", "-v", "error", "-y", "-f", "x11grab", "-video_size", "1280x900",
                        "-i", args.display, "-frames:v", "1", "-threads", "1", str(args.artifacts / name)],
                       env=environment, check=True, timeout=5)

    click_marker("ancestor")
    # The native selection animation lasts up to 600 ms. Pacing is diagnostic only; the result is
    # independently read from the real indicator owner, not inferred from this delay.
    time.sleep(0.8)
    screenshot("interaction-ancestor.png")
    first_phase = args.boot_log.read_text(errors="replace").split("NavInteractionProbe ready=ancestor", 1)[1]
    if not re.search(r"MainNav ItemInvoked:.*tag=StatusGroup:Working", first_phase):
        raise AssertionError("Pointer did not hit the native Working header; this is an invalid test input")
    (args.exchange / "ancestor.done").touch()
    click_marker("keyboard")
    command("xdotool", "keydown", "Up")
    command("xdotool", "keyup", "Up")
    command("xdotool", "keydown", "Down")
    command("xdotool", "keyup", "Down")
    time.sleep(0.1)
    (args.exchange / "keyboard.done").touch()
    wait_for(r"NavInteractionProbe ready=enter ")
    command("xdotool", "key", "Return")
    time.sleep(0.8)
    screenshot("interaction-keyboard.png")
    (args.exchange / "enter.done").touch()
    click_marker("leave-focus")
    time.sleep(0.8)
    screenshot("interaction-focus-release.png")
    (args.exchange / "leave-focus.done").touch()
    match = wait_for(r"NavInteractionProbe complete passed=(True|False)")
    if match[1] != "True":
        raise AssertionError("Native ancestor/focus contract failed; inspect interaction results in boot.log")
    print("[probe] Native unrelated-group collapse, focused-row Enter and focus-release migration passed.")


if __name__ == "__main__":
    main()
