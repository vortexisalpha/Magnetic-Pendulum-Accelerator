from dataclasses import dataclass
from fastapi import FastAPI, WebSocket
from state import MPData
from enum import Enum


app = FastAPI()

class Event(str, Enum):
    UpdateParams = "UpdateParams",
    UpdateMagnetPosition = "UpdateMagnetPosition",
    AddMagnet = "AddMagnet",
    RemoveMagnet = "RemoveMagnet",
    UpdateImage = "UpdateImage",
"""
json format:
    client: str
    event: str
    data: dict
"""

class Client:
    def __init__(self, name):
        self.name = name
        self.connected = False

    def handle_message(self, message, data):
        raise Exception("handle_message hasnt been implemented")

class ArUcoDetectionClient(Client):
    def __init__(self):
        super().__init__("ArUco")
    def handle_message(self, message, data):
        case message('')
    
class UnityClient(Client):
    def __init__(self):
        super().__init__("Unity")
    def handle_message(self, message, data):
        pass

class FPGAClient(Client):
    def __init__(self):
        super().__init__("FPGA")
    def handle_message(self, message, data):
        pass

class ConnectionManager:
    def __init__(self):
        self.mp_data = MPData()
        self.aruco_client = ArUcoDetectionClient()
        self.unity_client = UnityClient()
        self.fpga_client = FPGAClient()
        self.connections = [self.aruco_client, self.unity_client, self.fpga_client]
    
    async def OnMessage(self, message):
        match message['client']:
            case("Aruco"):
                self.aruco_client.handle_message(message['data'], self.mp_data)
            case("Unity"):
                self.unity_client.handle_message(message['data'], self.mp_data)
            case("FPGA"):
                self.fpga_client.handle_message(message['data'], self.mp_data)


connection_manager = ConnectionManager()


@app.websocket("/ws")
async def websocket_connect(ws : WebSocket):
    
    await ws.accept()

    while True:
        message = await ws.recieve_json()




