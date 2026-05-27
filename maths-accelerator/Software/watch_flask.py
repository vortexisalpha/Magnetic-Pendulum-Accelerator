#!/usr/bin/env python3
"""
Poll Flask /info and re-run cpu_fixedpoint when controller params change.

Unity posts slider values to /controller_data; this script detects the change
and launches a fresh basin-map compute + repost to /image.

Usage (from maths-accelerator/):
    python Software/watch_flask.py

Requires Flask server running and cpu_fixedpoint built:
    g++ -O2 -std=c++17 Software/cpu_fixedpoint.cpp -o Software/cpu_fixedpoint
"""

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path

import requests

ROOT = Path(__file__).resolve().parent.parent
BINARY = ROOT / "Software" / "cpu_fixedpoint"
DEFAULT_FLASK = "http://127.0.0.1:5000"

PARAM_KEYS = (
    "magnetic_strength",
    "damping_factor",
    "pendulum_length",
    "pendulum_height",
)


def fetch_params(base_url: str) -> dict | None:
    try:
        resp = requests.get(f"{base_url}/info", timeout=5)
        resp.raise_for_status()
        data = resp.json()
    except requests.RequestException as exc:
        print(f"[watch] Flask unreachable: {exc}", file=sys.stderr)
        return None

    params = {key: data.get(key) for key in PARAM_KEYS}
    if any(v is None for v in params.values()):
        print("[watch] /info missing controller fields", file=sys.stderr)
        return None
    if params["pendulum_height"] <= 0 or params["pendulum_length"] <= 0:
        print("[watch] waiting for valid pendulum params from Unity...", file=sys.stderr)
        return None

    return params


def run_simulation(flask_base: str, extra_args: list[str]) -> int:
    cmd = [str(BINARY), "--flask-base", flask_base, *extra_args]
    print(f"[watch] running: {' '.join(cmd)}")
    return subprocess.run(cmd, cwd=ROOT, check=False).returncode


def main() -> None:
    parser = argparse.ArgumentParser(description="Recompute basin map when Flask params change.")
    parser.add_argument("--flask-base", default=DEFAULT_FLASK)
    parser.add_argument(
        "--poll",
        type=float,
        default=0.25,
        help="Seconds between /info checks (default: 0.25)",
    )
    parser.add_argument(
        "--run-on-start",
        action="store_true",
        help="Compute once immediately, even if params have not changed yet",
    )
    parser.add_argument(
        "sim_args",
        nargs="*",
        help="Extra args forwarded to cpu_fixedpoint (e.g. --width 160 --height 120)",
    )
    args = parser.parse_args()

    if not BINARY.is_file():
        print(f"[watch] missing binary: {BINARY}", file=sys.stderr)
        print("Build with:", file=sys.stderr)
        print(
            "  g++ -O2 -std=c++17 Software/cpu_fixedpoint.cpp -o Software/cpu_fixedpoint",
            file=sys.stderr,
        )
        sys.exit(1)

    last_params: dict | None = None
    pending = args.run_on_start

    print(f"[watch] polling {args.flask_base}/info every {args.poll}s")
    print("[watch] move Unity sliders to trigger recompute (Ctrl+C to stop)")

    while True:
        params = fetch_params(args.flask_base)
        if params is not None:
            if pending or params != last_params:
                if last_params is not None:
                    changed = {
                        k: (last_params[k], params[k])
                        for k in PARAM_KEYS
                        if last_params[k] != params[k]
                    }
                    print(f"[watch] params changed: {json.dumps(changed)}")
                elif pending:
                    print(f"[watch] initial run with params: {json.dumps(params)}")

                rc = run_simulation(args.flask_base, args.sim_args)
                if rc != 0:
                    print(f"[watch] simulation exited with code {rc}", file=sys.stderr)
                else:
                    print("[watch] done — Unity can refresh GET /image")

                last_params = params
                pending = False

        time.sleep(args.poll)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\n[watch] stopped")
