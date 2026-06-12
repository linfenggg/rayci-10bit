#!/usr/bin/env python

from __future__ import annotations

import argparse
import signal
import subprocess
import sys
import time
from pathlib import Path


BHLOG = r"C:\Program Files\Bus Hound\bhlog.exe"
RAYCI = r"C:\Program Files\CINOGY\RayCi64 Lite\RayCi.exe"


def run_powershell(script: str, timeout: float) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script],
        check=True,
        capture_output=True,
        text=True,
        timeout=timeout,
    )


def close_existing_rayci(timeout_sec: float) -> None:
    script = r"""
$procs = @(Get-Process -Name 'RayCi' -ErrorAction SilentlyContinue)
if ($procs.Count -eq 0) {
    return
}

foreach ($proc in $procs) {
    try {
        if ($proc.MainWindowHandle -ne 0) {
            $null = $proc.CloseMainWindow()
        }
    } catch {
    }
}

Start-Sleep -Seconds 3
$remaining = @(Get-Process -Name 'RayCi' -ErrorAction SilentlyContinue)
if ($remaining.Count -gt 0) {
    $remaining | Stop-Process -Force
}
"""
    run_powershell(script, timeout_sec)


def wait_for_rayci_window(timeout_sec: float) -> str:
    script = rf"""
$deadline = (Get-Date).AddSeconds({timeout_sec})
while ((Get-Date) -lt $deadline) {{
    $proc = Get-Process -Name 'RayCi' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $proc -and $proc.MainWindowHandle -ne 0 -and $proc.MainWindowTitle) {{
        Write-Output $proc.MainWindowTitle
        exit 0
    }}
    Start-Sleep -Milliseconds 300
}}
throw 'Timed out waiting for RayCi main window.'
"""
    result = run_powershell(script, timeout_sec + 5.0)
    return result.stdout.strip()


def stop_bhlog(proc: subprocess.Popen[bytes]) -> None:
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


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Run a continuous Bus Hound capture across a full RayCi session."
    )
    parser.add_argument("logfile")
    parser.add_argument("--close-timeout-sec", type=float, default=15.0)
    parser.add_argument("--startup-timeout-sec", type=float, default=25.0)
    parser.add_argument("--post-startup-sec", type=float, default=5.0)
    parser.add_argument(
        "--no-launch-rayci",
        action="store_true",
        help="Do not launch RayCi automatically after starting Bus Hound.",
    )
    args = parser.parse_args()

    if not Path(BHLOG).exists():
        print(f"bhlog not found: {BHLOG}", file=sys.stderr)
        return 2

    if not args.no_launch_rayci and not Path(RAYCI).exists():
        print(f"RayCi executable not found: {RAYCI}", file=sys.stderr)
        return 3

    log_path = Path(args.logfile).resolve()
    log_path.parent.mkdir(parents=True, exist_ok=True)

    close_existing_rayci(args.close_timeout_sec)

    bhlog_proc = subprocess.Popen(
        [BHLOG, str(log_path)],
        creationflags=subprocess.CREATE_NEW_PROCESS_GROUP,
    )

    rayci_proc: subprocess.Popen[bytes] | None = None
    started_title = ""
    try:
        time.sleep(1.0)
        if not args.no_launch_rayci:
            rayci_proc = subprocess.Popen([RAYCI])
            started_title = wait_for_rayci_window(args.startup_timeout_sec)
            time.sleep(args.post_startup_sec)

        print(f"capture_log={log_path}", flush=True)
        if started_title:
            print(f'rayci_window="{started_title}"', flush=True)
        print("capture_status=running", flush=True)
        print("Press Enter to stop capture after the RayCi session is complete.", flush=True)
        input()
    except KeyboardInterrupt:
        print("capture_status=interrupted", flush=True)
    finally:
        stop_bhlog(bhlog_proc)

    print("capture_status=stopped", flush=True)
    if rayci_proc is not None:
        print("rayci_status=left-running", flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
