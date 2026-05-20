"""
Fixed-point magnetic pendulum model with detailed educational comments.

Why this file exists
--------------------
The floating-point simulator is a high-accuracy golden reference. It is useful
for checking the mathematical behaviour of the magnetic pendulum model, but it
is not how the FPGA will compute the result.

An FPGA implementation will normally use integer arithmetic, fixed-point
numbers, multipliers, shifts, lookup tables, registers and FSMs. This file is
therefore a software model of the hardware-style arithmetic.

The purpose is not only to produce another picture. The purpose is to create a
verification bridge:

    float64 / float32 Python golden reference
        -> fixed-point Python model
        -> SystemVerilog simulation
        -> FPGA output

The SystemVerilog implementation should eventually match this fixed-point
Python model very closely. The fixed-point Python model is much easier to debug
than HDL, so we use it to decide word lengths, scaling, saturation behaviour and
force approximations before writing the final hardware.

Fixed-point idea
----------------
A fixed-point value is stored as an integer. The integer is interpreted as a
scaled real number:

    real_value = raw_integer / 2^frac_bits

For example, if frac_bits = 20:

    real  1.0  -> raw  1048576
    real  0.5  -> raw   524288
    real -1.0  -> raw -1048576

Addition and subtraction are simple integer operations. Multiplication is more
subtle: multiplying two Q-format numbers doubles the number of fractional bits,
so we must shift right by frac_bits to return to the original scale.

Expensive force term
--------------------
The magnetic pendulum force includes:

    q^(-3/2)

where:

    q = dx^2 + dy^2 + h^2

This operation is easy in floating-point Python, but expensive in hardware. To
make the development gradual, this file supports two modes:

    float-force:
        The state variables are fixed-point, but q^(-3/2) is still computed in
        floating point and quantised back to fixed-point. This isolates the
        effect of state quantisation.

    lut-force:
        q^(-3/2) is approximated using a lookup table. This is closer to what
        the FPGA may implement.

Recommended commands
--------------------
Quick test:

    python magnetic_pendulum_fixed_point_model_commented.py --width 64 --height 64 --max-steps 1000

Normal fixed-point run:

    python magnetic_pendulum_fixed_point_model_commented.py --width 256 --height 256 --frac-bits 20

Hardware-like LUT-force run:

    python magnetic_pendulum_fixed_point_model_commented.py --width 256 --height 256 --force-mode lut-force --lut-size 4096

Compare against floating-point output:

    python magnetic_pendulum_fixed_point_model_commented.py \
        --ref-basin pendulum_outputs/euler_basin.npy \
        --ref-settled pendulum_outputs/euler_settled.npy
"""

from __future__ import annotations

# argparse lets us change parameters from the terminal without editing the file.
# Example:
#     python file.py --width 512 --height 512 --frac-bits 18
import argparse

# json is used to save a machine-readable summary of each run. This is useful
# for reports because it records the exact parameters and metrics used.
import json

# time is used to measure CPU runtime. Later, this helps compare CPU software
# against the FPGA accelerator.
import time

# dataclass is used to create small parameter containers without writing long
# __init__ methods. field(default_factory=...) is used for safe default objects.
# asdict converts dataclass objects into dictionaries for JSON saving.
from dataclasses import asdict, dataclass, field

# Path gives cleaner file/folder handling than plain strings.
from pathlib import Path

# Literal is used to restrict strings such as force_mode to known choices.
# Mapping, Optional and Tuple are used only for type hints; they make the code
# easier to read and easier for editors/type checkers to understand.
from typing import Literal, Mapping, Optional, Tuple

# matplotlib is used only for plotting and saving images.
import matplotlib.pyplot as plt

# NumPy is used for array-based computation. Even though the model is fixed-
# point, we still use NumPy arrays to process many pixels at once efficiently.
import numpy as np


# ForceMode is a type alias. It tells the reader that force_mode should be one
# of these exact strings, not an arbitrary string.
ForceMode = Literal["float-force", "lut-force"]


# ============================================================
# Fixed-point format and arithmetic helpers
# ============================================================

