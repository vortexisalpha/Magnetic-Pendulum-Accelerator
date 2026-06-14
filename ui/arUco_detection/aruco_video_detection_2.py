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


# =============================================================================
# Magnet defaults
# =============================================================================

mag_4 = {"uid": "marker_0", "x": 1.0, "y": 0.0}
mag_5 = {"uid": "marker_1", "x": -0.5, "y": 0.866}
mag_6 = {"uid": "marker_2", "x": -0.5, "y": -0.866}

fixed_3_mag = [mag_4, mag_5, mag_6]


# =============================================================================
# Network I/O
# =============================================================================

def post_json(endpoint: str, payload: dict) -> bool:
    """
    Side-effect function.
    Sends JSON payload to Flask server.
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


def magnet_update_position(mapped_mags_pos: list[Optional[np.ndarray]]) -> None:
    """
    Side-effect function.
    Sends the latest mapped magnet positions to Flask.

    If a magnet is not detected, this currently sends its default position.
    """
    debug_txt = ""

    for i, mag in enumerate(mapped_mags_pos):
        payload = fixed_3_mag[i].copy()

        if mag is not None:
            payload["x"] = float(mag[0])
            payload["y"] = float(mag[1])
            ok = post_json("magnet_update_position", payload)
            debug_txt += "Updated! " if ok else "UpdateFailed! "
        else:
            ok = post_json("magnet_update_position", payload)
            debug_txt += "UpdatedDefault! " if ok else "UpdateDefaultFailed! "

    print(debug_txt)


def pre_add() -> None:
    """
    Side-effect function.
    Adds the three fixed magnet IDs to Flask before the stream starts.
    """
    for mag in fixed_3_mag:
        success = False

        for _ in range(3):
            if post_json("magnet_add", mag):
                success = True
                break

        if not success:
            raise RuntimeError(f"Pre-adding {mag['uid']} failed")


def post_remove() -> None:
    """
    Side-effect function.
    Removes the three fixed magnet IDs from Flask after the stream ends.
    """
    for mag in fixed_3_mag:
        success = False

        for _ in range(3):
            if post_json("magnet_remove", mag):
                success = True
                break

        if not success:
            raise RuntimeError(f"Post-removing {mag['uid']} failed")


# =============================================================================
# Computer vision: detection and geometry
# =============================================================================

def detect_aruco_markers(
    frame: np.ndarray,
    detector: cv2.aruco.ArucoDetector,
) -> tuple[list, Optional[np.ndarray]]:
    """
    Pure-ish vision function.
    Converts frame to greyscale and detects ArUco markers.

    It does not draw, map, or send anything.
    """
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    corners, marker_ids, _ = detector.detectMarkers(gray)
    return corners, marker_ids


def diagonal_intersection(single_marker_corners: np.ndarray) -> Optional[tuple[float, float]]:
    """
    Pure geometry function.
    Finds the marker centre as the intersection of the projected diagonals.

    This uses homogeneous line intersection instead of slope special-casing. This avoids vertical-line
    edge cases.
    """
    pts = single_marker_corners[0].astype(np.float64)

    p0 = np.array([pts[0][0], pts[0][1], 1.0])
    p1 = np.array([pts[1][0], pts[1][1], 1.0])
    p2 = np.array([pts[2][0], pts[2][1], 1.0])
    p3 = np.array([pts[3][0], pts[3][1], 1.0])

    # Lines through opposite corners.
    line_02 = np.cross(p0, p2)
    line_13 = np.cross(p1, p3)

    # Intersection of the two diagonal lines.
    centre_h = np.cross(line_02, line_13)

    if abs(centre_h[2]) < 1e-9:
        return None

    x_center = centre_h[0] / centre_h[2]
    y_center = centre_h[1] / centre_h[2]

    return float(x_center), float(y_center)


def find_marker_centres(
    corners: list,
    marker_ids: Optional[np.ndarray],
) -> dict[int, tuple[float, float]]:
    """
    Pure function.
    Takes raw OpenCV ArUco detection output and returns:

        marker ID -> (x_center, y_center)

    The dictionary is keyed by marker ID, so it does not depend on the order
    in which OpenCV detected the markers.
    """
    centres: dict[int, tuple[float, float]] = {}

    if marker_ids is None:
        return centres

    for marker_id, single_marker_corners in zip(marker_ids, corners):
        centre = diagonal_intersection(single_marker_corners)

        if centre is None:
            continue

        m_id = int(marker_id[0])
        centres[m_id] = centre

    return centres


def extract_board_and_tokens(
    centres: dict[int, tuple[float, float]],
) -> tuple[list[Optional[tuple[float, float]]], list[Optional[tuple[float, float]]]]:
    """
    Pure function.
    Converts marker-ID dictionary into ordered lists.

    Board corners:
        marker IDs 0,1,2,3

    Magnet tokens:
        marker IDs 4,5,6
    """
    board_corners_pos = [centres.get(i) for i in range(4)]
    detected_mags_pos = [centres.get(i) for i in range(4, 7)]

    return board_corners_pos, detected_mags_pos


def compute_homographies(
    board_corners_pos: list[Optional[tuple[float, float]]],
) -> Optional[tuple[np.ndarray, np.ndarray]]:
    """
    Pure geometry function.
    Computes:
        H_sim  : camera pixel coordinates -> simulation coordinates
        H_view : camera pixel coordinates -> top-down debug image coordinates

    Returns None if any board corner is missing or if OpenCV fails to compute.
    """
    if any(corner is None for corner in board_corners_pos):
        return None

    board_corners_f = np.array(board_corners_pos, dtype=np.float32)

    H_sim, _ = cv2.findHomography(board_corners_f, DST_POINTS)
    H_view, _ = cv2.findHomography(board_corners_f, DST_IMSHOW)

    if H_sim is None or H_view is None:
        return None

    return H_sim, H_view


def token_transform(
    detected_mags_pos: list[Optional[tuple[float, float]]],
    H_sim: np.ndarray,
) -> list[Optional[np.ndarray]]:
    """
    Pure geometry function.
    Maps detected magnet token centres into simulation coordinates.
    """
    mapped_mags_pos: list[Optional[np.ndarray]] = [None, None, None]

    for i, token in enumerate(detected_mags_pos):
        if token is not None:
            token_f = np.array([[token]], dtype=np.float32)
            mapped = cv2.perspectiveTransform(token_f, H_sim)
            mapped_mags_pos[i] = mapped[0][0]

    return mapped_mags_pos


# =============================================================================
# Visualisation helpers
# =============================================================================

def annotate_marker_centres(
    frame: np.ndarray,
    centres: dict[int, tuple[float, float]],
) -> None:
    """
    Side-effect visualisation function.
    Draws centre dots on detected markers.
    """
    for centre in centres.values():
        x_center, y_center = centre
        x_center_px = int(round(x_center))
        y_center_px = int(round(y_center))

        cv2.circle(
            frame,
            (x_center_px, y_center_px),
            radius=5,
            color=(0, 0, 255),
            thickness=-1,
        )


def annotate_mapped_mags_pos(
    frame: np.ndarray,
    detected_mags_pos: list[Optional[tuple[float, float]]],
    mapped_mags_pos: list[Optional[np.ndarray]],
) -> None:
    """
    Side-effect visualisation function.
    Draws mapped simulation coordinates next to detected magnet tokens.
    """
    for i, detected_pos in enumerate(detected_mags_pos):
        if detected_pos is not None and mapped_mags_pos[i] is not None:
            detected_pos_np = np.array(detected_pos, dtype=np.float32)

            cv2.putText(
                frame,
                f"ID {i + 4}: {mapped_mags_pos[i]}",
                np.round(detected_pos_np).astype(int),
                _TEXT_FONT,
                4,
                (0, 255, 0),
                thickness=2,
            )


def annotate_fps(frame: np.ndarray, fps: float) -> None:
    """
    Side-effect visualisation function.
    Draws FPS at bottom-right.
    """
    text = f"FPS: {fps:.1f}"
    scale = 2
    thickness = 2
    margin = 10

    (text_w, text_h), baseline = cv2.getTextSize(
        text,
        _TEXT_FONT,
        scale,
        thickness,
    )

    h, w = frame.shape[:2]
    origin = (w - text_w - margin, h - baseline - margin)

    cv2.putText(
        frame,
        text,
        origin,
        _TEXT_FONT,
        scale,
        (0, 255, 0),
        thickness,
    )


def make_warped_debug_view(
    frame: np.ndarray,
    H_view: np.ndarray,
) -> np.ndarray:
    """
    Pure-ish visualisation function.
    Returns a top-down warped debug view.
    """
    return cv2.warpPerspective(
        frame.copy(),
        H_view,
        (WARPED_SIZE, WARPED_SIZE),
    )


# =============================================================================
# Per-frame pipeline
# =============================================================================

def process_frame(
    frame: np.ndarray,
    detector: cv2.aruco.ArucoDetector,
) -> np.ndarray:
    """
    Main per-frame pipeline.

    Responsibilities:
        1. Detect ArUco markers.
        2. Compute marker centres.
        3. Extract board corners and magnet token positions.
        4. Compute homographies if all four board corners exist.
        5. Map token positions to simulation coordinates.
        6. Send mapped positions to Flask.
        7. Return warped debug frame if possible.

    The original detect_markers() function did all of this in one block.
    This function is now just the orchestrator.
    """
    corners, marker_ids = detect_aruco_markers(frame, detector)

    if not corners or marker_ids is None:
        return frame

    cv2.aruco.drawDetectedMarkers(frame, corners, marker_ids)

    centres = find_marker_centres(corners, marker_ids)
    annotate_marker_centres(frame, centres)

    board_corners_pos, detected_mags_pos = extract_board_and_tokens(centres)

    homographies = compute_homographies(board_corners_pos)

    if homographies is None:
        print(
            "Not all corners detected! Homography not possible; "
            "cannot map token positions to simulation coordinates."
        )
        return frame

    H_sim, H_view = homographies

    mapped_mags_pos = token_transform(detected_mags_pos, H_sim)

    magnet_update_position(mapped_mags_pos)

    annotate_mapped_mags_pos(frame, detected_mags_pos, mapped_mags_pos)

    print(mapped_mags_pos)

    return make_warped_debug_view(frame, H_view)


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

    prev_time = perf_counter()

    try:
        while True:
            ret, frame = cap.read()

            if not ret:
                print("Failed to grab frame from camera; exiting.", file=sys.stderr)
                break

            now = perf_counter()
            fps = 1.0 / (now - prev_time)
            prev_time = now

            warped_frame = process_frame(frame, detector)

            annotate_fps(frame, fps)

            cv2.imshow(window_name_1, frame)
            cv2.imshow(window_name_2, warped_frame)

            key = cv2.waitKey(1) & 0xFF

            if key in (ord("q"), 27):
                break

    finally:
        cap.release()
        cv2.destroyAllWindows()


# =============================================================================
# Entry point
# =============================================================================

if __name__ == "__main__":
    # Clean up magnets from a previous crashed run.
    print("Removing any magnets from previous run")
    post_remove()

    print("Pre-adding magnets to Flask server")
    pre_add()

    try:
        stream_webcam_with_aruco()
    finally:
        print("Removing magnets from Flask server")
        post_remove()