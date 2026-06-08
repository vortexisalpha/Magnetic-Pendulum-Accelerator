"""
Hardware-aligned fixed-point magnetic pendulum simulator.

Features
--------
1. Single-run mode:
   - Change WIDTH, HEIGHT, TOTAL_BITS, FRAC_BITS, LUT_SIZE, etc. in USER SETTINGS.
   - Run the script normally.

2. Sweep mode:
   - Set RUN_SWEEP = True.
   - Edit FORMAT_SWEEP, LUT_SIZE_SWEEP and RESOLUTION_SWEEP.
   - The script runs all combinations and prints/saves a summary table.

3. Optional reference comparison:
   - Set REFERENCE_LABEL_PATH to an FPGA or floating-point .npy label map.
   - The script reports pixel-wise match rates.

Fixed-point convention
----------------------
The project convention used here is:

    QI.F means:
        TOTAL_BITS = I + F
        FRAC_BITS  = F

For example:
    Q4.14 -> TOTAL_BITS=18, FRAC_BITS=14

This matches the convention used in the current hardware-aligned scripts.
"""

from __future__ import annotations

import json
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Literal, Mapping, Optional, Tuple, List, Dict, Any

import matplotlib.pyplot as plt
import numpy as np


# =============================================================================
# USER SETTINGS
# =============================================================================

# ----------------------------
# Run mode
# ----------------------------
RUN_SWEEP = False

# ----------------------------
# Single-run settings
# ----------------------------
TOTAL_BITS = 18
FRAC_BITS = 14

WIDTH = 270
HEIGHT = 270

LUT_SIZE = 8192
LUT_Q_MAX = 32.0

OUT_DIR = "pendulum_fixed_outputs"

# ----------------------------
# Sweep settings
# ----------------------------
# Each entry is: ("label", total_bits, frac_bits)
# Examples:
#   ("Q4.14", 18, 14)
#   ("Q4.16", 20, 16)
#   ("Q5.13", 18, 13)
FORMAT_SWEEP = [
    ("Q4.14", 18, 14),
    ("Q4.16", 20, 16),
    ("Q4.18", 22, 18),
    ("Q4.20", 24, 20),
]

# LUT sizes must be powers of two.
LUT_SIZE_SWEEP = [
    1024,
    2048,
    4096,
    8192,
]

# Each entry is: (width, height)
RESOLUTION_SWEEP = [
    (270, 270),
]

SWEEP_OUT_DIR = "pendulum_sweep_outputs"

# In sweep mode, saving plots for every run can be slow and creates many files.
SWEEP_SAVE_PLOTS = False

# ----------------------------
# Coordinate window
# ----------------------------
X_MIN = -1.8
X_MAX = 1.8
Y_MIN = -1.8
Y_MAX = 1.8

# If True, output row 0 corresponds to the top of the physical grid.
# This matched your FPGA frame-buffer row ordering in the recent comparisons.
MIRROR_ROWS_FOR_FPGA = True

# ----------------------------
# Physical parameters
# ----------------------------
GAMMA = 0.20
OMEGA2 = 1.0
H2 = 0.25
MU = 1.0
DT = 0.01

# ----------------------------
# Simulation parameters
# ----------------------------
MAX_STEPS = 4000
R_SETTLE = 0.25
V_SETTLE = 0.05
MIN_CONSEC = 3
CLASSIFY_ON_TIMEOUT = True

# "float-force" or "lut-force".
# Use "lut-force" for hardware-aligned simulation.
FORCE_MODE = "lut-force"

# ----------------------------
# Output settings
# ----------------------------
SAVE_PLOTS = True
SHOW_PLOTS = False
PRINT_PROGRESS = True

# Optional reference label file for match-rate comparison.
# Example:
#   REFERENCE_LABEL_PATH = "frame_base40.npy"
REFERENCE_LABEL_PATH = "frame_base40.npy"

# If True, compare direct, vertical flip, horizontal flip, and both flips.
# Direct should be correct once MIRROR_ROWS_FOR_FPGA is set correctly.
CHECK_FLIPPED_ALIGNMENTS = True

COLOURS = {
    0: (30, 30, 30),
    1: (220, 60, 60),
    2: (60, 180, 60),
    3: (60, 60, 220),
}

MAGNET_POSITIONS = (
    (1.0, 0.0),
    (-0.5, 0.8660254037844386),
    (-0.5, -0.8660254037844386),
)


# =============================================================================
# Fixed-point helpers
# =============================================================================

ForceMode = Literal["float-force", "lut-force"]


