# Magnetic Pendulum API

API reference for the Flask backend that stores magnetic pendulum configuration data.

## Base URL

```text
http://localhost:5000
```

## Data model

### Magnet

Represents one magnet on the grid.

```json
{
  "uid": "marker_1",
  "x": 40.0,
  "y": 60.0
}
```

| Field | Type | Required | Description |
|---|---:|---:|---|
| `uid` | string | Yes | Unique magnet identifier. Intended to match the ArUco marker type. |
| `x` | float | Yes | Magnet x coordinate. |
| `y` | float | Yes | Magnet y coordinate. |

### Grid

Represents the visible simulation area.

```json
{
  "x_min": 0,
  "x_max": 160,
  "y_min": 0,
  "y_max": 120
}
```

| Field | Type | Default | Description |
|---|---:|---:|---|
| `x_min` | float | `0` | Minimum x coordinate. |
| `x_max` | float | `160` | Maximum x coordinate. |
| `y_min` | float | `0` | Minimum y coordinate. |
| `y_max` | float | `120` | Maximum y coordinate. |

### Magnetic pendulum data

The server stores the full simulation state in `mp_data`.

```json
{
  "magnets": {
    "magnet_0": {
      "x": 40.0,
      "y": 60.0
    }
  },
  "grid": {
    "x_min": 0,
    "x_max": 160,
    "y_min": 0,
    "y_max": 120
  },
  "magnetic_strength": 1,
  "damping_factor": 1,
  "pendulum_height": 1,
  "pendulum_length": 1
}
```

## Endpoints

## `GET /`

Checks that the backend is running.

### Response

```json
{
  "ping": "true"
}
```

### Example

```bash
curl http://localhost:5000/
```

## `GET /info`

Returns the current magnetic pendulum data.

### Response

```json
{
  "magnets": {
    "magnet_0": {
      "x": 40.0,
      "y": 60.0
    }
  },
  "grid": {
    "x_min": 0,
    "x_max": 160,
    "y_min": 0,
    "y_max": 120
  },
  "magnetic_strength": 1,
  "damping_factor": 1,
  "pendulum_height": 1,
  "pendulum_length": 1
}
```

### Example

```bash
curl http://localhost:5000/info
```

## `POST /magnet_add`

Adds a magnet to the current magnetic pendulum state.

### Request body

```json
{
  "uid": "marker_1",
  "x": 40.0,
  "y": 60.0
}
```

| Field | Type | Required | Description |
|---|---:|---:|---|
| `uid` | string | Yes | Unique magnet identifier. |
| `x` | float | Yes | Magnet x coordinate. |
| `y` | float | Yes | Magnet y coordinate. |

### Response

```json
{
  "ok": 200
}
```

### Example

```bash
curl -X POST http://localhost:5000/magnet_add \
  -H "Content-Type: application/json" \
  -d '{"uid": "marker_1", "x": 40.0, "y": 60.0}'
```

## `POST /magnet_remove`

Removes a magnet by `uid`.

### Request body

```json
{
  "uid": "marker_1"
}
```

| Field | Type | Required | Description |
|---|---:|---:|---|
| `uid` | string | Yes | Identifier of the magnet to remove. |

### Response

```json
{
  "ok": 200
}
```

### Example

```bash
curl -X POST http://localhost:5000/magnet_remove \
  -H "Content-Type: application/json" \
  -d '{"uid": "marker_1"}'
```

## `GET /magnet_update_position`

Updates the position of an existing magnet.

### Request body

```json
{
  "uid": "marker_1",
  "x": 80.0,
  "y": 30.0
}
```

| Field | Type | Required | Description |
|---|---:|---:|---|
| `uid` | string | Yes | Identifier of the magnet to update. |
| `x` | float | Yes | New x coordinate. |
| `y` | float | Yes | New y coordinate. |

### Response

```json
{
  "ok": 200
}
```

### Example

```bash
curl -X GET http://localhost:5000/magnet_update_position \
  -H "Content-Type: application/json" \
  -d '{"uid": "marker_1", "x": 80.0, "y": 30.0}'
```

### Note

