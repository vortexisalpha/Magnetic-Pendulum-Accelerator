from fastapi import FastAPI, WebSocket


app = FastAPI()

class Client:
    def __init__(self, name):
        self.name = name

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
        self.aruco_client = ArUcoDetectionClient()
        self.unity_client = UnityClient()
        self.fpga_client = FPGAClient()
        self.connections = [self.aruco_client, self.unity_client, self.fpga_client]



@app.websocket("/ws")
async def websocket_connect(ws : WebSocket):
    print("connecting...")
    
    await ws.accept()

    while True:
        message = await ws.receive_text()
