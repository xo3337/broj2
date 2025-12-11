import base64
import os
from datetime import datetime

import cv2
import numpy as np
from flask import Flask, jsonify, request
from ultralytics import YOLO

from lego_pose import estimate_pose, draw_pose_visualization

# -------------------------------------------------
#  Setup
# -------------------------------------------------
app = Flask(__name__)

MODEL_PATH = "best001.pt"
model = YOLO(MODEL_PATH)
CLASS_NAMES = model.names  # dict: id -> name

SAVE_FOLDER = "detections"
os.makedirs(SAVE_FOLDER, exist_ok=True)


def crop_play_area(img, top_cut_ratio=0.18, bottom_cut_ratio=0.15):
    """
    Crop the top UI area (step label + thumbnails) so YOLO does not detect them.

    Returns
    -------
    cropped : np.ndarray
        Cropped BGR image.
    y_start : int
        Vertical offset of the cropped region in the original image.
    """
    h, w = img.shape[:2]
    y_start = int(h * top_cut_ratio)
    y_end = int(h * (1.0 - bottom_cut_ratio))
    cropped = img[y_start:y_end, 0:w]
    return cropped, y_start


def draw_box_and_label(img, box, label, color=(0, 255, 0)):
    x1, y1, x2, y2 = map(int, box)
    cv2.rectangle(img, (x1, y1), (x2, y2), color, 2)
    cv2.putText(
        img,
        label,
        (x1, max(0, y1 - 10)),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.8,
        color,
        2,
        cv2.LINE_AA,
    )


def encode_image_to_base64(img_bgr):
    _, buf = cv2.imencode(".jpg", img_bgr)
    return base64.b64encode(buf.tobytes()).decode("utf-8")


