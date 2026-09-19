import cv2
import mediapipe as mp

from mediapipe.tasks import python
from mediapipe.tasks.python import vision

from config import MODEL_PATH, CAMERA_INDEX, LANDMARK_COUNT
from data.pose_data import PoseData, Landmark


class PoseTracker:

    def __init__(self):
        print(f"Loading model: {MODEL_PATH}")

        base_options = python.BaseOptions(
            model_asset_path=str(MODEL_PATH)
        )

        options = vision.PoseLandmarkerOptions(
            base_options=base_options,
            num_poses=1
        )

        self.landmarker = vision.PoseLandmarker.create_from_options(options)
        self.cap = cv2.VideoCapture(CAMERA_INDEX)

        # Request a higher resolution for a wider field of view
        self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1280)
        self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 720)

        self.frame = 0

    def get_pose(self):
        success, frame = self.cap.read()

        if not success:
            return None, None

        rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        mp_image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
        result = self.landmarker.detect(mp_image)

        if len(result.pose_landmarks) == 0:
            return frame, None

        pose = result.pose_world_landmarks[0]
        landmarks = []

        for i in range(LANDMARK_COUNT):
            lm = pose[i]
            landmarks.append(
                Landmark(x=lm.x, y=lm.y, z=lm.z, visibility=1.0)
            )

        self.frame += 1
        return frame, PoseData(frame=self.frame, landmarks=landmarks)

    def release(self):
        self.cap.release()
