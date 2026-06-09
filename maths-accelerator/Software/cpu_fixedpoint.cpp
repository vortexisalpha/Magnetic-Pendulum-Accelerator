
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>
#include <filesystem> 

// FixedFormat
// Maps to: Python class FixedFormat (dataclass, frozen=True)
//
// Describes the signed Q-format fixed-point representation.
//
//   real_value = raw_integer / 2^frac_bits
//
// frozen=True in Python is reproduced by making all fields const
// and only setting them in the constructor.

struct FixedFormat {
    const int     total_bits;
    const int     frac_bits;
    const int64_t scale;    // 2^frac_bits  -- Python: self.scale
    const int64_t min_raw;  // -(2^(total_bits-1))  -- Python: self.min_raw
    const int64_t max_raw;  // 2^(total_bits-1)-1   -- Python: self.max_raw

    // Python: __post_init__ validates the format
    FixedFormat(int total_bits_, int frac_bits_)
        : total_bits(total_bits_)
        , frac_bits(frac_bits_)
        , scale(int64_t(1) << frac_bits_)
        , min_raw(-(int64_t(1) << (total_bits_ - 1)))
        , max_raw( (int64_t(1) << (total_bits_ - 1)) - 1)
    {
        if (total_bits < 2)
            throw std::invalid_argument("total_bits must be at least 2");
        if (frac_bits < 0)
            throw std::invalid_argument("frac_bits must be non-negative");
        if (frac_bits >= total_bits - 1)
            throw std::invalid_argument(
                "frac_bits must leave at least one sign/integer bit");
    }

    // Python: def saturate(self, raw)
    // Clip a single raw int64 into [min_raw, max_raw].
    // Saturation chosen over wraparound — matches Python model.
    int64_t saturate(int64_t raw) const {
        if (raw > max_raw) return max_raw;
        if (raw < min_raw) return min_raw;
        return raw;
    }

    // Python: def to_fixed(self, value)
    // Convert real float -> fixed-point raw integer.
    // Steps: multiply by scale, round to nearest, saturate.
    int64_t to_fixed(double value) const {
        double  scaled = value * static_cast<double>(scale);
        int64_t raw    = static_cast<int64_t>(std::round(scaled));
        return saturate(raw);
    }

    // Python: def to_float(self, raw)
    // Convert fixed-point raw integer back to float.
    // Used for float-force mode and LUT generation.
    double to_float(int64_t raw) const {
        return static_cast<double>(raw) / static_cast<double>(scale);
    }
};

// Fixed-point arithmetic helpers
// Maps to: Python functions fx_add, fx_sub, fx_neg, fx_mul
//
// These are kept as free functions (not methods) to match the
// Python style exactly. Each mirrors one hardware operation.

// Python: def fx_add(fmt, a, b)
// raw_result = raw_a + raw_b, then saturate.
// Addition of two same-scale fixed-point values needs no rescaling.
inline int64_t fx_add(const FixedFormat& fmt, int64_t a, int64_t b) {
    return fmt.saturate(a + b);
}

// Python: def fx_sub(fmt, a, b)
// raw_result = raw_a - raw_b, then saturate.
inline int64_t fx_sub(const FixedFormat& fmt, int64_t a, int64_t b) {
    return fmt.saturate(a - b);
}

// Python: def fx_neg(fmt, a)
// raw_result = -raw_a, then saturate.
// Used for -gamma*vx and -omega2*x terms.
inline int64_t fx_neg(const FixedFormat& fmt, int64_t a) {
    return fmt.saturate(-a);
}

// Python: def fx_mul(fmt, a, b)
//
// real_a = raw_a / 2^F
// real_b = raw_b / 2^F
// true product = (raw_a * raw_b) / 2^(2F)
//
// To keep the result in Q-format (F fractional bits):
//   raw_result = (raw_a * raw_b) >> F
//
// __int128 prevents overflow when both operands are large int64_t.
// Python uses arbitrary-precision integers, so no overflow there.
inline int64_t fx_mul(const FixedFormat& fmt, int64_t a, int64_t b) {
    int64_t product = a * b; 
    int64_t shifted = static_cast<int64_t>(product >> fmt.frac_bits); 
    return fmt.saturate(shifted);
}

// Three-input saturating add — mirrors fx_adder_s6.sv (a + b + c, saturate).
// Used for the per-axis sum of the three magnet contributions in S6a.
inline int64_t fx_add3(const FixedFormat& fmt, int64_t a, int64_t b, int64_t c) {
    return fmt.saturate(a + b + c);
}

// Three-input saturating subtract — mirrors fx_sub_three_input.sv (a - b - c).
// Used in S6e: ax = mu*sum_dxf - gamma*vx - omega2*x.
inline int64_t fx_sub3(const FixedFormat& fmt, int64_t a, int64_t b, int64_t c) {
    return fmt.saturate(a - b - c);
}

