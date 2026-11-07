"""Sensor emulator for the ZAI 26Z project.

Sends measurements to the API (POST /api/measurements) with the sensor's API key.
The key is read from the SENSOR_KEY environment variable or the --key option.
"""

import argparse
import math
import os
import random
import re
import sys
import time
from datetime import datetime, timedelta, timezone

import requests

UNIT_SECONDS = {"s": 1, "m": 60, "h": 3600}


def parse_interval(text):
    """Parses an interval such as '30s', '10m' or '1h' (no unit means seconds)."""
    match = re.fullmatch(r"(\d+(?:\.\d+)?)([smh]?)", text.strip())
    if match is None:
        raise argparse.ArgumentTypeError("use a number with s, m or h, e.g. 30s, 10m, 1h")
    seconds = float(match.group(1)) * UNIT_SECONDS[match.group(2) or "s"]
    if seconds <= 0:
        raise argparse.ArgumentTypeError("the interval must be greater than zero")
    return timedelta(seconds=seconds)


def random_walk(low, high):
    """Generates random values that change smoothly and stay within [low, high]."""
    value = random.uniform(low, high)
    step = (high - low) * 0.1
    while True:
        value = min(high, max(low, value + random.uniform(-step, step)))
        yield round(value, 2)


def sine_wave(low, high, period=24):
    """Generates a sine wave with one full cycle every `period` values, plus a little noise."""
    middle = (low + high) / 2
    amplitude = (high - low) / 2 * 0.9
    noise = (high - low) * 0.02
    index = 0
    while True:
        value = middle + amplitude * math.sin(2 * math.pi * index / period) + random.uniform(-noise, noise)
        yield round(min(high, max(low, value)), 2)
        index += 1


# Value sources selected with --mode
MODES = {"random": random_walk, "sine": sine_wave}


def send_measurement(url, key, value, timestamp):
    """Sends one measurement; without a timestamp the server uses its current time."""
    body = {"value": value}
    if timestamp is not None:
        body["timestamp"] = timestamp.strftime("%Y-%m-%dT%H:%M:%SZ")
    return requests.post(f"{url}/api/measurements", json=body, headers={"X-API-Key": key}, timeout=10)


def describe_problem(response):
    """Builds a readable message from an RFC 9457 error response."""
    try:
        problem = response.json()
    except ValueError:
        return response.text.strip() or response.reason
    errors = problem.get("errors") or []
    if errors:
        return "; ".join(f"{error.get('field')}: {error.get('message')}" for error in errors)
    return problem.get("detail") or problem.get("title") or response.reason


def parse_args():
    parser = argparse.ArgumentParser(
        description="Sensor emulator: sends measurements to the ZAI API.",
        epilog="The API key is read from the SENSOR_KEY environment variable (or --key).",
    )
    parser.add_argument("--url", required=True, help="API address, e.g. https://example.sslip.io")
    parser.add_argument("--key", help="sensor API key (prefer the SENSOR_KEY environment variable)")
    parser.add_argument("--mode", choices=sorted(MODES), default="random", help="how values are generated (default: random)")
    parser.add_argument("--min", type=float, default=0.0, help="lowest generated value (default: 0)")
    parser.add_argument("--max", type=float, default=100.0, help="highest generated value (default: 100)")
    parser.add_argument("--count", type=int, default=20, help="number of measurements; 0 = until Ctrl+C (live mode only) (default: 20)")
    parser.add_argument("--interval", type=parse_interval, default=timedelta(seconds=10), help="time between measurements, e.g. 30s, 10m, 1h (default: 10s)")
    parser.add_argument("--past", action="store_true", help="send all measurements at once with timestamps going back from now")
    args = parser.parse_args()

    if args.min >= args.max:
        parser.error("--min must be lower than --max")
    if args.count < 0 or (args.past and args.count == 0):
        parser.error("--count must be positive (0 is allowed only in live mode)")
    return args


def send_and_report(url, key, value, timestamp, prefix):
    """Sends one measurement, prints the result and returns its outcome."""
    try:
        response = send_measurement(url, key, value, timestamp)
    except requests.RequestException as error:
        print(f"{prefix} CONNECTION ERROR: could not reach {url} ({type(error).__name__})")
        return "failed"
    if response.status_code == 201:
        saved = response.json()
        print(f"{prefix} 201 Created (id {saved.get('id')}, saved at {saved.get('timestamp')})")
        return "saved"
    if response.status_code == 401:
        print(f"{prefix} 401 Unauthorized: the API key is invalid or the sensor was unregistered. Stopping.")
        return "unauthorized"
    if response.status_code in (400, 422):
        print(f"{prefix} REJECTED {response.status_code}: {describe_problem(response)}")
        return "rejected"
    print(f"{prefix} ERROR {response.status_code}: {describe_problem(response)}")
    return "failed"


def main():
    args = parse_args()
    key = args.key or os.environ.get("SENSOR_KEY")
    if not key:
        print("Missing API key: set the SENSOR_KEY environment variable or use --key.", file=sys.stderr)
        return 2

    url = args.url.rstrip("/")
    values = MODES[args.mode](args.min, args.max)
    total = str(args.count) if args.count > 0 else "-"
    start = datetime.now(timezone.utc) - args.interval * (args.count - 1) if args.past else None
    live_start = datetime.now(timezone.utc)
    counts = {"saved": 0, "rejected": 0, "failed": 0, "unauthorized": 0}
    number = 0

    try:
        while args.count == 0 or number < args.count:
            number += 1
            value = next(values)
            timestamp = start + args.interval * (number - 1) if args.past else None
            shown_time = (timestamp or datetime.now(timezone.utc)).strftime("%Y-%m-%d %H:%M:%SZ")
            prefix = f"[{number:>3}/{total}] {shown_time}  value={value:>9.2f}  ->"

            outcome = send_and_report(url, key, value, timestamp, prefix)
            counts[outcome] += 1
            if outcome == "unauthorized":
                break  # stop sending if the API key is invalid

            # keep a fixed interval between live measurements
            if not args.past and (args.count == 0 or number < args.count):
                next_time = live_start + args.interval * number
                time.sleep(max(0, (next_time - datetime.now(timezone.utc)).total_seconds()))
    except KeyboardInterrupt:
        print("Stopped.")

    print(f"Done: {counts['saved']} saved, {counts['rejected']} rejected, {counts['failed']} failed.")
    return 0 if counts["saved"] == sum(counts.values()) else 1


if __name__ == "__main__":
    sys.exit(main())