# -------------------------------------------------
#  /check_piece : detect only the piece of the current step
# -------------------------------------------------
@app.route("/check_piece", methods=["POST"])
def check_piece():
    """
    Receives:
        {
          "image": "<base64>",
          "expected_class": "piece_name_from_unity",
          "step_index": 0
        }

    Returns JSON:
        {
          "success": true/false,
          "found": true/false,         # True only if the EXPECTED class was found
          "matched": true/false,       # expected_class found AND confidence >= THRESH
          "yolo_class": "xxx",         # equals expected_class when found, "" otherwise
          "expected_class": "xxx",
          "step_index": 0,
          "confidence": 0.87,          # confidence of the expected piece
          "annotated_image": "<base64>",
          "yaw": float or null,
          "pitch": float or null,
          "roll": float or null,
          "reproj_error": float or null,
          "center_x": float,           # center in FULL image pixels, or -1.0 if unknown
          "center_y": float,           # center in FULL image pixels, or -1.0 if unknown
          "error": "..."               # only if success = false
        }
    """
    try:
        data = request.get_json()
        if not data or "image" not in data or "expected_class" not in data:
            return jsonify(
                {
                    "success": False,
                    "error": "Missing 'image' or 'expected_class' in request",
                }
            )

        expected_class = str(data["expected_class"]).strip()
        step_index = int(data.get("step_index", -1))

        # ------------- Decode image -------------
        img_b64 = data["image"]
        img_bytes = base64.b64decode(img_b64)
        np_arr = np.frombuffer(img_bytes, np.uint8)
        frame = cv2.imdecode(np_arr, cv2.IMREAD_COLOR)

        if frame is None:
            return jsonify(
                {"success": False, "error": "Failed to decode image from Base64"}
            )

        # Crop the top UI area (so YOLO sees only the play area)
        frame_cropped, crop_y_start = crop_play_area(frame)

        # ------------- Run YOLO -------------
        results = model(frame_cropped)
        r = results[0]
        boxes = r.boxes
        kpts = getattr(r, "keypoints", None)

        # Default center in full image coordinates (unknown)
        center_x_full = -1.0
        center_y_full = -1.0

        # If there are no detections at all
        if boxes is None or len(boxes) == 0:
            ts = datetime.now().strftime("%Y%m%d_%H%M%S")
            save_name = os.path.join(SAVE_FOLDER, f"no_det_{ts}.jpg")
            cv2.imwrite(save_name, frame_cropped)

            return jsonify(
                {
                    "success": True,
                    "found": False,
                    "matched": False,
                    "yolo_class": "",
                    "expected_class": expected_class,
                    "step_index": step_index,
                    "confidence": 0.0,
                    "annotated_image": encode_image_to_base64(frame_cropped),
                    "yaw": None,
                    "pitch": None,
                    "roll": None,
                    "reproj_error": None,
                    "center_x": center_x_full,
                    "center_y": center_y_full,
                }
            )

        # -------------------- Filter by expected_class --------------------
        confs = boxes.conf.cpu().numpy()
        cls_ids = boxes.cls.cpu().numpy().astype(int)

        candidate_indices = []
        for i, cls_id in enumerate(cls_ids):
            cls_name = CLASS_NAMES.get(cls_id, f"class_{cls_id}")
            if cls_name == expected_class:
                candidate_indices.append(i)

        annotated = frame_cropped.copy()

        # Case 1: expected piece is NOT found at all in this frame
        if len(candidate_indices) == 0:
            # For debugging, draw all detections with their names
            for i, cls_id in enumerate(cls_ids):
                box = boxes.xyxy[i].cpu().numpy()
                cls_name = CLASS_NAMES.get(cls_id, f"class_{cls_id}")
                conf = float(confs[i])
                label = f"{cls_name} {conf:.2f}"
                draw_box_and_label(annotated, box, label)

            ts = datetime.now().strftime("%Y%m%d_%H%M%S")
            save_name = os.path.join(
                SAVE_FOLDER, f"no_expected_step{step_index}_{ts}.jpg"
            )
            cv2.imwrite(save_name, annotated)

            return jsonify(
                {
                    "success": True,
                    "found": False,          # expected_class is NOT in the frame
                    "matched": False,
                    "yolo_class": "",
                    "expected_class": expected_class,
                    "step_index": step_index,
                    "confidence": 0.0,
                    "annotated_image": encode_image_to_base64(annotated),
                    "yaw": None,
                    "pitch": None,
                    "roll": None,
                    "reproj_error": None,
                    "center_x": center_x_full,
                    "center_y": center_y_full,
                }
            )

        # Case 2: at least one detection for expected_class exists
        # Pick the candidate with the highest confidence
        best_i = max(candidate_indices, key=lambda idx: confs[idx])
        best_box = boxes.xyxy[best_i].cpu().numpy()
        best_conf = float(confs[best_i])
        best_cls_id = cls_ids[best_i]
        yolo_class = CLASS_NAMES.get(best_cls_id, f"class_{best_cls_id}")

        yaw = pitch = roll = None
        reproj_error = None

        # Extract keypoints for this detection (if available)
        image_points = None
        if kpts is not None and len(kpts) > best_i:
            try:
                image_points = (
                    kpts[best_i].xy[0].cpu().numpy().astype(np.float32)
                )
            except Exception:
                image_points = None

        pose_result = None
        if image_points is not None:
            pose_result = estimate_pose(
                image=annotated,
                keypoints_2d=image_points,
                part_id=yolo_class,
                camera_matrix=None,
                dist_coeffs=None,
            )

        # Draw visualization and compute full-image center
        if pose_result is not None and pose_result.get("success"):
            yaw = pose_result.get("yaw")
            pitch = pose_result.get("pitch")
            roll = pose_result.get("roll")
            reproj_error = pose_result.get("reproj_error")

            cx_local = pose_result.get("center_x")
            cy_local = pose_result.get("center_y")

            if isinstance(cx_local, (int, float)) and isinstance(
                cy_local, (int, float)
            ):
                center_x_full = float(cx_local)
                center_y_full = float(cy_local + crop_y_start)

            draw_pose_visualization(
                annotated,
                best_box,
                image_points,
                pose_result,
                yolo_class,
                best_conf,
            )
        else:
            label_text = f"{yolo_class} {best_conf:.2f}"
            draw_box_and_label(annotated, best_box, label_text)

        ts = datetime.now().strftime("%Y%m%d_%H%M%S")
        save_name = os.path.join(
            SAVE_FOLDER, f"expected_step{step_index}_{yolo_class}_{ts}.jpg"
        )
        cv2.imwrite(save_name, annotated)

        THRESH = 0.45
        matched = best_conf >= THRESH  # class is already guaranteed to match

        return jsonify(
            {
                "success": True,
                "found": True,              # expected_class was found
                "matched": matched,
                "yolo_class": yolo_class,   # should be equal to expected_class
                "expected_class": expected_class,
                "step_index": step_index,
                "confidence": best_conf,
                "annotated_image": encode_image_to_base64(annotated),
                "yaw": float(yaw) if yaw is not None else None,
                "pitch": float(pitch) if pitch is not None else None,
                "roll": float(roll) if roll is not None else None,
                "reproj_error": (
                    float(reproj_error) if reproj_error is not None else None
                ),
                "center_x": float(center_x_full),
                "center_y": float(center_y_full),
            }
        )

    except Exception as e:
        print("ERROR in /check_piece:", repr(e))
        return jsonify({"success": False, "error": str(e)})


if __name__ == "__main__":
    print("YOLO model loaded successfully!")
    app.run(host="0.0.0.0", port=5000, debug=True)
