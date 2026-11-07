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

## Options

| Option | Meaning | Default |
|---|---|---|
| `--url` | API address | required |
| `--key` | sensor API key (prefer `SENSOR_KEY`) | – |
| `--mode` | `random` (smooth random walk) or `sine` (sine wave with noise, one full cycle every 24 values) | `random` |
| `--min`, `--max` | range of generated values | `0`, `100` |
| `--count` | number of measurements; `0` = until Ctrl+C (live mode only) | `20` |
| `--interval` | time between measurements: `30s`, `10m`, `1h` | `10s` |
| `--past` | send all measurements at once with timestamps going back from now | off (live mode) |

Each measurement is printed with the server's answer. A value outside the series range is
reported as `REJECTED 422` with the server's message; an invalid or unregistered key stops the
generator with `401`.