@dataclass(frozen=True)
class FixedFormat:
    """
    Describe the signed fixed-point format used by the whole model.

    This dataclass answers the question:

        How should a real number be stored as an integer?

    Fields
    ------
    total_bits:
        Total number of bits in the fixed-point value, including sign bit,
        integer bits and fractional bits. For example, total_bits=32 models a
        signed 32-bit fixed-point datapath.

    frac_bits:
        Number of fractional bits. The scaling factor is 2^frac_bits.
        More fractional bits means better precision, but fewer integer bits.

    Example
    -------
    If total_bits=32 and frac_bits=20, the format is often called Q11.20 if the
    sign bit is counted separately as one sign bit plus 11 integer magnitude
    bits plus 20 fractional bits.

    The largest representable raw value is 2^(total_bits-1)-1, and the smallest
    is -2^(total_bits-1). The corresponding real range is this raw range divided
    by 2^frac_bits.

    Why frozen=True?
    ----------------
    The format should not change halfway through a simulation. If it did, old
    raw values would be interpreted with a different scale, which would be a
    serious bug. frozen=True prevents accidental modification.
    """

    total_bits: int = 32
    frac_bits: int = 20

    def __post_init__(self) -> None:
        """
        Validate the fixed-point format immediately after construction.

        dataclasses call __post_init__ after the automatically generated
        __init__ method. We use it here to reject impossible or unsafe formats.

        Conditions checked:
        - total_bits must be at least 2 because we need at least one sign bit
          and at least one data bit.
        - frac_bits cannot be negative.
        - frac_bits must leave room for the sign bit and at least one integer
          bit; otherwise the range would be too small for this pendulum model.
        """
        if self.total_bits < 2:
            raise ValueError("total_bits must be at least 2")
        if self.frac_bits < 0:
            raise ValueError("frac_bits must be non-negative")
        if self.frac_bits >= self.total_bits - 1:
            raise ValueError("frac_bits must leave at least one sign/integer bit")

    @property
    def scale(self) -> int:
        """
        Return the fixed-point scaling factor 2^frac_bits.

        If raw is the stored integer, then:

            real = raw / scale
            raw  = round(real * scale)
        """
        return 1 << self.frac_bits

    @property
    def min_raw(self) -> int:
        """
        Minimum signed raw integer representable by total_bits.

        For total_bits=32, this is -2^31.
        """
        return -(1 << (self.total_bits - 1))

    @property
    def max_raw(self) -> int:
        """
        Maximum signed raw integer representable by total_bits.

        For total_bits=32, this is 2^31 - 1.
        """
        return (1 << (self.total_bits - 1)) - 1

    def saturate(self, raw: np.ndarray | int) -> np.ndarray | int:
        """
        Clip raw integer values into the valid signed fixed-point range.

        Hardware has a choice when overflow happens:

        1. wraparound:
           The value wraps around like normal two's-complement arithmetic.
           This is cheap but can cause very large errors.

        2. saturation:
           The value is clipped to the largest or smallest representable value.
           This is safer and easier to debug.

        This model uses saturation because it is more robust for an educational
        golden fixed-point model. If you later decide to use wraparound in HDL,
        this function should be changed to match the hardware.
        """
        clipped = np.clip(raw, self.min_raw, self.max_raw).astype(np.int64)
        if np.isscalar(raw):
            return int(clipped)
        return clipped

    def to_fixed(self, value: np.ndarray | float | int) -> np.ndarray | int:
        """
        Convert a real-valued number or array into fixed-point raw integer form.

        Steps:
        1. Multiply the real value by the fixed-point scale.
        2. Round to the nearest integer.
        3. Saturate to the allowed raw integer range.

        This is mainly used for constants, initial conditions and LUT values.
        During the actual datapath update, we use fx_add, fx_sub and fx_mul so
        the operation sequence resembles hardware more clearly.
        """
        raw = np.rint(np.asarray(value, dtype=np.float64) * self.scale).astype(np.int64)
        raw = self.saturate(raw)
        if np.isscalar(value):
            return int(raw)
        return raw

    def to_float(self, raw: np.ndarray | int) -> np.ndarray | float:
        """
        Convert fixed-point raw integer(s) back to ordinary floating-point.

        This is useful for debugging, plotting, and for the temporary
        float-force mode where q^(-3/2) is evaluated using floating point.
        """
        out = np.asarray(raw, dtype=np.float64) / self.scale
        if np.isscalar(raw):
            return float(out)
        return out


# The following arithmetic helper functions are deliberately simple and
# explicit. Each one corresponds to an operation you would implement in hardware.
# Keeping them separate makes the software model easier to compare with HDL.

def fx_add(fmt: FixedFormat, a: np.ndarray | int, b: np.ndarray | int) -> np.ndarray | int:
    """
    Fixed-point addition with saturation.

    Since a and b have the same scale, addition is just raw integer addition:

        raw_result = raw_a + raw_b

    Then we saturate to avoid overflowing the modelled word length.
    """
    return fmt.saturate(np.asarray(a, dtype=np.int64) + np.asarray(b, dtype=np.int64))


def fx_sub(fmt: FixedFormat, a: np.ndarray | int, b: np.ndarray | int) -> np.ndarray | int:
    """
    Fixed-point subtraction with saturation.

    This represents:

        result = a - b

    using raw integer subtraction followed by saturation.
    """
    return fmt.saturate(np.asarray(a, dtype=np.int64) - np.asarray(b, dtype=np.int64))


def fx_neg(fmt: FixedFormat, a: np.ndarray | int) -> np.ndarray | int:
    """
    Fixed-point negation with saturation.

    This is used for terms such as -gamma*vx and -omega2*x.
    """
    return fmt.saturate(-np.asarray(a, dtype=np.int64))


def fx_mul(fmt: FixedFormat, a: np.ndarray | int, b: np.ndarray | int) -> np.ndarray | int:
    """
    Fixed-point multiplication.

    Suppose a and b are both stored as Q-format raw integers:

        real_a = raw_a / 2^F
        real_b = raw_b / 2^F

    The true product is:

        real_a * real_b = (raw_a * raw_b) / 2^(2F)

    To store the result again as Q-format with F fractional bits, the raw result
    should be:

        raw_result = (raw_a * raw_b) / 2^F

    In integer hardware this division by 2^F is implemented as an arithmetic
    right shift by F bits:

        raw_result = (raw_a * raw_b) >> F

    This function then saturates the shifted result to the chosen word length.
    """
    product = np.asarray(a, dtype=np.int64) * np.asarray(b, dtype=np.int64)
    shifted = product >> fmt.frac_bits
    return fmt.saturate(shifted)


# ============================================================
# Project configuration dataclasses
# ============================================================

@dataclass(frozen=True)
class PhysicsParams:
    """
    Physical parameters of the simplified magnetic pendulum model.

    These are still written as ordinary floats in the configuration because
    they are user-facing parameters. Before the simulation loop starts, they are
    converted to fixed-point raw integers by build_fixed_constants().

    gamma:
        Damping coefficient. Larger gamma removes energy faster and usually
        makes the pendulum settle sooner.

    omega2:
        Coefficient of the central restoring force. This models the tendency of
        the pendulum to return towards the origin.

    h2:
        Squared vertical separation between the bob and the magnet plane. It
        prevents the force from becoming singular when x,y is exactly above a
        magnet.

    mu:
        Magnet strength scaling factor.

    dt:
        Time step for the numerical integration. Smaller dt is usually more
        accurate but requires more iterations.
    """

    gamma: float = 0.20
    omega2: float = 1.0
    h2: float = 0.25
    mu: float = 1.0
    dt: float = 0.01


@dataclass(frozen=True)
class SimulationParams:
    """
    Parameters that control the simulation loop and stopping rule.

    max_steps:
        Maximum number of integration steps allowed for each pixel. If the
        pendulum has not strictly settled by this point, it may be classified by
        nearest magnet if classify_on_timeout is True.F

    r_settle:
        Position threshold. The bob must be within this radius of the nearest
        magnet to be considered settled.

    v_settle:
        Velocity threshold. The bob must also be moving slowly enough to be
        considered settled.

    min_consec:
        Required number of consecutive settled checks. This avoids declaring a
        pixel settled because it briefly passes close to a magnet at high speed.

    classify_on_timeout:
        If True, pixels that do not strictly settle before max_steps are still
        assigned to the nearest magnet. The settled mask still records whether
        the classification was strict.

    force_mode:
        Chooses how q^(-3/2) is computed. float-force is easier to debug;
        lut-force is closer to FPGA hardware.

    lut_size:
        Number of samples in the lookup table used by lut-force.

    lut_q_max:
        Maximum q value covered by the LUT. Larger values cover a wider range
        but reduce resolution if lut_size is fixed.
    """

    max_steps: int = 5000
    r_settle: float = 0.10
    v_settle: float = 0.01
    min_consec: int = 3
    classify_on_timeout: bool = True
    force_mode: ForceMode = "float-force"
    lut_size: int = 4096
    lut_q_max: float = 64.0


