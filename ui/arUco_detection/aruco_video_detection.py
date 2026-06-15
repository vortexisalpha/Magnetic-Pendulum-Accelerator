import sys
import cv2
import numpy as np
from time import perf_counter
from typing import Optional

import requests


# =============================================================================
# Configuration
# =============================================================================

_TEXT_FONT = cv2.FONT_HERSHEY_PLAIN

ARUCO_DICT_NAME = cv2.aruco.DICT_4X4_50

BOARD_CORNER_VAL = 1.8

# Simulation-coordinate destination points.
# Marker IDs 0,1,2,3 must correspond to these corners in this order, clockwise starting from top left
DST_POINTS = np.array([
    [-BOARD_CORNER_VAL,  BOARD_CORNER_VAL],
    [ BOARD_CORNER_VAL,  BOARD_CORNER_VAL],
    [ BOARD_CORNER_VAL, -BOARD_CORNER_VAL],
    [-BOARD_CORNER_VAL, -BOARD_CORNER_VAL],
], dtype=np.float32)

# Debug top-down view destination points.
WARPED_SIZE = 400
DST_IMSHOW = np.array([
    [0, 0],
    [WARPED_SIZE, 0],
    [WARPED_SIZE, WARPED_SIZE],
    [0, WARPED_SIZE],
], dtype=np.float32)

FLASK_BASE_URL = "http://35.179.111.223:5000"
REQUEST_TIMEOUT = 2.0

# Marker IDs reserved for board corners — never treated as magnets
BOARD_CORNER_IDS = {0, 1, 2, 3}

# Marker IDs that can represent magnets
MAGNET_IDS = {4, 5, 6}

# Number of consecutive frames a previously registered marker must be absent
# before it is removed from Flask. Prevents spurious removal due to detection
# noise or brief occlusion.
MISSING_FRAME_THRESHOLD = 5


# =============================================================================
# Dynamic magnet state
# =============================================================================

# Tracks which marker IDs are currently registered with Flask.
# Used to avoid duplicate /magnet_add calls and to detect disappearances.
registered_marker_ids: set[int] = set()

# Counts how many consecutive frames each registered marker has been absent.
# Key: marker ID. Value: consecutive missing frame count.
# Only markers currently in registered_marker_ids appear here.
missing_frame_counts: dict[int, int] = {}

# =============================================================================
# Performance measurement
# =============================================================================
# Cumulative per-stage latencies (seconds). Keys are the 7 pipeline stages.
cumulative_latencies: dict[str, float] = {
    "detect_markers": 0.0,
    "find_centres": 0.0,
    "extract_board_and_tokens": 0.0,
    "compute_homography": 0.0,
    "transform_tokens": 0.0,
    "sync_flask": 0.0,
    "annotate_and_warp": 0.0,
}

# Number of frames processed (used to compute averages on exit)
frame_count: int = 0

# Additional runtime timers (measuring calls in main loop)
cumulative_latencies.setdefault("process_frame_call", 0.0)
cumulative_latencies.setdefault("annotate_fps_call", 0.0)
cumulative_latencies.setdefault("imshow_frame", 0.0)
cumulative_latencies.setdefault("imshow_warped", 0.0)


# =============================================================================
# Network I/O
# =============================================================================

def post_json(endpoint: str, payload: dict) -> bool:
    """
    Side-effect function.
    Sends a JSON POST request to the Flask server.
    Returns True on HTTP 200, False on any failure.
    """
    try:
        response = requests.post(
            f"{FLASK_BASE_URL}/{endpoint}",
            json=payload,
            timeout=REQUEST_TIMEOUT,
        )
        if response.status_code != 200:
            print(f"POST /{endpoint} failed: {response.status_code} {response.text}")
            return False
        return True
    except requests.exceptions.RequestException as e:
        print(f"POST /{endpoint} exception: {e}")
        return False


def flask_magnet_add(marker_id: int, x: float, y: float) -> bool:
    """
    Side-effect function.
    Registers a new magnet with Flask.
    The uid is derived directly from the ArUco marker ID for stable identity.
    """
    return post_json("magnet_add", {
        "uid": f"marker_{marker_id}",
        "x": x,
        "y": y,
    })