// Hardware q-format constants. The RTL fixes these (lut_bram.sv / fx_adder_s3.sv):
//   main datapath : Q4.14  (W=18, F=14)
//   q value       : Q5.13  (18-bit unsigned, range [0,32))
// These are intentionally hard-coded because the LUT addressing and the
// Q4.14->Q5.13 conversion below only make sense at the synthesised widths.
namespace hw {
    constexpr int     W          = 18;
    constexpr int     F          = 14;          // Q4.14 fractional bits
    constexpr int64_t Q_SAT      = (int64_t(1) << W) - 1;   // 0x3FFFF, 18-bit unsigned max
    constexpr int     LUT_SIZE   = 4096;
    constexpr int     LUT_SHIFT  = 6;           // idx = q_raw[17:6] -> q_raw >> 6
    constexpr int64_t LUT_SAT    = 0x1FFFF;     // gen_lut.py SAT_MAX (Q4.14 output)
    constexpr double  Q_MAX_REAL = 32.0;        // LUT covers q in [0, 32)
}

// hw_build_q
// Mirrors fx_adder_s3.sv exactly.
//
//   sum_q414 = dx2 + dy2 + h2          (non-negative Q4.14, up to 20 bits)
//   q_q513   = sum_q414 >> 1           (Q4.14 -> Q5.13, 19-bit)
//   q        = (q_q513[18]) ? 0x3FFFF : q_q513[17:0]   (saturate to 18-bit)
//
// Returns an unsigned Q5.13 value (range [0, 32)) used both for the LUT
// address and for the nearest-magnet / settle comparisons.
inline int64_t hw_build_q(int64_t dx2, int64_t dy2, int64_t h2) {
    uint64_t sum = static_cast<uint64_t>(dx2) +
                   static_cast<uint64_t>(dy2) +
                   static_cast<uint64_t>(h2);     // Q4.14
    uint64_t q19 = sum >> 1;                       // Q5.13, 19-bit
    if (q19 & (uint64_t(1) << 18))                 // bit 18 set -> overflow
        return hw::Q_SAT;
    return static_cast<int64_t>(q19 & 0x3FFFF);    // low 18 bits
}

// PhysicsParams
// Maps to: Python dataclass PhysicsParams (frozen=True)
//
// Human-readable floating-point physical parameters.
// Converted to fixed-point once before the simulation loop.

struct PhysicsParams {
    double gamma  = 0.20;   // damping coefficient
    double omega2 = 1.0;    // central restoring force coefficient
    double h2     = 0.25;   // squared vertical separation (prevents singularity)
    double mu     = 1.0;    // magnet strength scaling
    double dt     = 0.01;   // integration timestep
};

// SimulationParams
// Maps to: Python dataclass SimulationParams (frozen=True)

struct SimulationParams {
    int    max_steps           = 5000;
    double r_settle            = 0.10;
    double v_settle            = 0.01;
    int    min_consec          = 3;      // consecutive settle checks required
    bool   classify_on_timeout = true;   // classify by nearest magnet on timeout
    bool   use_lut             = false;  // false=float-force, true=lut-force
    int    lut_size            = 4096;
    double lut_q_max           = 64.0;
};

// RenderParams
// Maps to: Python dataclass RenderParams (frozen=True)

struct RenderParams {
    double x_min   = -1.8;
    double x_max   =  1.8;
    double y_min   = -1.8;
    double y_max   =  1.8;
    int    width   = 160;
    int    height  = 120;
};

// Magnet positions
// Maps to: ProjectConfig.magnet_positions
//
// Default: equilateral triangle, matching Python defaults exactly.

struct MagnetPos { double x, y; };

const std::vector<MagnetPos> DEFAULT_MAGNETS = {
    {  1.0,  0.0               },
    { -0.5,  0.8660254037844386 },
    { -0.5, -0.8660254037844386 },
};

// FixedConstants
// Maps to: Python dataclass FixedConstants (frozen=True)
//
// Pre-converted fixed-point versions of physics constants.
// Built once before the inner loop — avoids repeated conversion.


struct FixedConstants {
    int64_t gamma;
    int64_t omega2;
    int64_t h2;
    int64_t mu;
    int64_t dt;
    // Settle thresholds — match the AXI register inputs to settle_check_s3.sv:
    //   sum_r_settle_sq_h_sq : r_settle^2 + h^2, in Q5.13 (compared against min_q)
    //   v_settle             : v_settle, in Q4.14 (compared against |vx| and |vy|)
    int64_t sum_r_settle_sq_h_sq;
    int64_t v_settle;

    struct FixedMagnet { int64_t x, y; };
    std::vector<FixedMagnet> magnets;
};

