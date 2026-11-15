"""What the runtime expects of the app whatever its state."""

from __future__ import annotations

from pathlib import Path

from fastapi.testclient import TestClient

from respawn.config import Settings
from respawn.main import create_app
from tests.conftest import DATABASE_URL, register


def test_healthz_answers_without_database_or_secrets(offline_client: TestClient) -> None:
    assert offline_client.get("/healthz").json() == {"status": "ok"}


def test_page_is_served_without_database(offline_client: TestClient) -> None:
    response = offline_client.get("/")
    assert response.status_code == 200
    assert "respawn" in response.text
    assert response.headers["content-security-policy"].startswith("default-src 'self'")


def test_data_routes_answer_503_without_database(offline_client: TestClient) -> None:
    response = offline_client.get("/api/v1/me")
    assert response.status_code == 503
    assert response.json()["error"]["code"] == "database_unavailable"


def test_database_is_picked_up_when_its_file_appears(tmp_path: Path) -> None:
    file = tmp_path / "database-url"
    app = create_app(Settings(database_url=None, database_url_file=file))
    with TestClient(app) as client:
        assert client.get("/api/v1/config").status_code == 503
        # The plain URI a managed database hands out, without the driver name.
        file.write_text(
            DATABASE_URL.replace("postgresql+psycopg://", "postgresql://") + "\n", encoding="utf-8"
        )
        assert client.get("/api/v1/config").status_code == 200
    app.state.database.dispose()


def test_a_forged_remote_user_header_gives_no_access(client: TestClient) -> None:
    register(client)
    client.cookies.clear()
    response = client.get(
        "/api/v1/me", headers={"Remote-User": "first@example.org", "Remote-Groups": "admins"}
    )
    assert response.status_code == 401


def test_openapi_description_is_published(client: TestClient) -> None:
    paths = client.get("/api/v1/openapi.json").json()["paths"]
    assert "/api/v1/me" in paths
    assert "/healthz" not in paths
