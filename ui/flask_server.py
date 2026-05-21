from _typeshed import DataclassInstance
from flask import Flask, request
from dataclasses import dataclass
import json


# TODO: DISCUSS LATER
SCREEN_SIZE_X = 160
SCREEN_SIZE_Y = 120

#make backend flask app
app = Flask(__name__)

#DATA DECLARATIONS:

#individual magnet x, y
@dataclass
class Magnet:
    x: float 
    y: float 

#coordinates of the actual visualisation
@dataclass
class Grid:
    x_min: float = 0
    x_max: float = SCREEN_SIZE_X
    y_min: float = 0
    y_max: float = SCREEN_SIZE_Y

#MPData stands for magnetic pendulum data
@dataclass
class MPData:
    mag_list: list[Magnet] = []
    grid: Grid = Grid()
    magnetic_strength: float = 1
    damping_factor: float = 1
    pendulum_height: float = 1
    pendulum_length: float = 1

#main construction function for main get request endpoint
def construct_mpdata_json(mp_data: MPData):
    data = {}
    #magnets
    for i, magnet in enumerate(mp_data.mag_list):
        temp_mag = {}
        temp_mag["x"] = magnet.x
        temp_mag["y"] = magnet.y

        data["magnets"][f"magnet_{i}"] = temp_mag
    
    #grid
    data["grid"]["x_min"] = mp_data.grid.x_min
    data["grid"]["x_max"] = mp_data.grid.x_max
    data["grid"]["y_min"] = mp_data.grid.y_min
    data["grid"]["y_max"] = mp_data.grid.y_max

    #other params
    data["magnetic_strength"] = MPData.magnetic_strength
    data["damping_factor"] = MPData.damping_factor
    data["pendulum_height"] = MPData.pendulum_height
    data["pendulum_length"] = MPData.pendulum_length

    return json.dumps(data)

#ALL ENDPOINTS:

#default ping for checking connection
@app.route('/')
def ping():
    data_o = {"ping" : "true"}
    return data_o

#all information user get request
@app.route('/info')
def info():
    return construct_mpdata_json(mp_data)

#magnet_add
"""
request must be of form:
    {
        x: float
        y: float
    }
"""
@app.route('/magnet_add', methods=["POST"])
def magnet_info():
    data = request.get_json()

    x = float(data["x"])
    y = float(data["y"])

    magnet = Magnet(x,y)
    mp_data.mag_list.append(magnet)

    return {"ok" : 200}

@app.route('/magnet_update_position')
def magnet_position():
    pass

@app.route('/magnetic_strength')
def magnetic_strength():
    pass

@app.route('/damping_factor')
def magnetic_density():
    pass

@app.route('/height_of_pendulum')
def height_of_pendulum():
    pass

@app.route('/length_of_pendulum')
def height_of_pendulum():
    pass

if __name__ == "__main__":
    mp_data = MPData()
    app.run()
