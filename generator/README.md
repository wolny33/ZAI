# Sensor emulator (generator)

Sends measurements to the API with `POST /api/measurements`, authenticated with a sensor's API key
(the key is shown once when an administrator registers the sensor).

## Setup

Python 3.10 or newer:

```
pip install -r generator/requirements.txt
```

## Usage

The API key is read from the `SENSOR_KEY` environment variable (or the `--key` option):

```powershell
$env:SENSOR_KEY = "<sensor API key>"     # PowerShell
export SENSOR_KEY="<sensor API key>"     # bash
```

Sample data – 20 measurements from the last ~10 hours, every 30 minutes, sent at once:

```
python generator/sensor.py --url https://example.sslip.io --past --count 20 --interval 30m --mode sine --min 18 --max 24
```

Live – one measurement every 10 seconds until Ctrl+C (the server sets the timestamp):

```
python generator/sensor.py --url https://example.sslip.io --count 0 --interval 10s --min 18 --max 24
```

## Real weather

With `--mode weather` the values are real weather instead of generated numbers. They come from one
of two public services, chosen with `--source` (no account or key needed for either):

- `open-meteo` (default) – [Open-Meteo](https://open-meteo.com/): any place, past and live data,
- `imgw` – [IMGW-PIB](https://danepubliczne.imgw.pl/) (Polish Institute of Meteorology and Water
  Management): readings of real weather stations, live data only.

### Open-Meteo

The place is looked up by name with the Open-Meteo geocoding API (default: Zakopane), or given
with `--latitude` and `--longitude`.

```
python generator/sensor.py --url https://example.sslip.io --mode weather --place "Kasprowy Wierch" --past --count 24 --interval 1h
python generator/sensor.py --url https://example.sslip.io --mode weather --variable rain --count 0 --interval 15m
```

| `--variable` | Unit | Value |
|---|---|---|
| `temperature` | °C | air temperature 2 m above the ground |
| `humidity` | % | relative humidity 2 m above the ground |
| `rain` | mm | rain sum: of the preceding hour in past mode, of the last 15 minutes in live mode |
| `snowfall` | cm | snowfall sum, like rain |

- **Past mode** downloads hourly data once (at most 92 days back) and every measurement gets the
  value of the nearest full hour, so use `--interval 1h` or longer.
- **Live mode** downloads the current value for every measurement; Open-Meteo updates it every
  15 minutes, so use `--interval 15m` or longer. Shorter intervals work, but values repeat
  (the generator prints a note).
- The data comes from numerical weather models fed with station observations, calculated for the
  given point and its altitude
- Real values can fall outside the series range; the server then rejects them with `422`.

### IMGW stations

`--place` is the name of one of the IMGW synoptic stations (letter case does not matter), e.g.
`Kasprowy Wierch`, `Zakopane`, `Śnieżka`, `Kraków`, `Warszawa`; the full list is at
<https://danepubliczne.imgw.pl/api/data/synop>. Only `temperature` and `humidity` are available.

```
python generator/sensor.py --url https://example.sslip.io --mode weather --source imgw --place "Kasprowy Wierch" --variable humidity --count 0 --interval 1h
```

- Every measurement sends the latest reading of the station; IMGW updates them every hour, so use
  `--interval 1h` (shorter intervals repeat the same value, the generator prints a note).
- The generator prints the time of the last reading and a note when it is more than 2 hours old.
- `--past` and coordinates cannot be used with IMGW.

Source of the data: Institute of Meteorology and Water Management – National Research Institute
(IMGW-PIB).

## Sample data

`seed_sample_data.py` creates three sample series, registers a sensor for each and runs the
generator with each sensor's key (20 past measurements per series, every hour):

```
python generator/seed_sample_data.py --url https://example.sslip.io
```

The administrator password is read from the `ADMIN_PASSWORD` environment variable or asked for.
Options: `--user` (default `admin`), `--count` (default `20`), `--interval` (default `1h`).

## Options

| Option | Meaning | Default |
|---|---|---|
| `--url` | API address | required |
| `--key` | sensor API key (prefer `SENSOR_KEY`) | – |
| `--mode` | `random` (smooth random walk), `sine` (sine wave with noise, one full cycle every 24 values) or `weather` (real weather from Open-Meteo) | `random` |
| `--min`, `--max` | range of generated values (`random` and `sine`) | `0`, `100` |
| `--source` | `weather` mode: `open-meteo` or `imgw` | `open-meteo` |
| `--variable` | `weather` mode: `temperature`, `humidity`, `rain` or `snowfall` (IMGW: only the first two) | `temperature` |
| `--place` | `weather` mode: place name to look up, or the IMGW station name | `Zakopane` |
| `--latitude`, `--longitude` | `weather` mode, Open-Meteo: coordinates instead of `--place` (both together) | – |
| `--count` | number of measurements; `0` = until Ctrl+C (live mode only) | `20` |
| `--interval` | time between measurements: `30s`, `10m`, `1h` | `10s` |
| `--past` | send all measurements at once with timestamps going back from now | off (live mode) |

Each measurement is printed with the server's answer. A value outside the series range is
reported as `REJECTED 422` with the server's message; an invalid or unregistered key stops the
generator with `401`. If weather data cannot be downloaded for a measurement, it is reported as
`WEATHER ERROR` and the generator goes on with the next one.