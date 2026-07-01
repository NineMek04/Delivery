import sys
import os

# Automatically append the 'route-optimizer' root directory to sys.path so tests can import 'main' and 'app' directly.
route_optimizer_path = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..', '..', 'route-optimizer'))
sys.path.insert(0, route_optimizer_path)

os.environ.setdefault("ROUTE_OPTIMIZER_API_KEY", "test-key")

from main import app
from app.core.security import verify_api_key

app.dependency_overrides[verify_api_key] = lambda: "test-key"
