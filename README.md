# The Last Commit 🌲🍄🌿

**What if your dream job depended on your ability to escape another world?**

The Last Commit is a fantasy coding adventure built for Knight Hacks. Explore an enchanted amber-crimson forest, solve programming challenges, and receive support from **Bao Bao**, an AI-powered panda companion. The story gives you **36 minutes** to find your way home before you become part of the world forever.



## The Problem

As computer science students, we know the pressure of technical interviews, coding challenges, and the constant pursuit of becoming better programmers. Practicing those skills can feel disconnected from the excitement that first drew us to technology.

We wanted to turn that pressure into an adventure: one where programming is part of the story, progress is visible in the world, and players can ask for help when they need it.



## Our Solution

**Welcome to Knight Hacks, a world you were never meant to enter.**

You awaken in a mysterious forest filled with ancient ruins. Your programming skills are your path home, and the countdown never stops. The adventure connects algorithmic problem-solving to the restoration of broken pillars and the search for a magical escape portal.

You are not alone. **Bao Bao** travels with you, carrying guidance inspired by **Grand Bao Bao's book**. He offers coding hints and encouragement as you work through the trials.

The game also explores facial-expression-based interaction using MediaPipe and OpenCV. Visible expression cues can help trigger supportive responses, adding another way for Bao Bao to encourage players.

The goal is to make programming practice feel less like studying and more like an adventure worth surviving.



## Key Features

- **Fantasy exploration:** A first-person adventure through an amber-crimson forest with ancient ruins, mossy stone pillars, and a magical escape story.
- **Programming trials:** Complete partially written programs, run Python solutions, and submit them against predefined test cases.
- **Visible progression:** Solved challenges restore broken pillars; the integrated scene also unlocks a central Dijkstra challenge after all four prerequisite problems pass.
- **Countdown-driven gameplay:** The story's 36-minute deadline adds urgency to the journey.
- **Bao Bao, the AI companion:** Request increasingly specific hints informed by the active problem, your code, recent results, previous hints, and remaining time.
- **Fallback support:** Built-in hints remain available when an OpenAI API key is not configured.
- **Face and hand tracking:** Camera-based interaction supports the team's exploration of a more responsive companion.
- **Saved progress and companion reactions:** Local saves, failure and celebration events, Boss progression, and the flower death/revive sequence are retained.
- **Automatic local backend startup:** Unity can start and reuse the Panda service using the Windows workflow documented below.



## Data

The prototype uses gameplay and interaction data rather than a conventional analytics dataset.

| Data | Purpose |
| --- | --- |
| Programming challenges, starter code, and predefined test cases | Define each trial and evaluate submitted solutions. |
| The player's code and latest run result | Provide execution feedback and context for Bao Bao's hints. |
| Earlier hints and remaining time | Help the assistant adapt its guidance to the current attempt. |
| Submission results and saved progress | Drive progression, prerequisite checks, and companion reactions. |
| Camera input when tracking is enabled | Support face and hand tracking and expression-based interaction. |
| 3D models, materials, textures, and scenes | Build the forest, ruins, pillars, panda, and other visual elements. |

The OpenAI hint context is described in **How It Works**. Face and hand tracking can be disabled through the scene's **PandaService** settings.



### Cost Estimation

Separate development expenses from the recurring cost of generated hints.

| Cost category | Estimation basis |
| --- | --- |
| OpenAI-generated hints | The selected model's pricing and the input/output token usage of each request. |
| Aholo asset generation | The service's applicable plan or credit charges for the asset-generation jobs used. |
| Local development and execution | The team's hardware and development setup; the documented Panda backend runs locally. |
| Future hosted services | Hosting or infrastructure costs if a remote deployment is introduced. |

For uncached text requests priced per million tokens:

```text
Estimated hint cost =
    (input tokens / 1,000,000 × input rate)
  + (output tokens / 1,000,000 × output rate)

Estimated session API cost = sum of billable request costs in that session
```

Account separately for cached-input pricing or any additional billed services when applicable. Use the selected model's rates from the [official OpenAI API pricing documentation](https://developers.openai.com/api/docs/pricing).

Exact dollar totals and measured per-session costs are not documented here. Asset-generation credits should be tracked separately from dollars unless the applicable plan establishes a conversion. When no API key is configured, the documented fallback path provides built-in hints.



## Cost Impact Methodology

The following method can be used to evaluate operating costs as the prototype develops:

1. **Separate one-time and recurring costs.** Record asset-generation jobs separately from runtime AI requests.
2. **Measure representative sessions.** Record request counts and billable token usage for players who request different amounts of help.
3. **Apply the relevant rates.** Calculate usage costs using the actual configured model and pricing for the chosen service.
4. **Compare hint modes.** Compare API-generated guidance with the built-in fallback path in terms of cost, usefulness, and response time.
5. **Evaluate the learning experience.** Consider challenge completion, repeated attempts, and player feedback alongside cost.

These are evaluation steps, not reported benchmark results. No numerical cost savings or learning-outcome improvements are claimed.



## How It Works

### Adventure and challenge progression

The story centers on restoring four broken pillars to escape the world. The current `ling-zara` flow described below includes **four prerequisite challenges followed by a fifth, central Dijkstra challenge**. The central challenge remains locked until all four prerequisites pass Submit.

Explore the forest, open a pillar's terminal, complete the code, and use Bao Bao's guidance when needed. Passing submissions advances the game; running code alone does not satisfy the prerequisite checks.

