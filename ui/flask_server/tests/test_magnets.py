import json

from flask_server.flask_server import mp_data


def test_magnet_add_appends_magnet(client):
    response = client.post(
        "/magnet_add",
        json={"uid": "marker_1", "x": 40.0, "y": 60.0},
    )

    assert response.status_code == 200
    assert response.get_json() == {"ok": 200}
    assert len(mp_data.mag_list) == 1
    assert mp_data.mag_list[0].uid == "marker_1"
    assert mp_data.mag_list[0].x == 40.0
    assert mp_data.mag_list[0].y == 60.0


def test_magnet_add_accepts_multiple_magnets(client):
    client.post("/magnet_add", json={"uid": "marker_1", "x": 1.0, "y": 2.0})
    client.post("/magnet_add", json={"uid": "marker_2", "x": 3.0, "y": 4.0})

    assert [magnet.uid for magnet in mp_data.mag_list] == ["marker_1", "marker_2"]


def test_magnet_add_updates_existing_uid(client):
    client.post("/magnet_add", json={"uid": "marker_1", "x": 1.0, "y": 2.0})

    response = client.post(
        "/magnet_add",
        json={"uid": "marker_1", "x": 3.0, "y": 4.0},
    )

    assert response.status_code == 200
    assert response.get_json() == {"ok": 200}
    assert len(mp_data.mag_list) == 1
    assert mp_data.mag_list[0].x == 3.0
    assert mp_data.mag_list[0].y == 4.0


def test_magnet_add_coerces_numeric_strings(client):
    client.post("/magnet_add", json={"uid": "marker_1", "x": "40.5", "y": "60.25"})

    assert mp_data.mag_list[0].x == 40.5
    assert mp_data.mag_list[0].y == 60.25


def test_magnet_remove_deletes_matching_uid(client):
    client.post("/magnet_add", json={"uid": "marker_1", "x": 1.0, "y": 2.0})
    client.post("/magnet_add", json={"uid": "marker_2", "x": 3.0, "y": 4.0})

    response = client.post("/magnet_remove", json={"uid": "marker_1"})

    assert response.status_code == 200
    assert response.get_json() == {"ok": 200}
    assert [magnet.uid for magnet in mp_data.mag_list] == ["marker_2"]


def test_magnet_remove_is_noop_for_unknown_uid(client):
    client.post("/magnet_add", json={"uid": "marker_1", "x": 1.0, "y": 2.0})

    response = client.post("/magnet_remove", json={"uid": "missing"})

    assert response.status_code == 200
    assert len(mp_data.mag_list) == 1


def test_magnet_update_position_changes_coordinates(client):
    client.post("/magnet_add", json={"uid": "marker_1", "x": 1.0, "y": 2.0})

    response = client.get(
        "/magnet_update_position",
        json={"uid": "marker_1", "x": 80.0, "y": 30.0},
    )

    assert response.status_code == 200
    assert response.get_json() == {"ok": 200}
    assert mp_data.mag_list[0].x == 80.0
    assert mp_data.mag_list[0].y == 30.0


def test_magnet_update_position_only_updates_matching_uid(client):
    client.post("/magnet_add", json={"uid": "marker_1", "x": 1.0, "y": 2.0})
    client.post("/magnet_add", json={"uid": "marker_2", "x": 3.0, "y": 4.0})

    client.get(
        "/magnet_update_position",
        json={"uid": "marker_2", "x": 50.0, "y": 60.0},
    )

    assert mp_data.mag_list[0].x == 1.0
    assert mp_data.mag_list[0].y == 2.0
    assert mp_data.mag_list[1].x == 50.0
    assert mp_data.mag_list[1].y == 60.0


def test_magnet_workflow_end_to_end(client):
    client.post("/magnet_add", json={"uid": "marker_1", "x": 10.0, "y": 20.0})
    client.get(
        "/magnet_update_position",
        json={"uid": "marker_1", "x": 15.0, "y": 25.0},
    )
    client.post("/magnet_remove", json={"uid": "marker_1"})

    payload = json.loads(client.get("/info").get_data(as_text=True))
    assert payload["magnets"] == {}
