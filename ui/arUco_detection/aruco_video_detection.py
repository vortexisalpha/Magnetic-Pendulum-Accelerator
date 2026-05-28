import sys
import cv2
import numpy as np
from time import perf_counter

# Font used when overlaying marker IDs on the frame.
_TEXT_FONT = cv2.FONT_HERSHEY_PLAIN

ARUCO_DICT_NAME = cv2.aruco.DICT_4X4_50

BOARD_CORNER_VAL = 1.8

DST_POINTS = np.array([
    [-BOARD_CORNER_VAL, BOARD_CORNER_VAL],
    [BOARD_CORNER_VAL, BOARD_CORNER_VAL],
    [BOARD_CORNER_VAL, -BOARD_CORNER_VAL],
    [-BOARD_CORNER_VAL, -BOARD_CORNER_VAL]
], dtype=np.float32)

# Additional image output to show homography mapping
WARPED_SIZE = 400

# We will use cv2.warpPerspective(), which maps to output image pixel coordinates, so they MUST be integers and positive
DST_IMSHOW = np.array([
    [0, 0],
    [WARPED_SIZE, 0],
    [WARPED_SIZE, WARPED_SIZE],
    [0, WARPED_SIZE]
], dtype=np.float32)


def token_transform(detected_tokens: list, H_sim: np.ndarray) -> list | None:

    token_mapped = [None, None, None]
    for i, token in enumerate(detected_tokens):
        if token is not None:
            # perspectiveTransform is designed to transform batches of curves or contours
            # hence must be shape (1, 1, 2)
            token_f = np.array([[token]], dtype=np.float32)
            mapped = cv2.perspectiveTransform(token_f, H_sim)
            token_mapped[i] = mapped[0][0]

    return token_mapped

# Need to decompose!
def detect_markers(frame: np.ndarray, detector: cv2.aruco.ArucoDetector) -> np.ndarray | None:
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    corners, marker_ids, _ = detector.detectMarkers(gray)

    # No markers detected — nothing to draw.
    if not corners:
        return frame
    
    # Draw markers on frame
    cv2.aruco.drawDetectedMarkers(frame, corners, marker_ids)

    # Iterates through all detected markers and finds their centers
    board_corners = [None, None, None, None]
    detected_tokens = [None, None, None]

    for marker_id, single_marker_corners in zip(marker_ids, corners):
        x0, y0 = single_marker_corners[0][0]
        x1, y1 = single_marker_corners[0][1]
        x2, y2 = single_marker_corners[0][2]
        x3, y3 = single_marker_corners[0][3]

        # Finds centers by calculating diagonal intersection
        # If both diagonals are vertical (unlikely)
        if abs(x0-x2) < 1e-6 and abs(x1-x3) < 1e-6:
            continue ## We don't do anything? What do we feed into the pos_transform?
        # If only diagonal joining (x0, y0) and (x2, y2) is vertical
        elif abs(x0-x2) < 1e-6:
            x_center = x0
            m2 = (y3-y1)/(x3-x1)
            y_center = m2*(x0-x1)+y1
        # If only diagonal joining (x1, y1) and (x3, y3) is vertical
        elif abs(x1-x3) < 1e-6:
            x_center = x1
            m1 = (y2-y0)/(x2-x0)
            y_center = m1*(x1-x0)+y0
        else:
            m1 = (y2-y0)/(x2-x0)
            m2 = (y3-y1)/(x3-x1)

            # if the diagonals appear to be parallel
            if abs(m1 - m2) < 1e-6:
                continue

            x_center = ((y1-y0)+x0*m1-x1*m2)/(m1-m2)
            y_center = m1*(x_center-x0)+y0
        
        m_id = marker_id[0]
        match m_id:
            case 0 | 1 | 2 | 3 :
                board_corners[m_id] = [x_center, y_center]
            case 4 | 5 | 6:
                detected_tokens[m_id-4] = [x_center, y_center]
            case _:
                pass

       # Int casting as pixel positions are integers, but prior rounding to increase accuracy since int casting rounds down
        x_center_px = int(round(x_center))
        y_center_px = int(round(y_center))

        # Annotating the center
        cv2.circle(frame, (x_center_px, y_center_px), radius=5, color=(0, 0, 255), thickness=-1)

    # Use detected board corners to do homography mapping, only when all 4 corners are present
    if None not in board_corners:
        board_corners_f = np.array(board_corners, dtype = np.float32)
        H_sim, _  = cv2.findHomography(board_corners_f, DST_POINTS)
        H_view, _ = cv2.findHomography(board_corners_f, DST_IMSHOW)

        token_mapped = token_transform(detected_tokens, H_sim)
        print(token_mapped)
        return cv2.warpPerspective(frame.copy(), H_view, (WARPED_SIZE, WARPED_SIZE))
    else:
        print("Not all corners detected!")
        return frame


def annotate_fps(frame: np.ndarray, fps: float) -> None:
    text = f"FPS: {fps:.1f}"
    font       = cv2.FONT_HERSHEY_PLAIN
    scale      = 2
    thickness  = 2
    margin     = 10

    # Measure how large the text will be so we can anchor it to the bottom-right
    (text_w, text_h), baseline = cv2.getTextSize(text, font, scale, thickness)

    h, w = frame.shape[:2]
    origin = (w - text_w - margin, h - baseline - margin)

    cv2.putText(frame, text, origin, font, scale, (0, 255, 0), thickness)


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
            
            # Calculates the time between reading 2 frames to find the framerate
            now = perf_counter()
            fps = 1.0/(now-prev_time)
            prev_time = now
            
            warped_frame = detect_markers(frame, detector)
            annotate_fps(frame, fps)
            cv2.imshow(window_name_1, frame)
            cv2.imshow(window_name_2, warped_frame)

            # waitKey(1) yields the GUI thread; mask to handle high bits on some OSes.
            key = cv2.waitKey(1) & 0xFF
            if key in (ord("q"), 27):
                break
    finally:
        cap.release()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    stream_webcam_with_aruco()
