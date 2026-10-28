"""
list_cameras.py
===============
Utility to list and test all video capture devices (DirectShow / Media Foundation)
available on Windows. Helps identify your PC webcam index vs Phone Link camera index.
"""

import os
import sys

os.environ["OPENCV_LOG_LEVEL"] = "OFF"
import cv2
try:
    cv2.utils.logging.setLogLevel(cv2.utils.logging.LOG_LEVEL_SILENT)
except Exception:
    pass

def scan_cameras(max_tested=6):
    print("=" * 60)
    print(" SCANNING VIDEO CAPTURE DEVICES ON WINDOWS")
    print("=" * 60)
    found = []

    for idx in range(max_tested):
        cap = cv2.VideoCapture(idx, cv2.CAP_DSHOW) if sys.platform.startswith("win") else cv2.VideoCapture(idx)
        if not cap.isOpened():
            cap = cv2.VideoCapture(idx)

        if cap.isOpened():
            ret, frame = cap.read()
            if ret and frame is not None:
                h, w = frame.shape[:2]
                backend = cap.getBackendName()
                print(f" -> Camera Index [{idx}]: ONLINE ({w}x{h}, Backend: {backend})")
                found.append(idx)
            cap.release()

    print("=" * 60)
    if not found:
        print("No camera devices detected. Ensure permissions and connections are enabled.")
    else:
        print(f"Found {len(found)} active camera device(s): {found}")
        if len(found) >= 2:
            print(f"To launch tracker with these cameras:")
            print(f"  python Python/app.py --front {found[0]} --side {found[1]}")
        else:
            print(f"Only 1 camera found (Index {found[0]}).")
            print("To use Phone Link as Camera 2:")
            print("1. Go to Windows Settings -> Bluetooth & devices -> Mobile devices")
            print("2. Turn on 'Allow this PC to access your mobile devices'")
            print("3. Click 'Manage devices' next to your phone -> toggle ON 'Use as a connected camera'")
    print("=" * 60)

if __name__ == "__main__":
    scan_cameras()
