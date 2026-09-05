from dataclasses import dataclass, asdict
from typing import List
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

    def to_json(self):
        return json.dumps({
            "frame": self.frame,
            "landmarks": [asdict(lm) for lm in self.landmarks]
        })