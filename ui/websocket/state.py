from dataclasses import dataclass 

SCREEN_SIZE_X = 160
SCREEN_SIZE_Y = 120

FPGA_PIXEL_BIT_DEPTH = 6

PHYSICAL_COORD_MIN = -1.8
PHYSICAL_COORD_MAX = 1.8

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

@dataclass
class MPData:
    mag_list: list[Magnet] = []
    ui_grid: Grid = Grid()
    physical_grid: Grid = Grid()
    magnetic_strength: float = 1
    damping_factor: float = 1
    pendulum_height: float = 1
    pendulum_length: float = 1
    image: list[list[int]] = [[0 for _ in range(SCREEN_SIZE_X)]  for _ in range(SCREEN_SIZE_Y)]
    image_bit_depth: int = FPGA_PIXEL_BIT_DEPTH
    image_received_at: float = 0.0
    image_version: int = 0

