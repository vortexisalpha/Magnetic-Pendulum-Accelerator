import numpy as np
import matplotlib.pyplot as plt

W, H = 160, 120

# Change these lines in image.py:
basin   = np.fromfile("cpp_outputs/basin.bin",
                       dtype=np.int16).reshape(H, W)
steps   = np.fromfile("cpp_outputs/steps.bin",
                       dtype=np.int32).reshape(H, W)
settled = np.fromfile("cpp_outputs/settled.bin",
                       dtype=np.uint8).reshape(H, W)

def basin_to_rgb(basin, settled=None):
    COLOURS = {
         0: [220,  60,  60],   # red   — magnet 0
         1: [ 60, 180,  60],   # green — magnet 1
         2: [ 60,  60, 220],   # blue  — magnet 2
        -1: [ 30,  30,  30],   # dark grey — unresolved
    }
    h, w = basin.shape
    img  = np.zeros((h, w, 3), dtype=np.uint8)
    for label, colour in COLOURS.items():
        img[basin == label] = colour

    # Darken timeout pixels
    if settled is not None:
        timeout = (settled == 0) & (basin != -1)
        img[timeout] = (img[timeout] * 0.45).astype(np.uint8)

    return img

fig, axes = plt.subplots(1, 2, figsize=(14, 6))

# Basin image
img = basin_to_rgb(basin, settled)
axes[0].imshow(img, origin="lower", interpolation="nearest")
axes[0].set_title("Fixed-Point Basin Map (C++ Baseline)")
axes[0].set_xlabel("Pixel column")
axes[0].set_ylabel("Pixel row")

# Steps heatmap
im = axes[1].imshow(steps, origin="lower",
                    cmap="inferno", interpolation="nearest")
plt.colorbar(im, ax=axes[1], label="Steps to settle")
axes[1].set_title("Iterations per pixel")
axes[1].set_xlabel("Pixel column")
axes[1].set_ylabel("Pixel row")

plt.tight_layout()
plt.savefig("cpp_outputs/results.png", dpi=200)
plt.show()
print("Saved results.png")
