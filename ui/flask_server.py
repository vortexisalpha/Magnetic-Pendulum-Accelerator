from flask import Flask

app = Flask(__name__)

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
