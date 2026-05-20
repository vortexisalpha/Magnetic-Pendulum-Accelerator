"""
magnetic_pendulum_float_golden_reference_detailed.py

Floating-point golden-reference model for the magnetic-pendulum FPGA project.

Purpose
-------
This script is intended to be the *software correctness reference* for the
hardware accelerator.  It computes a basin-of-attraction image: for each pixel,
the initial pendulum position (x0, y0) is simulated until it settles near one of
the magnets, and the pixel colour is chosen from the final magnet label.

Why this is useful for the FPGA project
---------------------------------------
1. It gives a clean mathematical reference for the image a = f(x, y).
2. It records extra debug data: final basin label, number of integration steps,
   and strict-settled/not-strict-settled mask.
3. It separates the floating-point model from later fixed-point hardware choices.
4. It makes the numerical choices explicit, so the FPGA implementation can
   deliberately match or approximate them.

Important modelling note
------------------------
This code uses a 2D pendulum-bob position (x, y) with a simplified central
restoring force, damping, and point-magnet attraction.  The magnets are assumed
to sit in the same horizontal plane, while h2 models the squared vertical
separation between the bob and the magnets.  This is not a full 3D mechanical
model; it is a compact dynamical system chosen because it produces rich,
fractal-like basin boundaries while still being implementable on an FPGA.

Default numerical method
------------------------
The default update is semi-implicit Euler:

    v[n+1] = v[n] + dt * a(x[n], v[n])
    x[n+1] = x[n] + dt * v[n+1]

This is deliberately simple: it maps naturally to an FPGA datapath containing
state registers, multipliers, adders, and a reciprocal/square-root approximation
or lookup-table for the magnet force term.

This is a golden *functional* reference, not the final CPU timing baseline.  
NumPy vectorisation can be very fast on a desktop CPU.  
For the coursework CPU baseline, we still want a separate scalar C++ implementation 
that mirrors the hardware loop more directly.

Run examples
------------
Default run:

    python magnetic_pendulum_float_golden_reference_detailed.py

Fast smoke test:

    python magnetic_pendulum_float_golden_reference_detailed.py --width 64 --height 64 --max-steps 500

Use float32 to estimate the effect of reduced precision:

    python magnetic_pendulum_float_golden_reference_detailed.py --dtype float32

Outputs
-------
By default, files are written to ./pendulum_outputs_detailed/

    basin_labels.npy       Integer basin label per pixel.
    step_count.npy         Number of simulation steps per pixel.
    strictly_settled.npy   Boolean mask: True only if strict settling criterion passed.
    result_bundle.npz      Convenient compressed bundle of the above arrays.
    basin_rgb.png          Raw RGB basin image, useful for reports/slides.
    basin_annotated.png    Basin image with axes and magnet markers.
    step_count.png         Heatmap of number of steps.
    step_histogram.png     Histogram of step counts.
    summary.json           Parameters and numerical statistics.

Author: Yuheng Fu
"""

from __future__ import annotations

import argparse
import json
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Dict, Iterable, Literal, Mapping, Optional, Tuple

import numpy as np
import matplotlib.pyplot as plt


# =============================================================================
# Configuration dataclasses
# =============================================================================
# Using dataclasses keeps the code readable and avoids hiding important choices
# inside global variables.  This is helpful when you later compare:
#
#   floating-point Python reference  <->  fixed-point Python reference
#   fixed-point Python reference     <->  HDL simulation
#   HDL simulation                   <->  FPGA hardware output
#
# When a mismatch appears, you want every parameter to be explicit.


FloatDTypeName = Literal["float32", "float64"]


@dataclass(frozen=True)
class PhysicsParams:
    """Physical/model parameters for the simplified magnetic pendulum.

    gamma:
        Linear damping coefficient.  Larger gamma removes energy faster, so
        pixels settle more quickly and the basin boundaries usually become less
        intricate.

    omega2:
        Coefficient of the central restoring force.  The restoring acceleration
        is -omega2 * x and -omega2 * y.  It approximates gravity pulling the
        pendulum bob back toward the origin for small angular displacement.

    h2:
        Squared vertical separation between the bob plane and magnet plane.
        The magnet force uses q = dx^2 + dy^2 + h2.  Keeping h2 > 0 avoids a
        singularity if the bob passes directly above a magnet.

    mu:
        Magnet strength.  In this default model all magnets use the same mu.
        If you later want non-identical magnets, change magnet_strengths in
        the functions below.
    """

    gamma: float = 0.20
    omega2: float = 1.00
    h2: float = 0.25
    mu: float = 1.00


