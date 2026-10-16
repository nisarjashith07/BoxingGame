import cv2
import threading
import time


class ThreadedCamera:
    """
    High-performance threaded camera capture for OpenCV.
    Runs frame capture in a background thread to prevent camera I/O from blocking
    the pose tracking pipeline, ensuring ultra-low latency and maximum FPS.
    """

    def __init__(self, source, width=1280, height=720, name="Camera"):
        self.source = source
        self.name = name
        self.width = width
        self.height = height

        self.cap = None
        self.ret = False
        self.frame = None
        self.running = False
        self.lock = threading.Lock()
        self.thread = None

        self._init_camera()

    def _init_camera(self):
        try:
            # Handle integer indices or string URLs
            src = int(self.source) if isinstance(self.source, str) and self.source.isdigit() else self.source
            self.cap = cv2.VideoCapture(src)

            if not self.cap.isOpened():
                print(f"[{self.name}] Warning: Could not open camera source: {self.source}")
                return

            if self.width:
                self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, self.width)
            if self.height:
                self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, self.height)

            # Minimize internal buffer to 1 frame for real-time VR tracking
            self.cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)

            self.ret, self.frame = self.cap.read()
            if not self.ret or self.frame is None:
                print(f"[{self.name}] Warning: Opened camera, but initial frame read failed.")
            else:
                h, w = self.frame.shape[:2]
                print(f"[{self.name}] Initialized successfully at {w}x{h}")

            self.running = True
            self.thread = threading.Thread(target=self._capture_loop, daemon=True)
            self.thread.start()

        except Exception as e:
            print(f"[{self.name}] Exception while initializing camera: {e}")

    def _capture_loop(self):
        while self.running and self.cap is not None and self.cap.isOpened():
            ret, frame = self.cap.read()
            if ret and frame is not None:
                with self.lock:
                    self.ret = ret
                    self.frame = frame
            else:
                time.sleep(0.01)
            time.sleep(0.002)

    def is_opened(self):
        return self.cap is not None and self.cap.isOpened() and self.running

    def read(self):
        """Returns (ret, frame) safely without blocking."""
        with self.lock:
            if not self.ret or self.frame is None:
                return False, None
            return True, self.frame.copy()

    def release(self):
        self.running = False
        if self.thread and self.thread.is_alive():
            self.thread.join(timeout=0.5)
        if self.cap:
            self.cap.release()
            self.cap = None
        print(f"[{self.name}] Camera released.")