// Python: def build_fixed_constants(fmt, cfg)
FixedConstants build_fixed_constants(
    const FixedFormat&          fmt,
    const PhysicsParams&        phys,
    const SimulationParams&     sim,
    const std::vector<MagnetPos>& magnets)
{
    FixedConstants c;
    c.gamma  = fmt.to_fixed(phys.gamma);
    c.omega2 = fmt.to_fixed(phys.omega2);
    c.h2     = fmt.to_fixed(phys.h2);
    c.mu     = fmt.to_fixed(phys.mu);
    c.dt     = fmt.to_fixed(phys.dt);

    // v_settle in Q4.14, same format as |vx|, |vy| inside the lane.
    c.v_settle = fmt.to_fixed(sim.v_settle);

    // sum_r_settle_sq_h_sq in Q5.13 (q-format), compared directly against min_q.
    // Software pre-computes r_settle^2 + h^2 and writes it to the AXI register.
    {
        double  thr_real = sim.r_settle * sim.r_settle + phys.h2;
        int64_t q_scale  = int64_t(1) << (hw::F - 1);        // 2^13 (Q5.13)
        int64_t raw      = static_cast<int64_t>(std::llround(thr_real * q_scale));
        if (raw < 0)            raw = 0;
        if (raw > hw::Q_SAT)    raw = hw::Q_SAT;
        c.sum_r_settle_sq_h_sq = raw;
    }

    for (const auto& m : magnets)
        c.magnets.push_back({ fmt.to_fixed(m.x), fmt.to_fixed(m.y) });

    return c;
}

// InversePowerLUT
// Mirrors the hardware BRAM (lut_bram.sv) loaded from gen_lut.py output.
//
// The table stores f(q) = q^(-3/2) for q in [0, 32), sampled at 4096 points
// with Q_PER_INDEX = 32/4096. Values are Q4.14, saturated at 0x1FFFF.
// Lookup uses the same address decode as the RTL: idx = q_raw[17:6].

struct InversePowerLUT {
    std::vector<int64_t> values_raw;   // Q4.14, hw::LUT_SIZE entries

    // lut_bram.sv: idx = (|addr[19:18]) ? 0xFFF : addr[17:6]
    // q is an 18-bit Q5.13 value, so addr[19:18] is always 0 and
    //   idx = q_raw >> 6   (clamped to [0, LUT_SIZE-1] for safety).
    int64_t lookup(int64_t q_raw) const {
        int64_t idx = q_raw >> hw::LUT_SHIFT;
        if (idx < 0) idx = 0;
        if (idx >= static_cast<int64_t>(values_raw.size()))
            idx = static_cast<int64_t>(values_raw.size()) - 1;
        return values_raw[static_cast<size_t>(idx)];
    }
};

// build_inverse_power_lut_hw
// Reproduces gen_lut.py exactly:
//
//   for i in range(LUT_SIZE):
//       q = i * (32 / LUT_SIZE)
//       val = 0x1FFFF                 if q == 0
//           = round(q^-1.5 * 2^F)     otherwise, saturated to 0x1FFFF
//
// The resulting table is identical to qinv32.mem used by the FPGA.
InversePowerLUT build_inverse_power_lut_hw() {
    const double scale = static_cast<double>(int64_t(1) << hw::F);  // 2^14
    const double q_per = hw::Q_MAX_REAL / hw::LUT_SIZE;             // 32/4096

    InversePowerLUT lut;
    lut.values_raw.resize(hw::LUT_SIZE);

    for (int i = 0; i < hw::LUT_SIZE; i++) {
        double  q = i * q_per;
        int64_t val;
        if (q == 0.0) {
            val = hw::LUT_SAT;                   // q^-3/2 -> inf, saturate
        } else {
            double raw = std::pow(q, -1.5) * scale;
            val = (raw >= static_cast<double>(hw::LUT_SAT))
                      ? hw::LUT_SAT
                      : static_cast<int64_t>(std::llround(raw));
        }
        lut.values_raw[i] = val;
    }
    return lut;
}

// LaneStepResult — the outputs of one hardware lane pass.
struct LaneStepResult {
    int64_t x, y, vx, vy;   // integrated (post-step) state
    int     nearest;        // nearest magnet index 0..2 (evaluated pre-step)
    bool    settle_now;     // settle condition met this step (pre-step state)
};

