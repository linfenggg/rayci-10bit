#!/usr/bin/env python

from __future__ import annotations

import argparse
import signal
import subprocess
import sys
import time
from pathlib import Path


BHLOG = r"C:\Program Files\Bus Hound\bhlog.exe"
CONTROL = str(Path(__file__).with_name("invoke-rayci-action.ps1"))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("logfile")
    parser.add_argument("action")
    parser.add_argument("--timeout-sec", type=float, default=45.0)
    parser.add_argument("--pre-delay-sec", type=float, default=1.5)
    parser.add_argument("--post-delay-sec", type=float, default=2.5)
    args = parser.parse_args()

    log_path = str(Path(args.logfile).resolve())
    Path(log_path).parent.mkdir(parents=True, exist_ok=True)

    proc = subprocess.Popen(
        [BHLOG, log_path],
        creationflags=subprocess.CREATE_NEW_PROCESS_GROUP,
    )

    try:
        time.sleep(args.pre_delay_sec)
        ps_cmd = [
            "powershell",
            "-NoProfile",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            CONTROL,
            "-Action",
            args.action,
        ]
        subprocess.run(ps_cmd, check=True, timeout=args.timeout_sec)
        time.sleep(args.post_delay_sec)
    except subprocess.TimeoutExpired as exc:
        print(f"action timed out after {args.timeout_sec}s: {exc.cmd}", file=sys.stderr)
        return 2
    finally:
        try:
            proc.send_signal(signal.CTRL_BREAK_EVENT)
        except Exception:
            proc.terminate()
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait(timeout=5)
        time.sleep(1.0)

    print(log_path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
