from _typeshed import DataclassInstance
from flask import Flask
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
    x_min: float
    x_max: float
    y_min: float
    y_max: float

#MPData stands for magnetic pendulum data
@dataclass
class MPData:
    mag_list: list[Magnet]
    grid: Grid
    magnetic_strength: float
    damping_factor: float   
    pendulum_height: float
    pendulum_length: float

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

@app.route('/')
def ping():
    data_o = {"ping" : "true"}
    return data_o

#get request that the user pings
@app.route('/info')
def info():
    pass

@app.route('/magnet_info')
def magnet_info():
    pass

@app.route('/magnet_position')
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
    app.run()
