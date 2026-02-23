from ultralytics import YOLO
from risk_inference import predict_dark_pattern_risk

yolo_model = YOLO("yolov8_dark_pattern.pt")

def check(image_path):
    results = yolo_model(image_path, conf=0.25, verbose=False)
    
    detections = []
    for r in results:
        boxes = r.boxes
        for box in boxes:
            x1, y1, x2, y2 = map(int, box.xyxy[0].tolist())
            conf = float(box.conf[0])
            class_name = yolo_model.names[int(box.cls[0])]
            detections.append({"bbox": [x1, y1, x2, y2], "confidence": conf, "class_name": class_name})

    return detections

def calculate_risk(detections_path):
    results = []
    for i, det in enumerate(detections_path):
        result = predict_dark_pattern_risk(det)
        results.append({
            "level": result["risk_level"],
            "score": result["probabilities"][result["risk_level"]] * 100
        })

    return results
