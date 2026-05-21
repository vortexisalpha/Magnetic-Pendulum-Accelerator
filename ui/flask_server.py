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
    uid: str 
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

#add a magnet
"""
request must be of form:
    {   
        uid: str (note that this will be the type of arUco marker)
        x: float
        y: float
    }
"""
@app.route('/magnet_add', methods=["POST"])
def magnet_add():
    data = request.get_json()

    uid = data["uid"]
    x = float(data["x"])
    y = float(data["y"])

    magnet = Magnet(uid, x, y)
    mp_data.mag_list.append(magnet)

    return {"ok" : 200}

#remove a magnet
"""
request must be of form:
    {
        uid:  str (type of arUco marker)
    }
"""
@app.route('/magnet_remove', methods=["POST"])
def magnet_remove():
    data = request.get_json()

    uid = data["uid"]
    for i, magnet in enumerate(mp_data.mag_list):
        if magnet.uid == uid:
            mp_data.mag_list.pop(i)

    return {"ok" : 200}

#update a magnets position
"""
request must be of form:
    {   
        uid: str (note that this will be the type of arUco marker)
        x: float
        y: float
    }
"""
@app.route('/magnet_update_position')
def magnet_update_position():
    data = request.get_json()

    uid = data["uid"]
    x = float(data["x"])
    y = float(data["y"])

    for i, magnet in enumerate(mp_data.mag_list):
        if magnet.uid == uid:
            mp_data.mag_list[i].x = x
            mp_data.mag_list[i].y = y

    return {"ok" : 200}

#change the magnetic strength
"""
request must be of form:
    {   
        magnetic_strength: float
    }
"""
@app.route('/magnetic_strength')
def magnetic_strength():
    data = request.get_json()

    magnetic_strength = float(data["magnetic_strength"])
    mp_data.magnetic_strength = magnetic_strength

    return {"ok" : 200}

#change the damping factor 
"""
request must be of form:
    {   
        damping_factor: float
    }
"""
@app.route('/damping_factor')
def damping_factor():
    data = request.get_json()

    damping_factor = float(data["damping_factor"])
    mp_data.damping_factor = damping_factor 

    return {"ok" : 200}

#change the pendulum height
"""
request must be of form:
    {   
        pendulum_height: float
    }
"""
@app.route('/pendulum_height')
def pendulum_height():
    data = request.get_json()

    pendulum_height = float(data["pendulum_height"])
    mp_data.pendulum_height = pendulum_height 

    return {"ok" : 200}

@app.route('/length_of_pendulum')
def length_of_pendulum():
    return {"ok" : 200}

if __name__ == "__main__":
    mp_data = MPData()
    app.run()
