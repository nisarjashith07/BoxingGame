import cv2

class PoseVisualizer:
    def draw(self, frame, pose):
        # We are now sending 3D World Landmarks over the network, 
        # so we skip drawing the 2D overlay here to avoid coordinate crashes.
        return frame
