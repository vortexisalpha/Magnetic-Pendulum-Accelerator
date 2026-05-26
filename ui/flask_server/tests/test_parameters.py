import pytest

from flask_server.flask_server import mp_data


@pytest.mark.parametrize(
    ("route", "field_name", "value"),
    [
        ("/magnetic_strength", "magnetic_strength", 2.5),
        ("/damping_factor", "damping_factor", 0.8),
        ("/pendulum_height", "pendulum_height", 10.0),
        ("/pendulum_length", "pendulum_length", 20.0),
    ],
)
def test_parameter_endpoints_update_state(client, route, field_name, value):
    response = client.get(route, json={field_name: value})

    assert response.status_code == 200
    assert response.get_json() == {"ok": 200}
    assert getattr(mp_data, field_name) == value


@pytest.mark.parametrize(
    ("route", "field_name", "raw_value", "expected"),
    [
        ("/magnetic_strength", "magnetic_strength", "3.75", 3.75),
        ("/damping_factor", "damping_factor", "0.25", 0.25),
        ("/pendulum_height", "pendulum_height", "12", 12.0),
        ("/pendulum_length", "pendulum_length", "18.5", 18.5),
    ],
)
def test_parameter_endpoints_coerce_numeric_strings(
    client,
    route,
    field_name,
    raw_value,
    expected,
):
    response = client.get(route, json={field_name: raw_value})

    assert response.status_code == 200
    assert getattr(mp_data, field_name) == expected


def test_parameter_updates_do_not_affect_unrelated_fields(client):
    client.get("/magnetic_strength", json={"magnetic_strength": 9.9})

    assert mp_data.magnetic_strength == 9.9
    assert mp_data.damping_factor == 1
    assert mp_data.pendulum_height == 1
    assert mp_data.pendulum_length == 1