@dataclass(frozen=True)
class RenderParams:
    """
    Parameters describing the output image and simulated x-y window.

    Each pixel corresponds to one initial condition:

        x(0)  = x0
        y(0)  = y0
        vx(0) = 0
        vy(0) = 0

    width and height define how many initial conditions are simulated. The
    x_min/x_max and y_min/y_max values define the physical coordinate range.

    For example, with width=256 and height=256, the program simulates:

        256 * 256 = 65536

    separate pendulum trajectories.

    FPGA relevance:
    In hardware, the accelerator will also need to convert a pixel index into
    an initial x0,y0 coordinate. The function make_initial_grid_fixed() models
    exactly that mapping.
    """

    x_min: float = -1.8
    x_max: float = 1.8
    y_min: float = -1.8
    y_max: float = 1.8
    width: int = 256
    height: int = 256


@dataclass(frozen=True)
class OutputParams:
    """
    Output file and plotting settings.

    out_dir:
        Folder where arrays, images and summary JSON will be saved.

    save_plots:
        If True, save PNG plots.

    show_plots:
        If True, display plots interactively. This is useful in a local Python
        session but usually less useful for batch runs.
    """

    out_dir: str = "pendulum_fixed_outputs"
    save_plots: bool = True
    show_plots: bool = False


@dataclass(frozen=True)
class ProjectConfig:
    """
    Top-level configuration object for one simulation run.

    This groups all settings into one object:

        cfg.physics
        cfg.sim
        cfg.render
        cfg.output
        cfg.magnet_positions
        cfg.colours

    Grouping these parameters is cleaner than using many unrelated global
    variables. It also makes it easier to pass the full configuration into
    functions and to save the full configuration into summary.json.

    magnet_positions:
        Tuple of (x,y) coordinates. The default three magnets form an
        equilateral triangle.

    colours:
        Mapping from basin label to RGB colour. Labels 0,1,2 correspond to the
        three magnets. Label -1 is used for unresolved pixels.
    """

    physics: PhysicsParams = field(default_factory=PhysicsParams)
    sim: SimulationParams = field(default_factory=SimulationParams)
    render: RenderParams = field(default_factory=RenderParams)
    output: OutputParams = field(default_factory=OutputParams)

    magnet_positions: Tuple[Tuple[float, float], ...] = (
        (1.0, 0.0),
        (-0.5, 0.8660254037844386),
        (-0.5, -0.8660254037844386),
    )

    colours: Mapping[int, Tuple[int, int, int]] = field(
        default_factory=lambda: {
            0: (220, 60, 60),
            1: (60, 180, 60),
            2: (60, 60, 220),
            -1: (30, 30, 30),
        }
    )


@dataclass(frozen=True)
class FixedConstants:
    """
    Fixed-point version of constants used inside the inner simulation loop.

    Why separate this from PhysicsParams?
    -------------------------------------
    PhysicsParams stores human-readable floating-point values. The inner loop
    should not repeatedly convert those values into fixed-point form because
    that would be inefficient and less hardware-like.

    FixedConstants stores the already-quantised raw integer versions:

        gamma, omega2, h2, mu, dt

    It also stores squared settling thresholds and fixed-point magnet
    coordinates.
    """

    gamma: int
    omega2: int
    h2: int
    mu: int
    dt: int
    r2_settle: int
    v2_settle: int
    magnets: Tuple[Tuple[int, int], ...]


def build_fixed_constants(fmt: FixedFormat, cfg: ProjectConfig) -> FixedConstants:
    """
    Convert floating-point configuration constants into fixed-point raw integers.

    This function should be called once before the main simulation loop.

    Example:
        cfg.physics.dt = 0.01
        fmt.frac_bits = 20

    Then:
        dt_raw = round(0.01 * 2^20)

    The returned FixedConstants object is what the datapath uses internally.
    """
    p = cfg.physics
    s = cfg.sim

    magnets = tuple((fmt.to_fixed(mx), fmt.to_fixed(my)) for mx, my in cfg.magnet_positions)

    return FixedConstants(
        gamma=fmt.to_fixed(p.gamma),
        omega2=fmt.to_fixed(p.omega2),
        h2=fmt.to_fixed(p.h2),
        mu=fmt.to_fixed(p.mu),
        dt=fmt.to_fixed(p.dt),
        r2_settle=fmt.to_fixed(s.r_settle * s.r_settle),
        v2_settle=fmt.to_fixed(s.v_settle * s.v_settle),
        magnets=magnets,
    )


# ============================================================
# Lookup table for q^(-3/2)
# ============================================================

@dataclass(frozen=True)
class InversePowerLUT:
    """
    Lookup table for approximating f(q) = q^(-3/2).

    q_min_raw:
        Smallest q value covered by the LUT, in fixed-point raw format.

    q_max_raw:
        Largest q value covered by the LUT, in fixed-point raw format.

    values_raw:
        Array of fixed-point raw values storing q^(-3/2) samples.

    Hardware interpretation:
    In the FPGA, this could be implemented using BRAM or distributed ROM. The
    address is derived from q, and the output is an approximate force scale.
    """

    q_min_raw: int
    q_max_raw: int
    values_raw: np.ndarray

    def lookup(self, q_raw: np.ndarray) -> np.ndarray:
        """
        Look up an approximate value of q^(-3/2).

        This version uses a very simple uniform LUT:

        1. Clip q to the LUT range.
        2. Convert q into an integer table index.
        3. Return the stored fixed-point value.

        There is no interpolation here. That is intentional for the first
        hardware-like model because a no-interpolation ROM is easy to implement
        in SystemVerilog. Later, interpolation can improve accuracy.
        """
        q_clip = np.clip(q_raw, self.q_min_raw, self.q_max_raw).astype(np.int64)
        numerator = (q_clip - self.q_min_raw) * (len(self.values_raw) - 1)
        denominator = max(1, self.q_max_raw - self.q_min_raw)
        idx = numerator // denominator
        return self.values_raw[idx]


