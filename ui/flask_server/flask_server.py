from dataclasses import dataclass, field
import json

from flask import Flask, request

# TODO: DISCUSS LATER
SCREEN_SIZE_X = 160
SCREEN_SIZE_Y = 120

app = Flask(__name__)


@dataclass
class Magnet:
    uid: str
    x: float
    y: float


@dataclass
class Grid:
    x_min: float = 0
    x_max: float = SCREEN_SIZE_X
    y_min: float = 0
    y_max: float = SCREEN_SIZE_Y


@dataclass
class MPData:
    mag_list: list[Magnet] = field(default_factory=list)
    grid: Grid = field(default_factory=Grid)
    magnetic_strength: float = 1
    damping_factor: float = 1
    pendulum_height: float = 1
    pendulum_length: float = 1


mp_data = MPData()


def reset_mp_data() -> None:
    mp_data.mag_list.clear()
    mp_data.grid = Grid()
    mp_data.magnetic_strength = 1
    mp_data.damping_factor = 1
    mp_data.pendulum_height = 1
    mp_data.pendulum_length = 1


def construct_mpdata_json(data: MPData) -> str:
    payload = {
        "magnets": {},
        "grid": {},
    }

    for index, magnet in enumerate(data.mag_list):
        payload["magnets"][f"magnet_{index}"] = {
            "x": magnet.x,
            "y": magnet.y,
        }

    payload["grid"]["x_min"] = data.grid.x_min
    payload["grid"]["x_max"] = data.grid.x_max
    payload["grid"]["y_min"] = data.grid.y_min
    payload["grid"]["y_max"] = data.grid.y_max

    payload["magnetic_strength"] = data.magnetic_strength
    payload["damping_factor"] = data.damping_factor
    payload["pendulum_height"] = data.pendulum_height
    payload["pendulum_length"] = data.pendulum_length

    return json.dumps(payload)


@app.route("/")
def ping():
    return {"ping": "true"}


@app.route("/info")
def info():
    return construct_mpdata_json(mp_data)


@app.route("/magnet_add", methods=["POST"])
def magnet_add():
    body = request.get_json()

    uid = body["uid"]
    x = float(body["x"])
    y = float(body["y"])

    mp_data.mag_list.append(Magnet(uid, x, y))

    return {"ok": 200}


@app.route("/magnet_remove", methods=["POST"])
def magnet_remove():
    body = request.get_json()

    uid = body["uid"]
    for index, magnet in enumerate(mp_data.mag_list):
        if magnet.uid == uid:
            mp_data.mag_list.pop(index)
            break

    return {"ok": 200}


@app.route("/magnet_update_position")
def magnet_update_position():
    body = request.get_json()

    uid = body["uid"]
    x = float(body["x"])
    y = float(body["y"])

    for index, magnet in enumerate(mp_data.mag_list):
        if magnet.uid == uid:
            mp_data.mag_list[index].x = x
            mp_data.mag_list[index].y = y
            break

    return {"ok": 200}


@app.route("/magnetic_strength")
def magnetic_strength():
    body = request.get_json()

    mp_data.magnetic_strength = float(body["magnetic_strength"])

    return {"ok": 200}


@app.route("/damping_factor")
def damping_factor():
    body = request.get_json()

    mp_data.damping_factor = float(body["damping_factor"])

    return {"ok": 200}


@app.route("/pendulum_height")
def pendulum_height():
    body = request.get_json()

    mp_data.pendulum_height = float(body["pendulum_height"])

    return {"ok": 200}


@app.route("/pendulum_length")
def length_of_pendulum():
    body = request.get_json()

    mp_data.pendulum_length = float(body["pendulum_length"])

    return {"ok": 200}


if __name__ == "__main__":
    app.run()
