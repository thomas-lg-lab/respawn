"""Local accounts: registration, sign-in, lockout, sign-out."""

from __future__ import annotations

import os

from fastapi.testclient import TestClient

from respawn.api import limiter
from tests.conftest import PASSWORD, register


def test_first_account_is_administrator_and_signed_in(client: TestClient) -> None:
    assert client.get("/api/v1/config").json()["registration_open"] is True
    response = register(client)
    assert response.status_code == 201
    assert response.json()["is_admin"] is True
    assert "HttpOnly" in response.headers["set-cookie"]
    me = client.get("/api/v1/me").json()
    assert me["email"] == "first@example.org"
    assert me["display_name"] == "First"


def test_registration_closes_after_the_first_account(client: TestClient) -> None:
    register(client)
    client.cookies.clear()
    assert client.get("/api/v1/config").json()["registration_open"] is False
    response = register(client, email="second@example.org", name="Second")
    assert response.status_code == 403
    assert response.json()["error"]["code"] == "registration_closed"


def test_weak_password_and_bad_email_are_refused(client: TestClient) -> None:
    weak = client.post(
        "/api/v1/auth/register", json={"email": "a@example.org", "display_name": "A", "password": "short"}
    )
    assert (weak.status_code, weak.json()["error"]["code"]) == (422, "weak_password")
    bad = client.post(
        "/api/v1/auth/register", json={"email": "not-an-email", "display_name": "A", "password": PASSWORD}
    )
    assert (bad.status_code, bad.json()["error"]["code"]) == (422, "invalid_email")
    missing = client.post("/api/v1/auth/register", json={"email": "a@example.org"})
    assert (missing.status_code, missing.json()["error"]["code"]) == (422, "invalid_request")


def test_login_logout(client: TestClient) -> None:
    register(client)
    assert client.post("/api/v1/auth/logout").status_code == 204
    assert client.get("/api/v1/me").status_code == 401

    wrong = client.post(
        "/api/v1/auth/login", json={"email": "first@example.org", "password": "wrong password"}
    )
    assert (wrong.status_code, wrong.json()["error"]["code"]) == (401, "invalid_credentials")
    unknown = client.post("/api/v1/auth/login", json={"email": "nobody@example.org", "password": PASSWORD})
    assert unknown.status_code == 401

    ok = client.post("/api/v1/auth/login", json={"email": " First@Example.org ", "password": PASSWORD})
    assert ok.status_code == 200
    assert client.get("/api/v1/me").status_code == 200


def test_repeated_failures_lock_the_account(client: TestClient) -> None:
    register(client)
    client.cookies.clear()
    for _ in range(3):  # lockout_threshold of the test settings
        client.post("/api/v1/auth/login", json={"email": "first@example.org", "password": "wrong password"})
    locked = client.post("/api/v1/auth/login", json={"email": "first@example.org", "password": PASSWORD})
    assert locked.status_code == 401


def test_a_session_ends_with_its_cookie(client: TestClient) -> None:
    register(client)
    token = client.cookies["respawn_session"]
    client.post("/api/v1/auth/logout")
    client.cookies.set("respawn_session", token)
    assert client.get("/api/v1/me").status_code == 401


def test_a_request_from_another_site_is_refused(client: TestClient) -> None:
    response = client.post(
        "/api/v1/auth/login",
        json={"email": "first@example.org", "password": PASSWORD},
        headers={"Origin": "https://elsewhere.example.org"},
    )
    assert (response.status_code, response.json()["error"]["code"]) == (403, "cross_site_request")


def test_sign_in_routes_are_rate_limited(client: TestClient) -> None:
    os.environ["RESPAWN_AUTH_RATE_LIMIT"] = "2/minute"
    limiter.reset()
    codes = [
        client.post("/api/v1/auth/login", json={"email": "x@example.org", "password": PASSWORD}).status_code
        for _ in range(3)
    ]
    assert codes == [401, 401, 429]