def flask_magnet_remove(marker_id: int) -> bool:
    """
    Side-effect function.
    Removes a magnet from Flask by its stable uid.
    """
    return post_json("magnet_remove", {
        "uid": f"marker_{marker_id}",
    })


def flask_magnet_update(marker_id: int, x: float, y: float) -> bool:
    """
    Side-effect function.
    Updates the position of an already-registered magnet on Flask.
    """
    return post_json("magnet_update_position", {
        "uid": f"marker_{marker_id}",
        "x": x,
        "y": y,
    })


def sync_magnets_to_flask(detected_mag_centres: dict[int, tuple[float, float]]) -> None:
    """
    Side-effect function.
    The main dynamic magnet sync logic. Called every frame after homography mapping.

    Compares the currently detected magnet set against the registered set and:
      - Adds newly appeared magnets to Flask
      - Updates positions of already-registered magnets
      - Increments missing frame counters for absent magnets
      - Removes magnets that have been absent for more than MISSING_FRAME_THRESHOLD frames

    detected_mag_centres: dict mapping marker_id -> (sim_x, sim_y) for
                          all magnet markers visible this frame.
    """
    currently_detected_ids = set(detected_mag_centres.keys())

    # --- Handle detected markers ---
    for marker_id, (sim_x, sim_y) in detected_mag_centres.items():

        if marker_id not in registered_marker_ids:
            # Newly appeared marker — add to Flask and register locally.
            # Flask's magnet_add is idempotent (see Flask changes), so a
            # crash-and-restart scenario won't cause duplication.
            ok = flask_magnet_add(marker_id, sim_x, sim_y)
            if ok:
                registered_marker_ids.add(marker_id)
                missing_frame_counts[marker_id] = 0
                print(f"Registered magnet marker_{marker_id}")
        else:
            # Already registered — update position and reset missing counter.
            flask_magnet_update(marker_id, sim_x, sim_y)
            missing_frame_counts[marker_id] = 0

    # --- Handle absent markers ---
    # Iterate over a snapshot of registered IDs since we may modify the set
    for marker_id in list(registered_marker_ids):

        if marker_id in currently_detected_ids:
            # Detected this frame, already handled above
            continue

        # Marker was registered but not detected this frame
        missing_frame_counts[marker_id] = missing_frame_counts.get(marker_id, 0) + 1

        if missing_frame_counts[marker_id] > MISSING_FRAME_THRESHOLD:
            # Absent for too long — treat as genuinely removed
            ok = flask_magnet_remove(marker_id)
            if ok:
                registered_marker_ids.discard(marker_id)
                missing_frame_counts.pop(marker_id, None)
                print(f"Removed magnet marker_{marker_id} after {MISSING_FRAME_THRESHOLD} missing frames")
        else:
            # Within threshold — hold last position on Flask, do nothing this frame
            print(f"marker_{marker_id} missing for {missing_frame_counts[marker_id]} frame(s), holding position")


def cleanup_all_registered_magnets() -> None:
    """
    Side-effect function.
    Removes all currently registered magnets from Flask on exit.
    Used in the finally block to leave Flask in a clean state.
    """
    for marker_id in list(registered_marker_ids):
        flask_magnet_remove(marker_id)
        registered_marker_ids.discard(marker_id)
        missing_frame_counts.pop(marker_id, None)
    print("All registered magnets removed from Flask.")


# =============================================================================
# Computer vision: detection and geometry
# =============================================================================

def detect_aruco_markers(
    frame: np.ndarray,
    detector: cv2.aruco.ArucoDetector,
) -> tuple[list, Optional[np.ndarray]]:
    """
    Pure-ish vision function.
    Converts frame to greyscale, downsizes, detects ArUco markers and scales up the corner pixel coordinates.
    """
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    small = cv2.resize(gray, None, fx=0.25, fy=0.25, interpolation=cv2.INTER_AREA)
    corners, marker_ids, _ = detector.detectMarkers(small)
    for corner in corners:
        corner *= 4
    return corners, marker_ids


