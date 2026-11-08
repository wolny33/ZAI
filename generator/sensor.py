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
from urllib.parse import urlparse

import requests

UNIT_SECONDS = {"s": 1, "m": 60, "h": 3600}

OPEN_METEO_URL = "https://api.open-meteo.com/v1/forecast"
GEOCODING_URL = "https://geocoding-api.open-meteo.com/v1/search"
IMGW_URL = "https://danepubliczne.imgw.pl/api/data/synop"
# Open-Meteo keeps hourly data for at most this many past days
MAX_PAST_DAYS = 92
# Values measured in weather mode (--variable) and their names in Open-Meteo
WEATHER_VARIABLES = {"temperature": "temperature_2m", "humidity": "relative_humidity_2m",
                     "rain": "rain", "snowfall": "snowfall"}
# Values that IMGW stations give and their field names
IMGW_FIELDS = {"temperature": "temperatura", "humidity": "wilgotnosc_wzgledna"}


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
    step = (high - low) * 0.1
    value = random.uniform(low + step, high - step)
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


# Generated values selected with --mode (the weather mode is handled separately)
MODES = {"random": random_walk, "sine": sine_wave}


class WeatherError(Exception):
    """Raised when weather data cannot be downloaded."""


def fetch_weather_json(url, params=None):
    """Sends a GET request to a weather service and returns the JSON body."""
    try:
        response = requests.get(url, params=params, timeout=10)
        response.raise_for_status()
        return response.json()
    except (requests.RequestException, ValueError) as error:
        raise WeatherError(f"{urlparse(url).netloc} request failed ({type(error).__name__})") from error


def find_place(name):
    """Finds a place by name and returns its latitude, longitude and full name."""
    results = fetch_weather_json(GEOCODING_URL, {"name": name, "count": 1}).get("results")
    if not results:
        raise WeatherError(f"place '{name}' was not found")
    place = results[0]
    return place["latitude"], place["longitude"], f"{place['name']}, {place.get('country', '?')}"


def open_meteo_source(latitude, longitude, variable, past_start=None):
    """Returns a function that gives the real weather value for a measurement time.
    In live mode every call downloads the current value. In past mode the hourly values
    are downloaded once and each measurement gets the value of the nearest hour.
    """
    location = {"latitude": latitude, "longitude": longitude}
    if past_start is None:
        def current_value(_measured_at):
            return fetch_weather_json(OPEN_METEO_URL, {**location, "current": variable})["current"][variable]
        return current_value

    days = min(MAX_PAST_DAYS, (datetime.now(timezone.utc) - past_start).days + 1)
    # Two forecast days, because the hour nearest to now can already be after midnight
    hourly = fetch_weather_json(OPEN_METEO_URL, {**location, "hourly": variable, "past_days": days, "forecast_days": 2})["hourly"]
    # Open-Meteo returns UTC times such as "2026-11-08T10:00"
    by_hour = {datetime.fromisoformat(hour).replace(tzinfo=timezone.utc): value
               for hour, value in zip(hourly["time"], hourly[variable])}

    def hourly_value(measured_at):
        hour = (measured_at + timedelta(minutes=30)).replace(minute=0, second=0, microsecond=0)
        if by_hour.get(hour) is None:
            raise WeatherError(f"no weather data for {hour:%Y-%m-%d %H:%M}Z")
        return by_hour[hour]
    return hourly_value


def find_imgw_station(name):
    """Finds an IMGW synoptic station by name (case does not matter).
    Returns the station id, its full name and the UTC time of its latest reading.
    """
    for reading in fetch_weather_json(IMGW_URL):
        if reading["stacja"].casefold() == name.casefold():
            # The date and the hour of the reading are separate fields
            reading_time = datetime.fromisoformat(reading["data_pomiaru"]).replace(
                hour=int(reading["godzina_pomiaru"]), tzinfo=timezone.utc)
            return reading["id_stacji"], reading["stacja"], reading_time
    raise WeatherError(f"IMGW station '{name}' was not found")


def imgw_source(station_id, field):
    """Returns a function that gives the latest reading of an IMGW station (updated every hour)."""
    def latest_value(_measured_at):
        # IMGW sends numbers as text, e.g. "1.4"
        value = fetch_weather_json(f"{IMGW_URL}/id/{station_id}")[field]
        if value is None:
            raise WeatherError("the latest IMGW reading has no value")
        return float(value)
    return latest_value


def make_value_source(args, past_start):
    """Returns a function that gives the value for a measurement time, depending on --mode and --source."""
    if args.mode != "weather":
        values = MODES[args.mode](args.min, args.max)
        return lambda _measured_at: next(values)
    if args.source == "imgw":
        return imgw_value_source(args)
    return open_meteo_value_source(args, past_start)


