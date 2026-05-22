import sys
import cv2
import numpy as np
from time import perf_counter

# Font used when overlaying marker IDs on the frame.
_TEXT_FONT = cv2.FONT_HERSHEY_PLAIN


def annotate_markers(frame: np.ndarray, dictionary: cv2.aruco.Dictionary) -> np.ndarray:
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    corners, marker_ids, _ = cv2.aruco.detectMarkers(gray, dictionary)

    # No markers detected — nothing to draw.
    if not corners:
        return frame

    for corner, marker_id in zip(corners, marker_ids):
        # Outline the marker with a yellow quadrilateral.
        cv2.polylines(
                frame, [corner.astype(np.int32)], True, (0, 255, 255), 3, cv2.LINE_AA
                )

        # Reshape (1, 4, 2) float corners → (4, 2) int pixel coords.
        pts = corner.reshape(4, 2).astype(int)
        top_right, _top_left, _bottom_right, _bottom_left = pts

        # Place the ID label near the top-right corner.
        cv2.putText(
                frame, f"id: {marker_id[0]}", top_right, _TEXT_FONT, 2, (255, 0, 255), 2
                )

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

            annotate_markers(frame, dictionary)
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