@dataclass(frozen=True)
class FixedFormat:
    total_bits: int
    frac_bits: int

    def __post_init__(self) -> None:
        if self.total_bits < 2:
            raise ValueError("total_bits must be at least 2")
        if self.frac_bits < 0:
            raise ValueError("frac_bits must be non-negative")
        if self.frac_bits >= self.total_bits - 1:
            raise ValueError("frac_bits must leave at least one sign/integer bit")

    @property
    def scale(self) -> int:
        return 1 << self.frac_bits

    @property
    def min_raw(self) -> int:
        return -(1 << (self.total_bits - 1))

    @property
    def max_raw(self) -> int:
        return (1 << (self.total_bits - 1)) - 1

    @property
    def integer_bits_including_sign(self) -> int:
        return self.total_bits - self.frac_bits

    @property
    def label(self) -> str:
        return f"Q{self.integer_bits_including_sign}.{self.frac_bits}"

    def saturate(self, raw: np.ndarray | int) -> np.ndarray | int:
        arr = np.asarray(raw, dtype=np.int64)
        clipped = np.clip(arr, self.min_raw, self.max_raw).astype(np.int64)
        if np.isscalar(raw):
            return int(clipped)
        return clipped

    def to_fixed(self, value: np.ndarray | float | int) -> np.ndarray | int:
        # Match a common FPGA-host quantiser: int(x * 2^F), i.e. truncation toward zero.
        raw = np.trunc(np.asarray(value, dtype=np.float64) * self.scale).astype(np.int64)
        raw = self.saturate(raw)
        if np.isscalar(value):
            return int(raw)
        return raw

    def to_float(self, raw: np.ndarray | int) -> np.ndarray | float:
        out = np.asarray(raw, dtype=np.float64) / self.scale
        if np.isscalar(raw):
            return float(out)
        return out


def fx_add(fmt: FixedFormat, a: np.ndarray | int, b: np.ndarray | int) -> np.ndarray | int:
    return fmt.saturate(np.asarray(a, dtype=np.int64) + np.asarray(b, dtype=np.int64))


def fx_sub(fmt: FixedFormat, a: np.ndarray | int, b: np.ndarray | int) -> np.ndarray | int:
    return fmt.saturate(np.asarray(a, dtype=np.int64) - np.asarray(b, dtype=np.int64))


def fx_add_three_input(fmt: FixedFormat, a, b, c):
    raw = (
        np.asarray(a, dtype=np.int64)
        + np.asarray(b, dtype=np.int64)
        + np.asarray(c, dtype=np.int64)
    )
    return fmt.saturate(raw)


def fx_sub_three_input(fmt: FixedFormat, a, b, c):
    raw = (
        np.asarray(a, dtype=np.int64)
        - np.asarray(b, dtype=np.int64)
        - np.asarray(c, dtype=np.int64)
    )
    return fmt.saturate(raw)


def fx_mul(fmt: FixedFormat, a: np.ndarray | int, b: np.ndarray | int) -> np.ndarray | int:
    product = np.asarray(a, dtype=np.int64) * np.asarray(b, dtype=np.int64)
    shifted = product >> fmt.frac_bits
    return fmt.saturate(shifted)


def fx_adder_s3_q_to_unsigned(a, b, c, W: int):
    """
    Model of fx_adder_s3 for q = dx^2 + dy^2 + h^2.

    Inputs are non-negative QI.F raw values.
    Output is unsigned W-bit q with one fewer fractional bit.

    For Q4.14 input, this gives unsigned Q5.13 q.
    """
    raw_sum = (
        np.asarray(a, dtype=np.int64)
        + np.asarray(b, dtype=np.int64)
        + np.asarray(c, dtype=np.int64)
    )
    shifted = raw_sum >> 1
    max_unsigned = (1 << W) - 1
    return np.clip(shifted, 0, max_unsigned).astype(np.int64)


# =============================================================================
# Configuration dataclasses
# =============================================================================

@dataclass(frozen=True)
class PhysicsParams:
    gamma: float
    omega2: float
    h2: float
    mu: float
    dt: float


@dataclass(frozen=True)
class SimulationParams:
    max_steps: int
    r_settle: float
    v_settle: float
    min_consec: int
    classify_on_timeout: bool
    force_mode: ForceMode
    lut_size: int
    lut_q_max: float


@dataclass(frozen=True)
class RenderParams:
    x_min: float
    x_max: float
    y_min: float
    y_max: float
    width: int
    height: int
    mirror_rows_for_fpga: bool


@dataclass(frozen=True)
class OutputParams:
    out_dir: str
    save_plots: bool
    show_plots: bool


