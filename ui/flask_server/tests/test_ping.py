def test_ping_returns_ok(client):
    response = client.get("/")

    assert response.status_code == 200
    assert response.get_json() == {"ping": "true"}


def test_ping_uses_get_only(client):
    response = client.post("/")

    assert response.status_code == 405
