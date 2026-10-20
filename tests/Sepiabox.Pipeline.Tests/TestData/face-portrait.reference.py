"""Golden reference for FaceDetectorTests (S-018): OpenCV's own YuNet + SFace pipeline on face-portrait.jpg.

Usage (models from `sepiabox-ingest models download`):
    pip install opencv-python-headless==4.10.0.84
    python face-portrait.reference.py <models folder> face-portrait.jpg > face-portrait.reference.json
"""
import base64
import json
import sys

import cv2
import numpy as np

models, image_path = sys.argv[1], sys.argv[2]
image = cv2.imread(image_path)
height, width = image.shape[:2]

detector = cv2.FaceDetectorYN.create(
    f"{models}/face_detection_yunet_2023mar.onnx", "", (width, height), 0.8, 0.3, 5000)
detector.setInputSize((width, height))
_, faces = detector.detect(image)
if faces is None or len(faces) != 1:
    sys.exit(f"expected one face, found {0 if faces is None else len(faces)}")

face = faces[0]
recognizer = cv2.FaceRecognizerSF.create(f"{models}/face_recognition_sface_2021dec.onnx", "")
feature = recognizer.feature(recognizer.alignCrop(image, face)).flatten().astype("<f4")
feature /= np.linalg.norm(feature)

json.dump({
    "opencv": cv2.__version__,
    "box": [round(float(v), 2) for v in face[:4]],
    "landmarks": [round(float(v), 2) for v in face[4:14]],
    "score": round(float(face[14]), 4),
    "embedding": base64.b64encode(feature.tobytes()).decode("ascii"),
}, sys.stdout, indent=2)
sys.stdout.write("\n")
