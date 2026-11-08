"""Unit tests of the sensor emulator functions (no server needed)."""

import argparse
from datetime import timedelta
from itertools import islice

import pytest

from sensor import describe_problem, parse_interval, random_walk, sine_wave


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
