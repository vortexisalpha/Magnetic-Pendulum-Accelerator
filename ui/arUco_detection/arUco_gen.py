import cv2

# create the dictionary for markers type
dictionary = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_50)
size_of_marker = 400  # size of marker.

# generating IDs with for loop
for marker_id in range(4):
    # generating the marker
    img = cv2.aruco.generateImageMarker(dictionary, marker_id, size_of_marker)

    print("Dimension of Marker: ", img.shape)
    # save/write the image
    cv2.imwrite("marker_image{}.png".format(marker_id), img)

# display the image(marker) on windows
cv2.imshow("Marker", img)
cv2.waitKey(0)