def diagonal_intersection(single_marker_corners: np.ndarray) -> Optional[tuple[float, float]]:
    """
    Pure geometry function.
    Finds the marker centre as the intersection of the two projected diagonals
    using homogeneous line intersection, avoiding vertical-line edge cases.
    """
    pts = single_marker_corners[0].astype(np.float64)

    p0 = np.array([pts[0][0], pts[0][1], 1.0])
    p1 = np.array([pts[1][0], pts[1][1], 1.0])
    p2 = np.array([pts[2][0], pts[2][1], 1.0])
    p3 = np.array([pts[3][0], pts[3][1], 1.0])

    line_02 = np.cross(p0, p2)
    line_13 = np.cross(p1, p3)
    centre_h = np.cross(line_02, line_13)

    if abs(centre_h[2]) < 1e-9:
        return None

    return float(centre_h[0] / centre_h[2]), float(centre_h[1] / centre_h[2])


def find_marker_centres(
    corners: list,
    marker_ids: Optional[np.ndarray],
) -> dict[int, tuple[float, float]]:
    """
    Pure function.
    Returns a dict mapping marker ID -> (x_center, y_center) in pixel space.
    Keyed by ID so detection order does not affect correctness.
    """
    centres: dict[int, tuple[float, float]] = {}

    if marker_ids is None:
        return centres

    for marker_id, single_marker_corners in zip(marker_ids, corners):
        centre = diagonal_intersection(single_marker_corners)
        if centre is None:
            continue
        centres[int(marker_id[0])] = centre

    return centres


def extract_board_and_tokens(
    centres: dict[int, tuple[float, float]],
) -> tuple[list[Optional[tuple[float, float]]], dict[int, tuple[float, float]]]:
    """
    Pure function.
    Splits detected marker centres into board corners and magnet tokens.

    Board corners: marker IDs 0-3, returned as an ordered list indexed by ID.
    Magnet tokens: marker IDs in MAGNET_IDS, returned as a dict keyed by marker ID.

    Returning tokens as a dict rather than a list preserves identity — if marker 5
    is absent, the dict simply has no key 5 rather than shifting marker 6 to index 1.
    """
    board_corners_pos = [centres.get(i) for i in range(4)]

    # Only include IDs that are designated magnet IDs
    detected_mag_centres_px = {
        m_id: centres[m_id]
        for m_id in MAGNET_IDS
        if m_id in centres
    }

    return board_corners_pos, detected_mag_centres_px


def compute_homographies(
    board_corners_pos: list[Optional[tuple[float, float]]],
) -> Optional[tuple[np.ndarray, np.ndarray]]:
    """
    Pure geometry function.
    Returns (H_sim, H_view) or None if any board corner is missing.
    """
    if any(corner is None for corner in board_corners_pos):
        return None

    board_corners_f = np.array(board_corners_pos, dtype=np.float32)
    H_sim,  _ = cv2.findHomography(board_corners_f, DST_POINTS)
    H_view, _ = cv2.findHomography(board_corners_f, DST_IMSHOW)

    if H_sim is None or H_view is None:
        return None

    return H_sim, H_view


def transform_mag_centres(
    detected_mag_centres_px: dict[int, tuple[float, float]],
    H_sim: np.ndarray,
) -> dict[int, tuple[float, float]]:
    """
    Pure geometry function.
    Maps magnet pixel-space centres to simulation coordinates using H_sim.

    Returns a dict keyed by marker ID so identity is preserved through the transform.
    Previously this returned a positional list, which lost the marker ID association
    and caused colour-shifting bugs when a middle marker was removed.
    """
    sim_coords: dict[int, tuple[float, float]] = {}

    for marker_id, px_centre in detected_mag_centres_px.items():
        token_f = np.array([[px_centre]], dtype=np.float32)  # shape (1, 1, 2)
        mapped = cv2.perspectiveTransform(token_f, H_sim)
        sim_x, sim_y = mapped[0][0]
        # Explicitly cast numpy.float32 to Python float for JSON serialisation
        sim_coords[marker_id] = (float(sim_x), float(sim_y))

    return sim_coords


# =============================================================================
# Visualisation helpers
# =============================================================================

