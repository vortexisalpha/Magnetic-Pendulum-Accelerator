from dataclasses import dataclass, field

from config import *

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
    image_bit_depth: int = FPGA_PIXEL_BIT_DEPTH
    image_received_at: float = 0.0
    image_version: int = 0
    image_version: int = 0

def construct_mpdata_json(data):
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

    return payload

def sync_physical_grid(data: MPData):
    data.physical_grid = ui_grid_to_physical_grid(data.ui_grid)