def imgw_value_source(args):
    """Finds the IMGW station from --place and returns its source of readings."""
    station_id, station_name, reading_time = find_imgw_station(args.place)
    print(f"Weather: {args.variable} at IMGW station {station_name} "
          f"(last reading {reading_time:%Y-%m-%d %H:%M}Z), data from IMGW-PIB")
    if datetime.now(timezone.utc) - reading_time > timedelta(hours=2):
        print("Note: the last reading of this station is more than 2 hours old.")
    if args.interval < timedelta(hours=1):
        print("Note: IMGW updates readings every hour, so some measurements repeat.")
    return imgw_source(station_id, IMGW_FIELDS[args.variable])


def open_meteo_value_source(args, past_start):
    """Finds the place for Open-Meteo and returns its source of weather values."""
    if args.latitude is not None:
        latitude, longitude, name = args.latitude, args.longitude, "the given coordinates"
    else:
        latitude, longitude, name = find_place(args.place)
    print(f"Weather: {args.variable} in {name} ({latitude:.2f}, {longitude:.2f}), data from Open-Meteo")
    if args.past and args.interval < timedelta(hours=1):
        print("Note: Open-Meteo has hourly data, so measurements in the same hour get the same value.")
    if not args.past and args.interval < timedelta(minutes=15):
        print("Note: Open-Meteo updates current values every 15 minutes, so some measurements repeat.")
    return open_meteo_source(latitude, longitude, WEATHER_VARIABLES[args.variable], past_start)


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
    parser.add_argument("--mode", choices=[*sorted(MODES), "weather"], default="random",
                        help="random walk, sine wave or real weather from Open-Meteo (default: random)")
    parser.add_argument("--min", type=float, default=0.0, help="lowest generated value, random and sine modes (default: 0)")
    parser.add_argument("--max", type=float, default=100.0, help="highest generated value, random and sine modes (default: 100)")
    parser.add_argument("--source", choices=["open-meteo", "imgw"], default="open-meteo",
                        help="weather mode: Open-Meteo (weather models, any place, past and live) "
                             "or IMGW (Polish weather stations, live only) (default: open-meteo)")
    parser.add_argument("--variable", choices=sorted(WEATHER_VARIABLES), default="temperature",
                        help="weather mode: temperature (°C), humidity (%%), rain (mm) or snowfall (cm) "
                             "(default: temperature)")
    parser.add_argument("--place", default="Zakopane",
                        help="weather mode: place to look up by name, or the IMGW station name (default: Zakopane)")
    parser.add_argument("--latitude", type=float, help="weather mode: latitude, used with --longitude instead of --place")
    parser.add_argument("--longitude", type=float, help="weather mode: longitude, used with --latitude instead of --place")
    parser.add_argument("--count", type=int, default=20, help="number of measurements; 0 = until Ctrl+C (live mode only) (default: 20)")
    parser.add_argument("--interval", type=parse_interval, default=timedelta(seconds=10), help="time between measurements, e.g. 30s, 10m, 1h (default: 10s)")
    parser.add_argument("--past", action="store_true", help="send all measurements at once with timestamps going back from now")
    args = parser.parse_args()

    if args.min >= args.max:
        parser.error("--min must be lower than --max")
    if args.count < 0 or (args.past and args.count == 0):
        parser.error("--count must be positive (0 is allowed only in live mode)")
    if (args.latitude is None) != (args.longitude is None):
        parser.error("--latitude and --longitude must be given together")
    if args.mode == "weather" and args.past and args.interval * (args.count - 1) >= timedelta(days=MAX_PAST_DAYS):
        parser.error(f"weather data goes back at most {MAX_PAST_DAYS} days")
    if args.mode == "weather" and args.source == "imgw":
        if args.past:
            parser.error("--source imgw gives only current readings, so it cannot be used with --past")
        if args.variable not in IMGW_FIELDS:
            parser.error("--source imgw gives only temperature and humidity")
        if args.latitude is not None:
            parser.error("--source imgw chooses the station by name with --place, not by coordinates")
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
    total = str(args.count) if args.count > 0 else "-"
    start = datetime.now(timezone.utc) - args.interval * (args.count - 1) if args.past else None
    try:
        value_at = make_value_source(args, start)
    except WeatherError as error:
        print(f"Weather data unavailable: {error}", file=sys.stderr)
        return 1
    live_start = datetime.now(timezone.utc)
    counts = {"saved": 0, "rejected": 0, "failed": 0, "unauthorized": 0}
    number = 0

    try:
        while args.count == 0 or number < args.count:
            number += 1
            timestamp = start + args.interval * (number - 1) if args.past else None
            measured_at = timestamp or datetime.now(timezone.utc)
            label = f"[{number:>3}/{total}] {measured_at:%Y-%m-%d %H:%M:%SZ}"

            try:
                value = value_at(measured_at)
            except WeatherError as error:
                print(f"{label}  WEATHER ERROR: {error}")
                outcome = "failed"
            else:
                outcome = send_and_report(url, key, value, timestamp, f"{label}  value={value:>9.2f}  ->")
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
