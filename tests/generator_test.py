"""Unit tests of the sensor emulator functions (no server needed)."""

import argparse
from datetime import datetime, timedelta, timezone
from itertools import islice

import pytest

from sensor import (WeatherError, describe_problem, find_place, parse_interval, random_walk, sine_wave,
                    weather_source)


@pytest.mark.parametrize("text, seconds", [("30s", 30), ("10m", 600), ("1.5h", 5400), ("45", 45)])
def test_parse_interval_reads_number_and_unit(text, seconds):
    assert parse_interval(text) == timedelta(seconds=seconds)


@pytest.mark.parametrize("text", ["abc", "10x", "-5s", "0", ""])
def test_parse_interval_rejects_invalid_text(text):
    with pytest.raises(argparse.ArgumentTypeError):
        parse_interval(text)


@pytest.mark.parametrize("generator", [random_walk, sine_wave])
def test_generated_values_stay_within_range(generator):
    values = list(islice(generator(18, 24), 1000))
    assert all(18 <= value <= 24 for value in values)


def test_sine_wave_covers_most_of_the_range_in_one_cycle():
    values = list(islice(sine_wave(0, 100), 24))
    assert max(values) - min(values) > 80


class FakeResponse:
    """Stands in for requests.Response in describe_problem tests."""

    def __init__(self, body=None, text="", reason="Unprocessable Content"):
        self.body = body
        self.text = text
        self.reason = reason

    def json(self):
        if self.body is None:
            raise ValueError("not JSON")
        return self.body


def test_describe_problem_lists_field_errors():
    response = FakeResponse({
        "title": "Unprocessable Content",
        "errors": [
            {"field": "value", "message": "Value must be between 0 and 100."},
            {"field": "timestamp", "message": "Value must be a date-time."},
        ],
    })
    assert describe_problem(response) == "value: Value must be between 0 and 100.; timestamp: Value must be a date-time."


def test_describe_problem_uses_detail_without_field_errors():
    response = FakeResponse({"title": "Unauthorized", "detail": "Missing or invalid API key."})
    assert describe_problem(response) == "Missing or invalid API key."


def test_describe_problem_falls_back_to_text_for_non_json():
    assert describe_problem(FakeResponse(text=" Bad Gateway ")) == "Bad Gateway"


def fake_open_meteo(monkeypatch, body):
    """Makes fetch_open_meteo return the given body instead of calling Open-Meteo."""
    monkeypatch.setattr("sensor.fetch_open_meteo", lambda url, params: body)


def test_find_place_returns_the_first_result(monkeypatch):
    fake_open_meteo(monkeypatch, {"results": [{"name": "Kasprowy Wierch", "country": "Poland",
                                               "latitude": 49.23192, "longitude": 19.98173}]})
    assert find_place("Kasprowy Wierch") == (49.23192, 19.98173, "Kasprowy Wierch, Poland")


def test_find_place_reports_an_unknown_name(monkeypatch):
    fake_open_meteo(monkeypatch, {"generationtime_ms": 0.5})
    with pytest.raises(WeatherError, match="was not found"):
        find_place("No such place")


def test_live_weather_reads_the_current_value(monkeypatch):
    fake_open_meteo(monkeypatch, {"current": {"time": "2026-11-08T10:00", "temperature_2m": 1.2}})
    value_at = weather_source(49.23, 19.98, "temperature_2m")
    assert value_at(datetime.now(timezone.utc)) == 1.2


def test_past_weather_takes_the_nearest_hour(monkeypatch):
    fake_open_meteo(monkeypatch, {"hourly": {"time": ["2026-11-08T10:00", "2026-11-08T11:00"],
                                             "temperature_2m": [1.5, 2.5]}})
    value_at = weather_source(49.23, 19.98, "temperature_2m", past_start=datetime.now(timezone.utc))
    assert value_at(datetime(2026, 11, 8, 10, 20, tzinfo=timezone.utc)) == 1.5
    assert value_at(datetime(2026, 11, 8, 10, 40, tzinfo=timezone.utc)) == 2.5


def test_past_weather_reports_a_missing_hour(monkeypatch):
    fake_open_meteo(monkeypatch, {"hourly": {"time": ["2026-11-08T10:00"], "temperature_2m": [None]}})
    value_at = weather_source(49.23, 19.98, "temperature_2m", past_start=datetime.now(timezone.utc))
    with pytest.raises(WeatherError, match="no weather data"):
        value_at(datetime(2026, 11, 8, 10, 0, tzinfo=timezone.utc))