import sys
import threading
from dataclasses import dataclass

import cv2
import numpy as np
from time import perf_counter
from typing import Optional

import requests


# Configuration

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
REQUEST_TIMEOUT = (0.15, 0.25)
SLOW_POST_SECONDS = 0.2

# Marker IDs reserved for board corners — never treated as magnets
BOARD_CORNER_IDS = {0, 1, 2, 3}

# Marker IDs that can represent magnets
MAGNET_IDS = {4, 5, 6}

# Number of consecutive frames a previously registered marker must be absent
# before it is removed from Flask. Prevents spurious removal due to detection
# noise or brief occlusion.
MISSING_FRAME_THRESHOLD = 8

# v3 latency optimisation: only send /magnet_update_position if the measured
# marker position has moved by more than this amount in simulation coordinates.
# The board range is roughly [-1.8, 1.8], so 0.02 is a small deadband. Tune this
# based on your observed ArUco jitter and acceptable responsiveness.
POSITION_EPSILON = 0.08
POSITION_EPSILON_SQ = POSITION_EPSILON * POSITION_EPSILON


# Dynamic magnet state

@dataclass
class MagnetTrackState:
    """
    Client-side state for one magnet marker synchronised with Flask.

    last_sent_pos is the last position that Flask has actually accepted, not
    merely the latest detected position. Comparing new detections against this
    value ensures small frame-to-frame jitter does not gradually drift the
    reference without ever notifying Flask.
    """
    last_sent_pos: tuple[float, float]
    missing_frames: int = 0


# Performance measurement

# Cumulative per-stage latencies (seconds).
cumulative_latencies: dict[str, float] = {
    "cap_read": 0.0,
    "detect_markers": 0.0,
    "find_centres": 0.0,
    "extract_board_and_tokens": 0.0,
    "compute_homography": 0.0,
    "transform_tokens": 0.0,
    "network_submit": 0.0,
    "annotate_and_warp": 0.0,
    "process_frame_call": 0.0,
    "annotate_fps_call": 0.0,
    "imshow_frame": 0.0,
    "imshow_warped": 0.0,
    "wait_key": 0.0,
}
max_latencies: dict[str, float] = dict.fromkeys(cumulative_latencies, 0.0)

# Number of frames processed (used to compute averages on exit)
frame_count: int = 0

def record_latency(name: str, value: float) -> None:
    cumulative_latencies[name] = cumulative_latencies.get(name, 0.0) + float(value)
    max_latencies[name] = max(max_latencies.get(name, 0.0), float(value))


def _position_changed_enough(
    current_pos: tuple[float, float],
    last_sent_pos: tuple[float, float],
) -> bool:
    """
    Pure helper.
    Returns True when the marker has moved far enough in simulation coordinates
    to justify a Flask update.

    v3 latency optimisation: compare squared distance instead of using sqrt.
    This avoids small jitter triggering redundant synchronous HTTP POSTs.
    """
    dx = current_pos[0] - last_sent_pos[0]
    dy = current_pos[1] - last_sent_pos[1]
    return (dx * dx + dy * dy) > POSITION_EPSILON_SQ


