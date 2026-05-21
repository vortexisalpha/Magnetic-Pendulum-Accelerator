from flask import Flask

app = Flask(__name__)

@app.route('/')
def ping():
    data_o = {"ping" : "true"}
    return data_o


app.run()