@dataclass(frozen=True)
class SimulationParams:
    """Numerical integration and stopping parameters.

    dt:
        Simulation time step.

    max_steps:
        Maximum number of integration steps per pixel.

    r_settle:
        Position threshold for declaring that the bob is close to a magnet.

    v_settle:
        Velocity threshold for declaring that the bob is almost stationary.

    min_consecutive:
        Number of consecutive time steps for which both position and velocity
        thresholds must hold.  This avoids falsely classifying a fast fly-by as
        a settled pixel.

    classify_on_timeout:
        If True, pixels that never strictly settle by max_steps are still
        assigned to the nearest magnet.  The strictly_settled mask records
        whether the strict criterion was actually satisfied.

        For report/debugging purposes this is useful because it gives every
        pixel a colour while still showing which pixels were not truly settled.

    dtype:
        Floating-point type used in the simulation.  float64 is the clean
        golden reference.  float32 is useful for estimating whether reduced
        precision changes many pixel labels before moving to fixed point.

    progress_interval:
        Print progress every N iterations.  Use 0 to disable progress prints.
    """

    dt: float = 0.01
    max_steps: int = 5000
    r_settle: float = 0.10
    v_settle: float = 0.01
    min_consecutive: int = 3
    classify_on_timeout: bool = True
    dtype: FloatDTypeName = "float64"
    progress_interval: int = 500


@dataclass(frozen=True)
class RenderParams:
    """Image-plane parameters.

    Each pixel corresponds to one initial condition:

        x(0) = x0
        y(0) = y0
        vx(0) = 0
        vy(0) = 0

    The image therefore visualises which magnet attracts the pendulum for each
    initial displacement.
    """

    x_min: float = -1.8
    x_max: float = 1.8
    y_min: float = -1.8
    y_max: float = 1.8
    width: int = 256
    height: int = 256


@dataclass(frozen=True)
class OutputParams:
    """Output and plotting parameters.

    darken_timeout_pixels:
        If True, pixels that were classified only because max_steps was reached
        are darkened in the saved PNGs.  The default is False because the
        default physical parameters often use timeout classification as the
        displayed result.  The strictly_settled.npy mask still preserves this
        information numerically.
    """

    out_dir: Path = Path("pendulum_outputs_detailed")
    show_plots: bool = False
    save_final_state: bool = False
    darken_timeout_pixels: bool = False


@dataclass(frozen=True)
class ProjectConfig:
    """Top-level configuration container."""

    physics: PhysicsParams = field(default_factory=PhysicsParams)
    sim: SimulationParams = field(default_factory=SimulationParams)
    render: RenderParams = field(default_factory=RenderParams)
    output: OutputParams = field(default_factory=OutputParams)

    # Three magnets at the vertices of an equilateral triangle.  The exact
    # symmetry is useful because it gives a good sanity check: for a symmetric
    # render window, the final basin pixel counts should be roughly balanced.
    magnet_positions: Tuple[Tuple[float, float], ...] = (
        (1.0, 0.0),
        (-0.5, 0.8660254037844386),
        (-0.5, -0.8660254037844386),
    )

    # RGB colours for magnet labels.  Label -1 means unresolved/unclassified.
    # The exact colours do not affect the numerical result; they only affect
    # the rendered image.
    colours: Mapping[int, Tuple[int, int, int]] = field(
        default_factory=lambda: {
            0: (220, 60, 60),    # red-ish
            1: (60, 180, 60),    # green-ish
            2: (60, 60, 220),    # blue-ish
            -1: (30, 30, 30),    # dark grey for unresolved pixels
        }
    )


@dataclass
class SimulationResult:
    """Arrays produced by the full-frame simulation."""

    basin: np.ndarray           # int16, shape (height, width)
    steps: np.ndarray           # int32, shape (height, width)
    strictly_settled: np.ndarray  # bool, shape (height, width)

    # Final state arrays are useful when debugging a hardware mismatch.
    # For example, if the final basin label is wrong, you can compare final_x
    # and final_y from software and HDL simulation to see where the trajectory
    # diverged.
    final_x: np.ndarray
    final_y: np.ndarray
    final_vx: np.ndarray
    final_vy: np.ndarray

    elapsed_s: float
    iterations_executed: int


# =============================================================================
# Small utilities
# =============================================================================


def dtype_from_name(name: FloatDTypeName) -> np.dtype:
    """Convert a user-facing dtype name into a NumPy dtype."""

    if name == "float32":
        return np.dtype(np.float32)
    if name == "float64":
        return np.dtype(np.float64)
    raise ValueError(f"Unsupported dtype {name!r}. Use 'float32' or 'float64'.")


def magnets_as_array(config: ProjectConfig) -> np.ndarray:
    """Return magnet positions as a NumPy array of shape (n_magnets, 2)."""

    dtype = dtype_from_name(config.sim.dtype)
    magnets = np.asarray(config.magnet_positions, dtype=dtype)

    if magnets.ndim != 2 or magnets.shape[1] != 2:
        raise ValueError("magnet_positions must have shape (n_magnets, 2).")

    return magnets


