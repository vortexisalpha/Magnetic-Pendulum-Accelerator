import cv2
import numpy as np

# define the fonts for draw text on image
font = cv2.FONT_HERSHEY_PLAIN

# create the dictionary for markers type
dictionary = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_50)

# Read the image and converts it to grayscale for faster detection
image = cv2.imread("./IMG_8940.jpg")
gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)

# Detect ArUco markers in the image.
params = cv2.aruco.DetectorParameters()
detector = cv2.aruco.ArucoDetector(dictionary, params)
corners, ids, rejected = detector.detectMarkers(gray)

# If markers are detected, draw them on the image.
if ids is not None:
    cv2.aruco.drawDetectedMarkers(image, corners, ids)

# Save the image.
cv2.imwrite("out_image1.png", image)
# Show the image.
cv2.imshow("image", image)
# wait until any key press on keyboard
cv2.waitKey(0)
# Close all windows.
cv2.destroyAllWindows()