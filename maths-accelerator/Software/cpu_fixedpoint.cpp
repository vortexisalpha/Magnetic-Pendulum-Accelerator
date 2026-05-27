
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
#include <cctype>
#include <cerrno>
#include <cstdio>
#include <cstdlib>
#include <sys/stat.h>

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
    __int128 product = static_cast<__int128>(a) * static_cast<__int128>(b);
    int64_t  shifted = static_cast<int64_t>(product >> fmt.frac_bits);
    return fmt.saturate(shifted);
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
    int64_t r2_settle;   // r_settle^2 in fixed-point
    int64_t v2_settle;   // v_settle^2 in fixed-point

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
    c.gamma     = fmt.to_fixed(phys.gamma);
    c.omega2    = fmt.to_fixed(phys.omega2);
    c.h2        = fmt.to_fixed(phys.h2);
    c.mu        = fmt.to_fixed(phys.mu);
    c.dt        = fmt.to_fixed(phys.dt);
    c.r2_settle = fmt.to_fixed(sim.r_settle * sim.r_settle);
    c.v2_settle = fmt.to_fixed(sim.v_settle * sim.v_settle);

    for (const auto& m : magnets)
        c.magnets.push_back({ fmt.to_fixed(m.x), fmt.to_fixed(m.y) });

    return c;
}

// InversePowerLUT
// Maps to: Python dataclass InversePowerLUT (frozen=True)
//
// Lookup table approximating f(q) = q^(-3/2).
// Uniform sampling, no interpolation — matches Python LUT exactly.

struct InversePowerLUT {
    int64_t              q_min_raw;
    int64_t              q_max_raw;
    std::vector<int64_t> values_raw;

    // Python: def lookup(self, q_raw)
    //
    // 1. Clip q to LUT range.
    // 2. Compute table index: idx = (q_clip - q_min) * (N-1) / (q_max - q_min)
    // 3. Return stored value.
    //
    // No interpolation — matches Python and FPGA BRAM behaviour.
    int64_t lookup(int64_t q_raw) const {
        int64_t q_clip = std::max(q_min_raw, std::min(q_max_raw, q_raw));
        int64_t n      = static_cast<int64_t>(values_raw.size()) - 1;
        int64_t denom  = std::max(int64_t(1), q_max_raw - q_min_raw);
        int64_t idx    = (q_clip - q_min_raw) * n / denom;
        idx = std::max(int64_t(0), std::min(n, idx));
        return values_raw[static_cast<size_t>(idx)];
    }
};

// Python: def build_inverse_power_lut(fmt, cfg)
//
// Build LUT from h2 to lut_q_max.
// q starts at h2 because q = dx^2 + dy^2 + h2 >= h2 always.
InversePowerLUT build_inverse_power_lut(
    const FixedFormat&      fmt_q,
    const PhysicsParams&    phys,
    const SimulationParams& sim)
{
    double q_min = phys.h2;
    double q_max = sim.lut_q_max;

    if (q_max <= q_min)
        throw std::invalid_argument("lut_q_max must be larger than h2");

    FixedFormat fmt_out(16, fmt_q.frac_bits);

    InversePowerLUT lut;
    lut.q_min_raw = fmt_q.to_fixed(q_min);
    lut.q_max_raw = fmt_q.to_fixed(q_max);
    lut.values_raw.resize(sim.lut_size);

    for (int i = 0; i < sim.lut_size; i++) {
        // Python: np.linspace(q_min, q_max, lut_size)
        double t = static_cast<double>(i) / (sim.lut_size - 1);
        double q = q_min + t * (q_max - q_min);
        double f = std::pow(q, -1.5);
        lut.values_raw[i] = fmt_out.to_fixed(f);
    }

    return lut;
}

// inverse_q_power_fixed
// Maps to: Python def inverse_q_power_fixed(fmt, cfg, q_raw, lut)
//
// Two modes:
//   float-force: convert to float, compute q^(-1.5), convert back
//   lut-force:   look up precomputed table