### Controls

| Action | Control |
| --- | --- |
| Reveal the pillars | Enter |
| Move | WASD |
| Open a pillar terminal | Look at a pillar and press E |
| Run Python | Ctrl+Enter |
| Submit a solution | Ctrl+Shift+Enter |
| Request guidance | **Panda hint** button |

### Existing scene and integration notes

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

| Technology | Role |
| --- | --- |
| Unity 6 | Game engine, scenes, gameplay integration, and rendering. |
| C# | Game logic, interactions, progression, and Unity-side integration. |
| Python 3.12 | Panda backend, supporting functionality, and the documented Python execution workflow. |
| Uvicorn | Runs the local Panda service in the documented manual-start workflow. |
| HTML | Used for the interactive coding interface described in the project story. |
| OpenAI API | Generates context-aware coding hints for Bao Bao. |
| MediaPipe | Face and hand tracking models and interaction support. |
| OpenCV | Supporting computer-vision functionality. |
| Blender | 3D asset and environment work. |
| Aholo / Lux3D | AI-assisted 3D asset generation. |
| Git and GitHub | Version control and team collaboration. |

ChatGPT, Claude, Perplexity, and GitHub Copilot also supported brainstorming, debugging, research, and implementation during development.



## Project Context

The Last Commit was created during **Knight Hacks**. Most of our team had never developed a game before, so we were learning unfamiliar tools while building an interactive experience under hackathon time constraints.

The forest and asset paths retain the earlier **The Amber Ruins** naming, while the project is presented as **The Last Commit**.

### Inspiration

Our love of gaming, storytelling, and computer science inspired us to connect technical interview practice with a fantasy adventure. We wanted programming skills to shape the player's fate and make every solved challenge feel meaningful.

### Challenges we ran into

- Building a 3D game while learning Unity and new game-development workflows.
- Connecting gameplay, the coding interface, AI-generated hints, and computer vision.
- Troubleshooting asset compatibility, missing textures, materials, shaders, and rendering.
- Collaborating on large assets and Unity scene files through Git and GitHub.
- Balancing the countdown's urgency with accessible, enjoyable programming practice.

### Accomplishments that we're proud of

We turned an ambitious idea into an interactive game during a single hackathon. For most of the team, this was our first game-development experience.

We are especially proud of Bao Bao, the integration of programming challenges into the environment, and the visible progression that gives solved problems a place in the story. Combining the forest, ruins, coding editor, and companion challenged us to learn and build together.

### What we learned

We gained hands-on experience with Unity 6, C#, Python, Blender, OpenAI API integration, MediaPipe, OpenCV, and interactive coding interfaces.

We learned how much coordination is needed when several people contribute scenes, scripts, and assets to one project. We also learned to connect technical functionality with the player's experience through storytelling, progression, and supportive feedback.

Being unfamiliar with a technology should not stop us from trying to build something ambitious.

### What's next for The Last Commit

- Expand the variety and difficulty of programming challenges.
- Improve Bao Bao's hints and companion interactions.
- Refine how facial-expression feedback contributes to the experience.
- Add environments, interactive characters, and puzzles covering more computer science concepts.
- Develop the prototype into a fuller adventure for programming practice and technical interview preparation.

**Because sometimes, the most important commit isn't the one you push to GitHub. It's the one you make to keep learning.**



## Limitations

- **Hackathon scope:** This is a prototype with a limited challenge set and room for further gameplay, polish, and compatibility work.
- **Generated guidance:** AI hints can be incomplete or incorrect; players should verify suggestions by running and submitting their solutions.
- **Test coverage:** Passing predefined test cases evaluates those cases and does not establish correctness for every possible input.
- **Camera-dependent interaction:** Face and hand tracking require camera access and can be affected by lighting and tracking conditions.
- **Expression interpretation:** Facial-expression cues are approximate and do not establish a player's emotional state or programming difficulty.
- **Windows setup:** The documented workflow expects Python 3.12, installed backend dependencies, and an accessible `panda-service` folder. The automatic-start instructions describe that setup rather than a standalone package for every machine.
- **Asset integration:** Materials and shaders may need adjustments when imported into different Unity configurations.
- **Evaluation:** This README does not report measured learning outcomes, per-session costs, or production-scale performance.



## Data Source

The game's challenges, starter programs, predefined test cases, and progress state are supplied by the project. No external challenge collection is identified in this README.

Bao Bao's generated hints come from the OpenAI API when configured; built-in hints support the fallback path. The tracking workflow uses the checked-in MediaPipe models in `panda-service/models`.

The environment combines Unity and Blender work with Aholo-generated assets. Asset filenames and the existing project layout are preserved in **Main Branch File Structure**.

Technical references:

- [Unity 6 Manual](https://docs.unity3d.com/6000.0/Documentation/Manual/UnityManual.html)
- [OpenAI API pricing documentation](https://developers.openai.com/api/docs/pricing)
- [MediaPipe Face Landmarker guide](https://developers.google.com/edge/mediapipe/solutions/vision/face_landmarker)
- [OpenCV documentation](https://docs.opencv.org/4.x/)

The MediaPipe guide describes facial landmarks and expression blendshape outputs; the game's interpretation of those signals is part of the companion interaction design.



## Project Contribution Team
Lingtong Meng, Monica Barbosa, Shirina Daniel & Zara Maraj
