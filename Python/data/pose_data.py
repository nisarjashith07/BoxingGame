from dataclasses import dataclass, asdict
from typing import List, Optional
import json


@dataclass
class Landmark:
    x: float
    y: float
    z: float
    visibility: float = 1.0


@dataclass
class PoseData:
    frame: int
    landmarks: List[Landmark]
    landmarks_right: Optional[List[Landmark]] = None
    landmarks_front: Optional[List[Landmark]] = None
    left_punch_speed: float = 0.0
    right_punch_speed: float = 0.0
    left_punch_state: str = "guard"
    right_punch_state: str = "guard"
    punch_type: str = "none"

    def to_json(self):
        payload = {
            "frame": self.frame,
            "landmarks": [asdict(lm) for lm in self.landmarks]
        }
        if self.landmarks_right:
            payload["landmarks_right"] = [asdict(lm) for lm in self.landmarks_right]
        if self.landmarks_front:
            payload["landmarks_front"] = [asdict(lm) for lm in self.landmarks_front]
        
        payload["left_punch_speed"] = round(float(self.left_punch_speed), 2)
        payload["right_punch_speed"] = round(float(self.right_punch_speed), 2)
        payload["left_punch_state"] = self.left_punch_state
        payload["right_punch_state"] = self.right_punch_state
        payload["punch_type"] = self.punch_type

        return json.dumps(payload)