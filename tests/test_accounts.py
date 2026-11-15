"""Local accounts: registration, sign-in, lockout, sign-out."""

from __future__ import annotations

import os

import pytest
from fastapi.testclient import TestClient
from uvicorn.middleware.proxy_headers import ProxyHeadersMiddleware

from respawn import accounts
from respawn.api import limiter
from respawn.security import verify_password
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


def test_the_browser_says_where_a_request_comes_from(client: TestClient) -> None:
    """Sec-Fetch-Site decides when the browser sends it, whatever the Host header became on the way."""
    body = {"email": "nobody@example.org", "password": PASSWORD}

    def code(**headers: str) -> int:
        return client.post("/api/v1/auth/login", json=body, headers=headers).status_code

    # 401: went through the guard, refused for its credentials only.
    rewritten = {"Origin": "https://games.example.org", "Host": "app.internal:8080"}
    assert code(**rewritten, **{"Sec-Fetch-Site": "same-origin"}) == 401
    assert code(**{"Sec-Fetch-Site": "none"}) == 401
    for elsewhere in ("cross-site", "same-site", "anything-else"):
        assert code(**rewritten, **{"Sec-Fetch-Site": elsewhere}) == 403, elsewhere
    # An Origin matching the Host does not make up for a browser saying the request is cross-site.
    assert code(**{"Origin": "http://testserver", "Sec-Fetch-Site": "cross-site"}) == 403
    # No Sec-Fetch-Site (an older browser): the Origin is compared with the Host.
    assert code(**rewritten) == 403
    assert code(Origin="http://testserver") == 401
    # Neither header: not a browser page (a script, a command-line client).
    assert code() == 401
    # A forwarded host is no proof of anything: it is not read.
    assert code(**rewritten, **{"X-Forwarded-Host": "games.example.org"}) == 403


def login_codes(client: TestClient, addresses: list[str]) -> list[int]:
    body = {"email": "x@example.org", "password": PASSWORD}
    return [
        client.post("/api/v1/auth/login", json=body, headers={"X-Forwarded-For": address}).status_code
        for address in addresses
    ]


def test_a_forged_forwarded_address_does_not_dodge_the_rate_limit(client: TestClient) -> None:
    """The peer is not a listed proxy (the default): what it says about the client's address is ignored."""
    os.environ["RESPAWN_AUTH_RATE_LIMIT"] = "2/minute"
    limiter.reset()
    # As uvicorn runs the app, with its default: only 127.0.0.1 is a trusted proxy.
    proxied = TestClient(ProxyHeadersMiddleware(client.app, trusted_hosts="127.0.0.1"))  # type: ignore[arg-type]
    assert login_codes(proxied, ["203.0.113.1", "203.0.113.2", "203.0.113.3"]) == [401, 401, 429]


def test_a_proxy_that_is_not_listed_is_reported_once(
    client: TestClient, caplog: pytest.LogCaptureFixture
) -> None:
    """Behind a proxy the app was not told about, the shared rate limit must not stay silent."""
    warning = "FORWARDED_ALLOW_IPS does not list"
    # No proxy at all, then a listed one: nothing to report.
    login_codes(TestClient(ProxyHeadersMiddleware(client.app, trusted_hosts="testclient")), ["203.0.113.1"])  # type: ignore[arg-type]
    client.post("/api/v1/auth/login", json={"email": "x@example.org", "password": PASSWORD})
    assert warning not in caplog.text
    unlisted = TestClient(ProxyHeadersMiddleware(client.app, trusted_hosts="127.0.0.1"))  # type: ignore[arg-type]
    login_codes(unlisted, ["203.0.113.1", "203.0.113.2", "198.51.100.7, 203.0.113.3"])
    assert caplog.text.count(warning) == 1


def test_clients_behind_a_trusted_proxy_are_limited_one_by_one(client: TestClient) -> None:
    """With the proxy listed in FORWARDED_ALLOW_IPS, each visitor has its own count."""
    os.environ["RESPAWN_AUTH_RATE_LIMIT"] = "2/minute"
    limiter.reset()
    # "testclient" is the peer address of the test client: here it stands for the reverse proxy.
    proxied = TestClient(ProxyHeadersMiddleware(client.app, trusted_hosts="testclient"))  # type: ignore[arg-type]
    assert login_codes(proxied, ["203.0.113.1", "203.0.113.1", "203.0.113.2"]) == [401, 401, 401]
    assert login_codes(proxied, ["203.0.113.1", "203.0.113.2", "203.0.113.2"]) == [429, 401, 429]
    # A visitor adding addresses of its own in front of the one the proxy appends stays in its bucket.
    assert login_codes(proxied, ["198.51.100.7, 203.0.113.1"]) == [429]


def test_sign_in_routes_are_rate_limited(client: TestClient) -> None:
    os.environ["RESPAWN_AUTH_RATE_LIMIT"] = "2/minute"
    limiter.reset()
    codes = [
        client.post("/api/v1/auth/login", json={"email": "x@example.org", "password": PASSWORD}).status_code
        for _ in range(3)
    ]
    assert codes == [401, 401, 429]


def test_a_locked_account_is_verified_like_any_other(
    client: TestClient, monkeypatch: pytest.MonkeyPatch
) -> None:
    """The password is checked even when the account is locked: the answer must not come faster."""
    register(client)
    client.cookies.clear()
    for _ in range(3):
        client.post("/api/v1/auth/login", json={"email": "first@example.org", "password": "wrong password"})
    verified: list[str] = []

    def verify(password: str, password_hash: str | None) -> bool:
        verified.append(password)
        return verify_password(password, password_hash)

    monkeypatch.setattr(accounts, "verify_password", verify)
    locked = client.post("/api/v1/auth/login", json={"email": "first@example.org", "password": PASSWORD})
    assert (locked.status_code, locked.json()["error"]["code"]) == (401, "invalid_credentials")
    assert verified == [PASSWORD]
