import math

#LUT generation script for q^-3/2
#NB: the script covers [0, 32)

# LUT/system parameters:
LUT_SIZE = 8192
F = 14

# MAX value for 18-bit unsigned fixed-point
SAT_MAX = 0x1FFFF

#converts floating point to fixed-point by multiplying by SCALE
SCALE = 1 << F

#step per LUT index since we want to cover 0 to 32 with LUT_SIZE (8192) entries
Q_PER_INDEX = 32.0 / LUT_SIZE

entries = []
for i in range(LUT_SIZE):
    q = i * Q_PER_INDEX
    if q == 0.0:
        val = SAT_MAX #q = 0 means q^-3/2 is infinite so we saturate to max value
    else:
        raw = q ** (-1.5) * SCALE #find the raw fixed-point value for q^-3/2
        if raw >= SAT_MAX: #if raw is more than max value, saturate
            val = SAT_MAX
        else:
            val = round(raw) #round raw to nearest integer
    entries.append(val)

with open("qinv32.mem", "w") as f:
    for v in entries:
        f.write(f"{v:05X}\n") #write each value as a 5-digit hexa number

print(f"qinv32.mem generated - check file directory")