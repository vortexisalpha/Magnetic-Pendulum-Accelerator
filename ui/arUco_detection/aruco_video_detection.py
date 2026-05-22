import sys
import cv2
import numpy as np

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
                frame, f"id: {marker_id[0]}", top_right, _TEXT_FONT, 1.3, (255, 0, 255), 2
                )

    return frame


def stream_webcam_with_aruco(
    camera_index: int = 0,
    window_name: str = "ArUco Webcam Stream",
    aruco_dict_name: int = cv2.aruco.DICT_4X4_50,
) -> None:
    dictionary = cv2.aruco.getPredefinedDictionary(aruco_dict_name)

    cap = cv2.VideoCapture(camera_index)
    if not cap.isOpened():
        raise RuntimeError(f"Could not open camera with index {camera_index}.")

    try:
        while True:
            ret, frame = cap.read()
            if not ret:
                print("Failed to grab frame from camera; exiting.", file=sys.stderr)
                break

            annotate_markers(frame, dictionary)
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