@dataclass(frozen=True)
class ProjectConfig:
    physics: PhysicsParams
    sim: SimulationParams
    render: RenderParams
    output: OutputParams
    magnet_positions: Tuple[Tuple[float, float], ...] = field(default_factory=lambda: MAGNET_POSITIONS)
    colours: Mapping[int, Tuple[int, int, int]] = field(default_factory=lambda: COLOURS)


@dataclass(frozen=True)
class FixedConstants:
    gamma: int
    omega2: int
    h2: int
    mu: int
    dt: int
    r2_settle: int
    v_settle: int
    magnets: Tuple[Tuple[int, int], ...]


def build_fixed_constants(fmt: FixedFormat, cfg: ProjectConfig) -> FixedConstants:
    p = cfg.physics
    s = cfg.sim

    magnets = tuple(
        (fmt.to_fixed(mx), fmt.to_fixed(my))
        for mx, my in cfg.magnet_positions
    )

    return FixedConstants(
        gamma=fmt.to_fixed(p.gamma),
        omega2=fmt.to_fixed(p.omega2),
        h2=fmt.to_fixed(p.h2),
        mu=fmt.to_fixed(p.mu),
        dt=fmt.to_fixed(p.dt),
        r2_settle=fmt.to_fixed(s.r_settle * s.r_settle),
        v_settle=fmt.to_fixed(s.v_settle),
        magnets=magnets,
    )


# =============================================================================
# LUT for q^(-3/2)
# =============================================================================

@dataclass(frozen=True)
class InversePowerLUT:
    W: int
    F: int
    LUT_SIZE: int
    LUT_ADDR_W: int
    values_raw: np.ndarray

    def lookup(self, q_raw: np.ndarray) -> np.ndarray:
        """
        General hardware-style LUT mapping.

        q_raw comes from fx_adder_s3 and has F-1 fractional bits.

        LUT covers q in [0, 32). Therefore:
            idx = floor(q_real * LUT_SIZE / 32)

        Since:
            q_real = q_raw / 2^(F-1)
            LUT_SIZE = 2^LUT_ADDR_W

        then:
            idx = q_raw >> ((F - 1) + 5 - LUT_ADDR_W)

        for non-negative shift. If the shift is negative, left shift instead.
        """
        addr = np.asarray(q_raw, dtype=np.int64)

        q_frac_bits = self.F - 1
        index_shift = q_frac_bits + 5 - self.LUT_ADDR_W

        if index_shift >= 0:
            idx_raw = addr >> index_shift
        else:
            idx_raw = addr << (-index_shift)

        idx = np.clip(idx_raw, 0, self.LUT_SIZE - 1).astype(np.int64)
        return self.values_raw[idx]


def build_inverse_power_lut(fmt: FixedFormat, cfg: ProjectConfig) -> InversePowerLUT:
    W = fmt.total_bits
    F = fmt.frac_bits
    lut_size = cfg.sim.lut_size

    if lut_size <= 0 or (lut_size & (lut_size - 1)) != 0:
        raise ValueError("LUT_SIZE must be a positive power of two.")

    lut_addr_w = int(np.log2(lut_size))

    q_max = cfg.sim.lut_q_max
    if abs(q_max - 32.0) > 1e-12:
        raise ValueError(
            f"This hardware-aligned model expects LUT_Q_MAX=32.0. Got {q_max}."
        )

    q_per_index = q_max / lut_size
    scale = 1 << F
    sat_max = (1 << (W - 1)) - 1  # signed positive max for LUT output

    entries = np.empty(lut_size, dtype=np.int64)

    for i in range(lut_size):
        q = i * q_per_index
        if q == 0.0:
            val = sat_max
        else:
            raw = (q ** (-1.5)) * scale
            val = sat_max if raw >= sat_max else int(round(raw))
        entries[i] = val

    return InversePowerLUT(
        W=W,
        F=F,
        LUT_SIZE=lut_size,
        LUT_ADDR_W=lut_addr_w,
        values_raw=entries,
    )


def inverse_q_power_fixed(
    fmt: FixedFormat,
    cfg: ProjectConfig,
    q_raw: np.ndarray,
    lut: Optional[InversePowerLUT],
) -> np.ndarray:
    if cfg.sim.force_mode == "float-force":
        q_frac_bits = fmt.frac_bits - 1
        q_float = np.asarray(q_raw, dtype=np.float64) / (1 << q_frac_bits)
        f_float = np.where(q_float <= 0.0, np.inf, q_float ** (-1.5))
        return fmt.to_fixed(f_float)

    if cfg.sim.force_mode == "lut-force":
        if lut is None:
            raise ValueError("lut-force selected but no LUT was built.")
        return lut.lookup(q_raw)

    raise ValueError(f"Unknown force mode: {cfg.sim.force_mode}")