// hw_lane_step
// Bit-faithful port of lane_main.sv (stages S1..S8).
//
// The nearest-magnet and settle decision are evaluated on the INCOMING state
// (RTL stage S3, before integration), exactly as the hardware does, then the
// state is advanced with semi-implicit Euler (S5..S8).
//
//   q_i  = ((dx_i^2 + dy_i^2 + h2) >> 1)            [fx_adder_s3 -> Q5.13]
//   f_i  = LUT[q_i]                                  [lut_bram   -> Q4.14]
//   ax   = mu*(dxf_0+dxf_1+dxf_2) - gamma*vx - omega2*x
//   vx' = vx + dt*ax ;  x' = x + dt*vx'
//
// The mu factor is applied AFTER summing the per-magnet terms (matches S6),
// which is numerically distinct from multiplying mu per magnet.
LaneStepResult hw_lane_step(
    const FixedFormat&     fmt,
    const FixedConstants&  c,
    const InversePowerLUT& lut,
    int64_t x, int64_t y, int64_t vx, int64_t vy)
{
    const int NM = static_cast<int>(c.magnets.size());

    // ── S1/S2/S3a: dx, dy, dx^2, dy^2, q ─────────────────────────────
    std::vector<int64_t> dx(NM), dy(NM), q(NM);
    for (int i = 0; i < NM; i++) {
        dx[i] = fx_sub(fmt, c.magnets[i].x, x);   // Q4.14, saturating
        dy[i] = fx_sub(fmt, c.magnets[i].y, y);
        int64_t dx2 = fx_mul(fmt, dx[i], dx[i]);  // Q4.14
        int64_t dy2 = fx_mul(fmt, dy[i], dy[i]);
        q[i] = hw_build_q(dx2, dy2, c.h2);        // Q5.13
    }

    // ── S3b: nearest magnet (lowest index wins on tie, q0<=q1<=q2) ────
    int     nearest = 0;
    int64_t min_q   = q[0];
    for (int i = 1; i < NM; i++) {
        if (q[i] < min_q) { min_q = q[i]; nearest = i; }
    }

    // ── S3c: settle check (per-component velocity, squared distance) ──
    int64_t abs_vx = (vx < 0) ? -vx : vx;
    int64_t abs_vy = (vy < 0) ? -vy : vy;
    bool settle_now = (min_q  < c.sum_r_settle_sq_h_sq) &&
                      (abs_vx < c.v_settle)             &&
                      (abs_vy < c.v_settle);

    // ── S4/S5: f_i = LUT[q_i], then dxf_i = dx_i*f_i ─────────────────
    int64_t dxf[3] = {0,0,0}, dyf[3] = {0,0,0};
    for (int i = 0; i < NM; i++) {
        int64_t f = lut.lookup(q[i]);             // Q4.14
        dxf[i] = fx_mul(fmt, dx[i], f);           // Q4.14
        dyf[i] = fx_mul(fmt, dy[i], f);
    }

    // ── S6a: saturating 3-input sum of the magnet contributions ──────
    int64_t sum_dxf = fx_add3(fmt, dxf[0], dxf[1], dxf[2]);
    int64_t sum_dyf = fx_add3(fmt, dyf[0], dyf[1], dyf[2]);

    // ── S6a..S6c: damping, restoring, and mu scaling ─────────────────
    int64_t gamma_vx  = fx_mul(fmt, c.gamma,  vx);
    int64_t gamma_vy  = fx_mul(fmt, c.gamma,  vy);
    int64_t omega2_x  = fx_mul(fmt, c.omega2, x);
    int64_t omega2_y  = fx_mul(fmt, c.omega2, y);
    int64_t mu_dxf    = fx_mul(fmt, c.mu, sum_dxf);
    int64_t mu_dyf    = fx_mul(fmt, c.mu, sum_dyf);

    // ── S6e: ax = mu*sum_dxf - gamma*vx - omega2*x ───────────────────
    int64_t ax = fx_sub3(fmt, mu_dxf, gamma_vx, omega2_x);
    int64_t ay = fx_sub3(fmt, mu_dyf, gamma_vy, omega2_y);

    // ── S7/S8: semi-implicit Euler (velocity first, then position) ───
    int64_t vx_new = fx_add(fmt, vx, fx_mul(fmt, c.dt, ax));
    int64_t vy_new = fx_add(fmt, vy, fx_mul(fmt, c.dt, ay));
    int64_t x_new  = fx_add(fmt, x,  fx_mul(fmt, c.dt, vx_new));
    int64_t y_new  = fx_add(fmt, y,  fx_mul(fmt, c.dt, vy_new));

    return { x_new, y_new, vx_new, vy_new, nearest, settle_now };
}

// make_initial_grid_fixed
// Maps to: Python def make_initial_grid_fixed(fmt, render)
//
// Generates fixed-point initial (x, y) for every pixel.
//
//   x0 = x_start + col * x_step
//   y0 = y_start + row * y_step
//
// Stored row-major: index = row * width + col

