import math

#LUT generation script for q^-3/2
#NB: the script covers [h2, h2 + WINDOW)

# LUT/system parameters:
LUT_SIZE = 4096
F = 14

H2     = 0.25
WINDOW = 8.0

# MAX value for 18-bit unsigned fixed-point
SAT_MAX = 0x1FFFF

# converts floating point to fixed-point by multiplying by SCALE
SCALE = 1 << F

#step per LUT index: WINDOW spread over LUT_SIZE entries (2^-9 for the 4096-entry)
Q_PER_INDEX = WINDOW / LUT_SIZE

entries = []
for i in range(LUT_SIZE):
    q = H2 + i * Q_PER_INDEX
    raw = q ** (-1.5) * SCALE #raw fixedpoint value
    if raw >= SAT_MAX:#starutaiton
        val = SAT_MAX
    else:
        val = round(raw)#round to nearest round number
    entries.append(val)

with open("qinv32.mem", "w") as f:
    for v in entries:
        f.write(f"{v:05X}\n") #write each value as a 5-digit hexa number
