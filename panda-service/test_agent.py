import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


class AgentStartupTest(unittest.TestCase):
    def test_missing_api_key_keeps_hints_available(self):
        env = dict(os.environ, OPENAI_API_KEY="")
        env["PYTHONPATH"] = str(Path(__file__).parent.resolve())
        with tempfile.TemporaryDirectory() as folder:
            result = subprocess.run(
                [sys.executable, "-c", "import agent; "
                 "assert agent.get_hint('A + B', '', '', 1) == agent.CANNED[1]; "
                 "assert agent.get_hint('A + B', '', '', 9) == agent.CANNED[3]; "
                 "assert agent.mood_for(1, 'stressed') == 'gentle'"],
                cwd=folder, env=env, capture_output=True, text=True,
                timeout=10,
            )
        self.assertEqual(result.returncode, 0, result.stderr)


if __name__ == "__main__":
    unittest.main()