# =============================================================================
# Pendulum datapath model
# =============================================================================

def acceleration_fixed(
    fmt: FixedFormat,
    cfg: ProjectConfig,
    const: FixedConstants,
    x: np.ndarray,
    y: np.ndarray,
    vx: np.ndarray,
    vy: np.ndarray,
    lut: Optional[InversePowerLUT],
) -> Tuple[np.ndarray, np.ndarray]:
    """
    Hardware-aligned acceleration calculation.

    This uses the RTL ordering:
        dx_invq_sum = sum_i(dx_i * invq_i)
        dy_invq_sum = sum_i(dy_i * invq_i)

        magnetic_x = mu * dx_invq_sum
        magnetic_y = mu * dy_invq_sum

        ax = magnetic_x - gamma*vx - omega2*x
        ay = magnetic_y - gamma*vy - omega2*y
    """
    dx_invq_terms = []
    dy_invq_terms = []

    for mx_raw, my_raw in const.magnets:
        dx = fx_sub(fmt, mx_raw, x)
        dy = fx_sub(fmt, my_raw, y)

        dx2 = fx_mul(fmt, dx, dx)
        dy2 = fx_mul(fmt, dy, dy)

        q = fx_adder_s3_q_to_unsigned(dx2, dy2, const.h2, W=fmt.total_bits)
        invq = inverse_q_power_fixed(fmt, cfg, q, lut)

        dx_invq_terms.append(fx_mul(fmt, dx, invq))
        dy_invq_terms.append(fx_mul(fmt, dy, invq))

    dx_invq_sum = fx_add_three_input(
        fmt, dx_invq_terms[0], dx_invq_terms[1], dx_invq_terms[2]
    )
    dy_invq_sum = fx_add_three_input(
        fmt, dy_invq_terms[0], dy_invq_terms[1], dy_invq_terms[2]
    )

    magnetic_x = fx_mul(fmt, const.mu, dx_invq_sum)
    magnetic_y = fx_mul(fmt, const.mu, dy_invq_sum)

    gamma_vx = fx_mul(fmt, const.gamma, vx)
    gamma_vy = fx_mul(fmt, const.gamma, vy)

    omega_x = fx_mul(fmt, const.omega2, x)
    omega_y = fx_mul(fmt, const.omega2, y)

    ax = fx_sub_three_input(fmt, magnetic_x, gamma_vx, omega_x)
    ay = fx_sub_three_input(fmt, magnetic_y, gamma_vy, omega_y)

    return ax, ay


def q_for_magnet_fixed(
    fmt: FixedFormat,
    const: FixedConstants,
    mx_raw: int,
    my_raw: int,
    x: np.ndarray,
    y: np.ndarray,
) -> np.ndarray:
    dx = fx_sub(fmt, mx_raw, x)
    dy = fx_sub(fmt, my_raw, y)

    dx2 = fx_mul(fmt, dx, dx)
    dy2 = fx_mul(fmt, dy, dy)

    return fx_adder_s3_q_to_unsigned(dx2, dy2, const.h2, W=fmt.total_bits)


def nearest_magnet_fixed(
    fmt: FixedFormat,
    const: FixedConstants,
    x: np.ndarray,
    y: np.ndarray,
) -> Tuple[np.ndarray, np.ndarray]:
    q_values = [
        q_for_magnet_fixed(fmt, const, mx_raw, my_raw, x, y)
        for mx_raw, my_raw in const.magnets
    ]

    q_stack = np.stack(q_values, axis=0)

    # np.argmin returns the first minimum, matching the RTL tie priority:
    # magnet 1, then magnet 2, then magnet 3.
    nearest_zero_based = np.argmin(q_stack, axis=0).astype(np.int16)
    nearest_label = (nearest_zero_based + 1).astype(np.int16)
    min_q = np.min(q_stack, axis=0).astype(np.int64)

    return nearest_label, min_q


