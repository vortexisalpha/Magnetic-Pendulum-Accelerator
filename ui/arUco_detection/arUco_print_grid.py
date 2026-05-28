import cv2
import numpy as np

# 1. Define dictionary and tracking parameters
dictionary = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_50)
num_markers = 7
marker_size_px = 400  # Resolution of each individual marker
padding_top_px = 40   # White space buffer above the marker
padding_bot_px = 90   # Extra white space buffer below the marker for text labels
padding_side_px = 50  # White space buffer on the left and right

# Calculate total block dimensions per marker
block_width = marker_size_px + (padding_side_px * 2)
block_height = marker_size_px + padding_top_px + padding_bot_px

# 2. Define grid dimensions (4 columns, 2 rows)
cols = 4
rows = 2
sheet_width = cols * block_width
sheet_height = rows * block_height

# Create a blank white canvas
canvas = np.ones((sheet_height, sheet_width), dtype=np.uint8) * 255

# 3. Generate each marker, paste it, and draw its ID text label
for marker_id in range(num_markers):
    # Generate the pure ArUco matrix image
    marker_img = cv2.aruco.generateImageMarker(dictionary, marker_id, marker_size_px)
    
    # Calculate grid position (row, col)
    row = marker_id // cols
    col = marker_id % cols
    
    # Calculate pixel offsets on the canvas for the marker matrix
    y_start = (row * block_height) + padding_top_px
    y_end = y_start + marker_size_px
    x_start = (col * block_width) + padding_side_px
    x_end = x_start + marker_size_px
    
    # Paste the marker matrix onto the canvas
    canvas[y_start:y_end, x_start:x_end] = marker_img
    
    # 4. Add the text label ("ID: X") underneath the marker frame
    text = f"ID: {marker_id}"
    font = cv2.FONT_HERSHEY_SIMPLEX
    font_scale = 1.0
    thickness = 2
    
    # Calculate text size to perfectly center it horizontally under the marker
    (text_w, text_h), _ = cv2.getTextSize(text, font, font_scale, thickness)
    text_x = (col * block_width) + int((block_width - text_w) / 2)
    text_y = y_end + 50  # Drop text 50 pixels below the bottom edge of the marker
    
    # Render the black text identifier onto the white canvas space
    cv2.putText(canvas, text, (text_x, text_y), font, font_scale, 0, thickness, cv2.LINE_AA)

# 5. Save the final sheet
cv2.imwrite("aruco_print_sheet.png", canvas)