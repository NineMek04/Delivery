import sys
import os

# Automatically append the 'ai-engine' root directory to sys.path so tests can import 'main' and 'app' directly.
ai_engine_path = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..', '..', 'ai-engine'))
sys.path.insert(0, ai_engine_path)