void make_initial_grid_fixed(
    const FixedFormat&  fmt,
    const RenderParams& r,
    std::vector<int64_t>& x_grid,
    std::vector<int64_t>& y_grid)
{
    // Python single-pixel special cases
    double x_step, x_start, y_step, y_start;

    if (r.width == 1) {
        x_step  = 0.0;
        x_start = 0.5 * (r.x_min + r.x_max);
    } else {
        x_step  = (r.x_max - r.x_min) / (r.width  - 1);
        x_start = r.x_min;
    }

    if (r.height == 1) {
        y_step  = 0.0;
        y_start = 0.5 * (r.y_min + r.y_max);
    } else {
        y_step  = (r.y_max - r.y_min) / (r.height - 1);
        y_start = r.y_min;
    }

    int64_t x_start_raw = fmt.to_fixed(x_start);
    int64_t y_start_raw = fmt.to_fixed(y_start);
    int64_t x_step_raw  = fmt.to_fixed(x_step);
    int64_t y_step_raw  = fmt.to_fixed(y_step);

    int n = r.width * r.height;
    x_grid.resize(n);
    y_grid.resize(n);

    for (int row = 0; row < r.height; row++) {
        // Python: ys = fmt.saturate(y_start_raw + rows * y_step_raw)
        int64_t y_raw = fmt.saturate(y_start_raw + row * y_step_raw);

        for (int col = 0; col < r.width; col++) {
            // Python: xs = fmt.saturate(x_start_raw + cols * x_step_raw)
            int64_t x_raw = fmt.saturate(x_start_raw + col * x_step_raw);

            x_grid[row * r.width + col] = x_raw;
            y_grid[row * r.width + col] = y_raw;
        }
    }
}

// BasinResult
// Maps to: Python dataclass BasinResult

struct BasinResult {
    std::vector<int16_t> basin;    // magnet index (-1 = unresolved)
    std::vector<int32_t> steps;    // steps taken per pixel
    std::vector<uint8_t> settled;  // 1 = strictly settled, 0 = not
    double               elapsed_s;
    int                  width;
    int                  height;
};

// generate_basin_map_fixed
// Maps to: Python def generate_basin_map_fixed(fmt, cfg)
//
// Main simulation loop. Processes all pixels until:
//   a) each pixel has met the settle condition for min_consec steps, or
//   b) max_steps is reached.
//
// Key differences from Python:
//   - Python is vectorised (NumPy processes all active pixels at once)
//   - C++ uses a scalar inner loop per pixel
//   - Results are identical; only execution style differs

BasinResult generate_basin_map_fixed(
    const FixedFormat&          fmt,
    const PhysicsParams&        phys,
    const SimulationParams&     sim,
    const RenderParams&         render,
    const std::vector<MagnetPos>& magnets)
{
    // Build fixed-point constants and the hardware LUT once before the loop.
    // The hardware has no float-force path: the LUT (qinv32.mem) is always used.
    FixedConstants  c   = build_fixed_constants(fmt, phys, sim, magnets);
    InversePowerLUT lut = build_inverse_power_lut_hw();

    int n = render.width * render.height;

    // Python: x, y = make_initial_grid_fixed(fmt, render)
    std::vector<int64_t> x(n), y(n);
    make_initial_grid_fixed(fmt, render, x, y);

    // Python: vx = np.zeros_like(x), vy = np.zeros_like(y)
    std::vector<int64_t> vx(n, 0), vy(n, 0);

    // Python: basin = np.full(..., -1), steps = np.zeros(...), settled = np.zeros(...)
    std::vector<int16_t> basin(n, -1);
    std::vector<int32_t> steps(n, 0);
    std::vector<uint8_t> settled(n, 0);

    // Python: active = np.ones(..., dtype=bool)
    std::vector<bool> active(n, true);

    // Python: consec = np.zeros(..., dtype=np.int16)
    std::vector<int> consec(n, 0);

    int active_count = n;

    auto t0 = std::chrono::high_resolution_clock::now();

    // Python: for step in range(1, sim.max_steps + 1):
    for (int step = 1; step <= sim.max_steps; step++) {

        for (int i = 0; i < n; i++) {
            if (!active[i]) continue;

            // One hardware lane pass: settle is evaluated on the incoming
            // state (RTL S3), then the state is integrated (RTL S5..S8).
            LaneStepResult s = hw_lane_step(fmt, c, lut, x[i], y[i], vx[i], vy[i]);

            // ── settle_count (settle_check_s3): saturate at 3 ────────────
            if (s.settle_now) {
                if (consec[i] < 3) consec[i]++;   // 2-bit counter, saturates at 3
            } else {
                consec[i] = 0;
            }

            // ── commit integrated state ──────────────────────────────────
            x[i]  = s.x;  y[i]  = s.y;
            vx[i] = s.vx; vy[i] = s.vy;

            // ── detect_settle: settled has priority over time_out ────────
            //   settled  = (settle_count >= consec_settle_count)
            //   time_out = ~settled && (step_cnt >= max_steps)
            if (consec[i] >= sim.min_consec) {
                basin[i]   = static_cast<int16_t>(s.nearest);
                steps[i]   = step;
                settled[i] = 1;
                active[i]  = false;
                active_count--;
            } else if (step >= sim.max_steps) {
                steps[i] = step;
                if (sim.classify_on_timeout)
                    basin[i] = static_cast<int16_t>(s.nearest);
                active[i] = false;
                active_count--;
            }
        }

        // Progress report — matches Python print format
        if (step == 1 || step % 500 == 0 ||
            step == sim.max_steps || active_count == 0)
        {
            auto now = std::chrono::high_resolution_clock::now();
            double elapsed = std::chrono::duration<double>(now - t0).count();
            std::cout << "step " << step << "/" << sim.max_steps
                      << " | active pixels " << active_count
                      << " | elapsed " << elapsed << "s\n"
                      << std::flush;
        }

        if (active_count == 0) break;
    }

    // Timeout (step_cnt >= max_steps) is handled inside the loop above, exactly
    // as detect_settle.sv routes a timed-out pixel to the output FIFO with the
    // nearest-magnet classification computed in that step.

    auto t1 = std::chrono::high_resolution_clock::now();
    double elapsed_s = std::chrono::duration<double>(t1 - t0).count();

    return { basin, steps, settled, elapsed_s, render.width, render.height };
}