def make_initial_grid_fixed(fmt: FixedFormat, render: RenderParams) -> Tuple[np.ndarray, np.ndarray]:
    if render.width <= 0 or render.height <= 0:
        raise ValueError("width and height must be positive.")

    if render.width == 1:
        x_step = 0.0
        x_start = 0.5 * (render.x_min + render.x_max)
    else:
        x_step = (render.x_max - render.x_min) / render.width
        x_start = render.x_min

    if render.height == 1:
        y_step = 0.0
        y_start = 0.5 * (render.y_min + render.y_max)
    else:
        y_step = (render.y_max - render.y_min) / render.height
        y_start = render.y_min

    x_start_raw = fmt.to_fixed(x_start)
    y_start_raw = fmt.to_fixed(y_start)
    x_step_raw = fmt.to_fixed(x_step)
    y_step_raw = fmt.to_fixed(y_step)

    cols = np.arange(render.width, dtype=np.int64)

    if render.mirror_rows_for_fpga:
        rows = (render.height - 1) - np.arange(render.height, dtype=np.int64)
    else:
        rows = np.arange(render.height, dtype=np.int64)

    xs = fmt.saturate(x_start_raw + cols * x_step_raw)
    ys = fmt.saturate(y_start_raw + rows * y_step_raw)

    x_grid, y_grid = np.meshgrid(xs, ys)
    return x_grid.astype(np.int64), y_grid.astype(np.int64)


@dataclass
class BasinResult:
    basin: np.ndarray
    steps: np.ndarray
    settled: np.ndarray
    elapsed_s: float


def generate_basin_map_fixed(fmt: FixedFormat, cfg: ProjectConfig) -> BasinResult:
    const = build_fixed_constants(fmt, cfg)
    lut = build_inverse_power_lut(fmt, cfg) if cfg.sim.force_mode == "lut-force" else None

    sum_r_settle_sq_h_sq = fx_adder_s3_q_to_unsigned(
        const.r2_settle,
        const.h2,
        0,
        W=fmt.total_bits,
    )
    sum_r_settle_sq_h_sq = int(np.asarray(sum_r_settle_sq_h_sq).item())

    render = cfg.render
    sim = cfg.sim

    x, y = make_initial_grid_fixed(fmt, render)
    vx = np.zeros_like(x, dtype=np.int64)
    vy = np.zeros_like(y, dtype=np.int64)

    basin = np.zeros((render.height, render.width), dtype=np.int16)
    steps = np.zeros((render.height, render.width), dtype=np.int32)
    settled = np.zeros((render.height, render.width), dtype=bool)

    active = np.ones((render.height, render.width), dtype=bool)
    consec = np.zeros((render.height, render.width), dtype=np.int16)

    t0 = time.time()

    for step in range(1, sim.max_steps + 1):
        # S3-style nearest and settle check on incoming state.
        nearest, min_q = nearest_magnet_fixed(fmt, const, x, y)

        vx_ok = np.abs(vx) < const.v_settle
        vy_ok = np.abs(vy) < const.v_settle

        settle_now = active & (min_q < sum_r_settle_sq_h_sq) & vx_ok & vy_ok

        consec[settle_now] += 1
        consec[active & ~settle_now] = 0

        done_settle = active & (consec >= sim.min_consec)
        done_timeout = active & ~done_settle & (step >= sim.max_steps)

        if np.any(done_settle):
            basin[done_settle] = nearest[done_settle]
            steps[done_settle] = step
            settled[done_settle] = True

        if np.any(done_timeout):
            if sim.classify_on_timeout:
                basin[done_timeout] = nearest[done_timeout]
            steps[done_timeout] = step

        advance = active & ~done_settle & ~done_timeout

        if np.any(advance):
            ax, ay = acceleration_fixed(fmt, cfg, const, x, y, vx, vy, lut)

            vx_new = fx_add(fmt, vx, fx_mul(fmt, const.dt, ax))
            vy_new = fx_add(fmt, vy, fx_mul(fmt, const.dt, ay))

            x_new = fx_add(fmt, x, fx_mul(fmt, const.dt, vx_new))
            y_new = fx_add(fmt, y, fx_mul(fmt, const.dt, vy_new))

            vx[advance] = vx_new[advance]
            vy[advance] = vy_new[advance]
            x[advance] = x_new[advance]
            y[advance] = y_new[advance]

        active[done_settle] = False
        active[done_timeout] = False

        if PRINT_PROGRESS and (
            step == 1 or step % 500 == 0 or step == sim.max_steps or not np.any(active)
        ):
            elapsed = time.time() - t0
            print(
                f"step {step:5d}/{sim.max_steps} | "
                f"active pixels {int(np.sum(active)):7d} | "
                f"elapsed {elapsed:6.1f}s"
            )

        if not np.any(active):
            break

    elapsed_s = time.time() - t0
    return BasinResult(basin=basin, steps=steps, settled=settled, elapsed_s=elapsed_s)


# =============================================================================
# Plotting and comparison
# =============================================================================