def build_initial_grid(config: ProjectConfig) -> Tuple[np.ndarray, np.ndarray]:
    """Create the initial-condition grid x0, y0.

    The returned arrays have shape:

        (height, width)

    Indexing convention:
        x[row, col] gives the x-coordinate of a pixel.
        y[row, col] gives the y-coordinate of a pixel.

    The plotting functions use origin='lower', so the first row is the bottom
    of the physical y-axis rather than the top of a conventional image.
    """

    dtype = dtype_from_name(config.sim.dtype)
    r = config.render

    xs = np.linspace(r.x_min, r.x_max, r.width, dtype=dtype)
    ys = np.linspace(r.y_min, r.y_max, r.height, dtype=dtype)

    # np.meshgrid with indexing='xy' gives x and y arrays that match image
    # coordinates naturally: columns vary in x, rows vary in y.
    x, y = np.meshgrid(xs, ys, indexing="xy")
    return x, y


def ensure_output_dir(config: ProjectConfig) -> Path:
    """Create the output directory if needed and return it."""

    out_dir = Path(config.output.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    return out_dir


# =============================================================================
# Physics model
# =============================================================================


def acceleration_arrays(
    x: np.ndarray,
    y: np.ndarray,
    vx: np.ndarray,
    vy: np.ndarray,
    config: ProjectConfig,
) -> Tuple[np.ndarray, np.ndarray]:
    """Compute acceleration arrays ax, ay for all pixels.

    Model equation
    --------------
    For each pixel, the state is:

        x, y, vx, vy

    The acceleration is:

        ax = -gamma * vx - omega2 * x
             + sum_j mu * (mx_j - x) / ((mx_j - x)^2 + (my_j - y)^2 + h2)^(3/2)

        ay = -gamma * vy - omega2 * y
             + sum_j mu * (my_j - y) / ((mx_j - x)^2 + (my_j - y)^2 + h2)^(3/2)

    Interpretation of the terms
    ---------------------------
    -gamma * v:
        Damping/friction term.  Without this, many trajectories would not settle.

    -omega2 * x:
        Central restoring force.  It approximates the pendulum's tendency to
        return to the origin.

    magnet term:
        Point-attractor force projected into the x-y plane.  The h2 term keeps
        the force finite.

    Hardware note
    -------------
    The expensive part is q^(-3/2).  On FPGA this could be approximated using:
        - a lookup table,
        - reciprocal square-root approximation,
        - piecewise polynomial approximation,
        - or a coarser force model for a first working implementation.
    """

    p = config.physics
    magnets = magnets_as_array(config)

    # Start with damping and central restoring force.
    ax = -p.gamma * vx - p.omega2 * x
    ay = -p.gamma * vy - p.omega2 * y

    # Add the contribution from each magnet.
    #
    # This loop is over magnets, not pixels.  There are only 3 magnets by
    # default, so the loop overhead is small while the pixel operations remain
    # vectorised by NumPy.
    for mx, my in magnets:
        dx = mx - x
        dy = my - y

        # q is squared 3D distance except that the vertical component is
        # represented only by h2.
        q = dx * dx + dy * dy + p.h2

        # Force magnitude factor.  q^(-3/2) = 1 / q^(3/2).
        inv_r3 = q ** (-1.5)

        ax = ax + p.mu * dx * inv_r3
        ay = ay + p.mu * dy * inv_r3

    return ax, ay


def nearest_magnet_arrays(
    x: np.ndarray,
    y: np.ndarray,
    config: ProjectConfig,
) -> Tuple[np.ndarray, np.ndarray]:
    """Return the nearest magnet label and squared distance for every pixel.

    Returns
    -------
    nearest:
        int16 array of shape (height, width).  nearest[row, col] is the index
        of the closest magnet.

    d2_min:
        floating-point array of shape (height, width).  d2_min[row, col] is
        the squared x-y plane distance to the closest magnet.

    Why squared distance?
    ---------------------
    We only compare distances, so using squared distance avoids a square root.
    The same trick is useful in hardware.
    """

    magnets = magnets_as_array(config)
    d2_per_magnet = []

    for mx, my in magnets:
        dx = mx - x
        dy = my - y
        d2_per_magnet.append(dx * dx + dy * dy)

    # Shape becomes (n_magnets, height, width).
    d2_stack = np.stack(d2_per_magnet, axis=0)

    nearest = np.argmin(d2_stack, axis=0).astype(np.int16)
    d2_min = np.min(d2_stack, axis=0)

    return nearest, d2_min


def semi_implicit_euler_step(
    x: np.ndarray,
    y: np.ndarray,
    vx: np.ndarray,
    vy: np.ndarray,
    active: np.ndarray,
    config: ProjectConfig,
) -> None:
    """Advance active pixels by one semi-implicit Euler step, in place.

    Semi-implicit Euler update:

        v[n+1] = v[n] + dt * a(x[n], v[n])
        x[n+1] = x[n] + dt * v[n+1]

    Why update only active pixels?
    ------------------------------
    Once a pixel has settled, its final label is known.  Continuing to simulate
    it wastes time and can slightly change the stored final state.  Therefore,
    inactive pixels are frozen.

    Why compute acceleration for all pixels?
    ----------------------------------------
    For simplicity and vectorisation, acceleration_arrays computes ax, ay for
    the full image.  We then apply the update only where active == True.  This
    is easy to understand and usually fast enough for a golden reference.
    """

    dt = config.sim.dt
    ax, ay = acceleration_arrays(x, y, vx, vy, config)

    # Update velocity first, then position using the new velocity.
    vx[active] = vx[active] + dt * ax[active]
    vy[active] = vy[active] + dt * ay[active]

    x[active] = x[active] + dt * vx[active]
    y[active] = y[active] + dt * vy[active]


# =============================================================================
# Single-point and full-frame simulation
# =============================================================================


def simulate_one_point(
    x0: float,
    y0: float,
    config: ProjectConfig,
) -> Tuple[int, int, bool]:
    """Simulate one initial condition.

    This is mainly a sanity-check helper.  It is easier to debug one trajectory
    than a full image.

    Returns
    -------
    label:
        Magnet index if classified, or -1 if unresolved and classify_on_timeout
        is False.

    steps:
        Number of integration steps executed.

    strictly_settled:
        True if the strict position + velocity settling criterion was satisfied.
        False if the result came from timeout classification.
    """

    dtype = dtype_from_name(config.sim.dtype)

    # Use tiny 1x1 arrays so that the single-point path uses exactly the same
    # acceleration and integration functions as the full-frame path.  This
    # avoids accidental disagreement between "test" and "real" code.
    x = np.array([[x0]], dtype=dtype)
    y = np.array([[y0]], dtype=dtype)
    vx = np.zeros_like(x)
    vy = np.zeros_like(y)
    active = np.ones_like(x, dtype=bool)

    r2_settle = config.sim.r_settle * config.sim.r_settle
    v2_settle = config.sim.v_settle * config.sim.v_settle
    consecutive = 0
    last_label = -1

    for step in range(1, config.sim.max_steps + 1):
        semi_implicit_euler_step(x, y, vx, vy, active, config)

        nearest, d2_min = nearest_magnet_arrays(x, y, config)
        speed2 = vx * vx + vy * vy

        last_label = int(nearest[0, 0])

        is_close = bool(d2_min[0, 0] < r2_settle)
        is_slow = bool(speed2[0, 0] < v2_settle)

        if is_close and is_slow:
            consecutive += 1
            if consecutive >= config.sim.min_consecutive:
                return last_label, step, True
        else:
            consecutive = 0

    # Timeout path: either classify by nearest magnet or leave unresolved.
    if config.sim.classify_on_timeout:
        return last_label, config.sim.max_steps, False
    return -1, config.sim.max_steps, False


def generate_basin_map(config: ProjectConfig) -> SimulationResult:
    """Generate the complete basin-of-attraction image.

    The key arrays are:

        basin[row, col]             -> final magnet label
        steps[row, col]             -> number of steps needed
        strictly_settled[row, col]  -> did the strict settling test pass?

    Basin labels:
        0, 1, 2, ...  = magnet index
        -1            = unresolved/unclassified

    In the default mode, timeout pixels are assigned to the nearest magnet,
    but strictly_settled remains False.  This gives a complete visual image
    while preserving honest information about numerical convergence.
    """

    x, y = build_initial_grid(config)
    vx = np.zeros_like(x)
    vy = np.zeros_like(y)

    height, width = x.shape

    basin = np.full((height, width), fill_value=-1, dtype=np.int16)
    steps = np.zeros((height, width), dtype=np.int32)
    strictly_settled = np.zeros((height, width), dtype=bool)

    # active == True means "this pixel still needs simulation".
    active = np.ones((height, width), dtype=bool)

    # Consecutive settling count for each pixel.  A pixel only becomes "done"
    # after min_consecutive successful settle checks.
    consecutive = np.zeros((height, width), dtype=np.int16)

    r2_settle = config.sim.r_settle * config.sim.r_settle
    v2_settle = config.sim.v_settle * config.sim.v_settle

    t0 = time.perf_counter()
    last_step = 0

    for step in range(1, config.sim.max_steps + 1):
        last_step = step

        semi_implicit_euler_step(x, y, vx, vy, active, config)

        nearest, d2_min = nearest_magnet_arrays(x, y, config)
        speed2 = vx * vx + vy * vy

        # A pixel is currently settled only if it is both close to the nearest
        # magnet and moving slowly.
        settle_now = active & (d2_min < r2_settle) & (speed2 < v2_settle)

        # Increase the consecutive count where the criterion holds.
        consecutive[settle_now] += 1

        # Reset the count for active pixels that fail the criterion this step.
        consecutive[active & ~settle_now] = 0

        # A pixel is done only after the criterion has held for several
        # consecutive steps.
        done_now = active & (consecutive >= config.sim.min_consecutive)

        if np.any(done_now):
            basin[done_now] = nearest[done_now]
            steps[done_now] = step
            strictly_settled[done_now] = True
            active[done_now] = False

        if config.sim.progress_interval > 0:
            should_print = (
                step == 1
                or step % config.sim.progress_interval == 0
                or step == config.sim.max_steps
                or not np.any(active)
            )

            if should_print:
                elapsed = time.perf_counter() - t0
                active_count = int(np.sum(active))
                done_count = height * width - active_count
                print(
                    f"step {step:5d}/{config.sim.max_steps} | "
                    f"done {done_count:7d}/{height * width} | "
                    f"active {active_count:7d} | "
                    f"elapsed {elapsed:7.2f}s"
                )

        if not np.any(active):
            break

    # Handle pixels that were never strictly settled.
    if np.any(active):
        nearest, _ = nearest_magnet_arrays(x, y, config)

        if config.sim.classify_on_timeout:
            basin[active] = nearest[active]
        else:
            basin[active] = -1

        steps[active] = config.sim.max_steps

    elapsed_s = time.perf_counter() - t0

    return SimulationResult(
        basin=basin,
        steps=steps,
        strictly_settled=strictly_settled,
        final_x=x,
        final_y=y,
        final_vx=vx,
        final_vy=vy,
        elapsed_s=elapsed_s,
        iterations_executed=last_step,
    )


# =============================================================================
# Analysis helpers
# =============================================================================


def pixel_match_rate(
    reference_basin: np.ndarray,
    candidate_basin: np.ndarray,
    mask: Optional[np.ndarray] = None,
) -> float:
    """Compute the pixel label match rate between two basin maps.

    This will be useful later when comparing:
        - float64 reference vs float32 reference,
        - floating-point reference vs fixed-point Python,
        - fixed-point Python vs HDL simulation,
        - HDL simulation vs actual FPGA capture.

    Parameters
    ----------
    reference_basin:
        Reference integer labels.

    candidate_basin:
        Candidate integer labels.

    mask:
        Optional boolean mask.  For example, use strictly_settled to ignore
        timeout-classified pixels.

    Returns
    -------
    match_rate:
        Fraction in [0, 1].
    """

    if reference_basin.shape != candidate_basin.shape:
        raise ValueError("Basin maps must have the same shape.")

    if mask is None:
        valid = np.ones_like(reference_basin, dtype=bool)
    else:
        if mask.shape != reference_basin.shape:
            raise ValueError("Mask must have the same shape as the basin maps.")
        valid = mask.astype(bool)

    if not np.any(valid):
        return float("nan")

    return float(np.mean(reference_basin[valid] == candidate_basin[valid]))


def compute_statistics(result: SimulationResult, config: ProjectConfig) -> Dict[str, object]:
    """Compute numerical summary statistics for the run."""

    basin = result.basin
    steps = result.steps
    settled = result.strictly_settled

    total_pixels = int(basin.size)
    flat_steps = steps.ravel()

    basin_counts: Dict[str, int] = {}
    for label in sorted(np.unique(basin).tolist()):
        basin_counts[str(int(label))] = int(np.sum(basin == label))

    stats: Dict[str, object] = {
        "total_pixels": total_pixels,
        "iterations_executed": int(result.iterations_executed),
        "elapsed_s": float(result.elapsed_s),
        "average_ms_per_pixel": float(1000.0 * result.elapsed_s / total_pixels),
        "strictly_settled_pixels": int(np.sum(settled)),
        "strictly_settled_percent": float(100.0 * np.mean(settled)),
        "step_mean": float(np.mean(flat_steps)),
        "step_median": float(np.median(flat_steps)),
        "step_p90": float(np.percentile(flat_steps, 90)),
        "step_p99": float(np.percentile(flat_steps, 99)),
        "step_max": int(np.max(flat_steps)),
        "basin_counts": basin_counts,
    }

    # Useful symmetry sanity check for the default 3-magnet configuration.
    n_magnets = len(config.magnet_positions)
    magnet_counts = [int(np.sum(basin == k)) for k in range(n_magnets)]
    if magnet_counts:
        stats["magnet_count_min"] = int(min(magnet_counts))
        stats["magnet_count_max"] = int(max(magnet_counts))
        stats["magnet_count_spread_percent_of_image"] = float(
            100.0 * (max(magnet_counts) - min(magnet_counts)) / total_pixels
        )

    return stats


def print_single_point_tests(config: ProjectConfig) -> None:
    """Run a small set of trajectory sanity checks."""

    test_points = [
        (1.0, 0.0),                 # exactly above magnet 0
        (-0.5, 0.8660254037844386), # exactly above magnet 1
        (-0.5, -0.8660254037844386),# exactly above magnet 2
        (0.0, 0.0),                 # symmetric centre point
        (2.0, 2.0),                 # outside render area corner
        (-2.0, -2.0),               # outside render area corner
    ]

    print("\nSingle-pixel sanity checks:")
    for x0, y0 in test_points:
        label, n_steps, ok = simulate_one_point(x0, y0, config)
        status = "strictly settled" if ok else "timeout / nearest-classified"
        print(
            f"  x0={x0:+.4f}, y0={y0:+.4f} "
            f"-> label {label:2d}, steps {n_steps:5d}, {status}"
        )


def print_summary(result: SimulationResult, config: ProjectConfig) -> None:
    """Print a human-readable summary to the terminal."""

    stats = compute_statistics(result, config)

    print("\nRun summary:")
    print(f"  resolution               : {config.render.width} x {config.render.height}")
    print(f"  dtype                    : {config.sim.dtype}")
    print(f"  iterations executed      : {result.iterations_executed}")
    print(f"  elapsed                  : {result.elapsed_s:.3f} s")
    print(f"  average time per pixel   : {stats['average_ms_per_pixel']:.6f} ms/pixel")
    print(
        "  strictly settled         : "
        f"{stats['strictly_settled_pixels']} / {stats['total_pixels']} "
        f"({stats['strictly_settled_percent']:.2f}%)"
    )

    print("\nStep statistics:")
    print(f"  mean                     : {stats['step_mean']:.2f}")
    print(f"  median                   : {stats['step_median']:.2f}")
    print(f"  90th percentile          : {stats['step_p90']:.2f}")
    print(f"  99th percentile          : {stats['step_p99']:.2f}")
    print(f"  max                      : {stats['step_max']}")

    print("\nBasin pixel counts:")
    for label, count in stats["basin_counts"].items():
        percent = 100.0 * count / stats["total_pixels"]
        print(f"  label {label:>2s}: {count:7d} pixels ({percent:6.2f}%)")


# =============================================================================
# Plotting and saving
# =============================================================================


def basin_to_rgb(
    basin: np.ndarray,
    config: ProjectConfig,
    strictly_settled: Optional[np.ndarray] = None,
    darken_timeout_pixels: bool = True,
) -> np.ndarray:
    """Convert an integer basin-label map into an RGB image.

    Pixels that were classified only at timeout can optionally be darkened.  This
    is useful because the image remains complete, but the viewer can still see
    which regions are numerically uncertain.
    """

    height, width = basin.shape
    rgb = np.zeros((height, width, 3), dtype=np.uint8)

    for label, colour in config.colours.items():
        rgb[basin == int(label)] = np.asarray(colour, dtype=np.uint8)

    if strictly_settled is not None and darken_timeout_pixels:
        timeout = ~strictly_settled
        rgb[timeout] = (0.45 * rgb[timeout]).astype(np.uint8)

    return rgb


def save_raw_basin_png(result: SimulationResult, config: ProjectConfig) -> Path:
    """Save a raw RGB basin image without axes."""

    out_dir = ensure_output_dir(config)
    rgb = basin_to_rgb(
        result.basin,
        config,
        result.strictly_settled,
        darken_timeout_pixels=config.output.darken_timeout_pixels,
    )
    out_path = out_dir / "basin_rgb.png"

    # plt.imsave saves the array directly, without axes, labels, or margins.
    plt.imsave(out_path, rgb)
    return out_path


def save_annotated_basin_plot(result: SimulationResult, config: ProjectConfig) -> Path:
    """Save a basin plot with axes and magnet markers."""

    out_dir = ensure_output_dir(config)
    out_path = out_dir / "basin_annotated.png"

    rgb = basin_to_rgb(
        result.basin,
        config,
        result.strictly_settled,
        darken_timeout_pixels=config.output.darken_timeout_pixels,
    )
    magnets = magnets_as_array(config)
    r = config.render

    plt.figure(figsize=(8, 8))
    plt.imshow(
        rgb,
        origin="lower",
        extent=[r.x_min, r.x_max, r.y_min, r.y_max],
        interpolation="nearest",
    )

    # Mark magnet locations in white.  This makes the plot easier to explain in
    # a report because each basin colour can be linked to a physical magnet.
    for i, (mx, my) in enumerate(magnets):
        plt.plot(mx, my, "w+", markersize=12, markeredgewidth=2)
        plt.text(mx + 0.06, my + 0.06, f"M{i}", color="white", fontsize=10)

    plt.title(
        f"Magnetic pendulum basin map "
        f"({r.width}x{r.height}, {config.sim.dtype}, dt={config.sim.dt})"
    )
    plt.xlabel("initial x position, x0")
    plt.ylabel("initial y position, y0")
    plt.tight_layout()
    plt.savefig(out_path, dpi=200, bbox_inches="tight")
    if config.output.show_plots:
        plt.show()
    plt.close()

    return out_path


def save_step_count_plot(result: SimulationResult, config: ProjectConfig) -> Path:
    """Save a heatmap showing how many steps each pixel needed."""

    out_dir = ensure_output_dir(config)
    out_path = out_dir / "step_count.png"
    r = config.render

    plt.figure(figsize=(7, 6))
    plt.imshow(
        result.steps,
        origin="lower",
        extent=[r.x_min, r.x_max, r.y_min, r.y_max],
        interpolation="nearest",
        cmap="inferno",
    )
    plt.colorbar(label="integration steps")
    plt.title("Steps to strict settling or timeout classification")
    plt.xlabel("initial x position, x0")
    plt.ylabel("initial y position, y0")
    plt.tight_layout()
    plt.savefig(out_path, dpi=200, bbox_inches="tight")
    if config.output.show_plots:
        plt.show()
    plt.close()

    return out_path


def save_step_histogram(result: SimulationResult, config: ProjectConfig) -> Path:
    """Save a histogram of step counts."""

    out_dir = ensure_output_dir(config)
    out_path = out_dir / "step_histogram.png"

    flat = result.steps.ravel()
    p99 = int(np.percentile(flat, 99))

    plt.figure(figsize=(8, 4.5))
    plt.hist(flat, bins=60, edgecolor="white")
    plt.axvline(p99, linestyle="--", label=f"99th percentile = {p99}")
    plt.xlabel("integration steps")
    plt.ylabel("pixel count")
    plt.title("Distribution of settling/classification time")
    plt.legend()
    plt.tight_layout()
    plt.savefig(out_path, dpi=200, bbox_inches="tight")
    if config.output.show_plots:
        plt.show()
    plt.close()

    return out_path


def save_arrays(result: SimulationResult, config: ProjectConfig) -> Dict[str, Path]:
    """Save numerical arrays in NumPy format."""

    out_dir = ensure_output_dir(config)

    paths: Dict[str, Path] = {}

    paths["basin_labels"] = out_dir / "basin_labels.npy"
    paths["step_count"] = out_dir / "step_count.npy"
    paths["strictly_settled"] = out_dir / "strictly_settled.npy"

    np.save(paths["basin_labels"], result.basin)
    np.save(paths["step_count"], result.steps)
    np.save(paths["strictly_settled"], result.strictly_settled)

    # Compressed bundle is convenient for loading all core arrays at once:
    #
    #   data = np.load("result_bundle.npz")
    #   basin = data["basin"]
    #
    bundle_path = out_dir / "result_bundle.npz"
    bundle_kwargs = {
        "basin": result.basin,
        "steps": result.steps,
        "strictly_settled": result.strictly_settled,
    }

    if config.output.save_final_state:
        bundle_kwargs.update(
            final_x=result.final_x,
            final_y=result.final_y,
            final_vx=result.final_vx,
            final_vy=result.final_vy,
        )

    np.savez_compressed(bundle_path, **bundle_kwargs)
    paths["result_bundle"] = bundle_path

    return paths


def config_to_jsonable(config: ProjectConfig) -> Dict[str, object]:
    """Convert the configuration to a JSON-friendly dictionary."""

    # asdict handles nested dataclasses, but Path is not directly JSON serialisable.
    cfg = asdict(config)
    cfg["output"]["out_dir"] = str(cfg["output"]["out_dir"])

    # Mapping keys may become integers in Python, but JSON object keys are
    # strings.  Convert explicitly so the saved file is predictable.
    cfg["colours"] = {str(k): list(v) for k, v in config.colours.items()}

    return cfg


def save_summary_json(result: SimulationResult, config: ProjectConfig) -> Path:
    """Save parameters and statistics to summary.json."""

    out_dir = ensure_output_dir(config)
    out_path = out_dir / "summary.json"

    payload = {
        "config": config_to_jsonable(config),
        "statistics": compute_statistics(result, config),
        "notes": [
            "basin label -1 means unresolved/unclassified.",
            "strictly_settled=false means the pixel did not satisfy the strict position+velocity criterion.",
            "If classify_on_timeout=true, timeout pixels are still coloured by nearest magnet.",
            "Near basin boundaries, tiny numerical differences can change labels; compare both image-level match rate and visual boundary behaviour.",
        ],
    }

    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2)

    return out_path


