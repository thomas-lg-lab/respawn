"""Sign-in with an ID token, against a provider answering from memory."""

from __future__ import annotations

import time

import httpx
import pytest
from fastapi.testclient import TestClient

from respawn import oidc
from respawn.config import _providers
from respawn.main import create_app
from tests.conftest import CLIENT_ID, ISSUER, FakeIssuer, register, settings

NONCE = "nonce-123456"


def exchange(client: TestClient, token: str, nonce: str = NONCE, provider: str = "test") -> httpx.Response:
    return client.post("/api/v1/auth/oidc", json={"provider": provider, "id_token": token, "nonce": nonce})


def test_config_lists_the_providers(client: TestClient) -> None:
    providers = client.get("/api/v1/config").json()["oidc_providers"]
    assert providers == [{"name": "test", "issuer": "https://issuer.test", "client_id": "respawn-test"}]


def test_a_valid_token_creates_the_first_account(client: TestClient, issuer: FakeIssuer) -> None:
    response = exchange(client, issuer.token())
    assert response.status_code == 200
    assert response.json() == client.get("/api/v1/me").json()
    assert response.json()["is_admin"] is True
    assert response.json()["display_name"] == "Player One"


def test_the_same_identity_signs_in_to_the_same_account(client: TestClient, issuer: FakeIssuer) -> None:
    first = exchange(client, issuer.token()).json()
    client.cookies.clear()
    # Another token of the same subject, the e-mail changed meanwhile: resolved by issuer and subject.
    again = exchange(client, issuer.token(email="renamed@example.org", iat=int(time.time()) - 1))
    assert again.status_code == 200
    assert again.json()["id"] == first["id"]


def test_refused_tokens(client: TestClient, issuer: FakeIssuer) -> None:
    now = int(time.time())
    refused = {
        "another audience": issuer.token(aud="another-client"),
        "another issuer": issuer.token(iss="https://elsewhere.test"),
        "expired": issuer.token(exp=now - 600, iat=now - 900),
        "another nonce": issuer.token(nonce="nonce-other-value"),
        "no subject": issuer.token(sub=None),
        "signed by another key": issuer.token(key=issuer.other_key),
        "not a token": "not.a.token",
    }
    for reason, token in refused.items():
        response = exchange(client, token)
        assert response.status_code == 401, reason
        assert response.json()["error"]["code"] == "invalid_token", reason
    assert client.get("/api/v1/me").status_code == 401


def test_unknown_provider(client: TestClient, issuer: FakeIssuer) -> None:
    assert exchange(client, issuer.token(), provider="nope").status_code == 404


def test_a_token_is_exchanged_once(client: TestClient, issuer: FakeIssuer) -> None:
    token = issuer.token()
    assert exchange(client, token).status_code == 200
    client.cookies.clear()
    assert exchange(client, token).status_code == 401


def test_a_new_identity_is_refused_when_registration_is_closed(
    client: TestClient, issuer: FakeIssuer
) -> None:
    register(client)
    client.cookies.clear()
    response = exchange(client, issuer.token(sub="someone-else", email="other@example.org"))
    assert (response.status_code, response.json()["error"]["code"]) == (403, "registration_closed")


def test_a_signed_in_account_links_the_provider(client: TestClient, issuer: FakeIssuer) -> None:
    account = register(client).json()
    linked = exchange(client, issuer.token(email="different@example.org"))
    assert linked.status_code == 200
    assert linked.json()["id"] == account["id"]
    client.cookies.clear()
    again = exchange(client, issuer.token(iat=int(time.time()) - 1))
    assert again.json()["id"] == account["id"]


def test_a_used_token_stays_refused_during_the_leeway(client: TestClient, issuer: FakeIssuer) -> None:
    """A token is accepted for a minute after it expires; its record must outlive that minute."""
    now = int(time.time())
    token = issuer.token(exp=now - 30, iat=now - 300)
    assert exchange(client, token).status_code == 200
    client.cookies.clear()
    # Another sign-in runs the clean-up of the records.
    assert exchange(client, issuer.token()).status_code == 200
    client.cookies.clear()
    assert exchange(client, token).status_code == 401


def test_an_issuer_with_a_trailing_slash(client: TestClient, issuer: FakeIssuer) -> None:
    """Configured with or without its final slash, a provider naming itself with one is accepted."""
    for age, configured in enumerate((ISSUER, ISSUER + "/")):
        providers = _providers(f'[{{"name": "test", "issuer": "{configured}", "client_id": "{CLIENT_ID}"}}]')
        app = create_app(settings(oidc_providers=providers), oidc_transport=issuer.transport())
        with TestClient(app) as slashed:
            token = issuer.token(iss=ISSUER + "/", iat=int(time.time()) - age)
            assert exchange(slashed, token).status_code == 200, configured
            assert exchange(slashed, issuer.token(iss=ISSUER + "//")).status_code == 401
        app.state.database.dispose()


def test_refused_tokens_fetch_nothing(client: TestClient, issuer: FakeIssuer) -> None:
    assert exchange(client, "not.a.token").status_code == 401
    assert exchange(client, "garbage").status_code == 401
    assert issuer.requests == 0  # not shaped like a token: the provider is not even asked for its keys
    assert exchange(client, issuer.token()).status_code == 200
    fetched = issuer.requests
    assert fetched == 2  # discovery and keys
    for token in (
        "not.a.token",
        issuer.token(key=issuer.other_key),
        issuer.token(aud="another-client"),
        issuer.token(exp=int(time.time()) - 600),
    ):
        assert exchange(client, token).status_code == 401
    assert issuer.requests == fetched


def test_an_unknown_key_fetches_the_keys_again_but_not_at_will(
    client: TestClient, issuer: FakeIssuer, monkeypatch: pytest.MonkeyPatch
) -> None:
    assert exchange(client, issuer.token()).status_code == 200
    fetched = issuer.requests
    issuer.rotate()
    client.cookies.clear()
    # The keys were fetched a moment ago: a token naming another key is refused without asking again.
    assert exchange(client, issuer.token(iat=int(time.time()) - 1)).status_code == 401
    assert issuer.requests == fetched
    # Once the minimum interval has passed, the new key is fetched and the token accepted.
    monkeypatch.setattr(oidc, "MIN_REFRESH_SECONDS", 0)
    assert exchange(client, issuer.token(iat=int(time.time()) - 2)).status_code == 200
    assert issuer.requests == fetched + 2


def test_an_unreachable_provider_is_not_asked_on_every_request(issuer: FakeIssuer) -> None:
    calls = 0

    def down(request: httpx.Request) -> httpx.Response:
        nonlocal calls
        calls += 1
        return httpx.Response(503)

    app = create_app(settings(), oidc_transport=httpx.MockTransport(down))
    with TestClient(app) as client:
        for _ in range(3):
            response = exchange(client, issuer.token())
            # Not a refused token: nothing could be checked.
            assert (response.status_code, response.json()["error"]["code"]) == (503, "provider_unavailable")
        assert exchange(client, "not.a.token").status_code == 401
    app.state.database.dispose()
    assert calls == 1