// Output helpers
// Maps to: Python save_outputs(), print summary prints

// Save raw binary file — readable in Python with np.fromfile
template<typename T>
void save_binary(const std::vector<T>& data, const std::string& path) {
    std::ofstream f(path, std::ios::binary);
    if (!f) throw std::runtime_error("Cannot open " + path);
    f.write(reinterpret_cast<const char*>(data.data()),
            data.size() * sizeof(T));
}

void save_summary_json(
    const FixedFormat&      fmt,
    const PhysicsParams&    phys,
    const SimulationParams& sim,
    const RenderParams&     render,
    const BasinResult&      result,
    const std::string&      path)
{
    int    n          = render.width * render.height;
    double mean_steps = 0.0;
    int    strict     = 0;

    for (int i = 0; i < n; i++) {
        mean_steps += result.steps[i];
        if (result.settled[i]) strict++;
    }
    mean_steps /= n;

    // Compute p99 steps
    std::vector<int32_t> sorted_steps = result.steps;
    std::sort(sorted_steps.begin(), sorted_steps.end());
    int p99_steps = sorted_steps[static_cast<size_t>(0.99 * n)];

    // Integer-bit count uses the RTL convention (sign bit included in the
    // integer part), so an 18-bit / 14-frac value is labelled Q4.14 like the HW.
    int integer_bits = fmt.total_bits - fmt.frac_bits;

    std::ofstream f(path);
    if (!f) throw std::runtime_error("Cannot open " + path);

    f << "{\n";
    f << "  \"fixed_format\": {\n";
    f << "    \"total_bits\": " << fmt.total_bits << ",\n";
    f << "    \"frac_bits\": "  << fmt.frac_bits  << ",\n";
    f << "    \"format_name\": \"Q" << integer_bits << "." << fmt.frac_bits << "\"\n";
    f << "  },\n";
    f << "  \"physics\": {\n";
    f << "    \"gamma\":  " << phys.gamma  << ",\n";
    f << "    \"omega2\": " << phys.omega2 << ",\n";
    f << "    \"h2\":     " << phys.h2     << ",\n";
    f << "    \"mu\":     " << phys.mu     << ",\n";
    f << "    \"dt\":     " << phys.dt     << "\n";
    f << "  },\n";
    f << "  \"simulation\": {\n";
    f << "    \"max_steps\":  " << sim.max_steps  << ",\n";
    f << "    \"r_settle\":   " << sim.r_settle   << ",\n";
    f << "    \"v_settle\":   " << sim.v_settle   << ",\n";
    f << "    \"min_consec\": " << sim.min_consec << ",\n";
    f << "    \"force_mode\": \"" << (sim.use_lut ? "lut-force" : "float-force") << "\"\n";
    f << "  },\n";
    f << "  \"render\": {\n";
    f << "    \"width\":  " << render.width  << ",\n";
    f << "    \"height\": " << render.height << ",\n";
    f << "    \"x_min\":  " << render.x_min  << ",\n";
    f << "    \"x_max\":  " << render.x_max  << ",\n";
    f << "    \"y_min\":  " << render.y_min  << ",\n";
    f << "    \"y_max\":  " << render.y_max  << "\n";
    f << "  },\n";
    f << "  \"results\": {\n";
    f << "    \"elapsed_s\":              " << result.elapsed_s             << ",\n";
    f << "    \"pixels\":                 " << n                            << ",\n";
    f << "    \"pixels_per_second\":      " << n / result.elapsed_s        << ",\n";
    f << "    \"mean_steps\":             " << mean_steps                   << ",\n";
    f << "    \"p99_steps\":              " << p99_steps                    << ",\n";
    f << "    \"strict_settled_pixels\":  " << strict                       << ",\n";
    f << "    \"strict_settled_fraction\":" << static_cast<double>(strict)/n << "\n";
    f << "  }\n";
    f << "}\n";
}