inline int64_t inverse_q_power_fixed(
    const FixedFormat&      fmt,
    bool                    use_lut,
    const InversePowerLUT*  lut,
    int64_t                 q_raw)
{
    if (use_lut) {
        return lut->lookup(q_raw);
    } else {
        // float-force: matches Python
        //   q_float = fmt.to_float(q_raw)
        //   f_float = q_float ** (-1.5)
        //   return fmt.to_fixed(f_float)
        double q_f = fmt.to_float(q_raw);
        if (q_f <= 0.0) return fmt.max_raw; // guard against zero
        double f   = std::pow(q_f, -1.5);
        return fmt.to_fixed(f);
    }
}

// acceleration_fixed
// Maps to: Python def acceleration_fixed(fmt, cfg, const, x, y, vx, vy, lut)
//
// Computes ax, ay for one pixel using fixed-point arithmetic.
//
// Equation:
//   ax = -gamma*vx - omega2*x + mu * sum_i [ dx_i * q_i^(-3/2) ]
//   ay = -gamma*vy - omega2*y + mu * sum_i [ dy_i * q_i^(-3/2) ]
//
// where q_i = dx_i^2 + dy_i^2 + h2

void acceleration_fixed(
    const FixedFormat&     fmt,
    const FixedConstants&  c,
    bool                   use_lut,
    const InversePowerLUT* lut,
    int64_t  x,  int64_t  y,
    int64_t  vx, int64_t  vy,
    int64_t& ax_out, int64_t& ay_out)
{
    FixedFormat fmt_q(19, fmt.frac_bits);
    // Python:
    //   ax = fx_add(fmt,
    //       fx_neg(fmt, fx_mul(fmt, const.gamma, vx)),
    //       fx_neg(fmt, fx_mul(fmt, const.omega2, x)))
    int64_t ax = fx_add(fmt,
        fx_neg(fmt, fx_mul(fmt, c.gamma,  vx)),
        fx_neg(fmt, fx_mul(fmt, c.omega2, x)));

    int64_t ay = fx_add(fmt,
        fx_neg(fmt, fx_mul(fmt, c.gamma,  vy)),
        fx_neg(fmt, fx_mul(fmt, c.omega2, y)));

    // Python: for mx_raw, my_raw in const.magnets:
    for (const auto& m : c.magnets) {
        // Python: dx = fx_sub(fmt, mx_raw, x)
        int64_t dx  = fx_sub(fmt, m.x, x);
        int64_t dy  = fx_sub(fmt, m.y, y);

        // Python: dx2 = fx_mul(fmt, dx, dx)
        int64_t dx2 = fx_mul(fmt, dx, dx);
        int64_t dy2 = fx_mul(fmt, dy, dy);

        // Python: q = fx_add(fmt, fx_add(fmt, dx2, dy2), const.h2)
        int64_t q = fx_add(fmt_q, fx_add(fmt, dx2, dy2), c.h2);

        // Python: f = inverse_q_power_fixed(fmt, cfg, q, lut)
        int64_t f = inverse_q_power_fixed(fmt_q, use_lut, lut, q);

        // Python: ax = fx_add(fmt, ax, fx_mul(fmt, const.mu, fx_mul(fmt, dx, f)))
        ax = fx_add(fmt, ax, fx_mul(fmt, c.mu, fx_mul(fmt, dx, f)));
        ay = fx_add(fmt, ay, fx_mul(fmt, c.mu, fx_mul(fmt, dy, f)));
    }

    ax_out = ax;
    ay_out = ay;
}

// nearest_magnet_fixed
// Maps to: Python def nearest_magnet_fixed(fmt, const, x, y)
//
// Returns index of nearest magnet and squared distance to it.
// Tie-breaking: lowest index wins (matches Python argmin behaviour).