def basin_to_rgb(cfg: ProjectConfig, basin: np.ndarray, settled: Optional[np.ndarray] = None) -> np.ndarray:
    h, w = basin.shape
    img = np.zeros((h, w, 3), dtype=np.uint8)

    for label, colour in cfg.colours.items():
        img[basin == label] = np.array(colour, dtype=np.uint8)

    if settled is not None:
        timeout = ~settled
        img[timeout] = (0.45 * img[timeout]).astype(np.uint8)

    return img


def plot_basin(cfg: ProjectConfig, result: BasinResult, out_dir: Path) -> None:
    img = basin_to_rgb(cfg, result.basin, None)
    r = cfg.render

    plt.figure(figsize=(8, 8))
    plt.imshow(
        img,
        origin="upper" if r.mirror_rows_for_fpga else "lower",
        extent=[r.x_min, r.x_max, r.y_min, r.y_max],
        interpolation="nearest",
    )

    for i, (mx, my) in enumerate(cfg.magnet_positions):
        plt.plot(mx, my, "w+", markersize=12, markeredgewidth=2)
        plt.text(mx + 0.08, my + 0.08, f"M{i}", color="white", fontsize=9)

    plt.title(f"Fixed-point basin: {r.width}x{r.height}, LUT={cfg.sim.lut_size}, {cfg.sim.force_mode}")
    plt.xlabel("initial x")
    plt.ylabel("initial y")
    plt.tight_layout()
    plt.savefig(out_dir / "fixed_basin_annotated.png", dpi=200, bbox_inches="tight")

    if cfg.output.show_plots:
        plt.show()
    else:
        plt.close()

    plt.imsave(out_dir / "fixed_basin.png", img)


def plot_steps(cfg: ProjectConfig, result: BasinResult, out_dir: Path) -> None:
    r = cfg.render

    plt.figure(figsize=(7, 5))
    plt.imshow(
        result.steps,
        origin="upper" if r.mirror_rows_for_fpga else "lower",
        extent=[r.x_min, r.x_max, r.y_min, r.y_max],
        interpolation="nearest",
    )
    plt.colorbar(label="steps")
    plt.title(f"Steps to settle/classify: {r.width}x{r.height}, LUT={cfg.sim.lut_size}")
    plt.xlabel("initial x")
    plt.ylabel("initial y")
    plt.tight_layout()
    plt.savefig(out_dir / "fixed_steps.png", dpi=200, bbox_inches="tight")

    if cfg.output.show_plots:
        plt.show()
    else:
        plt.close()


def label_counts(arr: np.ndarray) -> Dict[str, int]:
    return {str(int(label)): int(np.sum(arr == label)) for label in sorted(np.unique(arr))}


def compare_labels(test_basin: np.ndarray, reference_path: Optional[str]) -> Dict[str, Any]:
    if reference_path is None:
        return {}

    ref_path = Path(reference_path)
    if not ref_path.exists():
        return {"error": f"reference file not found: {reference_path}"}

    ref = np.load(ref_path)

    if ref.shape != test_basin.shape:
        return {
            "error": "shape mismatch",
            "reference_shape": list(ref.shape),
            "test_shape": list(test_basin.shape),
        }

    comparisons = {
        "direct": test_basin,
    }

    if CHECK_FLIPPED_ALIGNMENTS:
        comparisons.update(
            {
                "vertical_flip": np.flipud(test_basin),
                "horizontal_flip": np.fliplr(test_basin),
                "both_flips": np.flipud(np.fliplr(test_basin)),
            }
        )

    results = {}
    for name, candidate in comparisons.items():
        matches = int(np.sum(ref == candidate))
        total = int(ref.size)
        results[name] = {
            "matches": matches,
            "total": total,
            "match_rate": matches / total,
            "match_rate_percent": 100.0 * matches / total,
        }

    best_name = max(results, key=lambda k: results[k]["match_rate"])
    return {
        "reference_path": str(ref_path),
        "reference_shape": list(ref.shape),
        "reference_label_counts": label_counts(ref),
        "test_label_counts": label_counts(test_basin),
        "alignments": results,
        "best_alignment": best_name,
        "best_match_rate": results[best_name]["match_rate"],
        "best_match_rate_percent": results[best_name]["match_rate_percent"],
    }


