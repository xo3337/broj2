import numpy as np
import cv2


# ----------------------------------------------------------------------
# Helper: compute 2D center from YOLO keypoints
# ----------------------------------------------------------------------
def compute_center_from_keypoints(keypoints_2d):
    """
    Compute the 2D center of an object from its keypoints.

    Parameters
    ----------
    keypoints_2d : np.ndarray
        Array of shape (N, 2) containing (x, y) keypoints in image pixels.

    Returns
    -------
    (cx, cy) : tuple of floats or None
        The center of all keypoints. Returns None if input is invalid.
    """
    if keypoints_2d is None:
        return None

    pts = np.asarray(keypoints_2d, dtype=np.float32)

    if pts.ndim != 2 or pts.shape[1] != 2 or pts.shape[0] == 0:
        return None

    cx = float(np.mean(pts[:, 0]))
    cy = float(np.mean(pts[:, 1]))
    return (cx, cy)


# ----------------------------------------------------------------------
# Main API: estimate "pose" (here: only 2D center, no rotation)
# ----------------------------------------------------------------------
def estimate_pose(
    image,
    keypoints_2d,
    part_id=None,
    camera_matrix=None,
    dist_coeffs=None,
):
    """
    Simple pose-like estimation based on YOLO keypoints.

    This version:
      - DOES NOT compute any rotation (no yaw/pitch/roll).
      - ONLY computes the 2D center of the detected part in the image.

    Parameters
    ----------
    image : np.ndarray
        BGR image (OpenCV format). It will NOT be modified here.
    keypoints_2d : np.ndarray
        Array of shape (N, 2) with the 2D keypoints (x, y) in pixels.
    part_id : str or int, optional
        Name or ID of the detected part (YOLO class name).
    camera_matrix : any, optional
        Kept for compatibility with older versions (not used here).
    dist_coeffs : any, optional
        Kept for compatibility with older versions (not used here).

    Returns
    -------
    pose_result : dict
        {
            "success": True/False,
            "part_id": part_id,
            "center_x": float or None,
            "center_y": float or None,
            "yaw": None,
            "pitch": None,
            "roll": None,
            "reproj_error": None,
        }
    """
    center = compute_center_from_keypoints(keypoints_2d)

    if center is None:
        return {
            "success": False,
            "part_id": part_id,
            "center_x": None,
            "center_y": None,
            "yaw": None,
            "pitch": None,
            "roll": None,
            "reproj_error": None,
        }

    cx, cy = center

    return {
        "success": True,
        "part_id": part_id,
        "center_x": float(cx),
        "center_y": float(cy),
        # Rotation is disabled in this version:
        "yaw": None,
        "pitch": None,
        "roll": None,
        "reproj_error": None,
    }


# ----------------------------------------------------------------------
# Visualization: draw bounding box and center (no keypoints, show conf)
# ----------------------------------------------------------------------
def draw_pose_visualization(
    image,
    box_xyxy,
    keypoints_2d,
    pose_result,
    part_id=None,
    confidence=None,
):
    """
    Draw visualization on the image:
      - bounding box (green)
      - single center point (magenta)
      - label with class name + confidence

    All other keypoints are NOT drawn in this version.

    Parameters
    ----------
    image : np.ndarray
        BGR image (OpenCV format). The drawing is done in-place.
    box_xyxy : np.ndarray or list
        Bounding box [x1, y1, x2, y2] in image pixels.
    keypoints_2d : np.ndarray
        Array of shape (N, 2) with the 2D keypoints (x, y) in pixels.
        (kept for compatibility but not drawn)
    pose_result : dict
        Result from estimate_pose(), expected keys: "success", "center_x", "center_y".
    part_id : str or int, optional
        Name or ID of the detected part (YOLO class label).
    confidence : float, optional
        Detection confidence score from YOLO (0–1).
    """
    if image is None or box_xyxy is None:
        return

    # Ensure numpy array for the box
    box = np.asarray(box_xyxy, dtype=np.float32).reshape(-1)
    if box.shape[0] < 4:
        return

    x1, y1, x2, y2 = box
    x1, y1, x2, y2 = int(x1), int(y1), int(x2), int(y2)

    # 1) Draw bounding box in green
    cv2.rectangle(image, (x1, y1), (x2, y2), (0, 255, 0), 2)

    # 2) Draw ONLY the center in magenta (no surrounding keypoints)
    if pose_result is not None and pose_result.get("success"):
        cx = pose_result.get("center_x")
        cy = pose_result.get("center_y")
        if cx is not None and cy is not None:
            cv2.circle(image, (int(cx), int(cy)), 5, (255, 0, 255), -1)

    # 3) Draw label text (class + confidence)
    if part_id is None:
        label = "part"
    else:
        label = str(part_id)

    if confidence is not None:
        try:
            label = f"{label} {float(confidence):.2f}"
        except Exception:
            # If conversion fails, keep label without confidence
            pass

    cv2.putText(
        image,
        label,
        (x1, max(0, y1 - 10)),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.6,
        (0, 255, 0),
        2,
        cv2.LINE_AA,
    )