def build_inverse_power_lut(fmt: FixedFormat, cfg: ProjectConfig) -> InversePowerLUT:
    """
    Build the LUT used to approximate q^(-3/2).

    q starts at h2 because:

        q = dx^2 + dy^2 + h2

    and dx^2 + dy^2 cannot be negative. Therefore the smallest physically
    possible q is h2.

    The LUT covers q from h2 to lut_q_max. Values larger than lut_q_max are
    clipped during lookup. This is a simple first version; later you could use a
    non-uniform LUT with more samples near small q, where q^(-3/2) changes most
    rapidly.
    """
    q_min = cfg.physics.h2
    q_max = cfg.sim.lut_q_max

    if q_max <= q_min:
        raise ValueError("lut_q_max must be larger than h2")

    q_values = np.linspace(q_min, q_max, cfg.sim.lut_size, dtype=np.float64)
    f_values = q_values ** (-1.5)

    return InversePowerLUT(
        q_min_raw=fmt.to_fixed(q_min),
        q_max_raw=fmt.to_fixed(q_max),
        values_raw=fmt.to_fixed(f_values),
    )


def inverse_q_power_fixed(
    fmt: FixedFormat,
    cfg: ProjectConfig,
    q_raw: np.ndarray,
    lut: Optional[InversePowerLUT],
) -> np.ndarray:
    """
    Compute or approximate q^(-3/2) and return it as fixed-point raw integers.

    Inputs
    ------
    q_raw:
        q = dx^2 + dy^2 + h2 in fixed-point raw format.

    lut:
        Lookup table object if force_mode is lut-force. It is None in
        float-force mode.

    Modes
    -----
    float-force:
        Convert q_raw back to float, compute q^(-1.5), then convert the result
        to fixed-point. This is not fully hardware-realistic, but it is useful
        for isolating the effect of fixed-point state updates.

    lut-force:
        Use a precomputed LUT. This is closer to FPGA implementation.
    """
    if cfg.sim.force_mode == "float-force":
        q_float = fmt.to_float(q_raw)
        f_float = q_float ** (-1.5)
        return fmt.to_fixed(f_float)

    if cfg.sim.force_mode == "lut-force":
        if lut is None:
            raise ValueError("lut-force selected but no LUT was provided")
        return lut.lookup(q_raw)

    raise ValueError(f"Unknown force mode: {cfg.sim.force_mode}")