void print_summary(
    const FixedFormat&   fmt,
    const RenderParams&  render,
    const BasinResult&   result)
{
    int n            = render.width * render.height;
    // Integer-bit count uses the RTL convention (sign bit included in the
    // integer part), so an 18-bit / 14-frac value is labelled Q4.14 like the HW.
    int integer_bits = fmt.total_bits - fmt.frac_bits;
    int strict       = 0;

    for (int i = 0; i < n; i++)
        if (result.settled[i]) strict++;

    std::cout << "\n=== Results ===\n";
    std::cout << "Format            : Q" << integer_bits
              << "." << fmt.frac_bits << "\n";
    std::cout << "Resolution        : " << render.width
              << " x " << render.height << "\n";
    std::cout << "Elapsed           : " << result.elapsed_s << "s\n";
    std::cout << "Pixels/second     : " << n / result.elapsed_s << "\n";
    std::cout << "ms/pixel          : "
              << 1000.0 * result.elapsed_s / n << "\n";
    std::cout << "Strict settled    : " << strict << " / " << n << " ("
              << 100.0 * strict / n << "%)\n";

    // Basin counts per magnet
    int counts[4] = {0,0,0,0};
    for (int i = 0; i < n; i++) {
        int label = result.basin[i];
        if (label >= -1 && label <= 2) counts[label + 1]++;
    }
    std::cout << "Unresolved (-1)   : " << counts[0] << "\n";
    for (int j = 0; j < 3; j++)
        std::cout << "Magnet " << j << " pixels   : " << counts[j+1] << "\n";
}

// Command-line argument parsing
// Maps to: Python def parse_args() and build_config_from_args()

struct Args {
    // FixedFormat — matches the 9-lane RTL datapath (W=18, F=14 -> Q4.14)
    int    total_bits  = 18;
    int    frac_bits   = 14;
    // RenderParams — matches pixel_generator_9_lanes.v (IMG_W=IMG_H=270)
    int    width       = 270;
    int    height      = 270;
    double x_min       = -1.8;
    double x_max       =  1.8;
    double y_min       = -1.8;
    double y_max       =  1.8;
    // PhysicsParams (runtime AXI registers on hardware; canonical defaults here)
    double gamma       = 0.20;
    double omega2      = 1.0;
    double h2          = 0.25;
    double mu          = 1.0;
    double dt          = 0.01;
    // SimulationParams — max_steps matches TRAJ_DEPTH (4096)
    int    max_steps   = 4096;
    double r_settle    = 0.30;
    double v_settle    = 0.05;
    int    min_consec  = 3;
    bool   use_lut     = true;   // hardware is LUT-only; kept for CLI compatibility
    int    lut_size    = 4096;
    double lut_q_max   = 32.0;   // hardware LUT covers q in [0, 32)
    bool   no_classify_on_timeout = false;
    // OutputParams
    std::string out_dir = "cpp_outputs";
};

Args parse_args(int argc, char* argv[]) {
    Args a;
    for (int i = 1; i < argc; i++) {
        std::string key = argv[i];
        std::string val = (i + 1 < argc) ? argv[i + 1] : "";

        if      (key == "--total-bits")  { a.total_bits  = std::stoi(val); i++; }
        else if (key == "--frac-bits")   { a.frac_bits   = std::stoi(val); i++; }
        else if (key == "--width")       { a.width       = std::stoi(val); i++; }
        else if (key == "--height")      { a.height      = std::stoi(val); i++; }
        else if (key == "--x-min")       { a.x_min       = std::stod(val); i++; }
        else if (key == "--x-max")       { a.x_max       = std::stod(val); i++; }
        else if (key == "--y-min")       { a.y_min       = std::stod(val); i++; }
        else if (key == "--y-max")       { a.y_max       = std::stod(val); i++; }
        else if (key == "--gamma")       { a.gamma       = std::stod(val); i++; }
        else if (key == "--omega2")      { a.omega2      = std::stod(val); i++; }
        else if (key == "--h2")          { a.h2          = std::stod(val); i++; }
        else if (key == "--mu")          { a.mu          = std::stod(val); i++; }
        else if (key == "--dt")          { a.dt          = std::stod(val); i++; }
        else if (key == "--max-steps")   { a.max_steps   = std::stoi(val); i++; }
        else if (key == "--r-settle")    { a.r_settle    = std::stod(val); i++; }
        else if (key == "--v-settle")    { a.v_settle    = std::stod(val); i++; }
        else if (key == "--min-consec")  { a.min_consec  = std::stoi(val); i++; }
        else if (key == "--lut-size")    { a.lut_size    = std::stoi(val); i++; }
        else if (key == "--lut-q-max")   { a.lut_q_max   = std::stod(val); i++; }
        else if (key == "--out-dir")     { a.out_dir     = val;            i++; }
        else if (key == "--no-classify-on-timeout") { a.no_classify_on_timeout = true; }
        else if (key == "--force-mode") {
            a.use_lut = (val == "lut-force");
            i++;
        }
    }
    return a;
}

