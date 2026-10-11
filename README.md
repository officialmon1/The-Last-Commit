# The Last Commit 🌲🍄🌿



## The Problem



## Our Solution



## Key Features



## Data



### Cost Estimation



## Cost Impact Methodology



## How It Works

The `ling-zara` scene keeps the `lingtong` first-person code-terminal flow and adds Zara's Panda companion.
Press Enter to reveal the pillars, then walk with WASD, look at a pillar and press E.
Complete the four nearby problems in any order. The larger central pillar holds the final Dijkstra problem
and stays locked until all four prerequisite problems have passed Submit. Its label shows the current progress.
Run Python with Ctrl+Enter, Submit with Ctrl+Shift+Enter,
and use **Panda hint** for increasingly specific hints. The assistant receives the active problem, your code,
latest run result, earlier hints and remaining time. Real submissions drive its failure and celebration events.
The local saves, Boss progression, countdown and flower death/revive sequence are retained.

### Panda assistant setup (Windows)

From the project root, install the existing backend dependencies with Python 3.12:

```powershell
py -3.12 -m venv panda-service/.venv
./panda-service/.venv/Scripts/python.exe -m pip install -r panda-service/requirements.txt
```

For generated hints, create `panda-service/.env` from `.env.example` and set your `OPENAI_API_KEY`.
Without a key, Panda uses the built-in fallback hints and the backend still starts.
Unity starts the backend automatically on Play and stops the process it started when Play ends.
You can also start it manually from `panda-service` with `.venv/Scripts/python.exe -m uvicorn main:app`;
Unity reuses an already running service at `127.0.0.1:8000`.
Windows builds inside `Builds/` also start Panda automatically: the game searches its parent folders
for the project's `panda-service` folder and reads that folder's `.env`. No separate setup is needed on this machine.

Zara's face/hand tracking remains available and starts automatically by default.
Allow camera access, or disable **Auto Enable Face Tracking** on the scene's **PandaService** object.
The checked-in MediaPipe models are in `panda-service/models`.

Run backend checks with `panda-service/.venv/Scripts/python.exe -m unittest discover -s panda-service -p 'test_*.py'`.
For the Unity integration check, enter a fresh Play session in `SampleScene` and, before opening a terminal, choose
**Tools > Panda > Check integration (Play mode)**. It uses a temporary save and checks real Python Run/Submit,
failure events, the hint button, celebration, all prerequisite combinations, saved progress, central pillar layout, context and JSON payloads.

## Tech Stack



## Project Context



## Limitations



## Data Source



## Project Contribution Team
Lingtong Meng, Monica Barbosa, Shirina Daniel & Zara Maraj
