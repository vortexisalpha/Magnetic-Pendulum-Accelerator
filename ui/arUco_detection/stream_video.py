import sys
import cv2


def stream_webcam(camera_index: int = 0, window_name: str = "Webcam Stream") -> None:
    cap = cv2.VideoCapture(camera_index)

    if not cap.isOpened():
        raise RuntimeError(f"Could not open camera with index {camera_index}.")

    try:
        while True:
            ret, frame = cap.read()
            if not ret:
                print("Failed to grab frame from camera; exiting.", file=sys.stderr)
                break

            cv2.imshow(window_name, frame)

            # Exit on 'q' or ESC key press.
            key = cv2.waitKey(1) & 0xFF
            if key in (ord("q"), 27):
                break
    finally:
        cap.release()
        cv2.destroyAllWindows()


if __name__ == "__main__":
    stream_webcam()
