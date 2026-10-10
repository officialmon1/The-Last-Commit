import json
import os
from pathlib import Path
import socket
import subprocess
import sys
import time
import unittest
from urllib.error import URLError
from urllib.request import Request, urlopen


class BackendSmokeTest(unittest.TestCase):
    def test_local_game_events_and_fallback_hint(self):
        with socket.socket() as listener:
            listener.bind(("127.0.0.1", 0))
            port = listener.getsockname()[1]
        process = subprocess.Popen(
            [sys.executable, "-m", "uvicorn", "main:app", "--port", str(port)],
            cwd=Path(__file__).parent, env=dict(os.environ, OPENAI_API_KEY=""),
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        )
        base = f"http://127.0.0.1:{port}"

        def request(path, body=None):
            data = None if body is None else json.dumps(body).encode()
            with urlopen(Request(base + path, data, {"Content-Type": "application/json"}), timeout=2) as response:
                return json.load(response)

        try:
            deadline = time.monotonic() + 20
            while True:
                try:
                    self.assertTrue(request("/health")["ok"])
                    break
                except URLError:
                    if process.poll() is not None or time.monotonic() >= deadline:
                        self.fail("The backend could not start without an API key.")
                    time.sleep(0.1)
            self.assertEqual(request("/event", {"type": "failed"})["failed_runs"], 1)
            self.assertEqual(request("/event", {"type": "failed"})["failed_runs"], 2)
            hint = request("/hint", {
                "problem": "A + B", "code": "print(0)", "test_results": "Wrong answer",
                "level": 1, "previous_hints": [], "minutes_left": 3.5,
            })
            self.assertTrue(hint["text"])
            self.assertEqual(hint["player_state"], "stuck")
            self.assertEqual(request("/event", {"type": "solved"})["celebrate"], "normal")
            self.assertEqual(request("/event", {"type": "reset"})["failed_runs"], 0)
            self.assertFalse(request("/status", {"idle_seconds": 20})["face_on"])
        finally:
            process.terminate()
            process.wait(timeout=10)


if __name__ == "__main__":
    unittest.main()