This endpoint currently uses `GET` while reading a JSON request body. Since it updates server state, it should probably use `POST` or `PATCH`.

## `GET /magnetic_strength`

Updates the magnetic strength value.

### Request body

```json
{
  "magnetic_strength": 2.5
}
```

| Field | Type | Required | Description |
|---|---:|---:|---|
| `magnetic_strength` | float | Yes | New magnetic strength value. |

### Response

```json
{
  "ok": 200
}
```

### Example

```bash
curl -X GET http://localhost:5000/magnetic_strength \
  -H "Content-Type: application/json" \
  -d '{"magnetic_strength": 2.5}'
```

### Note

This endpoint currently uses `GET` while updating server state. It should probably use `POST` or `PATCH`.

## `GET /damping_factor`

Updates the damping factor value.

### Request body

```json
{
  "damping_factor": 0.8
}
```

| Field | Type | Required | Description |
|---|---:|---:|---|
| `damping_factor` | float | Yes | New damping factor value. |

### Response

```json
{
  "ok": 200
}
```

### Example

```bash
curl -X GET http://localhost:5000/damping_factor \
  -H "Content-Type: application/json" \
  -d '{"damping_factor": 0.8}'
```

### Note

This endpoint currently uses `GET` while updating server state. It should probably use `POST` or `PATCH`.

## `GET /pendulum_height`

Updates the pendulum height value.

### Request body

```json
{
  "pendulum_height": 10.0
}
```

| Field | Type | Required | Description |
|---|---:|---:|---|
| `pendulum_height` | float | Yes | New pendulum height value. |

### Response

```json
{
  "ok": 200
}
```

### Example

```bash
curl -X GET http://localhost:5000/pendulum_height \
  -H "Content-Type: application/json" \
  -d '{"pendulum_height": 10.0}'
```

### Note

This endpoint currently uses `GET` while updating server state. It should probably use `POST` or `PATCH`.

## `GET /pendulum_length`

Updates the pendulum length value.

### Request body

```json
{
  "pendulum_length": 20.0
}
```

| Field | Type | Required | Description |
|---|---:|---:|---|
| `pendulum_length` | float | Yes | New pendulum length value. |

### Response

```json
{
  "ok": 200
}
```

### Example

```bash
curl -X GET http://localhost:5000/pendulum_length \
  -H "Content-Type: application/json" \
  -d '{"pendulum_length": 20.0}'
```

### Note

This endpoint currently uses `GET` while updating server state. It should probably use `POST` or `PATCH`.

## Status responses

Most update endpoints currently return:

```json
{
  "ok": 200
}
```

This is a JSON response body. The actual HTTP status code is still Flask's default `200 OK` unless a different status code is returned explicitly.

## Suggested endpoint cleanup

The current API works best if read actions use `GET` and update actions use `POST` or `PATCH`.

Suggested methods:

| Endpoint | Current method | Suggested method |
|---|---:|---:|
| `/` | `GET` | `GET` |
| `/info` | `GET` | `GET` |
| `/magnet_add` | `POST` | `POST` |
| `/magnet_remove` | `POST` | `POST` |
| `/magnet_update_position` | `GET` | `PATCH` |
| `/magnetic_strength` | `GET` | `PATCH` |
| `/damping_factor` | `GET` | `PATCH` |
| `/pendulum_height` | `GET` | `PATCH` |
| `/pendulum_length` | `GET` | `PATCH` |

## Implementation notes

The code has a few points worth fixing before relying on `/info`:

1. `construct_mpdata_json` creates `data = {}` but writes to `data["magnets"]` and `data["grid"]` before creating those keys.
2. `construct_mpdata_json` reads `MPData.magnetic_strength` instead of `mp_data.magnetic_strength`.
3. `MPData.mag_list: list[Magnet] = []` uses a mutable default list. Use `field(default_factory=list)`.
4. `MPData.grid: Grid = Grid()` uses a mutable default object. Use `field(default_factory=Grid)`.
5. The update endpoints that read JSON bodies should declare methods explicitly, such as `methods=["POST"]` or `methods=["PATCH"]`.
