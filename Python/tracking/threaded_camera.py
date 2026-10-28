import cv2
import threading
import time
import sys


class ThreadedCamera:
    """
    Ultra-low-latency threaded camera capture for OpenCV on Windows.
    Uses DirectShow (CAP_DSHOW) to bypass Windows Media Foundation's 3-frame buffer,
    locking internal buffer to 1 frame for real-time VR tracking.
    """

    def __init__(self, source, width=640, height=480, name="Camera"):
        self.source = source
        self.name = name
        self.width = width
        self.height = height

        self.cap = None
        self.ret = False
        self.frame = None
        self.frame_time = 0.0
        self.running = False
        self.lock = threading.Lock()
        self.thread = None

        self._init_camera()

    def _init_camera(self):
        try:
            # Check if source is integer camera index
            is_index = False
            src = self.source
            if isinstance(src, int):
                is_index = True
            elif isinstance(src, str) and src.isdigit():
                src = int(src)
                is_index = True

            # Use DirectShow on Windows for zero buffer latency
            if is_index and sys.platform.startswith("win"):
                self.cap = cv2.VideoCapture(src, cv2.CAP_DSHOW)
            else:
                self.cap = cv2.VideoCapture(src)

            if not self.cap.isOpened():
                # Fallback to default backend if DSHOW fails
                self.cap = cv2.VideoCapture(src)

            if not self.cap.isOpened():
                print(f"[{self.name}] Warning: Could not open camera source: {self.source}")
                return

            if self.width:
                self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, self.width)
            if self.height:
                self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, self.height)

            # Request high frame rate and strictly 1-frame buffer
            self.cap.set(cv2.CAP_PROP_FPS, 60)
            self.cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)

            self.ret, self.frame = self.cap.read()
            if not self.ret or self.frame is None:
                print(f"[{self.name}] Warning: Opened camera, but initial frame read failed.")
            else:
                h, w = self.frame.shape[:2]
                print(f"[{self.name}] Zero-latency capture initialized at {w}x{h}")

            self.running = True
            self.thread = threading.Thread(target=self._capture_loop, daemon=True)
            self.thread.start()

        except Exception as e:
            print(f"[{self.name}] Exception while initializing camera: {e}")

    def _capture_loop(self):
        # Dedicated hardware polling loop with zero sleep delay
        while self.running and self.cap is not None and self.cap.isOpened():
            # grab() is fast hardware frame query
            grabbed = self.cap.grab()
            if grabbed:
                capture_time = time.perf_counter()
                ret, frame = self.cap.retrieve()
                if ret and frame is not None:
                    with self.lock:
                        self.ret = ret
                        self.frame = frame
                        self.frame_time = capture_time
            else:
                time.sleep(0.005)

    def is_opened(self):
        return self.cap is not None and self.cap.isOpened() and self.running

    def read(self):
        """Returns (ret, frame) safely without blocking."""
        with self.lock:
            if not self.ret or self.frame is None:
                return False, None
            return True, self.frame

    def read_timestamped(self):
        """Returns (ret, frame, frame_time) safely without blocking."""
        with self.lock:
            if not self.ret or self.frame is None:
                return False, None, 0.0
            return True, self.frame, self.frame_time

    def release(self):
        self.running = False
        if self.thread and self.thread.is_alive():
            self.thread.join(timeout=0.5)
        if self.cap:
            self.cap.release()
            self.cap = None
        print(f"[{self.name}] Camera released.")
