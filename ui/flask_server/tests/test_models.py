import json

import pytest

from flask_server.flask_server import (
    MPData,
    Grid,
    Magnet,
    SCREEN_SIZE_X,
    SCREEN_SIZE_Y,
    construct_mpdata_json,
)


def test_magnet_stores_fields():
    magnet = Magnet(uid="marker_1", x=10.5, y=20.25)

    assert magnet.uid == "marker_1"
    assert magnet.x == 10.5
    assert magnet.y == 20.25


def test_grid_defaults_match_screen_size():
    grid = Grid()

    assert grid.x_min == 0
    assert grid.x_max == SCREEN_SIZE_X
    assert grid.y_min == 0
    assert grid.y_max == SCREEN_SIZE_Y


def test_mpdata_defaults_are_independent():
    first = MPData()
    second = MPData()

    first.mag_list.append(Magnet("a", 1.0, 2.0))
    first.grid.x_max = 999

    assert second.mag_list == []
    assert second.grid.x_max == SCREEN_SIZE_X


def test_construct_mpdata_json_empty_state():
    payload = json.loads(construct_mpdata_json(MPData()))

    assert payload["magnets"] == {}
    assert payload["grid"] == {
        "x_min": 0,
        "x_max": SCREEN_SIZE_X,
        "y_min": 0,
        "y_max": SCREEN_SIZE_Y,
    }
    assert payload["magnetic_strength"] == 1
    assert payload["damping_factor"] == 1
    assert payload["pendulum_height"] == 1
    assert payload["pendulum_length"] == 1


def test_construct_mpdata_json_includes_magnets_and_custom_values():
    data = MPData(
        mag_list=[
            Magnet("marker_1", 40.0, 60.0),
            Magnet("marker_2", 10.0, 15.0),
        ],
        magnetic_strength=2.5,
        damping_factor=0.8,
        pendulum_height=10.0,
        pendulum_length=20.0,
    )
    data.grid.x_max = 200

    payload = json.loads(construct_mpdata_json(data))

    assert payload["magnets"] == {
        "magnet_0": {"x": 40.0, "y": 60.0},
        "magnet_1": {"x": 10.0, "y": 15.0},
    }
    assert payload["grid"]["x_max"] == 200
    assert payload["magnetic_strength"] == 2.5
    assert payload["damping_factor"] == 0.8
    assert payload["pendulum_height"] == 10.0
    assert payload["pendulum_length"] == 20.0


@pytest.mark.parametrize(
    ("field_name", "value"),
    [
        ("magnetic_strength", 3.3),
        ("damping_factor", 0.5),
        ("pendulum_height", 12.0),
        ("pendulum_length", 18.0),
    ],
)
def test_construct_mpdata_json_uses_instance_fields(field_name, value):
    data = MPData(**{field_name: value})

    payload = json.loads(construct_mpdata_json(data))

    assert payload[field_name] == value
