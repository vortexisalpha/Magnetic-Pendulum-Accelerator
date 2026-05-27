import sys
import cv2
import numpy as np
from time import perf_counter

# Font used when overlaying marker IDs on the frame.
_TEXT_FONT = cv2.FONT_HERSHEY_PLAIN

def marker_pos_transform():
    pass

def detect_markers(frame: np.ndarray, dictionary: cv2.aruco.Dictionary) -> np.ndarray:
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    params = cv2.aruco.DetectorParameters()
    detector = cv2.aruco.ArucoDetector(dictionary, params)
    corners, marker_ids, _ = detector.detectMarkers(gray)

    # No markers detected — nothing to draw.
    if not corners:
        return frame
    
    cv2.aruco.drawDetectedMarkers(frame, corners, marker_ids)
    # Obtains the corner pixel coordinates of all detected markers, finds and annotates the center
    for single_marker_corners in corners:
        x0, y0 = single_marker_corners[0][0]
        x1, y1 = single_marker_corners[0][1]
        x2, y2 = single_marker_corners[0][2]
        x3, y3 = single_marker_corners[0][3]

        if (x2 - x0) == 0 or (x3 - x1) == 0:
        # fall back to simple average of all four corners
            x_intersect = (x0 + x1 + x2 + x3) / 4
            y_intersect = (y0 + y1 + y2 + y3) / 4

        m1 = (y2-y0)/(x2-x0)
        m2 = (y3-y1)/(x3-x1)

        #if abs(m1 - m2) < 1e-6: #what to do?

        x_intersect = ((y1-y0)+x0*m1-x1*m2)/(m1-m2)
        y_intersect = m1*x_intersect+y0-x0*m1
        cx = int(x_intersect)
        cy = int(y_intersect)
        cv2.circle(frame, (cx, cy), radius=5, color=(0, 0, 255), thickness=-1)
    
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