// main
// Maps to: Python def main()

int main(int argc, char* argv[]) {

    Args args = parse_args(argc, argv);

    // Build config objects from args
    // Python: fmt, cfg = build_config_from_args(args)
    FixedFormat fmt(args.total_bits, args.frac_bits);

    PhysicsParams phys;
    phys.gamma  = args.gamma;
    phys.omega2 = args.omega2;
    phys.h2     = args.h2;
    phys.mu     = args.mu;
    phys.dt     = args.dt;

    SimulationParams sim;
    sim.max_steps           = args.max_steps;
    sim.r_settle            = args.r_settle;
    sim.v_settle            = args.v_settle;
    sim.min_consec          = args.min_consec;
    sim.classify_on_timeout = !args.no_classify_on_timeout;
    sim.use_lut             = args.use_lut;
    sim.lut_size            = args.lut_size;
    sim.lut_q_max           = args.lut_q_max;

    RenderParams render;
    render.x_min  = args.x_min;
    render.x_max  = args.x_max;
    render.y_min  = args.y_min;
    render.y_max  = args.y_max;
    render.width  = args.width;
    render.height = args.height;

    // Python: print_run_header(fmt, cfg)
    // Integer-bit count uses the RTL convention (sign bit included in the
    // integer part), so an 18-bit / 14-frac value is labelled Q4.14 like the HW.
    int integer_bits = fmt.total_bits - fmt.frac_bits;
    std::cout << "Fixed-point magnetic pendulum (C++ baseline)\n";
    std::cout << "resolution          : " << render.width
              << " x " << render.height << "\n";
    std::cout << "fixed format        : signed Q" << integer_bits
              << "." << fmt.frac_bits << "\n";
    std::cout << "raw range           : ["
              << fmt.min_raw << ", " << fmt.max_raw << "]\n";
    std::cout << "force mode          : "
              << (sim.use_lut ? "lut-force" : "float-force") << "\n";
    std::cout << "gamma, omega2, mu   : "
              << phys.gamma  << ", "
              << phys.omega2 << ", "
              << phys.mu     << "\n";
    std::cout << "dt, max_steps       : "
              << phys.dt     << ", "
              << sim.max_steps << "\n";
    std::cout << "settling thresholds : r="
              << sim.r_settle << ", v=" << sim.v_settle << "\n\n";

    // Python: result = generate_basin_map_fixed(fmt, cfg)
    BasinResult result = generate_basin_map_fixed(
        fmt, phys, sim, render, DEFAULT_MAGNETS);

    // Python: print(f"\nDone in {result.elapsed_s:.2f}s")
    int n = render.width * render.height;
    std::cout << "\nDone in " << result.elapsed_s << "s\n";
    std::cout << "Pixels per second: " << n / result.elapsed_s << "\n";
    std::cout << "Average time per pixel: "
              << 1000.0 * result.elapsed_s / n << " ms\n";

    print_summary(fmt, render, result);

    // Python: save_outputs(...)
    // Create output directory
    std::filesystem::create_directories(args.out_dir);

    save_binary(result.basin,   args.out_dir + "/basin.bin");
    save_binary(result.steps,   args.out_dir + "/steps.bin");
    save_binary(result.settled, args.out_dir + "/settled.bin");
    save_summary_json(fmt, phys, sim, render, result,
                      args.out_dir + "/summary.json");

    std::cout << "\nSaved outputs in: " << args.out_dir << "/\n";
    std::cout << "  basin.bin      (int16, H x W, load: np.fromfile(..., np.int16))\n";
    std::cout << "  steps.bin      (int32, H x W, load: np.fromfile(..., np.int32))\n";
    std::cout << "  settled.bin    (uint8, H x W, load: np.fromfile(..., np.uint8))\n";
    std::cout << "  summary.json\n";

    return 0;
}
