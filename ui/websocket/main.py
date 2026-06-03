from dataclasses import dataclass
from fastapi import FastAPI, WebSocket
from state import MPData


app = FastAPI()

"""
Events:
    UpdateParams
    UpdateMagnetPosition
    AddMagnet
    RemoveMagnet
    UpdateImage
"""

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

class UnityClient(Client):
    def __init__(self):
        super().__init__("Unity")

class FPGAClient(Client):
    def __init__(self):
        super().__init__("FPGA")


class ConnectionManager:
    def __init__(self):
        self.mp_data = MPData()
        self.aruco_client = ArUcoDetectionClient()
        self.unity_client = UnityClient()
        self.fpga_client = FPGAClient()
        self.connections = [self.aruco_client, self.unity_client, self.fpga_client]
    
    async def OnMessage(self, message):
        if message['client'] == "ArUco":
            self.aruco_client.handle_message(message, self.mp_data)
        elif message['client'] == "Unity":
            self.unity_client.handle_message(message, self.mp_data)
        elif message['client'] == "FPGA":
            self.fpga_client.handle_message(message, self.mp_data)


connection_manager = ConnectionManager()


@app.websocket("/ws")
async def websocket_connect(ws : WebSocket):
    
    await ws.accept()

    while True:
        message = await ws.recieve_json()




