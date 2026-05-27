import sys
import cv2
import numpy as np
from time import perf_counter

# Font used when overlaying marker IDs on the frame.
_TEXT_FONT = cv2.FONT_HERSHEY_PLAIN


def detect_markers(frame: np.ndarray, dictionary: cv2.aruco.Dictionary) -> np.ndarray:
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    params = cv2.aruco.DetectorParameters()
    detector = cv2.aruco.ArucoDetector(dictionary, params)
    corners, marker_ids, _ = detector.detectMarkers(gray)

    # No markers detected — nothing to draw.
    if not corners:
        return frame
    
    cv2.aruco.drawDetectedMarkers(frame, corners, marker_ids)

    board_corners = [None, None, None, None]
    detected_tokens = []
    # Iterates through all detected markers and finds their centers
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
        
        # Int casting as pixel positions are integers, but prior rounding to increase accuracy since int casting rounds down
        x_center_px = int(round(x_center))
        y_center_px = int(round(y_center))
        id = marker_id[0]
        match id:
            case 0 | 1 | 2 | 3 :
                board_corners[id] = [x_center_px, y_center_px]
            case _:
                pass # Magnet token detected, handle separately

        # Annotating the center

        cv2.circle(frame, (x_center_px, y_center_px), radius=5, color=(0, 0, 255), thickness=-1)

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
    window_name: str = "ArUco Webcam Stream",
    aruco_dict_name: int = cv2.aruco.DICT_4X4_50,
) -> None:
    dictionary = cv2.aruco.getPredefinedDictionary(aruco_dict_name)

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

            detect_markers(frame, dictionary)
            annotate_fps(frame, fps)
            cv2.imshow(window_name, frame)

            # waitKey(1) yields the GUI thread; mask to handle high bits on some OSes.
            key = cv2.waitKey(1) & 0xFF
            if key in (ord("q"), 27):
                break
    finally:
        cap.release()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    stream_webcam_with_aruco()
