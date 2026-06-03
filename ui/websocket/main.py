from dataclasses import dataclass
from fastapi import FastAPI, WebSocket
from state import MPData, Magnet
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
        self.ws = None

    def connect(self, ws):
        self.connected = True
        self.ws = ws

class ArUcoDetectionClient(Client):
    def __init__(self):
        super().__init__("ArUco")

    def handle_message(self, event, message_data, mp_data):
        match event:
            case Event.UpdateMagnetPosition:
                uid = message_data['uid']
                x = float(message_data['x'])
                y = float(message_data['y'])
                self.update_magnet_position(uid, x, y, mp_data)

            case Event.RemoveMagnet:
                uid = message_data['uid']
                self.remove_magnet(uid, mp_data)

            case Event.AddMagnet:
                uid = message_data['uid']
                x = float(message_data['x'])
                y = float(message_data['y'])
                self.add_magnet(uid, x, y, mp_data)

    def update_magnet_position(self, uid, x, y, mp_data):
        for magnet in mp_data.mag_list:
            if magnet.uid == uid:
                magnet.x = x
                magnet.y = y
                return

    def remove_magnet(self, uid, mp_data):
        for i, magnet in enumerate(mp_data.mag_list):
            if magnet.uid == uid:
                mp_data.mag_list.pop(i)
                return

    def add_magnet(self, uid, x, y, mp_data):
        mp_data.mag_list.append(Magnet(uid, x, y))
        return

class UnityClient(Client):
    def __init__(self):
        super().__init__("Unity")

    def handle_message(self, event, message_data, mp_data):
        pass


class FPGAClient(Client):
    def __init__(self):
        super().__init__("FPGA")
    def handle_message(self, event, message_data, mp_data):
        pass


class ConnectionManager:
    def __init__(self):
        self.mp_data = MPData()
        self.aruco_client = ArUcoDetectionClient()
        self.unity_client = UnityClient()
        self.fpga_client = FPGAClient()
        self.connections = {"ArUco" : self.aruco_client, "Unity" : self.unity_client, "FPGA" : self.fpga_client}
    
    async def on_message(self, ws, client_name):
        message = await ws.recieve_json()
        event = Event(message['event'])
        client = self.connections[client_name]
        client.handle_message(event, message['data'], self.mp_data)
        await self.notify_all_except(client_name, event)

    async def notify_all_except(self, client, event):
        pass


connection_manager = ConnectionManager()

ALLOWED_CLIENTS = ["ArUco", "Unity", "FPGA"]

@app.websocket("/ws/{client_name}")
async def websocket_connect(ws, client_name):
    
    if client_name not in ALLOWED_CLIENTS:
        await ws.close()
        return

    match client_name:
        case "ArUco":
            connection_manager.aruco_client.connect(ws)
        case "Unity":
            connection_manager.unity_client.connect(ws)
        case "FPGA":
            connection_manager.fpga_client.connect(ws)

    await ws.accept()

    while True:
        await connection_manager.on_message(ws, client_name)




