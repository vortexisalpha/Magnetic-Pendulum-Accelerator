import math
import requests

SCREEN_SIZE_X = 160
SCREEN_SIZE_Y = 120

# Four "magnets" — one per category (top 2 bits)
magnets = [
    (40, 30),    # category 0  -> black in your palette
    (120, 30),   # category 1  -> red
    (40, 90),    # category 2  -> green
    (120, 90),   # category 3  -> blue
]

image = []
for y in range(SCREEN_SIZE_Y):
    row = []
    for x in range(SCREEN_SIZE_X):
        # Find which magnet this pixel is closest to
        distances = [math.hypot(x - mx, y - my) for mx, my in magnets]
        nearest = min(range(4), key=lambda i: distances[i])
        dist = distances[nearest]

        category = nearest                       # 0..3, top 2 bits
        value = min(int(dist * 40), 4095)        # 0..4095, bottom 12 bits

        packed = (category << 12) | value
        row.append(packed)
    image.append(row)

resp = requests.post(
    "http://localhost:5000/image",
    json={"image": image},
)

for i, (uid, x, y) in enumerate([
    ("m0", 40,  30),
    ("m1", 120, 30),
    ("m2", 40,  90),
    ("m3", 120, 90),
]):
    requests.post("http://localhost:5000/magnet_add",
                  json={"uid": uid, "x": x, "y": y})

print(resp.status_code, resp.json())

