"""Creates sample data: three series with one sensor each, filled with past measurements.

The series and sensors are created by the administrator; the measurements are sent by
the generator (sensor.py) with each sensor's API key. The keys are kept only in memory.
"""

import argparse
import getpass
import os
import subprocess
import sys
from pathlib import Path

import requests

from sensor import describe_problem, parse_interval

SENSOR_SCRIPT = Path(__file__).with_name("sensor.py")

# Each sample: the series, its sensor and how the generator produces values
SAMPLES = [
    {
        "series": {"name": "Living room temperature", "minValue": 15, "maxValue": 30, "color": "#d62728", "unit": "°C"},
        "sensor": "Living room thermometer",
        "generator": ["--mode", "sine", "--min", "19", "--max", "24"],
    },
    {
        "series": {"name": "Living room humidity", "minValue": 0, "maxValue": 100, "color": "#1f77b4", "unit": "%"},
        "sensor": "Living room hygrometer",
        "generator": ["--mode", "random", "--min", "35", "--max", "55"],
    },
    {
        "series": {"name": "Outdoor temperature", "minValue": -30, "maxValue": 40, "color": "#2ca02c", "unit": "°C"},
        "sensor": "Balcony thermometer",
        "generator": ["--mode", "sine", "--min", "2", "--max", "12"],
    },
]


def post(url, path, body, token=None):
    """Sends a POST request (with the admin token, if given) and stops the script if it fails."""
    headers = {"Authorization": f"Bearer {token}"} if token else {}
    try:
        response = requests.post(f"{url}{path}", json=body, headers=headers, timeout=10)
    except requests.RequestException as error:
        sys.exit(f"POST {path} failed: could not reach {url} ({type(error).__name__})")
    if response.status_code not in (200, 201, 204):
        sys.exit(f"POST {path} failed with {response.status_code}: {describe_problem(response)}")
    return response.json() if response.content else None


def parse_args():
    parser = argparse.ArgumentParser(
        description="Creates three sample series with sensors and sends past measurements with the generator.",
                epilog="The administrator password is read from the ADMIN_PASSWORD environment variable; if it is not set, the script asks for it.",
    )
    parser.add_argument("--url", required=True, help="API address, e.g. https://example.sslip.io")
    parser.add_argument("--user", default="admin", help="administrator login (default: admin)")
    parser.add_argument("--count", type=int, default=20, help="measurements per series (default: 20)")
    parser.add_argument("--interval", default="1h", help="time between measurements (default: 1h)")
    args = parser.parse_args()
    
    if args.count < 1:
        parser.error("--count must be positive")
    try:
        parse_interval(args.interval)
    except argparse.ArgumentTypeError as error:
        parser.error(f"--interval: {error}")
    return args


def main():
    args = parse_args()
    url = args.url.rstrip("/")
    password = os.environ.get("ADMIN_PASSWORD") or getpass.getpass("Admin password: ")

    token = post(url, "/api/auth/login", {"username": args.user, "password": password})["accessToken"]
    failed = 0  
    try:
        for sample in SAMPLES:
            series = post(url, "/api/series", sample["series"], token)
            sensor = post(url, "/api/sensors", {"name": sample["sensor"], "seriesId": series["id"]}, token)
            print(f"\nSeries \"{series['name']}\" (id {series['id']}), sensor \"{sensor['name']}\" (id {sensor['id']})", flush=True)

            # The generator gets the key through its environment, never on the command line
            command = [sys.executable, str(SENSOR_SCRIPT), "--url", url, "--past",
                       "--count", str(args.count), "--interval", args.interval, *sample["generator"]]
            result = subprocess.run(command, env={**os.environ, "SENSOR_KEY": sensor["apiKey"]})
            if result.returncode != 0:
                failed += 1
    finally:
        post(url, "/api/auth/logout", None, token)

    print("\nAll sample data sent." if failed == 0 else f"\n{failed} series did not get all measurements.")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