int nearest_magnet_fixed(
    const FixedFormat&    fmt,
    const FixedConstants& c,
    int64_t x, int64_t y,
    int64_t& d2_min_out)
{
    int     nearest = 0;
    int64_t d2_min  = INT64_MAX;

    for (int i = 0; i < static_cast<int>(c.magnets.size()); i++) {
        // Python: dx = fx_sub(fmt, mx_raw, x)
        int64_t dx = fx_sub(fmt, c.magnets[i].x, x);
        int64_t dy = fx_sub(fmt, c.magnets[i].y, y);

        // Python: d2 = fx_add(fmt, fx_mul(fmt, dx, dx), fx_mul(fmt, dy, dy))
        int64_t d2 = fx_add(fmt, fx_mul(fmt, dx, dx), fx_mul(fmt, dy, dy));

        // Python: np.argmin — lowest index wins on tie
        if (d2 < d2_min) {
            d2_min  = d2;
            nearest = i;
        }
    }

    d2_min_out = d2_min;
    return nearest;
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
    // Build fixed-point constants and optional LUT once before the loop.
    // Python: const = build_fixed_constants(fmt, cfg)
    //         lut   = build_inverse_power_lut(fmt, cfg) if lut-force else None
    FixedFormat fmt_q(19, fmt.frac_bits); 

    FixedConstants c = build_fixed_constants(fmt, phys, sim, magnets);


    InversePowerLUT lut;
    if (sim.use_lut)
        lut = build_inverse_power_lut(fmt_q, phys, sim);
    const InversePowerLUT* lut_ptr = sim.use_lut ? &lut : nullptr;

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

            // ── Acceleration ────────────────────────────────
            // Python: ax, ay = acceleration_fixed(fmt, cfg, const, x, y, vx, vy, lut)
            int64_t ax, ay;
            acceleration_fixed(fmt, c, sim.use_lut, lut_ptr,
                               x[i], y[i], vx[i], vy[i], ax, ay);

            // ── Semi-implicit Euler: velocity update ─────────
            // Python:
            //   vx_new = fx_add(fmt, vx, fx_mul(fmt, const.dt, ax))
            //   vy_new = fx_add(fmt, vy, fx_mul(fmt, const.dt, ay))
            //   vx[active] = vx_new[active]
            //   vy[active] = vy_new[active]
            int64_t vx_new = fx_add(fmt, vx[i], fx_mul(fmt, c.dt, ax));
            int64_t vy_new = fx_add(fmt, vy[i], fx_mul(fmt, c.dt, ay));
            vx[i] = vx_new;
            vy[i] = vy_new;

            // ── Semi-implicit Euler: position update ─────────
            // Python:
            //   x_new = fx_add(fmt, x, fx_mul(fmt, const.dt, vx))  <- vx already updated
            //   y_new = fx_add(fmt, y, fx_mul(fmt, const.dt, vy))
            //   x[active] = x_new[active]
            //   y[active] = y_new[active]
            //
            // Note: Python uses the NEW vx/vy here (semi-implicit Euler).
            // After the vx[active] = vx_new[active] line, vx IS the new velocity.
            x[i] = fx_add(fmt, x[i], fx_mul(fmt, c.dt, vx_new));
            y[i] = fx_add(fmt, y[i], fx_mul(fmt, c.dt, vy_new));

            // ── Settling check ───────────────────────────────
            // Python:
            //   nearest, d2_min = nearest_magnet_fixed(fmt, const, x, y)
            //   speed2 = fx_add(fmt, fx_mul(fmt, vx, vx), fx_mul(fmt, vy, vy))
            //   settle_now = active & (d2_min < const.r2_settle) & (speed2 < const.v2_settle)
            int64_t d2_min;
            int nearest = nearest_magnet_fixed(fmt, c, x[i], y[i], d2_min);

            int64_t speed2 = fx_add(fmt,
                fx_mul(fmt, vx[i], vx[i]),
                fx_mul(fmt, vy[i], vy[i]));

            bool settle_now = (d2_min < c.r2_settle) && (speed2 < c.v2_settle);

            // Python:
            //   consec[settle_now] += 1
            //   consec[active & ~settle_now] = 0
            if (settle_now) {
                consec[i]++;
            } else {
                consec[i] = 0;
            }

            // Python:
            //   done_now = active & (consec >= sim.min_consec)
            //   basin[done_now]   = nearest[done_now]
            //   steps[done_now]   = step
            //   settled[done_now] = True
            //   active[done_now]  = False
            if (consec[i] >= sim.min_consec) {
                basin[i]   = static_cast<int16_t>(nearest);
                steps[i]   = step;
                settled[i] = 1;
                active[i]  = false;
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

    // Python:
    //   if np.any(active):
    //       nearest, _ = nearest_magnet_fixed(fmt, const, x, y)
    //       if sim.classify_on_timeout:
    //           basin[active] = nearest[active]
    //       steps[active] = sim.max_steps
    if (active_count > 0) {
        for (int i = 0; i < n; i++) {
            if (!active[i]) continue;
            steps[i] = sim.max_steps;
            if (sim.classify_on_timeout) {
                int64_t d2_min;
                int nearest = nearest_magnet_fixed(fmt, c, x[i], y[i], d2_min);
                basin[i] = static_cast<int16_t>(nearest);
            }
        }
    }

    auto t1 = std::chrono::high_resolution_clock::now();
    double elapsed_s = std::chrono::duration<double>(t1 - t0).count();

    return { basin, steps, settled, elapsed_s, render.width, render.height };
}

// Output helpers
// Maps to: Python save_outputs(), print summary prints

// Create path and all parents (like pathlib.Path.mkdir(parents=True)).
static bool mkdir_p(const std::string& path) {
    if (path.empty())
        return false;
    struct stat st;
    if (stat(path.c_str(), &st) == 0)
        return S_ISDIR(st.st_mode);
    const size_t slash = path.find_last_of('/');
    if (slash != std::string::npos) {
        const std::string parent = path.substr(0, slash);
        if (!parent.empty() && !mkdir_p(parent))
            return false;
    }
    if (mkdir(path.c_str(), 0755) != 0 && errno != EEXIST)
        return false;
    return stat(path.c_str(), &st) == 0 && S_ISDIR(st.st_mode);
}

// Flask integration — fetch controller params from GET /info, repost via export_to_flask.py

struct FlaskConfig {
    double magnetic_strength = 1.0;
    double damping_factor    = 0.20;
    double pendulum_length   = 9.8;
    double pendulum_height   = 0.5;
    bool   fetched           = false;
};

static std::string shell_escape(const std::string& value) {
    std::string escaped = "\"";
    for (char ch : value) {
        if (ch == '"' || ch == '\\' || ch == '$' || ch == '`')
            escaped.push_back('\\');
        escaped.push_back(ch);
    }
    escaped.push_back('"');
    return escaped;
}

static std::string http_get(const std::string& url) {
    const std::string cmd = "curl -sf " + shell_escape(url);
    FILE* pipe = popen(cmd.c_str(), "r");
    if (!pipe)
        throw std::runtime_error("Failed to run curl");

    std::string response;
    char buffer[4096];
    while (true) {
        size_t n = std::fread(buffer, 1, sizeof(buffer), pipe);
        if (n == 0)
            break;
        response.append(buffer, n);
    }

    const int status = pclose(pipe);
    if (status != 0 || response.empty())
        throw std::runtime_error("HTTP GET failed: " + url);

    return response;
}

static bool json_number(const std::string& json, const std::string& key, double& out) {
    const std::string needle = "\"" + key + "\":";
    const size_t pos = json.find(needle);
    if (pos == std::string::npos)
        return false;

    size_t i = pos + needle.size();
    while (i < json.size() && std::isspace(static_cast<unsigned char>(json[i])))
        i++;

    char* end = nullptr;
    out = std::strtod(json.c_str() + i, &end);
    return end != json.c_str() + i;
}

static FlaskConfig fetch_flask_config(const std::string& flask_base) {
    const std::string json = http_get(flask_base + "/info");

    FlaskConfig cfg;
    if (!json_number(json, "magnetic_strength", cfg.magnetic_strength) ||
        !json_number(json, "damping_factor",    cfg.damping_factor)    ||
        !json_number(json, "pendulum_length",   cfg.pendulum_length)   ||
        !json_number(json, "pendulum_height",    cfg.pendulum_height)) {
        throw std::runtime_error("Flask /info JSON missing required controller fields");
    }

    cfg.fetched = true;

    if (cfg.pendulum_height <= 0.0)
        throw std::runtime_error("Flask pendulum_height must be positive");
    if (cfg.pendulum_length <= 0.0)
        throw std::runtime_error("Flask pendulum_length must be positive");

    return cfg;
}

// Unity slider labels map directly onto the simplified model:
//   damping_factor    -> gamma
//   magnetic_strength -> mu
//   pendulum_length L -> omega2 = g / L
//   pendulum_height h -> h2 = h^2
static void apply_flask_config(PhysicsParams& phys, const FlaskConfig& cfg) {
    constexpr double g = 9.8;

    phys.gamma  = cfg.damping_factor;
    phys.mu     = cfg.magnetic_strength;
    phys.omega2 = g / cfg.pendulum_length;
    phys.h2     = cfg.pendulum_height * cfg.pendulum_height;
}

static void repost_to_flask(const std::string& flask_base) {
    const std::string arg = shell_escape(flask_base);
    const std::string cmd_venv =
        ".venv/bin/python Software/export_to_flask.py --flask-base " + arg;
    const std::string cmd_system =
        "python3 Software/export_to_flask.py --flask-base " + arg;

    std::cout << "\nReposting image and magnets to Flask...\n";
    if (std::system(cmd_venv.c_str()) == 0)
        return;
    if (std::system(cmd_system.c_str()) == 0)
        return;

    throw std::runtime_error(
        "Failed to repost results via Software/export_to_flask.py");
}

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

    int integer_bits = fmt.total_bits - fmt.frac_bits - 1;

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
    int integer_bits = fmt.total_bits - fmt.frac_bits - 1;
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
    // FixedFormat
    int    total_bits  = 16;
    int    frac_bits   = 12;
    // RenderParams
    int    width       = 160;
    int    height      = 120;
    double x_min       = -1.8;
    double x_max       =  1.8;
    double y_min       = -1.8;
    double y_max       =  1.8;
    // PhysicsParams
    double gamma       = 0.20;
    double omega2      = 1.0;
    double h2          = 0.25;
    double mu          = 1.0;
    double dt          = 0.01;
    // SimulationParams
    int    max_steps   = 4095;
    double r_settle    = 0.30;
    double v_settle    = 0.05;
    int    min_consec  = 3;
    bool   use_lut     = true;
    int    lut_size    = 4096;
    double lut_q_max   = 64.0;
    bool   no_classify_on_timeout = false;
    // OutputParams
    std::string out_dir = "cpp_outputs";
    // Flask integration
    std::string flask_base = "http://127.0.0.1:5000";
    bool        use_flask  = true;
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
        else if (key == "--flask-base")  { a.flask_base  = val;            i++; }
        else if (key == "--no-flask")    { a.use_flask   = false; }
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

    if (args.use_flask) {
        try {
            FlaskConfig flask = fetch_flask_config(args.flask_base);
            apply_flask_config(phys, flask);
            std::cout << "Flask controller params (GET /info)\n";
            std::cout << "  magnetic_strength : " << flask.magnetic_strength << "\n";
            std::cout << "  damping_factor    : " << flask.damping_factor    << "\n";
            std::cout << "  pendulum_length   : " << flask.pendulum_length   << "\n";
            std::cout << "  pendulum_height   : " << flask.pendulum_height   << "\n";
            std::cout << "  -> gamma, mu, omega2, h2 : "
                      << phys.gamma  << ", "
                      << phys.mu     << ", "
                      << phys.omega2 << ", "
                      << phys.h2     << "\n\n";
        } catch (const std::exception& ex) {
            std::cerr << "Warning: Flask fetch failed (" << ex.what()
                      << "); using CLI physics defaults.\n\n";
        }
    }

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
    int integer_bits = fmt.total_bits - fmt.frac_bits - 1;
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
    if (!mkdir_p(args.out_dir))
        throw std::runtime_error("Cannot create output directory: " + args.out_dir);

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

    if (args.use_flask) {
        try {
            repost_to_flask(args.flask_base);
        } catch (const std::exception& ex) {
            std::cerr << "Warning: Flask repost failed (" << ex.what() << ")\n";
        }
    }

    return 0;
}
