import time
from contextlib import asynccontextmanager

import cv2
from fastapi import FastAPI
from fastapi.responses import FileResponse, Response, StreamingResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel

from agent import get_hint, mood_for, CANNED
from coach import Coach
from face import FaceTracker

tracker = None      # created only when the player opts in to face tracking
coach = Coach()


@asynccontextmanager
async def lifespan(app):
    yield
    if tracker is not None:
        tracker.stop()


app = FastAPI(lifespan=lifespan)
app.mount("/static/assets", StaticFiles(directory="static/assets"), name="assets")


def current_player_state():
    """Always comes from the live face tracker (plus failed runs). Clients can't override it."""
    state = "calm"
    if tracker is not None:
        face = tracker.snapshot()
        if face["present"] and not face["calibrating"]:
            state = face["player_state"]          # calm | stuck | stressed
    if state == "calm" and coach.failed_runs >= 2:
        state = "stuck"
    return state


# ---------------- hints ----------------
class HintRequest(BaseModel):
    problem: str = ""
    code: str = ""
    test_results: str = ""
    level: int = 1
    previous_hints: list[str] = []
    minutes_left: float | None = None


@app.post("/hint")
def hint(req: HintRequest):
    level = max(1, min(3, req.level))
    state = current_player_state()
    try:
        text = get_hint(req.problem, req.code, req.test_results or "none provided",
                        level, req.previous_hints, state, req.minutes_left)
    except Exception as e:
        print("Hint error:", e)
        text = CANNED[level]
    return {"text": text, "mood": mood_for(level, state), "player_state": state}


# ---------------- face tracking (opt-in) ----------------
@app.post("/face/start")
def face_start():
    global tracker
    if tracker is None:
        t = FaceTracker()
        t.start()
        for _ in range(60):                      # wait up to ~6s for the camera to open
            if t.camera_ok is not None:
                break
            time.sleep(0.1)
        if t.camera_ok is not True:
            t.stop()
            return {"ok": False, "error": "Could not open the camera. Check permission or CAMERA_INDEX in .env."}
        tracker = t
    tracker.calibrate(5)
    return {"ok": True}


@app.post("/face/stop")
def face_stop():
    global tracker
    if tracker is not None:
        tracker.stop()
        tracker = None
    return {"ok": True}


def _render(frame, snap, overlay):
    view = cv2.flip(frame, 1)                    # mirror, like a selfie camera
    H, W = view.shape[:2]
    if overlay:
        dbg = snap["debug"]
        if dbg["zone"]:
            zx0, zy0, zx1, zy1 = dbg["zone"]
            cv2.rectangle(view, (int((1 - zx1) * W), int(zy0 * H)),
                          (int((1 - zx0) * W), int(zy1 * H)), (255, 200, 0), 2)
        for pts in dbg["hands"]:
            for (x, y) in pts:
                cv2.circle(view, (int((1 - x) * W), int(y * H)), 5, (0, 255, 255), -1)
    view = cv2.resize(view, (320, int(H * 320 / W)))
    ok, buf = cv2.imencode(".jpg", view, [cv2.IMWRITE_JPEG_QUALITY, 70])
    return buf.tobytes() if ok else None


def _mjpeg(overlay):
    t = tracker
    while t is not None and tracker is t:        # stops when tracking is turned off
        frame = t.latest_frame()
        if frame is not None:
            jpg = _render(frame, t.snapshot(), overlay)
            if jpg:
                yield b"--frame\r\nContent-Type: image/jpeg\r\n\r\n" + jpg + b"\r\n"
        time.sleep(0.07)


@app.get("/face/video")
def face_video(debug: int = 0):
    """Live preview of the tracker's own frames, for the page. Stays on this computer."""
    if tracker is None:
        return Response(status_code=204)
    return StreamingResponse(_mjpeg(bool(debug)),
                             media_type="multipart/x-mixed-replace; boundary=frame",
                             headers={"Cache-Control": "no-store"})


# ---------------- game <-> Panda (Unity will call these too) ----------------
class StatusRequest(BaseModel):
    idle_seconds: float = 0.0


@app.post("/status")
def status(req: StatusRequest):
    if tracker is None:
        return {"face_on": False, "state": "off", "player_state": "calm",
                "calibrating": False, "present": False, "offer": None,
                "failed_runs": coach.failed_runs, "why": "face tracking is off"}
    face = tracker.snapshot()
    offer = coach.decide(face, req.idle_seconds)
    return {"face_on": True, "state": face["state"], "player_state": current_player_state(),
            "calibrating": face["calibrating"], "present": face["present"],
            "offer": offer, "failed_runs": coach.failed_runs, "why": coach.why}


class EventRequest(BaseModel):
    type: str      # failed | solved | accepted | dismissed | reset


@app.post("/event")
def event(req: EventRequest):
    face = tracker.snapshot() if tracker is not None else None
    return coach.event(req.type, face)


# ---------------- page ----------------
@app.get("/")
def index():
    return FileResponse("static/index.html")


@app.get("/favicon.ico")
def favicon():
    return Response(status_code=204)