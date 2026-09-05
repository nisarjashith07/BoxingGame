import cv2

from tracking.tracker import PoseTracker
from tracking.visualizer import PoseVisualizer
from networking.udp_sender import UDPSender

tracker = PoseTracker()
sender = UDPSender()

visualizer = PoseVisualizer()

while True:

    frame, pose = tracker.get_pose()

    if frame is None:
        break

    sender.send(pose)
    frame = visualizer.draw(frame, pose)

    cv2.imshow("Boxing AI", frame)

    if cv2.waitKey(1) == 27:
        break

tracker.release()

cv2.destroyAllWindows()