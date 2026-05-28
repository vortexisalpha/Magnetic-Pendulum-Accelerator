import argparse
import json
from pathlib import Path

import numpy as np
import requests

W, H = 160, 120
BIT_DEPTH = 14
ITER_MASK = 0xFFF          # bottom 12 bits
CATEGORY_SHIFT = 12        # top 2 bits
FLASK_BASE = "http://35.179.111.223:5000"
OUT_DIR = Path("cpp_outputs")

# Matches DEFAULT_MAGNETS in cpu_fixedpoint.cpp (physical x, y)
DEFAULT_MAGNETS = [
    ("m0",  1.0,  0.0),
    ("m1", -0.5,  0.8660254037844386),
    ("m2", -0.5, -0.8660254037844386),
]

# Basin labels from C++: 0, 1, 2 = magnet index; -1 = unresolved
BASIN_TO_CATEGORY = {
    0: 0,
    1: 1,
    2: 2,
   -1: 3,
}


def load_render_config() -> dict:
    defaults = {
        "width": W,
        "height": H,
        "x_min": -1.8,
        "x_max": 1.8,
        "y_min": -1.8,
        "y_max": 1.8,
    }
    summary_path = OUT_DIR / "summary.json"
    if summary_path.exists():
        with summary_path.open(encoding="utf-8") as f:
            defaults.update(json.load(f)["render"])
    return defaults


def phys_to_pixel(x: float, y: float, render: dict) -> tuple[float, float]:
    """Map simulation coordinates to Flask screen pixels (0..width, 0..height)."""
    width, height = render["width"], render["height"]
    x_min, x_max = render["x_min"], render["x_max"]
    y_min, y_max = render["y_min"], render["y_max"]

    if width > 1:
        px = (x - x_min) / (x_max - x_min) * (width - 1)
    else:
        px = 0.5 * (x_min + x_max)

    if height > 1:
        py = (y - y_min) / (y_max - y_min) * (height - 1)
    else:
        py = 0.5 * (y_min + y_max)

    return px, py


def load_basin_maps(width: int, height: int) -> tuple[np.ndarray, np.ndarray]:
    basin = np.fromfile(OUT_DIR / "basin.bin", dtype=np.int16).reshape(height, width)
    steps = np.fromfile(OUT_DIR / "steps.bin", dtype=np.int32).reshape(height, width)
    return basin, steps


def pack_pixel(category: int, iterations: int) -> int:
    """Pack category (2 bits) and iterations (12 bits) into one 14-bit integer."""
    value = int(iterations) & ITER_MASK
    return (int(category) << CATEGORY_SHIFT) | value


def basin_steps_to_image(basin: np.ndarray, steps: np.ndarray) -> list[list[int]]:
    image = []
    for row in range(basin.shape[0]):
        packed_row = []
        for col in range(basin.shape[1]):
            label = int(basin[row, col])
            category = BASIN_TO_CATEGORY.get(label, 3)
            packed_row.append(pack_pixel(category, steps[row, col]))
        image.append(packed_row)
    return image


def post_magnets(render: dict, base_url: str = FLASK_BASE) -> None:
    """Sync C++ magnet positions to the Flask server."""
    for uid in ("m0", "m1", "m2", "m3"):
        requests.post(
            f"{base_url}/magnet_remove",
            json={"uid": uid},
            timeout=10,
        )

    for uid, x_phys, y_phys in DEFAULT_MAGNETS:
        x, y = phys_to_pixel(x_phys, y_phys, render)
        resp = requests.post(
            f"{base_url}/magnet_add",
            json={"uid": uid, "x": x, "y": y},
            timeout=10,
        )
        resp.raise_for_status()
        print(f"Posted magnet {uid} at ({x:.1f}, {y:.1f}) -> {resp.status_code}")


def post_to_flask(image: list[list[int]], base_url: str = FLASK_BASE) -> None:
    resp = requests.post(f"{base_url}/image", json={"image": image}, timeout=10)
    resp.raise_for_status()
    print(f"Posted image to {base_url}/image -> {resp.status_code} {resp.json()}")


def save_json_payload(width: int, height: int, image: list[list[int]]) -> Path:
    payload = {
        "width": width,
        "height": height,
        "bitDepth": BIT_DEPTH,
        "image": image,
    }
    out_path = OUT_DIR / "flask_image.json"
    with out_path.open("w", encoding="utf-8") as f:
        json.dump(payload, f)
    return out_path


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="Pack C++ basin outputs and POST to the Flask server.",
    )
    parser.add_argument(
        "--flask-base",
        default=FLASK_BASE,
        help="Flask server base URL (default: http://35.179.111.223:5000/)",
    )
    args = parser.parse_args()

    render = load_render_config()
    width, height = render["width"], render["height"]
    basin, steps = load_basin_maps(width, height)
    image = basin_steps_to_image(basin, steps)

    out_path = save_json_payload(width, height, image)
    print(f"Saved {width}x{height} packed image to {out_path}")

    post_magnets(render, args.flask_base)
    post_to_flask(image, args.flask_base)