def save_result(
    fmt: FixedFormat,
    cfg: ProjectConfig,
    result: BasinResult,
    comparison: Dict[str, Any],
) -> Dict[str, Any]:
    out_dir = Path(cfg.output.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    np.save(out_dir / "fixed_basin.npy", result.basin)
    np.save(out_dir / "fixed_steps.npy", result.steps)
    np.save(out_dir / "fixed_settled.npy", result.settled)

    if cfg.output.save_plots:
        plot_basin(cfg, result, out_dir)
        plot_steps(cfg, result, out_dir)

    flat_steps = result.steps.ravel()
    total_pixels = cfg.render.width * cfg.render.height

    summary = {
        "format_label": fmt.label,
        "fixed_format": asdict(fmt),
        "config": asdict(cfg),
        "elapsed_s": result.elapsed_s,
        "pixels": total_pixels,
        "pixels_per_second": total_pixels / result.elapsed_s if result.elapsed_s > 0 else None,
        "strict_settled_pixels": int(np.sum(result.settled)),
        "strict_settled_fraction": float(np.mean(result.settled)),
        "mean_steps": float(np.mean(flat_steps)),
        "median_steps": float(np.median(flat_steps)),
        "p99_steps": int(np.percentile(flat_steps, 99)),
        "max_steps_observed": int(np.max(flat_steps)),
        "basin_counts": label_counts(result.basin),
        "comparison": comparison,
    }

    with open(out_dir / "summary.json", "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)

    return summary


# =============================================================================
# Run helpers
# =============================================================================

def make_config(
    *,
    total_bits: int,
    frac_bits: int,
    width: int,
    height: int,
    lut_size: int,
    out_dir: str,
    save_plots: bool,
) -> Tuple[FixedFormat, ProjectConfig]:
    fmt = FixedFormat(total_bits=total_bits, frac_bits=frac_bits)

    physics = PhysicsParams(
        gamma=GAMMA,
        omega2=OMEGA2,
        h2=H2,
        mu=MU,
        dt=DT,
    )

    sim = SimulationParams(
        max_steps=MAX_STEPS,
        r_settle=R_SETTLE,
        v_settle=V_SETTLE,
        min_consec=MIN_CONSEC,
        classify_on_timeout=CLASSIFY_ON_TIMEOUT,
        force_mode=FORCE_MODE,
        lut_size=lut_size,
        lut_q_max=LUT_Q_MAX,
    )

    render = RenderParams(
        x_min=X_MIN,
        x_max=X_MAX,
        y_min=Y_MIN,
        y_max=Y_MAX,
        width=width,
        height=height,
        mirror_rows_for_fpga=MIRROR_ROWS_FOR_FPGA,
    )

    output = OutputParams(
        out_dir=out_dir,
        save_plots=save_plots,
        show_plots=SHOW_PLOTS,
    )

    cfg = ProjectConfig(
        physics=physics,
        sim=sim,
        render=render,
        output=output,
        magnet_positions=tuple(MAGNET_POSITIONS),
        colours=COLOURS,
    )

    return fmt, cfg


def print_run_header(fmt: FixedFormat, cfg: ProjectConfig) -> None:
    print("\n" + "=" * 80)
    print(
        f"Run: {fmt.label}, W={fmt.total_bits}, F={fmt.frac_bits}, "
        f"{cfg.render.width}x{cfg.render.height}, LUT={cfg.sim.lut_size}"
    )
    print("=" * 80)
    print(f"force mode          : {cfg.sim.force_mode}")
    print(f"coordinate window   : x=[{cfg.render.x_min}, {cfg.render.x_max}], y=[{cfg.render.y_min}, {cfg.render.y_max}]")
    print(f"physical params     : gamma={cfg.physics.gamma}, omega2={cfg.physics.omega2}, mu={cfg.physics.mu}, h2={cfg.physics.h2}, dt={cfg.physics.dt}")
    print(f"settling params     : r={cfg.sim.r_settle}, v={cfg.sim.v_settle}, min_consec={cfg.sim.min_consec}, max_steps={cfg.sim.max_steps}")
    print(f"output dir          : {cfg.output.out_dir}")


def run_one(
    *,
    label: str,
    total_bits: int,
    frac_bits: int,
    width: int,
    height: int,
    lut_size: int,
    out_dir: str,
    save_plots: bool,
) -> Dict[str, Any]:
    fmt, cfg = make_config(
        total_bits=total_bits,
        frac_bits=frac_bits,
        width=width,
        height=height,
        lut_size=lut_size,
        out_dir=out_dir,
        save_plots=save_plots,
    )

    print_run_header(fmt, cfg)

    result = generate_basin_map_fixed(fmt, cfg)
    comparison = compare_labels(result.basin, REFERENCE_LABEL_PATH)
    summary = save_result(fmt, cfg, result, comparison)

    total_pixels = width * height
    print(f"\nDone in {result.elapsed_s:.2f}s")
    print(f"Pixels per second   : {total_pixels / result.elapsed_s:.1f}")
    print(f"Strict settled      : {np.sum(result.settled)} / {total_pixels} ({100*np.mean(result.settled):.2f}%)")

    if comparison:
        if "error" in comparison:
            print(f"Reference comparison: {comparison['error']}")
        else:
            best = comparison["best_alignment"]
            best_pct = comparison["best_match_rate_percent"]
            direct_pct = comparison["alignments"]["direct"]["match_rate_percent"]
            print(f"Direct match        : {direct_pct:.2f}%")
            print(f"Best match          : {best_pct:.2f}% ({best})")

    return summary


def safe_name(text: str) -> str:
    return text.replace(".", "_").replace("/", "_").replace("\\", "_").replace(" ", "_")


def run_sweep() -> List[Dict[str, Any]]:
    all_results = []

    sweep_root = Path(SWEEP_OUT_DIR)
    sweep_root.mkdir(parents=True, exist_ok=True)

    for fmt_label, total_bits, frac_bits in FORMAT_SWEEP:
        for width, height in RESOLUTION_SWEEP:
            for lut_size in LUT_SIZE_SWEEP:
                run_label = f"{fmt_label}_{width}x{height}_lut{lut_size}"
                out_dir = sweep_root / safe_name(run_label)

                summary = run_one(
                    label=fmt_label,
                    total_bits=total_bits,
                    frac_bits=frac_bits,
                    width=width,
                    height=height,
                    lut_size=lut_size,
                    out_dir=str(out_dir),
                    save_plots=SWEEP_SAVE_PLOTS,
                )

                compact = {
                    "format": fmt_label,
                    "total_bits": total_bits,
                    "frac_bits": frac_bits,
                    "width": width,
                    "height": height,
                    "lut_size": lut_size,
                    "strict_settled_fraction_percent": 100.0 * summary["strict_settled_fraction"],
                    "elapsed_s": summary["elapsed_s"],
                    "pixels_per_second": summary["pixels_per_second"],
                    "out_dir": str(out_dir),
                }

                comp = summary.get("comparison", {})
                if comp and "error" not in comp:
                    compact["direct_match_rate_percent"] = comp["alignments"]["direct"]["match_rate_percent"]
                    compact["best_match_rate_percent"] = comp["best_match_rate_percent"]
                    compact["best_alignment"] = comp["best_alignment"]

                all_results.append(compact)

    with open(sweep_root / "sweep_results.json", "w", encoding="utf-8") as f:
        json.dump(all_results, f, indent=2)

    print("\n" + "=" * 80)
    print("SWEEP SUMMARY")
    print("=" * 80)

    has_match = any("direct_match_rate_percent" in row for row in all_results)

    if has_match:
        header = (
            f"{'Format':<8} {'W':>3} {'F':>3} {'Res':>10} {'LUT':>6} "
            f"{'Settled %':>10} {'Direct %':>10} {'Best %':>10} {'Best align':>14}"
        )
    else:
        header = (
            f"{'Format':<8} {'W':>3} {'F':>3} {'Res':>10} {'LUT':>6} "
            f"{'Settled %':>10} {'Elapsed s':>10} {'Pix/s':>12}"
        )

    print(header)
    print("-" * len(header))

    for row in all_results:
        res = f"{row['width']}x{row['height']}"
        if has_match and "direct_match_rate_percent" in row:
            print(
                f"{row['format']:<8} {row['total_bits']:>3} {row['frac_bits']:>3} "
                f"{res:>10} {row['lut_size']:>6} "
                f"{row['strict_settled_fraction_percent']:>10.2f} "
                f"{row['direct_match_rate_percent']:>10.2f} "
                f"{row['best_match_rate_percent']:>10.2f} "
                f"{row['best_alignment']:>14}"
            )
        else:
            print(
                f"{row['format']:<8} {row['total_bits']:>3} {row['frac_bits']:>3} "
                f"{res:>10} {row['lut_size']:>6} "
                f"{row['strict_settled_fraction_percent']:>10.2f} "
                f"{row['elapsed_s']:>10.2f} "
                f"{row['pixels_per_second']:>12.1f}"
            )

    print(f"\nSaved sweep table to: {sweep_root / 'sweep_results.json'}")
    return all_results


def main() -> None:
    if RUN_SWEEP:
        run_sweep()
    else:
        run_one(
            label=f"Q{TOTAL_BITS - FRAC_BITS}.{FRAC_BITS}",
            total_bits=TOTAL_BITS,
            frac_bits=FRAC_BITS,
            width=WIDTH,
            height=HEIGHT,
            lut_size=LUT_SIZE,
            out_dir=OUT_DIR,
            save_plots=SAVE_PLOTS,
        )


if __name__ == "__main__":
    main()
