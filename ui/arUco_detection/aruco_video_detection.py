import sys
import cv2
import numpy as np
from time import perf_counter
import requests

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


###
# ADD/REMOVE MAGNET NOT AVAILABLE YET, THEREFORE PRE-ADD 3 MAGNETS WITH DEFAULT POSITION
# ALL 3 MAGNETS MUST BE ALWAYS IN FRAME AND DETECTED, THEREFORE IF NONE -> DEFAULT
mag_4 = {"uid": "marker_0", "x": 1.0, "y": 0.0}
mag_5 = {"uid": "marker_1", "x": -0.5, "y": 0.866}
mag_6 = {"uid": "marker_2", "x": -0.5,  "y": -0.866}
fixed_3_mag = [mag_4, mag_5, mag_6]

# Updates the position of a detected magnet, if not detected, we set back to default
def magnet_update_position(mapped_mags_pos: list) -> None:
    debug_txt = ""
    for i, mag in enumerate(mapped_mags_pos):
        default_mag = fixed_3_mag[i].copy() #otherwise, passed by reference, changes defaults
        if mag is not None:
            default_mag["x"] = float(mag[0]) #Explicitly cast numpy.float32 back to python float, otherwise not JSON serializable
            default_mag["y"] = float(mag[1])
            response = requests.post('http://35.179.111.223:5000/magnet_update_position', json = default_mag)
            if(response.status_code==200): debug_txt += "Updated! "
            else: print("Error POSTing")

        else:
            response = requests.post('http://35.179.111.223:5000/magnet_update_position', json = default_mag)
            if(response.status_code!=200): debug_txt += "UpdatedDefault! "
            else: print("Error POSTing")
    print(debug_txt)
###


def token_transform(detected_mags_pos: list, H_sim: np.ndarray) -> list:

    mapped_mags_pos = [None, None, None]
    for i, token in enumerate(detected_mags_pos):
        if token is not None:
            # perspectiveTransform is designed to transform batches of curves or contours
            # hence must be shape (1, 1, 2)
            token_f = np.array([[token]], dtype=np.float32)
            mapped = cv2.perspectiveTransform(token_f, H_sim)
            mapped_mags_pos[i] = mapped[0][0]

    print(type(mapped_mags_pos))
    return mapped_mags_pos

def annotate_mapped_mags_pos(frame: np.ndarray, detected_mags_pos: list, mapped_mags_pos: list) -> None:
    for i, detected_pos in enumerate(detected_mags_pos):
        if detected_pos is not None:
            detected_pos_np = np.array(detected_pos, dtype=np.float32)
            cv2.putText(frame, f"ID {i+4}: {str(mapped_mags_pos[i])}", np.round(detected_pos_np).astype(int), _TEXT_FONT, 4, (0, 255, 0), thickness = 2)

# Functional Decomp
def find_marker_centres(corners: list, marker_ids: np.ndarray)-> dict[int, tuple[float, float]]:
    centers = {}
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
        centers[m_id] = (x_center, y_center)
        return centers

    


# Need to decompose! Also, implicitly mutating frame while also returning a new warped frame seems unconventional
def detect_markers(frame: np.ndarray, detector: cv2.aruco.ArucoDetector) -> np.ndarray | None:
    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    corners, marker_ids, _ = detector.detectMarkers(gray)

    # No markers detected — nothing to draw.
    if not corners:
        return frame
    
    # Draw markers on frame
    cv2.aruco.drawDetectedMarkers(frame, corners, marker_ids)

    # Iterates through all detected markers and finds their centers
    board_corners_pos = [None, None, None, None]
    detected_mags_pos = [None, None, None]

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
                board_corners_pos[m_id] = [x_center, y_center]
            case 4 | 5 | 6:
                detected_mags_pos[m_id-4] = [x_center, y_center]
            case _:
                pass

       # Int casting as pixel positions are integers, but prior rounding to increase accuracy since int casting rounds down
        x_center_px = int(round(x_center))
        y_center_px = int(round(y_center))

        # Annotating the center
        cv2.circle(frame, (x_center_px, y_center_px), radius=5, color=(0, 0, 255), thickness=-1)

    # Use detected board corners to do homography mapping, only when all 4 corners are present
    if None not in board_corners_pos:
        board_corners_f = np.array(board_corners_pos, dtype = np.float32)
        H_sim, _  = cv2.findHomography(board_corners_f, DST_POINTS)
        H_view, _ = cv2.findHomography(board_corners_f, DST_IMSHOW)

        mapped_mags_pos = token_transform(detected_mags_pos, H_sim)
        ###
        magnet_update_position(mapped_mags_pos)
        ###
        annotate_mapped_mags_pos(frame, detected_mags_pos, mapped_mags_pos)
        print(mapped_mags_pos)

        return cv2.warpPerspective(frame.copy(), H_view, (WARPED_SIZE, WARPED_SIZE))
    else:
        print("Not all corners detected! Homography not possible, can't map detected token positions to top-down square view")
        return frame


def annotate_fps(frame: np.ndarray, fps: float) -> None:
    text = f"FPS: {fps:.1f}"
    font       = _TEXT_FONT
    scale      = 2
    thickness  = 2
    margin     = 10

    # Measure how large the text will be so we can anchor it to the bottom-right
    (text_w, text_h), baseline = cv2.getTextSize(text, font, scale, thickness)

    h, w = frame.shape[:2]
    origin = (w - text_w - margin, h - baseline - margin)

    cv2.putText(frame, text, origin, font, scale, (0, 255, 0), thickness)

# Pre-add 3 magnets, 3 trials for each, otherwise error
###
def pre_add()-> None:
    for mag in fixed_3_mag:
        i = 0
        status_success = False
        while(i<3):
            i += 1
            response = requests.post('http://35.179.111.223:5000/magnet_add', json = mag)
            if response.status_code == 200:
                status_success = True
                break
        # Distinguish between failed POST exiting final iteration and success
        if(not status_success): raise RuntimeError(f"Pre-adding {mag['uid']} failed")

def post_remove() -> None:
    for mag in fixed_3_mag:
        i = 0
        status_success = False
        while(i<3):
            i += 1
            response = requests.post('http://35.179.111.223:5000/magnet_remove', json = mag)
            if response.status_code == 200:
                status_success = True
                break
        # Distinguish between failed POST exiting final iteration and success
        if(not status_success): raise RuntimeError(f"Post-removing {mag['uid']} failed")

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
    post_remove() # Removing any magnets added in previous run that was exited prematurely due to crash
    print("Pre-adding magnets to Flask server")
    pre_add()
    stream_webcam_with_aruco()
    print("Removing magnets from Flask server")
    post_remove()
