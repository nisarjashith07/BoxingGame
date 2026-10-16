import cv2
import sys
from pathlib import Path

# Ensure Python directory is in sys.path
sys.path.insert(0, str(Path(__file__).resolve().parent))

from tracking.dual_tracker import DualPoseTracker
from tracking.visualizer import DualPoseVisualizer
from networking.udp_sender import UDPSender


def main():
    print("=" * 60)
    print("  STARTING VR BOXING DUAL-CAMERA TRACKING PIPELINE")
    print("  Front Camera: PC Webcam (Lateral + Height)")
    print("  Side Camera:  Phone Cam on Right (Optical Punch Depth)")
    print("  Hotkeys:")
    print("    [Q] / [ESC] - Quit")
    print("    [M]         - Toggle Side Camera Horizontal Mirror")
    print("    [R]         - Rotate Side Camera 90 degrees")
    print("=" * 60)

    tracker = DualPoseTracker()
    sender = UDPSender()
    visualizer = DualPoseVisualizer()

    window_name = "VR Boxing AI - Dual Camera MoCap"
    cv2.namedWindow(window_name, cv2.WINDOW_NORMAL)
    cv2.resizeWindow(window_name, 1280, 720)

    try:
        while True:
            front_frame, side_frame, pose = tracker.get_dual_pose()

            if front_frame is None and side_frame is None:
                print("No camera feeds available. Exiting...")
                break

            # Send 3D fused pose to Unity via UDP
            if pose is not None:
                sender.send(pose)

            # Render dual camera telemetry dashboard
            dashboard = visualizer.render_dashboard(front_frame, side_frame, pose, tracker)

            cv2.imshow(window_name, dashboard)

            key = cv2.waitKey(1) & 0xFF
            if key == 27 or key == ord('q') or key == ord('Q'):
                print("Exit requested by user.")
                break
            elif key == ord('m') or key == ord('M'):
                tracker.toggle_side_flip()
            elif key == ord('r') or key == ord('R'):
                tracker.rotate_side_90()
            elif key == ord('c') or key == ord('C'):
                tracker.start_calibration()

    except KeyboardInterrupt:
        print("\nPipeline interrupted by user.")
    finally:
        tracker.release()
        cv2.destroyAllWindows()
        print("Pipeline closed cleanly.")


if __name__ == "__main__":
    main()