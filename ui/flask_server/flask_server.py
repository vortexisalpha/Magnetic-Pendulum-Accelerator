from dataclasses import dataclass, field
import json

from flask import Flask, request

# TODO: DISCUSS LATER
SCREEN_SIZE_X = 160
SCREEN_SIZE_Y = 120

FPGA_PIXEL_BIT_DEPTH = 6

PHYSICAL_COORD_MIN = -1.8
PHYSICAL_COORD_MAX = 1.8

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


def _ui_axis_to_physical(value, ui_min, ui_max):
    if ui_max == ui_min:
        return (PHYSICAL_COORD_MIN + PHYSICAL_COORD_MAX) / 2
    t = (value - ui_min) / (ui_max - ui_min)
    return PHYSICAL_COORD_MIN + t * (PHYSICAL_COORD_MAX - PHYSICAL_COORD_MIN)


def ui_grid_to_physical_grid(ui_grid: Grid) -> Grid:
    return Grid(
        x_min=_ui_axis_to_physical(ui_grid.x_min, ui_grid.x_min, ui_grid.x_max),
        x_max=_ui_axis_to_physical(ui_grid.x_max, ui_grid.x_min, ui_grid.x_max),
        y_min=_ui_axis_to_physical(ui_grid.y_min, ui_grid.y_min, ui_grid.y_max),
        y_max=_ui_axis_to_physical(ui_grid.y_max, ui_grid.y_min, ui_grid.y_max),
    )


def default_image() -> list[list[int]]:
    return [[0 for _ in range(SCREEN_SIZE_X)]  for _ in range(SCREEN_SIZE_Y)] 

@dataclass
class MPData:
    mag_list: list[Magnet] = field(default_factory=list)
    ui_grid: Grid = field(default_factory=Grid)
    physical_grid: Grid = field(default_factory=Grid)
    magnetic_strength: float = 1
    damping_factor: float = 1
    pendulum_height: float = 1
    pendulum_length: float = 1
    image: list[list[int]] = field(default_factory=default_image)


mp_data = MPData()


def sync_physical_grid(data: MPData) -> None:
    data.physical_grid = ui_grid_to_physical_grid(data.ui_grid)


sync_physical_grid(mp_data)


def reset_mp_data() -> None:
    mp_data.mag_list.clear()
    mp_data.ui_grid = Grid()
    sync_physical_grid(mp_data)
    mp_data.magnetic_strength = 1
    mp_data.damping_factor = 1
    mp_data.pendulum_height = 1
    mp_data.pendulum_length = 1


def construct_mpdata_json(data: MPData) -> str:
    payload = {
        "magnets": {},
        "ui_grid": {},
        "physical_grid": {},
    }

    for index, magnet in enumerate(data.mag_list):
        payload["magnets"][f"magnet_{index}"] = {
            "x": magnet.x,
            "y": magnet.y,
        }

    sync_physical_grid(data)

    payload["ui_grid"]["x_min"] = data.ui_grid.x_min
    payload["ui_grid"]["x_max"] = data.ui_grid.x_max
    payload["ui_grid"]["y_min"] = data.ui_grid.y_min
    payload["ui_grid"]["y_max"] = data.ui_grid.y_max

    payload["physical_grid"]["x_min"] = data.physical_grid.x_min
    payload["physical_grid"]["x_max"] = data.physical_grid.x_max
    payload["physical_grid"]["y_min"] = data.physical_grid.y_min
    payload["physical_grid"]["y_max"] = data.physical_grid.y_max

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


@app.route("/magnet_update_position", methods=["POST"])
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

@app.route("/controller_data", methods=["POST"])
def controller_data():
    body = request.get_json()

    mp_data.damping_factor = float(body["dampingFactor"])
    mp_data.magnetic_strength = float(body["magneticStrength"])
    mp_data.pendulum_length = float(body["pendulumLength"])
    mp_data.pendulum_height = float(body["pendulumHeight"])

    print(f"set damping_factor to {mp_data.damping_factor}")
    print(f"set magnetic_strength to {mp_data.magnetic_strength}")
    print(f"set pendulum_length to {mp_data.pendulum_length}")
    print(f"set pendulum_height to {mp_data.pendulum_height}")
    print(f"set magnetic_strength to {mp_data.magnetic_strength}")

    return {"ok": 200}


def bytes_to_image(raw: bytes):
    return [list(raw[row * SCREEN_SIZE_X : (row + 1) * SCREEN_SIZE_X]) for row in range(SCREEN_SIZE_Y)]


@app.route("/image", methods=["POST"])
def image_post():
    raw = request.get_data()
    expected = SCREEN_SIZE_X * SCREEN_SIZE_Y
    if len(raw) != expected:
        return {
            "error": f"expected {expected} bytes, got {len(raw)}",
        }, 400

    mp_data.image = bytes_to_image(raw)
    return {"ok": 200}

@app.route("/image", methods=["GET"])
def image_get():

     return {
        "width": SCREEN_SIZE_X,
        "height": SCREEN_SIZE_Y,
        "bitDepth": FPGA_PIXEL_BIT_DEPTH,
        "image": mp_data.image,
    }


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5000)
