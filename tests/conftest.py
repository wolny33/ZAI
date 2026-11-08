"""Shared settings and fixtures for the API tests (pytest loads this file automatically)."""

import os
import uuid

import pytest
import requests

# Test series get this prefix, so they are easy to tell apart from real data
NAME_PREFIX = "SMOKE-"


@pytest.fixture(scope="session")
def api_url():
    """Returns the API address from API_URL (the local Docker backend by default)."""
    return os.environ.get("API_URL", "http://127.0.0.1:8080").rstrip("/")


@pytest.fixture(scope="session")
def admin_headers(api_url):
    """Logs in once for all API tests and logs out after them."""
    password = os.environ.get("ADMIN_PASSWORD")
    if not password:
        pytest.fail("Set the ADMIN_PASSWORD environment variable to run the API tests.")
    username = os.environ.get("ADMIN_USERNAME", "admin")
    response = requests.post(f"{api_url}/api/auth/login",
                             json={"username": username, "password": password}, timeout=10)
    assert response.status_code == 200, response.text
    headers = {"Authorization": f"Bearer {response.json()['accessToken']}"}
    yield headers
    requests.post(f"{api_url}/api/auth/logout", headers=headers, timeout=10)


@pytest.fixture
def make_series(api_url, admin_headers):
    """Returns a function that creates test series; all of them are deleted after the test."""
    created_ids = []

    def create(**fields):
        body = {"name": f"{NAME_PREFIX}{uuid.uuid4().hex[:8]}", "minValue": 0, "maxValue": 100,
                "color": "#123456", **fields}
        response = requests.post(f"{api_url}/api/series", json=body, headers=admin_headers, timeout=10)
        assert response.status_code == 201, response.text
        created_ids.append(response.json()["id"])
        return response.json()

    yield create
    # Deleting a series also deletes its sensors and measurements
    for series_id in created_ids:
        requests.delete(f"{api_url}/api/series/{series_id}", headers=admin_headers, timeout=10)


@pytest.fixture
def sensor(api_url, admin_headers, make_series):
    """Registers a sensor for a new test series with the range 0-100."""
    series = make_series()
    response = requests.post(f"{api_url}/api/sensors", json={"name": f"{NAME_PREFIX}sensor", "seriesId": series["id"]},
                             headers=admin_headers, timeout=10)
    assert response.status_code == 201, response.text
    return response.json()