class FlaskMagnetSyncWorker:
    """
    Owns Flask magnet state and performs network I/O off the camera thread.

    The main loop only submits the newest detected magnet snapshot. If Flask is
    slow, pending snapshots are replaced instead of queued so the worker catches
    up to the latest physical marker state.
    """

    def __init__(self, base_url: str, timeout: tuple[float, float]) -> None:
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout
        self.session = requests.Session()
        self.tracked_magnets: dict[int, MagnetTrackState] = {}
        self._lock = threading.Lock()
        self._event = threading.Event()
        self._stop = threading.Event()
        self._latest_snapshot: Optional[dict[int, tuple[float, float]]] = None
        self._thread = threading.Thread(
            target=self._run,
            name="FlaskMagnetSync",
            daemon=True,
        )
        self._started = False
        self._closed = False
        self.submitted_snapshots = 0
        self.processed_snapshots = 0
        self.replaced_pending_snapshots = 0
        self.post_count = 0
        self.post_failures = 0
        self.post_total_seconds = 0.0
        self.post_max_seconds = 0.0

    def start(self) -> None:
        if self._started:
            return
        self._started = True
        self._thread.start()

    def submit(self, detected_mag_centres: dict[int, tuple[float, float]]) -> None:
        snapshot = {
            marker_id: (float(pos[0]), float(pos[1]))
            for marker_id, pos in detected_mag_centres.items()
        }
        with self._lock:
            if self._latest_snapshot is not None:
                self.replaced_pending_snapshots += 1
            self._latest_snapshot = snapshot
            self.submitted_snapshots += 1
            self._event.set()

    def stop_and_cleanup(self) -> None:
        if self._started:
            self._stop.set()
            self._event.set()
            self._thread.join(timeout=2.0)
            if self._thread.is_alive():
                print("Flask sync worker did not stop within 2.0 seconds.")

        for marker_id in list(self.tracked_magnets.keys()):
            if self._post_json("magnet_remove", {"uid": f"marker_{marker_id}"}):
                self.tracked_magnets.pop(marker_id, None)

        if not self._closed:
            self.session.close()
            self._closed = True
        print("All registered magnets removed from Flask.")

    def print_stats(self) -> None:
        avg_post = self.post_total_seconds / self.post_count if self.post_count else 0.0
        print("Flask sync worker stats:")
        print(f"  submitted snapshots: {self.submitted_snapshots}")
        print(f"  processed snapshots: {self.processed_snapshots}")
        print(f"  replaced pending snapshots: {self.replaced_pending_snapshots}")
        print(f"  POST count: {self.post_count}")
        print(f"  POST failures: {self.post_failures}")
        print(f"  POST avg/max seconds: {avg_post:.6f} / {self.post_max_seconds:.6f}")

    def _run(self) -> None:
        while True:
            self._event.wait(timeout=0.1)

            with self._lock:
                snapshot = self._latest_snapshot
                self._latest_snapshot = None
                self._event.clear()

            if snapshot is not None:
                self.processed_snapshots += 1
                self._sync_snapshot(snapshot)

            if self._stop.is_set():
                with self._lock:
                    if self._latest_snapshot is None:
                        break

    def _sync_snapshot(self, detected_mag_centres: dict[int, tuple[float, float]]) -> None:
        currently_detected_ids = set(detected_mag_centres.keys())

        for marker_id, current_pos in detected_mag_centres.items():
            sim_x, sim_y = current_pos

            if marker_id not in self.tracked_magnets:
                ok = self._post_json("magnet_add", {
                    "uid": f"marker_{marker_id}",
                    "x": sim_x,
                    "y": sim_y,
                })
                if ok:
                    self.tracked_magnets[marker_id] = MagnetTrackState(
                        last_sent_pos=(sim_x, sim_y),
                        missing_frames=0,
                    )
                    print(f"Registered magnet marker_{marker_id}")
                continue

            state = self.tracked_magnets[marker_id]
            state.missing_frames = 0

            if _position_changed_enough(current_pos, state.last_sent_pos):
                ok = self._post_json("magnet_update_position", {
                    "uid": f"marker_{marker_id}",
                    "x": sim_x,
                    "y": sim_y,
                })
                if ok:
                    state.last_sent_pos = (sim_x, sim_y)
                    print(f"Updated marker_{marker_id}: ({sim_x:.3f}, {sim_y:.3f})")

        for marker_id in list(self.tracked_magnets.keys()):
            if marker_id in currently_detected_ids:
                continue

            state = self.tracked_magnets[marker_id]
            state.missing_frames += 1

            if state.missing_frames > MISSING_FRAME_THRESHOLD:
                ok = self._post_json("magnet_remove", {
                    "uid": f"marker_{marker_id}",
                })
                if ok:
                    self.tracked_magnets.pop(marker_id, None)
                    print(
                        f"Removed magnet marker_{marker_id} after "
                        f"{MISSING_FRAME_THRESHOLD} missing frames"
                    )

    def _post_json(self, endpoint: str, payload: dict) -> bool:
        t0 = perf_counter()
        ok = False
        try:
            response = self.session.post(
                f"{self.base_url}/{endpoint}",
                json=payload,
                timeout=self.timeout,
            )
            ok = response.status_code == 200
            if not ok:
                print(f"POST /{endpoint} failed: {response.status_code} {response.text}")
            return ok
        except requests.exceptions.RequestException as e:
            print(f"POST /{endpoint} exception: {e}")
            return False
        finally:
            elapsed = perf_counter() - t0
            self.post_count += 1
            self.post_total_seconds += elapsed
            self.post_max_seconds = max(self.post_max_seconds, elapsed)
            if not ok:
                self.post_failures += 1
            if elapsed >= SLOW_POST_SECONDS:
                print(f"Slow POST /{endpoint}: {elapsed:.3f} seconds")