def save_all_outputs(result: SimulationResult, config: ProjectConfig) -> Dict[str, Path]:
    """Save all plots, arrays, and summary files."""

    paths: Dict[str, Path] = {}
    paths["basin_rgb_png"] = save_raw_basin_png(result, config)
    paths["basin_annotated_png"] = save_annotated_basin_plot(result, config)
    paths["step_count_png"] = save_step_count_plot(result, config)
    paths["step_histogram_png"] = save_step_histogram(result, config)
    paths.update(save_arrays(result, config))
    paths["summary_json"] = save_summary_json(result, config)
    return paths


# =============================================================================
# Command-line interface
# =============================================================================


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments.

    The defaults reproduce the intended golden-reference setup.  Arguments are
    provided so that you can quickly run smaller images for testing or compare
    float64 and float32 behaviour.
    """

    parser = argparse.ArgumentParser(
        description="Floating-point golden reference for magnetic-pendulum basin rendering."
    )

    # Render settings
    parser.add_argument("--width", type=int, default=256, help="Output image width in pixels.")
    parser.add_argument("--height", type=int, default=256, help="Output image height in pixels.")
    parser.add_argument("--x-min", type=float, default=-1.8, help="Minimum x initial position.")
    parser.add_argument("--x-max", type=float, default=1.8, help="Maximum x initial position.")
    parser.add_argument("--y-min", type=float, default=-1.8, help="Minimum y initial position.")
    parser.add_argument("--y-max", type=float, default=1.8, help="Maximum y initial position.")

    # Physics settings
    parser.add_argument("--gamma", type=float, default=0.20, help="Damping coefficient.")
    parser.add_argument("--omega2", type=float, default=1.00, help="Central restoring coefficient.")
    parser.add_argument("--h2", type=float, default=0.25, help="Squared vertical magnet separation.")
    parser.add_argument("--mu", type=float, default=1.00, help="Magnet strength.")

    # Numerical settings
    parser.add_argument("--dt", type=float, default=0.01, help="Simulation time step.")
    parser.add_argument("--max-steps", type=int, default=5000, help="Maximum steps per pixel.")
    parser.add_argument("--r-settle", type=float, default=0.10, help="Position settling radius.")
    parser.add_argument("--v-settle", type=float, default=0.01, help="Velocity settling threshold.")
    parser.add_argument(
        "--min-consecutive",
        type=int,
        default=3,
        help="Consecutive settled steps required before accepting a pixel.",
    )
    parser.add_argument(
        "--dtype",
        choices=["float32", "float64"],
        default="float64",
        help="Floating-point type used for simulation.",
    )
    parser.add_argument(
        "--no-timeout-classification",
        action="store_true",
        help="Leave non-settled timeout pixels as label -1 instead of assigning nearest magnet.",
    )
    parser.add_argument(
        "--progress-interval",
        type=int,
        default=500,
        help="Print progress every N steps; use 0 to disable.",
    )

    # Output settings
    parser.add_argument(
        "--out-dir",
        type=Path,
        default=Path("pendulum_outputs_detailed"),
        help="Directory for output files.",
    )
    parser.add_argument(
        "--show-plots",
        action="store_true",
        help="Display plots interactively as well as saving them.",
    )
    parser.add_argument(
        "--save-final-state",
        action="store_true",
        help="Include final x/y/vx/vy arrays in result_bundle.npz.",
    )
    parser.add_argument(
        "--darken-timeouts",
        action="store_true",
        help="Darken pixels that were classified only at max_steps in the saved PNGs.",
    )

    return parser.parse_args()


def make_config_from_args(args: argparse.Namespace) -> ProjectConfig:
    """Build a ProjectConfig from parsed command-line arguments."""

    physics = PhysicsParams(
        gamma=args.gamma,
        omega2=args.omega2,
        h2=args.h2,
        mu=args.mu,
    )

    sim = SimulationParams(
        dt=args.dt,
        max_steps=args.max_steps,
        r_settle=args.r_settle,
        v_settle=args.v_settle,
        min_consecutive=args.min_consecutive,
        classify_on_timeout=not args.no_timeout_classification,
        dtype=args.dtype,
        progress_interval=args.progress_interval,
    )

    render = RenderParams(
        x_min=args.x_min,
        x_max=args.x_max,
        y_min=args.y_min,
        y_max=args.y_max,
        width=args.width,
        height=args.height,
    )

    output = OutputParams(
        out_dir=args.out_dir,
        show_plots=args.show_plots,
        save_final_state=args.save_final_state,
        darken_timeout_pixels=args.darken_timeouts,
    )

    return ProjectConfig(
        physics=physics,
        sim=sim,
        render=render,
        output=output,
    )


def validate_config(config: ProjectConfig) -> None:
    """Catch common configuration mistakes early."""

    if config.render.width <= 0 or config.render.height <= 0:
        raise ValueError("width and height must be positive.")

    if config.render.x_min >= config.render.x_max:
        raise ValueError("x_min must be smaller than x_max.")

    if config.render.y_min >= config.render.y_max:
        raise ValueError("y_min must be smaller than y_max.")

    if config.sim.dt <= 0:
        raise ValueError("dt must be positive.")

    if config.sim.max_steps <= 0:
        raise ValueError("max_steps must be positive.")

    if config.sim.r_settle <= 0:
        raise ValueError("r_settle must be positive.")

    if config.sim.v_settle <= 0:
        raise ValueError("v_settle must be positive.")

    if config.sim.min_consecutive <= 0:
        raise ValueError("min_consecutive must be positive.")

    if config.physics.h2 <= 0:
        raise ValueError("h2 should be positive to avoid singular magnet forces.")


def main() -> None:
    """Main entry point."""

    args = parse_args()
    config = make_config_from_args(args)
    validate_config(config)

    ensure_output_dir(config)

    print("Floating-point magnetic-pendulum golden reference")
    print("-------------------------------------------------")
    print(f"resolution       : {config.render.width} x {config.render.height}")
    print(f"window           : x=[{config.render.x_min}, {config.render.x_max}], "
          f"y=[{config.render.y_min}, {config.render.y_max}]")
    print(f"dtype            : {config.sim.dtype}")
    print(f"dt, max_steps    : {config.sim.dt}, {config.sim.max_steps}")
    print(f"gamma, omega2    : {config.physics.gamma}, {config.physics.omega2}")
    print(f"h2, mu           : {config.physics.h2}, {config.physics.mu}")
    print(f"output directory : {Path(config.output.out_dir).resolve()}")

    print_single_point_tests(config)

    print("\nGenerating full basin map:")
    result = generate_basin_map(config)

    print_summary(result, config)

    print("\nSaving outputs:")
    saved_paths = save_all_outputs(result, config)
    for name, path in saved_paths.items():
        print(f"  {name:24s}: {path}")

    print("\nDone.")


if __name__ == "__main__":
    main()
