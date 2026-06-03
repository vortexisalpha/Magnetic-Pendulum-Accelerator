from dataclasses import dataclass
from fastapi import FastAPI, WebSocket
from state import MPData, Magnet, construct_mpdata_json
from enum import Enum
from config import *

app = FastAPI()

class Event(str, Enum):
    #client to server events:
    UpdateParams = "UpdateParams", #unity
    UpdateMagnetPosition = "UpdateMagnetPosition", #aruco
    AddMagnet = "AddMagnet", #aruco
    RemoveMagnet = "RemoveMagnet", #aruco
    UpdateImage = "UpdateImage", #fpga
    #server to client events:
    ImageUpdated = "ImageUpdated",
    MPDataUpdated = "MPDataUpdated"

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

    async def send(self, event, mp_data):
        if not self.ws:
            return

        data = {}

        if event == Event.MPDataUpdated:
            data = construct_mpdata_json(mp_data)
        elif event == Event.ImageUpdated:
             data = {
                "width": SCREEN_SIZE_X,
                "height": SCREEN_SIZE_Y,
                "bitDepth": mp_data.image_bit_depth,
                "image": mp_data.image,
                "receivedAt": mp_data.image_received_at,
                "version": mp_data.image_version,
                }
        else:
            raise Exception("something went wrong with sending to client")
            
        await self.ws.send_json(data)

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
        if event == Event.UpdateParams:
            mp_data.magnetic_strength = message_data["magnetic_strength"] 
            mp_data.damping_factor = message_data["damping_factor"] 
            mp_data.pendulum_height = message_data["pendulum_height"] 
            mp_data.pendulum_length = message_data["pendulum_length"] 
        else:
            raise Exception("something went wrong in unity handle message (wrong event)")


class FPGAClient(Client):
    def __init__(self):
        super().__init__("FPGA")
    def handle_message(self, event, message_data, mp_data):
        if event == Event.UpdateImage:
            mp_data.image = message_data["image"] 
            mp_data.image_bit_depth = message_data["bitDepth"] 
        else:
            raise Exception("something went wrong in unity handle message (wrong event)")

class ConnectionManager:
    def __init__(self):
        self.mp_data = MPData()
        self.aruco_client = ArUcoDetectionClient()
        self.unity_client = UnityClient()
        self.fpga_client = FPGAClient()
        self.connections = {"ArUco" : self.aruco_client, "Unity" : self.unity_client, "FPGA" : self.fpga_client}

    def connect(self, client_name, ws):
        self.connections[client_name].connect(ws)
    
    async def on_message(self, ws, client_name):
        message = await ws.recieve_json()
        event = Event(message['event'])
        client = self.connections[client_name]
        client.handle_message(event, message['data'], self.mp_data)

        await self.notify_relevant(client_name, event)

    async def notify_relevant(self, exception_client, event):
        #note that aruco marker never needs to be sent updates
        send_event = Event.MPDataUpdated
        if event == Event.UpdateImage:
            send_event = Event.ImageUpdated

        for name, client in self.connections.items():
            if name != exception_client and name != "ArUco":
                await client.send(send_event, self.mp_data)



connection_manager = ConnectionManager()

ALLOWED_CLIENTS = ["ArUco", "Unity", "FPGA"]

@app.websocket("/ws/{client_name}")
async def websocket_connect(ws, client_name):
    
    if client_name not in ALLOWED_CLIENTS:
        await ws.close()
        return

    await ws.accept()
    connection_manager.connect(client_name, ws)

    while True:
        await connection_manager.on_message(ws, client_name)




