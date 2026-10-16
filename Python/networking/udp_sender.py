import socket
import time

from config import UNITY_IP, UNITY_PORT


class UDPSender:
    """
    High-throughput UDP sender for streaming real-time 3D pose data to Unity.
    """

    def __init__(self):
        self.socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.target = (UNITY_IP, UNITY_PORT)
        self.packet_count = 0
        self.last_log = time.time()
        print(f"[UDPSender] Streaming 3D Pose data to Unity at {UNITY_IP}:{UNITY_PORT}")

    def send(self, pose):
        if pose is None:
            return

        try:
            data = pose.to_json()
            self.socket.sendto(data.encode('utf-8'), self.target)
            self.packet_count += 1

            now = time.time()
            if now - self.last_log >= 3.0:
                print(f"[UDPSender] Sent {self.packet_count} packets. Latest punch: {pose.punch_type} (L: {pose.left_punch_speed:.1f} m/s, R: {pose.right_punch_speed:.1f} m/s)")
                self.last_log = now
        except Exception as e:
            print(f"[UDPSender] Send error: {e}")
