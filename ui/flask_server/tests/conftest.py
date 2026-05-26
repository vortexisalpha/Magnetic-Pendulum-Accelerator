import pytest

from flask_server.flask_server import app, reset_mp_data


@pytest.fixture
def client():
    app.config["TESTING"] = True
    with app.test_client() as test_client:
        yield test_client


@pytest.fixture(autouse=True)
def clean_state():
    reset_mp_data()
    yield
    reset_mp_data()
