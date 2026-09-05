import socket

from config import UNITY_IP
from config import UNITY_PORT


class UDPSender:

    def __init__(self):

        self.socket = socket.socket(

            socket.AF_INET,

            socket.SOCK_DGRAM

        )

    def send(self, pose):

        if pose is None:

            return

        data = pose.to_json()
        print(data)
        self.socket.sendto(

            data.encode(),

            (UNITY_IP, UNITY_PORT)

        )