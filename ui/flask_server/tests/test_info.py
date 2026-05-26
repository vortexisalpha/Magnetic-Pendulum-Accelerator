import json


def test_info_returns_default_state(client):
    response = client.get("/info")

    assert response.status_code == 200
    payload = json.loads(response.get_data(as_text=True))
    assert payload["magnets"] == {}
    assert payload["grid"]["x_max"] == 160
    assert payload["magnetic_strength"] == 1


def test_info_reflects_added_magnets(client):
    client.post(
        "/magnet_add",
        json={"uid": "marker_1", "x": 40.0, "y": 60.0},
    )
    client.post(
        "/magnet_add",
        json={"uid": "marker_2", "x": 10.0, "y": 15.0},
    )

    response = client.get("/info")
    payload = json.loads(response.get_data(as_text=True))

    assert payload["magnets"] == {
        "magnet_0": {"x": 40.0, "y": 60.0},
        "magnet_1": {"x": 10.0, "y": 15.0},
    }


def test_info_reflects_updated_parameters(client):
    client.get("/magnetic_strength", json={"magnetic_strength": 2.5})
    client.get("/damping_factor", json={"damping_factor": 0.75})
    client.get("/pendulum_height", json={"pendulum_height": 9.0})
    client.get("/pendulum_length", json={"pendulum_length": 11.0})

    payload = json.loads(client.get("/info").get_data(as_text=True))

    assert payload["magnetic_strength"] == 2.5
    assert payload["damping_factor"] == 0.75
    assert payload["pendulum_height"] == 9.0
    assert payload["pendulum_length"] == 11.0