# ============================================================
# Fixed-point pendulum physics
# ============================================================

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
    Compute acceleration ax, ay for all active pixels using fixed-point maths.

    Floating-point equation being approximated:

        ax = -gamma*vx - omega2*x + sum_i mu*(mx_i - x) / q_i^(3/2)
        ay = -gamma*vy - omega2*y + sum_i mu*(my_i - y) / q_i^(3/2)

    where:

        q_i = (mx_i - x)^2 + (my_i - y)^2 + h^2

    Important implementation details
    --------------------------------
    - x, y, vx and vy are arrays of raw fixed-point integers.
    - ax and ay are also returned as raw fixed-point integers.
    - Every multiply uses fx_mul(), which shifts the product back by frac_bits.
    - Every addition/subtraction uses saturation.

    FPGA relevance
    --------------
    This function is the software version of the hardware datapath. The future
    SystemVerilog design will likely implement the same sequence using
    multipliers, adders, registers, a LUT and an FSM.
    """
    # Start with damping and central restoring terms:
    #
    #     ax = -gamma*vx - omega2*x
    #     ay = -gamma*vy - omega2*y
    #
    # Each multiplication returns a fixed-point value. Then we negate and add.
    ax = fx_add(
        fmt,
        fx_neg(fmt, fx_mul(fmt, const.gamma, vx)),
        fx_neg(fmt, fx_mul(fmt, const.omega2, x)),
    )
    ay = fx_add(
        fmt,
        fx_neg(fmt, fx_mul(fmt, const.gamma, vy)),
        fx_neg(fmt, fx_mul(fmt, const.omega2, y)),
    )

    # Add the attraction from each magnet. The default model uses three magnets,
    # but this loop would also work for a different number of magnets.
    for mx_raw, my_raw in const.magnets:
        # Vector from current pendulum position to magnet position.
        dx = fx_sub(fmt, mx_raw, x)
        dy = fx_sub(fmt, my_raw, y)

        # Squared horizontal distance terms.
        dx2 = fx_mul(fmt, dx, dx)
        dy2 = fx_mul(fmt, dy, dy)

        # q = dx^2 + dy^2 + h^2.
        q = fx_add(fmt, fx_add(fmt, dx2, dy2), const.h2)

        # f approximates q^(-3/2). This is the most hardware-expensive part.
        f = inverse_q_power_fixed(fmt, cfg, q, lut)

        # Magnetic force contribution:
        #
        #     ax += mu * dx * f
        #     ay += mu * dy * f
        #
        # We do this as two fixed-point multiplications. In hardware, this could
        # be pipelined or time-multiplexed across magnets.
        ax = fx_add(fmt, ax, fx_mul(fmt, const.mu, fx_mul(fmt, dx, f)))
        ay = fx_add(fmt, ay, fx_mul(fmt, const.mu, fx_mul(fmt, dy, f)))

    return ax, ay


def nearest_magnet_fixed(
    fmt: FixedFormat,
    const: FixedConstants,
    x: np.ndarray,
    y: np.ndarray,
) -> Tuple[np.ndarray, np.ndarray]:
    """
    Find the nearest magnet for each pixel.

    This is used in two places:
    1. To check whether a pixel has settled near a magnet.
    2. To classify timeout pixels by nearest magnet if classify_on_timeout is
       enabled.

    Returns
    -------
    nearest:
        Integer array containing the index of the closest magnet.

    d2_min:
        Fixed-point raw squared distance to the nearest magnet.
    """
    d2_all = []

    for mx_raw, my_raw in const.magnets:
        dx = fx_sub(fmt, mx_raw, x)
        dy = fx_sub(fmt, my_raw, y)
        d2 = fx_add(fmt, fx_mul(fmt, dx, dx), fx_mul(fmt, dy, dy))
        d2_all.append(d2)

    # Shape after stacking: number_of_magnets x height x width.
    d2_stack = np.stack(d2_all, axis=0)

    # argmin over axis 0 chooses the magnet index with minimum distance for
    # every pixel.
    nearest = np.argmin(d2_stack, axis=0).astype(np.int16)
    d2_min = np.min(d2_stack, axis=0).astype(np.int64)
    return nearest, d2_min


# ============================================================
# Grid generation and full-frame simulation
# ============================================================

def make_initial_grid_fixed(fmt: FixedFormat, render: RenderParams) -> Tuple[np.ndarray, np.ndarray]:
    """
    Create fixed-point initial x and y coordinate grids.

    The image-plane mapping is:

        x0 = x_min + col * x_step
        y0 = y_min + row * y_step

    where:

        x_step = (x_max - x_min) / (width  - 1)
        y_step = (y_max - y_min) / (height - 1)

    Each pixel therefore represents one initial pendulum displacement.

    FPGA relevance
    --------------
    The hardware accelerator will probably contain counters for row and column.
    It will use those counters to generate x0 and y0. This function is the
    Python version of that pixel-to-coordinate mapping.
    """
    if render.width <= 0 or render.height <= 0:
        raise ValueError("width and height must be positive")

    # Special case: if there is only one pixel in a dimension, place it at the
    # centre of the requested coordinate interval.
    if render.width == 1:
        x_step = 0.0
        x_start = 0.5 * (render.x_min + render.x_max)
    else:
        x_step = (render.x_max - render.x_min) / (render.width - 1)
        x_start = render.x_min

    if render.height == 1:
        y_step = 0.0
        y_start = 0.5 * (render.y_min + render.y_max)
    else:
        y_step = (render.y_max - render.y_min) / (render.height - 1)
        y_start = render.y_min

    # Convert start values and step sizes into fixed-point constants.
    x_start_raw = fmt.to_fixed(x_start)
    y_start_raw = fmt.to_fixed(y_start)
    x_step_raw = fmt.to_fixed(x_step)
    y_step_raw = fmt.to_fixed(y_step)

    # Pixel column and row indices.
    cols = np.arange(render.width, dtype=np.int64)
    rows = np.arange(render.height, dtype=np.int64)

    # Hardware-like coordinate generation:
    #
    #     x[col] = x_start + col * x_step
    #     y[row] = y_start + row * y_step
    xs = fmt.saturate(x_start_raw + cols * x_step_raw)
    ys = fmt.saturate(y_start_raw + rows * y_step_raw)

    # meshgrid expands one row of x values and one column of y values into full
    # 2D arrays matching the image size.
    x_grid, y_grid = np.meshgrid(xs, ys)
    return x_grid.astype(np.int64), y_grid.astype(np.int64)


@dataclass
class BasinResult:
    """
    Container for the output of one full-frame simulation.

    basin:
        Integer label image. Each pixel is 0, 1, 2, ... depending on which
        magnet it was classified to. -1 means unresolved.

    steps:
        Number of integration steps taken before settling/classification.

    settled:
        Boolean mask. True means the pixel met the strict settling rule. False
        means it was classified only after timeout, or remained unresolved.

    elapsed_s:
        Runtime in seconds.
    """

    basin: np.ndarray
    steps: np.ndarray
    settled: np.ndarray
    elapsed_s: float


def generate_basin_map_fixed(fmt: FixedFormat, cfg: ProjectConfig) -> BasinResult:
    """
    Generate the full fixed-point magnetic pendulum basin map.

    High-level algorithm
    --------------------
    1. Convert constants to fixed-point.
    2. Build the initial x,y grid, one initial condition per pixel.
    3. Set all initial velocities to zero.
    4. Repeatedly update acceleration, velocity and position.
    5. Check which pixels have settled.
    6. Stop when all pixels are done or max_steps is reached.
    7. Classify remaining active pixels by nearest magnet if enabled.

    This function is vectorised with NumPy, meaning it processes many pixels at
    once. The hardware implementation will instead process pixels through a
    datapath, possibly one or several pixels at a time.
    """
    const = build_fixed_constants(fmt, cfg)
    lut = build_inverse_power_lut(fmt, cfg) if cfg.sim.force_mode == "lut-force" else None

    render = cfg.render
    sim = cfg.sim

    # Initial positions are the pixel coordinate grid. Initial velocities are
    # zero because each pendulum starts from rest.
    x, y = make_initial_grid_fixed(fmt, render)
    vx = np.zeros_like(x, dtype=np.int64)
    vy = np.zeros_like(y, dtype=np.int64)

    # Output arrays.
    basin = np.full((render.height, render.width), -1, dtype=np.int16)
    steps = np.zeros((render.height, render.width), dtype=np.int32)
    settled = np.zeros((render.height, render.width), dtype=bool)

    # active tracks pixels that still need to be simulated. Once a pixel settles,
    # we stop updating it.
    active = np.ones((render.height, render.width), dtype=bool)

    # consec counts how many consecutive iterations each pixel has satisfied the
    # settling condition.
    consec = np.zeros((render.height, render.width), dtype=np.int16)

    t0 = time.time()

    for step in range(1, sim.max_steps + 1):
        # Compute acceleration using the current state.
        ax, ay = acceleration_fixed(fmt, cfg, const, x, y, vx, vy, lut)

        # Semi-implicit Euler update:
        #
        #     v[n+1] = v[n] + dt*a[n]
        #     x[n+1] = x[n] + dt*v[n+1]
        #
        # This is simple and hardware-friendly because it is mainly multiply-add
        # logic. We first compute new candidate values for every pixel, then
        # write them only into active pixels.
        vx_new = fx_add(fmt, vx, fx_mul(fmt, const.dt, ax))
        vy_new = fx_add(fmt, vy, fx_mul(fmt, const.dt, ay))

        vx[active] = vx_new[active]
        vy[active] = vy_new[active]

        x_new = fx_add(fmt, x, fx_mul(fmt, const.dt, vx))
        y_new = fx_add(fmt, y, fx_mul(fmt, const.dt, vy))

        x[active] = x_new[active]
        y[active] = y_new[active]

        # Check nearest magnet and speed after the update.
        nearest, d2_min = nearest_magnet_fixed(fmt, const, x, y)
        speed2 = fx_add(fmt, fx_mul(fmt, vx, vx), fx_mul(fmt, vy, vy))

        # A pixel is considered settled if it is near a magnet and slow enough.
        settle_now = active & (d2_min < const.r2_settle) & (speed2 < const.v2_settle)

        # Only consecutive settling counts. If a pixel fails the condition, its
        # consecutive counter is reset to zero.
        consec[settle_now] += 1
        consec[active & ~settle_now] = 0

        # A pixel is finished only after min_consec consecutive settled checks.
        done_now = active & (consec >= sim.min_consec)
        if np.any(done_now):
            basin[done_now] = nearest[done_now]
            steps[done_now] = step
            settled[done_now] = True
            active[done_now] = False

        # Print occasional progress updates. This is useful for large images.
        if step == 1 or step % 500 == 0 or step == sim.max_steps or not np.any(active):
            elapsed = time.time() - t0
            active_count = int(np.sum(active))
            print(
                f"step {step:5d}/{sim.max_steps} | "
                f"active pixels {active_count:7d} | "
                f"elapsed {elapsed:6.1f}s"
            )

        if not np.any(active):
            break

    # Handle pixels that did not strictly settle before max_steps.
    if np.any(active):
        nearest, _ = nearest_magnet_fixed(fmt, const, x, y)
        if sim.classify_on_timeout:
            basin[active] = nearest[active]
        steps[active] = sim.max_steps

    elapsed_s = time.time() - t0
    return BasinResult(basin=basin, steps=steps, settled=settled, elapsed_s=elapsed_s)


# ============================================================
# Plotting, saving, and comparison helpers
# ============================================================

def basin_to_rgb(cfg: ProjectConfig, basin: np.ndarray, settled: Optional[np.ndarray] = None) -> np.ndarray:
    """
    Convert basin labels into an RGB image.

    Example mapping by default:

        0  -> red-ish
        1  -> green-ish
        2  -> blue-ish
        -1 -> dark grey

    If a settled mask is supplied, timeout-classified pixels are darkened. This
    lets us visually distinguish strict physical settling from forced nearest-
    magnet classification.
    """
    h, w = basin.shape
    img = np.zeros((h, w, 3), dtype=np.uint8)

    for label, colour in cfg.colours.items():
        img[basin == label] = np.array(colour, dtype=np.uint8)

    if settled is not None:
        timeout = ~settled
        img[timeout] = (0.45 * img[timeout]).astype(np.uint8)

    return img


def plot_basin(cfg: ProjectConfig, result: BasinResult, out_dir: Path) -> None:
    """
    Save the fixed-point basin image.

    This creates two outputs:
    1. fixed_basin_annotated.png: with axes, title and magnet markers.
    2. fixed_basin.png: raw RGB image without axes, easier for comparison or UI.
    """
    img = basin_to_rgb(cfg, result.basin, result.settled)
    r = cfg.render

    plt.figure(figsize=(8, 8))
    plt.imshow(
        img,
        origin="lower",
        extent=[r.x_min, r.x_max, r.y_min, r.y_max],
        interpolation="nearest",
    )

    for i, (mx, my) in enumerate(cfg.magnet_positions):
        plt.plot(mx, my, "w+", markersize=12, markeredgewidth=2)
        plt.text(mx + 0.08, my + 0.08, f"M{i}", color="white", fontsize=9)

    plt.title(f"Fixed-point magnetic pendulum basin ({r.width}x{r.height})")
    plt.xlabel("initial x")
    plt.ylabel("initial y")
    plt.tight_layout()
    plt.savefig(out_dir / "fixed_basin_annotated.png", dpi=200, bbox_inches="tight")

    if cfg.output.show_plots:
        plt.show()
    else:
        plt.close()

    plt.imsave(out_dir / "fixed_basin.png", img, origin="lower")


def plot_steps(cfg: ProjectConfig, result: BasinResult, out_dir: Path) -> None:
    """
    Save a heatmap showing how many steps each pixel needed.

    This is useful because difficult boundary regions often take longer to
    settle. In the final report, this plot helps justify why the accelerator is
    computationally intensive.
    """
    r = cfg.render

    plt.figure(figsize=(6, 6))
    plt.imshow(
        result.steps,
        origin="lower",
        extent=[r.x_min, r.x_max, r.y_min, r.y_max],
        cmap="inferno",
    )
    plt.colorbar(label="steps")
    plt.title(f"Fixed-point steps to settle/classify ({r.width}x{r.height})")
    plt.xlabel("initial x")
    plt.ylabel("initial y")
    plt.tight_layout()
    plt.savefig(out_dir / "fixed_steps.png", dpi=200, bbox_inches="tight")

    if cfg.output.show_plots:
        plt.show()
    else:
        plt.close()


def plot_histogram(result: BasinResult, out_dir: Path) -> None:
    """
    Save a histogram of settling/classification step counts.

    The 99th percentile is drawn because it is a useful robust statistic. The
    maximum may be dominated by timeout pixels, while the 99th percentile gives
    a better sense of typical hard cases.
    """
    flat = result.steps.ravel()
    p99 = int(np.percentile(flat, 99))

    plt.figure(figsize=(8, 4))
    plt.hist(flat, bins=60, edgecolor="white")
    plt.axvline(p99, linestyle="--", label=f"99th percentile = {p99}")
    plt.xlabel("steps")
    plt.ylabel("pixel count")
    plt.title("Fixed-point settling/classification time distribution")
    plt.legend()
    plt.tight_layout()
    plt.savefig(out_dir / "fixed_histogram.png", dpi=200, bbox_inches="tight")
    plt.close()


def pixel_match_rate(
    ref_basin: np.ndarray,
    test_basin: np.ndarray,
    mask: Optional[np.ndarray] = None,
) -> Tuple[float, int, int]:
    """
    Compute pixel-by-pixel classification agreement between two basin maps.

    Parameters
    ----------
    ref_basin:
        Reference basin labels, usually from the floating-point golden model.

    test_basin:
        Basin labels from the fixed-point model or later from hardware.

    mask:
        Optional boolean mask specifying which pixels should be compared. For
        example, you may want to compare only pixels that strictly settled in
        both models.

    Returns
    -------
    match_rate:
        Fraction of compared pixels with the same label.

    matches:
        Number of matching pixels.

    compared:
        Number of pixels included in the comparison.
    """
    if ref_basin.shape != test_basin.shape:
        raise ValueError(f"shape mismatch: ref {ref_basin.shape}, test {test_basin.shape}")

    if mask is None:
        mask = np.ones_like(ref_basin, dtype=bool)

    compared = int(np.sum(mask))
    if compared == 0:
        return 0.0, 0, 0

    matches = int(np.sum((ref_basin == test_basin) & mask))
    return matches / compared, matches, compared


def compare_against_reference(
    cfg: ProjectConfig,
    result: BasinResult,
    ref_basin_path: Optional[str],
    ref_settled_path: Optional[str],
    out_dir: Path,
) -> dict:
    """
    Compare fixed-point output against saved floating-point reference arrays.

    This function is optional. If no reference path is supplied, it returns an
    empty dictionary.

    It computes:
    - all-pixel match rate
    - optional strict-settled-only match rate
    - a mismatch image

    Interpretation warning
    ----------------------
    Perfect agreement is not expected near basin boundaries. The magnetic
    pendulum is sensitive to small numerical changes. A good fixed-point design
    should match most stable interior regions, while mismatches concentrated on
    boundaries are usually acceptable and explainable.
    """
    if ref_basin_path is None:
        return {}

    ref_basin = np.load(ref_basin_path)

    all_rate, all_matches, all_total = pixel_match_rate(ref_basin, result.basin)
    summary = {
        "pixel_match_rate_all": all_rate,
        "pixel_matches_all": all_matches,
        "pixel_compared_all": all_total,
    }

    print("\nComparison against floating-point reference:")
    print(f"  all pixels       : {100*all_rate:6.2f}% ({all_matches}/{all_total})")

    if ref_settled_path is not None and Path(ref_settled_path).exists():
        ref_settled = np.load(ref_settled_path)
        strict_mask = ref_settled & result.settled
        strict_rate, strict_matches, strict_total = pixel_match_rate(ref_basin, result.basin, strict_mask)
        summary.update(
            {
                "pixel_match_rate_strict_settled_only": strict_rate,
                "pixel_matches_strict_settled_only": strict_matches,
                "pixel_compared_strict_settled_only": strict_total,
            }
        )
        print(
            f"  strict settled   : {100*strict_rate:6.2f}% "
            f"({strict_matches}/{strict_total})"
        )

    # Save a visual mismatch map. With gray_r, matching pixels are white and
    # mismatching pixels are black. Boundary-dominated mismatches are expected.
    mismatch = ref_basin != result.basin
    plt.figure(figsize=(6, 6))
    plt.imshow(mismatch, origin="lower", cmap="gray_r", interpolation="nearest")
    plt.title("Mismatch map: fixed-point vs floating-point")
    plt.xlabel("pixel column")
    plt.ylabel("pixel row")
    plt.tight_layout()
    plt.savefig(out_dir / "fixed_vs_float_mismatch.png", dpi=200, bbox_inches="tight")
    plt.close()

    return summary


def save_outputs(
    fmt: FixedFormat,
    cfg: ProjectConfig,
    result: BasinResult,
    comparison_summary: dict,
) -> None:
    """
    Save all numerical arrays, plots and summary information.

    Numerical arrays:
    - fixed_basin.npy
    - fixed_steps.npy
    - fixed_settled.npy
    - fixed_all_outputs.npz

    Plots:
    - fixed_basin.png
    - fixed_basin_annotated.png
    - fixed_steps.png
    - fixed_histogram.png

    Summary:
    - summary.json

    The JSON summary is especially useful for your report because it records the
    fixed-point format, runtime, step statistics, settling fraction, basin
    counts and comparison metrics.
    """
    out_dir = Path(cfg.output.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    np.save(out_dir / "fixed_basin.npy", result.basin)
    np.save(out_dir / "fixed_steps.npy", result.steps)
    np.save(out_dir / "fixed_settled.npy", result.settled)
    np.savez_compressed(
        out_dir / "fixed_all_outputs.npz",
        basin=result.basin,
        steps=result.steps,
        settled=result.settled,
    )

    if cfg.output.save_plots:
        plot_basin(cfg, result, out_dir)
        plot_steps(cfg, result, out_dir)
        plot_histogram(result, out_dir)

    flat_steps = result.steps.ravel()
    summary = {
        "fixed_format": asdict(fmt),
        "config": asdict(cfg),
        "elapsed_s": result.elapsed_s,
        "pixels": int(cfg.render.width * cfg.render.height),
        "pixels_per_second": float((cfg.render.width * cfg.render.height) / result.elapsed_s),
        "mean_steps": float(np.mean(flat_steps)),
        "median_steps": float(np.median(flat_steps)),
        "p99_steps": int(np.percentile(flat_steps, 99)),
        "max_steps_observed": int(np.max(flat_steps)),
        "strict_settled_pixels": int(np.sum(result.settled)),
        "strict_settled_fraction": float(np.mean(result.settled)),
        "basin_counts": {
            str(label): int(np.sum(result.basin == label))
            for label in sorted(set(result.basin.ravel().tolist()))
        },
        "comparison": comparison_summary,
    }

    with open(out_dir / "summary.json", "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)

    print(f"\nSaved outputs in: {out_dir.resolve()}")
    print("  fixed_basin.png")
    print("  fixed_basin_annotated.png")
    print("  fixed_steps.png")
    print("  fixed_histogram.png")
    print("  fixed_basin.npy")
    print("  fixed_steps.npy")
    print("  fixed_settled.npy")
    print("  summary.json")


# ============================================================
# Command-line interface
# ============================================================

def parse_args() -> argparse.Namespace:
    """
    Define and parse command-line options.

    This lets you run experiments without editing the source code every time.
    For example:

        --width 512 --height 512
        --frac-bits 18
        --force-mode lut-force

    The defaults are taken from the dataclasses so that the default values have
    a single source of truth.
    """
    default_fmt = FixedFormat()
    default_physics = PhysicsParams()
    default_sim = SimulationParams()
    default_render = RenderParams()
    default_output = OutputParams()

    parser = argparse.ArgumentParser(
        description="Fixed-point magnetic pendulum basin-map simulator."
    )

    # Rendering / image-plane options.
    parser.add_argument("--width", type=int, default=default_render.width)
    parser.add_argument("--height", type=int, default=default_render.height)
    parser.add_argument("--x-min", type=float, default=default_render.x_min)
    parser.add_argument("--x-max", type=float, default=default_render.x_max)
    parser.add_argument("--y-min", type=float, default=default_render.y_min)
    parser.add_argument("--y-max", type=float, default=default_render.y_max)

    # Physical model options.
    parser.add_argument("--gamma", type=float, default=default_physics.gamma)
    parser.add_argument("--omega2", type=float, default=default_physics.omega2)
    parser.add_argument("--h2", type=float, default=default_physics.h2)
    parser.add_argument("--mu", type=float, default=default_physics.mu)
    parser.add_argument("--dt", type=float, default=default_physics.dt)

    # Simulation / stopping-rule options.
    parser.add_argument("--max-steps", type=int, default=default_sim.max_steps)
    parser.add_argument("--r-settle", type=float, default=default_sim.r_settle)
    parser.add_argument("--v-settle", type=float, default=default_sim.v_settle)
    parser.add_argument("--min-consec", type=int, default=default_sim.min_consec)
    parser.add_argument("--no-classify-on-timeout", action="store_true")

    # Fixed-point format options.
    parser.add_argument("--total-bits", type=int, default=default_fmt.total_bits)
    parser.add_argument("--frac-bits", type=int, default=default_fmt.frac_bits)

    # Force approximation options.
    parser.add_argument(
        "--force-mode",
        choices=["float-force", "lut-force"],
        default=default_sim.force_mode,
        help="How to approximate q^(-3/2).",
    )
    parser.add_argument("--lut-size", type=int, default=default_sim.lut_size)
    parser.add_argument("--lut-q-max", type=float, default=default_sim.lut_q_max)

    # Output options.
    parser.add_argument("--out-dir", type=str, default=default_output.out_dir)
    parser.add_argument("--show-plots", action="store_true")
    parser.add_argument("--no-save-plots", action="store_true")

    # Optional comparison against floating-point reference outputs.
    parser.add_argument(
        "--ref-basin",
        type=str,
        default=None,
        help="Optional path to floating-point basin .npy file for comparison.",
    )
    parser.add_argument(
        "--ref-settled",
        type=str,
        default=None,
        help="Optional path to floating-point settled .npy file for strict-settled comparison.",
    )

    return parser.parse_args()


def build_config_from_args(args: argparse.Namespace) -> Tuple[FixedFormat, ProjectConfig]:
    """
    Convert parsed command-line arguments into FixedFormat and ProjectConfig.

    Separating this from parse_args() is useful because parse_args() only knows
    about strings and terminal options, while the rest of the program wants
    structured dataclass objects.
    """
    fmt = FixedFormat(total_bits=args.total_bits, frac_bits=args.frac_bits)

    physics = PhysicsParams(
        gamma=args.gamma,
        omega2=args.omega2,
        h2=args.h2,
        mu=args.mu,
        dt=args.dt,
    )

    sim = SimulationParams(
        max_steps=args.max_steps,
        r_settle=args.r_settle,
        v_settle=args.v_settle,
        min_consec=args.min_consec,
        classify_on_timeout=not args.no_classify_on_timeout,
        force_mode=args.force_mode,
        lut_size=args.lut_size,
        lut_q_max=args.lut_q_max,
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
        save_plots=not args.no_save_plots,
        show_plots=args.show_plots,
    )

    cfg = ProjectConfig(physics=physics, sim=sim, render=render, output=output)
    return fmt, cfg


def print_run_header(fmt: FixedFormat, cfg: ProjectConfig) -> None:
    """
    Print a readable summary of the run before simulation starts.

    This helps prevent mistakes such as accidentally running 256x256 when you
    meant 512x512, or using float-force when you meant lut-force.
    """
    r = cfg.render
    p = cfg.physics
    s = cfg.sim

    integer_bits_excluding_sign = fmt.total_bits - fmt.frac_bits - 1

    print("Running fixed-point magnetic pendulum model")
    print(f"resolution          : {r.width} x {r.height}")
    print(f"fixed format        : signed Q{integer_bits_excluding_sign}.{fmt.frac_bits}")
    print(f"raw range           : [{fmt.min_raw}, {fmt.max_raw}]")
    print(f"force mode          : {s.force_mode}")
    print(f"gamma, omega2, mu   : {p.gamma}, {p.omega2}, {p.mu}")
    print(f"dt, max_steps       : {p.dt}, {s.max_steps}")
    print(f"settling thresholds : r={s.r_settle}, v={s.v_settle}\n")


def main() -> None:
    """
    Program entry point.

    Execution order:
    1. Read command-line arguments.
    2. Build fixed-point format and project configuration.
    3. Generate the fixed-point basin map.
    4. Optionally compare with floating-point reference outputs.
    5. Save arrays, images and summary JSON.
    """
    args = parse_args()
    fmt, cfg = build_config_from_args(args)

    print_run_header(fmt, cfg)

    result = generate_basin_map_fixed(fmt, cfg)

    print(f"\nDone in {result.elapsed_s:.2f}s")
    print(f"Pixels per second: {(cfg.render.width * cfg.render.height) / result.elapsed_s:.1f}")
    print(f"Average time per pixel: {1000 * result.elapsed_s / (cfg.render.width * cfg.render.height):.3f} ms")
    print(f"Strict settled pixels: {np.sum(result.settled)} / {cfg.render.width * cfg.render.height}")

    out_dir = Path(cfg.output.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    comparison_summary = compare_against_reference(
        cfg=cfg,
        result=result,
        ref_basin_path=args.ref_basin,
        ref_settled_path=args.ref_settled,
        out_dir=out_dir,
    )

    save_outputs(fmt, cfg, result, comparison_summary)


if __name__ == "__main__":
    main()
