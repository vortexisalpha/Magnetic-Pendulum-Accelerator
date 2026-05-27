# ArUco Marker Allocations

This project utilizes the first 7 ArUco markers from OpenCV's predefined dictionary: `cv2.aruco.DICT_4X4_50`. These markers carry unique identifiers from **ID 0** to **ID 6**.

* **Homography Mapping (4 Markers):** Positioned on the corners of the token-holding plate to establish the coordinate tracking plane.
* **Token Detection (3 Markers):** Mounted directly on the physical components to track and identify the tokens.

---

## Marker Allocation Matrix

| Marker ID | Assignment | Component Type |
| :---: | :--- | :--- |
| **0** | Token Plate: Top-Left Corner | Homography Reference |
| **1** | Token Plate: Top-Right Corner | Homography Reference |
| **2** | Token Plate: Bottom-Right Corner | Homography Reference |
| **3** | Token Plate: Bottom-Left Corner | Homography Reference |
| **4** | Magnet 1 | Token Detection |
| **5** | Magnet 2 | Token Detection |
| **6** | Magnet 3 | Token Detection |

> **Note:** The homography reference markers (IDs 0–3) are mapped in a **clockwise direction** starting from the top-left corner. This sequence mirrors the structural tracking sequence of the `corners` array output returned by `cv2.aruco.detectMarkers()`.