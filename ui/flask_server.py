from _typeshed import DataclassInstance
from flask import Flask
from dataclasses import dataclass

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

@app.route('/')
def ping():
    data_o = {"ping" : "true"}
    return data_o

#get request that the user pings
@app.route('/info')
def info():
    data_o = {
            "magnet_1" : {
                "x": ""
                "y": ""
                }
            "strength"
            "length"...
            }
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
