"""Tests of the running API: our contract extensions and details the course tests do not check.
They need a running backend (API_URL, by default http://127.0.0.1:8080) and the ADMIN_PASSWORD variable.
"""

from datetime import datetime
import requests


def problem(response):
    """Checks that an error response follows RFC 9457 and returns its body."""
    assert response.headers["Content-Type"].startswith("application/problem+json")
    body = response.json()
    assert body["status"] == response.status_code
    assert body["title"]
    return body


def field_errors(body):
    """Turns the errors list of a problem response into {field: message}."""
    return {error["field"]: error["message"] for error in body["errors"]}


def test_series_keeps_icon_and_unit(api_url, make_series):
    created = make_series(icon="thermometer", unit="°C")
    assert created["icon"] == "thermometer"
    assert created["unit"] == "°C"

    response = requests.get(f"{api_url}/api/series/{created['id']}", timeout=10)
    assert response.status_code == 200
    assert response.json() == created


def test_missing_fields_are_listed_in_one_response(api_url, admin_headers):
    response = requests.post(f"{api_url}/api/series", json={}, headers=admin_headers, timeout=10)
    assert response.status_code == 400
    body = problem(response)
    assert body["instance"] == "/api/series"
    assert field_errors(body) == {
        "name": "Field is required.",
        "minValue": "Field is required.",
        "maxValue": "Field is required.",
        "color": "Field is required.",
    }


def test_null_in_a_required_field_is_a_wrong_type(api_url):
    response = requests.post(f"{api_url}/api/auth/login", json={"username": None, "password": None}, timeout=10)
    assert response.status_code == 400
    assert field_errors(problem(response)) == {
        "username": "Value must be a string.",
        "password": "Value must be a string.",
    }


def test_broken_rules_return_422_with_every_field(api_url, admin_headers):
    body = {"name": "", "minValue": 10, "maxValue": 5, "color": "red"}
    response = requests.post(f"{api_url}/api/series", json=body, headers=admin_headers, timeout=10)
    assert response.status_code == 422
    assert field_errors(problem(response)) == {
        "name": "Value must not be empty.",
        "maxValue": "Value must be greater than minValue.",
        "color": "Value must be a color in the #RRGGBB format.",
    }


def test_created_sensor_has_flat_fields_and_a_long_key(sensor):
    assert set(sensor) == {"id", "name", "seriesId", "createdAt", "lastMeasurementAt", "apiKey"}
    assert sensor["lastMeasurementAt"] is None
    assert len(sensor["apiKey"]) == 43


def test_sensor_list_does_not_reveal_keys(api_url, admin_headers, sensor):
    response = requests.get(f"{api_url}/api/sensors", headers=admin_headers, timeout=10)
    assert response.status_code == 200
    assert sensor["apiKey"] not in response.text
    assert all("apiKey" not in item for item in response.json())


def test_measurement_records_its_sensor_and_updates_last_measurement_time(api_url, admin_headers, sensor):
    timestamp = "2026-01-10T10:00:00Z"
    response = requests.post(f"{api_url}/api/measurements", json={"value": 21.5, "timestamp": timestamp},
                             headers={"X-API-Key": sensor["apiKey"]}, timeout=10)
    assert response.status_code == 201
    measurement = response.json()
    assert measurement["sensorId"] == sensor["id"]
    assert measurement["seriesId"] == sensor["seriesId"]

    response = requests.get(f"{api_url}/api/sensors/{sensor['id']}", headers=admin_headers, timeout=10)
    assert datetime.fromisoformat(response.json()["lastMeasurementAt"]) == datetime.fromisoformat(timestamp)


def test_value_outside_range_is_rejected_with_the_range(api_url, sensor):
    response = requests.post(f"{api_url}/api/measurements", json={"value": 150},
                             headers={"X-API-Key": sensor["apiKey"]}, timeout=10)
    assert response.status_code == 422
    assert field_errors(problem(response)) == {"value": "Value must be between 0 and 100."}


def test_unknown_address_returns_404_without_internal_details(api_url):
    response = requests.get(f"{api_url}/api/no-such-resource", timeout=10)
    assert response.status_code == 404
    body = problem(response)
    assert body["traceId"]
    # Only the standard fields, no exception details
    assert set(body) <= {"type", "title", "status", "detail", "instance", "errors", "traceId"}
