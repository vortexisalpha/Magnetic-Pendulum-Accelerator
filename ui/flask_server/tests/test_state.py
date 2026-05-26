import json

from flask_server.flask_server import Magnet, mp_data, reset_mp_data


def test_reset_mp_data_clears_magnets_and_defaults():
    mp_data.mag_list.append(Magnet("marker_1", 1.0, 2.0))
    mp_data.magnetic_strength = 5.0
    mp_data.damping_factor = 0.25
    mp_data.pendulum_height = 9.0
    mp_data.pendulum_length = 11.0
    mp_data.grid.x_max = 999

    reset_mp_data()

    assert mp_data.mag_list == []
    assert mp_data.magnetic_strength == 1
    assert mp_data.damping_factor == 1
    assert mp_data.pendulum_height == 1
    assert mp_data.pendulum_length == 1
    assert mp_data.grid.x_max == 160


def test_reset_mp_data_leaves_shared_state_ready_for_next_request(client):
    mp_data.mag_list.append(Magnet("marker_1", 1.0, 2.0))
    mp_data.magnetic_strength = 7.7

    reset_mp_data()

    response = client.get("/info")
    payload = json.loads(response.get_data(as_text=True))

    assert payload["magnets"] == {}
    assert payload["magnetic_strength"] == 1