flask_sync_worker = FlaskMagnetSyncWorker(FLASK_BASE_URL, REQUEST_TIMEOUT)


def sync_magnets_to_flask(detected_mag_centres: dict[int, tuple[float, float]]) -> None:
    flask_sync_worker.submit(detected_mag_centres)


def cleanup_all_registered_magnets() -> None:
    flask_sync_worker.stop_and_cleanup()
    flask_sync_worker.print_stats()


# Computer vision: detection and geometry

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


# Visualisation helpers

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


# Per-frame pipeline

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
        "network_submit": 0.0,
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
        t0 = perf_counter()
        sync_magnets_to_flask({})
        t1 = perf_counter()
        stage_times["network_submit"] = t1 - t0
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
    stage_times["network_submit"] = t1 - t0

    # Stage 7: annotation + warped debug view
    t0 = perf_counter()
    annotate_marker_centres(frame, centres)
    annotate_mapped_mags_pos(frame, detected_mag_centres_px, detected_mag_centres_sim)
    warped = make_warped_debug_view(frame, H_view)
    t1 = perf_counter()
    stage_times["annotate_and_warp"] = t1 - t0

    return warped, stage_times


# Main webcam loop

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

    flask_sync_worker.start()

    prev_time = -1
    fps = 0
    i = 0
    averageFPS = 0
    try:
        while True:
            t_cap0 = perf_counter()
            ret, frame = cap.read()
            t_cap1 = perf_counter()
            cap_read_time = t_cap1 - t_cap0
            if not ret:
                record_latency("cap_read", cap_read_time)
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
            record_latency("cap_read", cap_read_time)
            for name, val in stage_times.items():
                record_latency(name, val)
            record_latency("process_frame_call", proc_time)

            # Time annotate_fps
            t_a0 = perf_counter()
            annotate_fps(frame, fps)
            t_a1 = perf_counter()
            record_latency("annotate_fps_call", t_a1 - t_a0)

            # Time imshow for the main frame
            t_i0 = perf_counter()
            cv2.imshow(window_name_1, frame)
            t_i1 = perf_counter()
            record_latency("imshow_frame", t_i1 - t_i0)

            # Time imshow for the warped view
            t_i0 = perf_counter()
            cv2.imshow(window_name_2, warped_frame)
            t_i1 = perf_counter()
            record_latency("imshow_warped", t_i1 - t_i0)

            t_w0 = perf_counter()
            key = cv2.waitKey(1) & 0xFF
            t_w1 = perf_counter()
            record_latency("wait_key", t_w1 - t_w0)
            if key in (ord("q"), 27):
                break

    finally:
        cap.release()
        cv2.destroyAllWindows()
        print(f"Average framerate: {averageFPS}")

        # Output average per-stage latencies
        if frame_count > 0:
            print("Per-stage latencies (seconds):")
            for name, total in cumulative_latencies.items():
                print(
                    f"  {name}: avg {total / frame_count:.6f}, "
                    f"max {max_latencies.get(name, 0.0):.6f}"
                )
        else:
            print("No frames processed; no latency data available.")


# Entry point

if __name__ == "__main__":
    try:
        stream_webcam_with_aruco()
    finally:
        # Clean up all magnets we registered during this session
        print("Cleaning up registered magnets from Flask...")
        cleanup_all_registered_magnets()
