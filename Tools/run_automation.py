#!/usr/bin/env python3
"""Run a deterministic Velkhana Slice automation scenario using only the Python stdlib."""

import argparse
import json
import pathlib
import subprocess
import sys
import time
import urllib.error
import urllib.request


def request(base_url, method, path, payload=None, timeout=180):
    data = None
    headers = {}
    if payload is not None:
        data = json.dumps(payload, separators=(",", ":")).encode("utf-8")
        headers["Content-Type"] = "application/json"
    call = urllib.request.Request(base_url + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(call, timeout=timeout) as response:
            body = response.read().decode("utf-8")
    except urllib.error.HTTPError as error:
        body = error.read().decode("utf-8", errors="replace")
        raise RuntimeError(f"{method} {path} returned HTTP {error.code}: {body}") from error
    return json.loads(body) if body else None


def wait_until_ready(base_url, timeout):
    deadline = time.monotonic() + timeout
    last_error = None
    while time.monotonic() < deadline:
        try:
            health = request(base_url, "GET", "/health", timeout=2)
            if health.get("ready"):
                return
        except (OSError, RuntimeError) as error:
            last_error = error
        time.sleep(0.1)
    raise RuntimeError(f"automation API did not become ready: {last_error}")


def run_scenario(base_url, scenario, output_path=None):
    reset = dict(scenario.get("reset", {}))
    reset.setdefault("paused", True)
    state = request(base_url, "POST", "/reset", reset)

    output = None
    if output_path:
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output = output_path.open("w", encoding="utf-8", newline="\n")
        output.write(json.dumps({"label": "reset", "state": state}, separators=(",", ":")) + "\n")

    try:
        for index, step in enumerate(scenario.get("steps", []), start=1):
            label = step.get("label", f"step_{index:03d}")
            controls = step.get("input", {})
            request(base_url, "POST", "/input", controls)
            state = request(
                base_url,
                "POST",
                "/step",
                {"frames": int(step.get("frames", 1))},
                timeout=max(180, int(step.get("frames", 1)) // 30 + 30),
            )

            capture = step.get("capture")
            if capture:
                capture_path = capture if isinstance(capture, str) else f"captures/{label}.png"
                request(base_url, "POST", "/capture", {"path": capture_path})

            if output:
                output.write(json.dumps({"label": label, "state": state}, separators=(",", ":")) + "\n")
                output.flush()

            hunter = state.get("hunter") or {}
            monster = state.get("monster") or {}
            print(
                f"[{index:03d}] {label}: frame={state.get('simulationFrame')} "
                f"hunter={hunter.get('state')} monster={monster.get('state')} "
                f"action={((monster.get('attack') or {}).get('name'))}"
            )
    finally:
        if output:
            output.close()

    request(base_url, "POST", "/input", {})
    return state


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("scenario", type=pathlib.Path, help="scenario JSON file")
    parser.add_argument("--url", default="http://127.0.0.1:47777")
    parser.add_argument("--output", type=pathlib.Path, help="write one labelled state per step as JSONL")
    parser.add_argument("--exe", type=pathlib.Path, help="launch this standalone player before running")
    parser.add_argument("--telemetry", type=pathlib.Path, help="player-side per-fixed-frame JSONL path")
    parser.add_argument("--startup-timeout", type=float, default=30.0)
    parser.add_argument("--keep-running", action="store_true", help="do not POST /quit to a launched player")
    args = parser.parse_args()

    scenario = json.loads(args.scenario.read_text(encoding="utf-8"))
    process = None
    if args.exe:
        command = [str(args.exe), "-automation"]
        port = args.url.rstrip("/").rsplit(":", 1)[-1]
        if port.isdigit():
            command += ["-automation-port", port]
        if args.telemetry:
            command += ["-telemetry", str(args.telemetry)]
        process = subprocess.Popen(command)

    try:
        wait_until_ready(args.url.rstrip("/"), args.startup_timeout)
        run_scenario(args.url.rstrip("/"), scenario, args.output)
    finally:
        if process is not None and not args.keep_running:
            try:
                request(args.url.rstrip("/"), "POST", "/quit", {}, timeout=3)
            except Exception:
                process.terminate()
            try:
                process.wait(timeout=10)
            except subprocess.TimeoutExpired:
                process.kill()


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"automation failed: {error}", file=sys.stderr)
        raise SystemExit(1)