def annotate_marker_centres(
    frame: np.ndarray,
    centres: dict[int, tuple[float, float]],
) -> None:
    """Side-effect. Draws centre dots on all detected markers."""
    for centre in centres.values():
        x_px = int(round(centre[0]))
        y_px = int(round(centre[1]))
        cv2.circle(frame, (x_px, y_px), radius=5, color=(0, 0, 255), thickness=-1)


def annotate_mapped_mags_pos(
    frame: np.ndarray,
    detected_mag_centres_px: dict[int, tuple[float, float]],
    detected_mag_centres_sim: dict[int, tuple[float, float]],
) -> None:
    """
    Side-effect. Draws simulation coordinates next to each detected magnet token.
    Both dicts are keyed by marker ID so annotation is always tied to the correct marker.
    """
    for marker_id, px_centre in detected_mag_centres_px.items():
        if marker_id not in detected_mag_centres_sim:
            continue
        sim_pos = detected_mag_centres_sim[marker_id]
        label = f"ID {marker_id}: ({sim_pos[0]:.2f}, {sim_pos[1]:.2f})"
        origin = (int(round(px_centre[0])), int(round(px_centre[1])))
        cv2.putText(frame, label, origin, _TEXT_FONT, 2, (0, 255, 0), thickness=2)


def annotate_fps(frame: np.ndarray, fps: float) -> None:
    """Side-effect. Draws FPS counter at bottom-right."""
    text = f"FPS: {fps:.1f}"
    scale, thickness, margin = 2, 2, 10
    (text_w, _), baseline = cv2.getTextSize(text, _TEXT_FONT, scale, thickness)
    h, w = frame.shape[:2]
    cv2.putText(
        frame, text,
        (w - text_w - margin, h - baseline - margin),
        _TEXT_FONT, scale, (0, 255, 0), thickness,
    )


def make_warped_debug_view(frame: np.ndarray, H_view: np.ndarray) -> np.ndarray:
    """Pure-ish. Returns a top-down warped view of the board."""
    return cv2.warpPerspective(frame.copy(), H_view, (WARPED_SIZE, WARPED_SIZE))


# =============================================================================
# Per-frame pipeline
# =============================================================================

def process_frame(
    frame: np.ndarray,
    detector: cv2.aruco.ArucoDetector,
) -> tuple[np.ndarray, dict]:
    """
    Orchestrates the per-frame pipeline:
      1. Detect ArUco markers
      2. Find marker centres in pixel space
      3. Split into board corners and magnet tokens
      4. Compute homographies from board corners
      5. Transform magnet pixel centres to simulation coordinates
      6. Sync magnet state to Flask dynamically
      7. Annotate frame and return warped debug view
    """
    # Prepare per-stage timers
    stage_times = {
        "detect_markers": 0.0,
        "find_centres": 0.0,
        "extract_board_and_tokens": 0.0,
        "compute_homography": 0.0,
        "transform_tokens": 0.0,
        "sync_flask": 0.0,
        "annotate_and_warp": 0.0,
    }

    # Stage 1: detection
    t0 = perf_counter()
    corners, marker_ids = detect_aruco_markers(frame, detector)
    if corners and marker_ids is not None:
        cv2.aruco.drawDetectedMarkers(frame, corners, marker_ids)
    t1 = perf_counter()
    stage_times["detect_markers"] = t1 - t0

    # If no markers, still sync with empty detection set and return
    if not corners or marker_ids is None:
        sync_magnets_to_flask({})
        return frame, stage_times

    # Stage 2: find centres
    t0 = perf_counter()
    centres = find_marker_centres(corners, marker_ids)
    t1 = perf_counter()
    stage_times["find_centres"] = t1 - t0

    # Stage 3: extract board corners and token centres
    t0 = perf_counter()
    board_corners_pos, detected_mag_centres_px = extract_board_and_tokens(centres)
    t1 = perf_counter()
    stage_times["extract_board_and_tokens"] = t1 - t0

    # Stage 4: compute homography
    t0 = perf_counter()
    homographies = compute_homographies(board_corners_pos)
    t1 = perf_counter()
    stage_times["compute_homography"] = t1 - t0

    if homographies is None:
        print("Not all board corners detected — skipping homography and magnet sync.")
        return frame, stage_times

    H_sim, H_view = homographies

    # Stage 5: transform token centres to simulation coords
    t0 = perf_counter()
    detected_mag_centres_sim = transform_mag_centres(detected_mag_centres_px, H_sim)
    t1 = perf_counter()
    stage_times["transform_tokens"] = t1 - t0

    # Stage 6: sync to Flask
    t0 = perf_counter()
    sync_magnets_to_flask(detected_mag_centres_sim)
    t1 = perf_counter()
    stage_times["sync_flask"] = t1 - t0

    # Stage 7: annotation + warped debug view
    t0 = perf_counter()
    annotate_marker_centres(frame, centres)
    annotate_mapped_mags_pos(frame, detected_mag_centres_px, detected_mag_centres_sim)
    warped = make_warped_debug_view(frame, H_view)
    t1 = perf_counter()
    stage_times["annotate_and_warp"] = t1 - t0

    return warped, stage_times


# =============================================================================
# Main webcam loop
# =============================================================================

def stream_webcam_with_aruco(
    camera_index: int = 0,
    window_name_1: str = "ArUco Webcam Stream",
    window_name_2: str = "Warped Webcam Stream",
) -> None:
    dictionary = cv2.aruco.getPredefinedDictionary(ARUCO_DICT_NAME)
    params = cv2.aruco.DetectorParameters()
    detector = cv2.aruco.ArucoDetector(dictionary, params)

    cap = cv2.VideoCapture(camera_index)
    if not cap.isOpened():
        raise RuntimeError(f"Could not open camera with index {camera_index}.")

    prev_time = -1
    fps = 0
    i = 0
    averageFPS = 0
    try:
        while True:
            ret, frame = cap.read()
            if not ret:
                print("Failed to grab frame from camera; exiting.", file=sys.stderr)
                break
            now = perf_counter()
            if(prev_time != -1):
                fps = 1.0 / (now - prev_time)
                i+=1
                averageFPS = (averageFPS*(i-1)+fps)/i
            prev_time = now

            # Time the entire process_frame call
            t_pf0 = perf_counter()
            warped_frame, stage_times = process_frame(frame, detector)
            t_pf1 = perf_counter()
            proc_time = t_pf1 - t_pf0

            # Aggregate per-stage latencies
            global frame_count
            frame_count += 1
            for name, val in stage_times.items():
                cumulative_latencies[name] = cumulative_latencies.get(name, 0.0) + float(val)
            cumulative_latencies["process_frame_call"] += proc_time

            # Time annotate_fps
            t_a0 = perf_counter()
            annotate_fps(frame, fps)
            t_a1 = perf_counter()
            cumulative_latencies["annotate_fps_call"] += (t_a1 - t_a0)

            # Time imshow for the main frame
            t_i0 = perf_counter()
            cv2.imshow(window_name_1, frame)
            t_i1 = perf_counter()
            cumulative_latencies["imshow_frame"] += (t_i1 - t_i0)

            # Time imshow for the warped view
            t_i0 = perf_counter()
            cv2.imshow(window_name_2, warped_frame)
            t_i1 = perf_counter()
            cumulative_latencies["imshow_warped"] += (t_i1 - t_i0)

            key = cv2.waitKey(1) & 0xFF
            if key in (ord("q"), 27):
                break

    finally:
        cap.release()
        cv2.destroyAllWindows()
        print(f"Average framerate: {averageFPS}")

        # Output average per-stage latencies
        if frame_count > 0:
            print("Average per-stage latencies (seconds):")
            for name, total in cumulative_latencies.items():
                print(f"  {name}: {total / frame_count:.6f}")
        else:
            print("No frames processed; no latency data available.")


# =============================================================================
# Entry point
# =============================================================================

if __name__ == "__main__":
    try:
        stream_webcam_with_aruco()
    finally:
        # Clean up all magnets we registered during this session
        print("Cleaning up registered magnets from Flask...")
        cleanup_all_registered_magnets()