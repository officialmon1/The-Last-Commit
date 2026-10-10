import os
import sys
import threading
import time
from statistics import median

import cv2
import mediapipe as mp
from dotenv import load_dotenv
from mediapipe.tasks import python as mp_python
from mediapipe.tasks.python import vision

load_dotenv()
FACE_MODEL = "models/face_landmarker.task"
HAND_MODEL = "models/hand_landmarker.task"

# ---- Tuning knobs (use the live preview to adjust) ----
GAIN = 2.0                 # raise if "concerned" is hard to trigger
CONCERN_T = 0.12           # smoothed concern needed
CONCERN_HOLD_S = 1.0       # seconds it must stay above the threshold
SMILE_T = 0.30             # smoothed smile above baseline
HAND_POINTS = 6            # of 21 points a hand needs inside the head zone to count
HANDS_HOLD_TWO_S = 1.0     # both hands on head
HANDS_HOLD_ONE_S = 2.5     # one hand on head (could just be scratching)
STRESS_RELEASE_S = 1.0     # stay "stressed" this long after hands leave (stops flicker)
FACE_LOST_GRACE_S = 1.5    # keep the last face box this long if hands cover the face

# How much each signal counts toward "concerned" (after subtracting your baseline)
WEIGHTS = {"furrow": 1.0, "worry": 0.8, "tense": 0.9, "squint": 0.7}

# What the AI/prompt layer should see
PLAYER_STATE = {"calm": "calm", "happy": "calm", "concerned": "stuck",
                "stressed": "stressed", "away": "calm"}


def _avg(s, *names):
    return sum(s.get(n, 0.0) for n in names) / len(names)


def extract(scores):
    return {
        "smile": _avg(scores, "mouthSmileLeft", "mouthSmileRight"),
        "furrow": _avg(scores, "browDownLeft", "browDownRight"),
        "worry": scores.get("browInnerUp", 0.0),
        "tense": _avg(scores, "mouthPressLeft", "mouthPressRight",
                      "mouthFrownLeft", "mouthFrownRight"),
        "squint": _avg(scores, "eyeSquintLeft", "eyeSquintRight"),
    }


# Head zone, as fractions of the face box (forehead to chin).
ZONE_TOP = 0.50      # how far ABOVE the face box it reaches (was 0.9, too high)
ZONE_BOTTOM = 0.30   # how far down from the top of the face box (forehead only)
ZONE_SIDE = 0.40     # extra width on each side (temples and ears)


def head_zone(box):
    x0, y0, x1, y1 = box
    w, h = x1 - x0, y1 - y0
    return (x0 - ZONE_SIDE * w, y0 - ZONE_TOP * h, x1 + ZONE_SIDE * w, y0 + ZONE_BOTTOM * h)


def hands_on_head(hands, box):
    """How many hands are on the head (0, 1 or 2)."""
    if box is None or not hands:
        return 0
    zx0, zy0, zx1, zy1 = head_zone(box)
    n = 0
    for pts in hands:
        inside = sum(1 for (x, y) in pts if zx0 <= x <= zx1 and zy0 <= y <= zy1)
        if inside >= HAND_POINTS:
            n += 1
    return n


class FaceTracker:
    """Reads the webcam in a background thread. Frames stay in memory only:
    nothing is saved or sent anywhere."""

    def __init__(self, camera_index=None):
        self.camera_index = camera_index if camera_index is not None else int(os.getenv("CAMERA_INDEX", "0"))
        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._thread = None
        self._frame = None
        self.camera_ok = None
        self._last_seen = 0.0
        self._concern = 0.0
        self._smile = 0.0
        self._raw = {k: 0.0 for k in ("smile", "furrow", "worry", "tense", "squint")}
        self._base = {k: 0.0 for k in self._raw}
        self._concern_since = None
        self._hands_since = None
        self._hands_n = 0
        self._stress_hold_until = 0.0
        self._face_box = None
        self._face_box_t = 0.0
        self._debug = {"zone": None, "hands": []}
        self._state = "calm"
        self._cal_until = 0.0
        self._cal_samples = []

    # ---------- public API ----------
    def start(self):
        self._thread = threading.Thread(target=self._run, daemon=True)
        self._thread.start()

    def stop(self):
        self._stop.set()

    def calibrate(self, seconds=5):
        """Call while the player holds a relaxed, neutral face."""
        with self._lock:
            self._cal_samples = []
            self._cal_until = time.monotonic() + seconds

    def snapshot(self):
        with self._lock:
            now = time.monotonic()
            present = now - self._last_seen < 1.5
            state = self._state if present else "away"
            return {
                "present": present,
                "state": state,                          # calm | concerned | stressed | happy | away
                "player_state": PLAYER_STATE[state],     # what agent.py understands
                "concern": round(self._concern, 2),
                "smile": round(self._smile, 2),
                "hands_on_head": self._hands_n,
                "calibrating": now < self._cal_until,
                "raw": {k: round(v, 2) for k, v in self._raw.items()},
                "debug": {"zone": self._debug["zone"], "hands": self._debug["hands"]},
            }

    def latest_frame(self):
        with self._lock:
            return self._frame

    # ---------- internals ----------
    def _run(self):
        face_lm = vision.FaceLandmarker.create_from_options(vision.FaceLandmarkerOptions(
            base_options=mp_python.BaseOptions(model_asset_path=FACE_MODEL),
            running_mode=vision.RunningMode.VIDEO,
            output_face_blendshapes=True,
            num_faces=1,
        ))
        hand_lm = vision.HandLandmarker.create_from_options(vision.HandLandmarkerOptions(
            base_options=mp_python.BaseOptions(model_asset_path=HAND_MODEL),
            running_mode=vision.RunningMode.VIDEO,
            num_hands=2,
        ))
        cap = cv2.VideoCapture(self.camera_index)
        if not cap.isOpened():
            self.camera_ok = False
            print("ERROR: could not open the camera. Check camera permission "
                  "or try a different camera index.")
            return
        self.camera_ok = True
        last_ts = 0
        while not self._stop.is_set():
            ok, frame = cap.read()
            if not ok:
                time.sleep(0.05)
                continue
            with self._lock:
                self._frame = frame
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            img = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)
            ts = max(int(time.monotonic() * 1000), last_ts + 1)   # must strictly increase
            last_ts = ts

            fres = face_lm.detect_for_video(img, ts)
            f, box = None, None
            if fres.face_blendshapes and fres.face_landmarks:
                scores = {b.category_name: b.score for b in fres.face_blendshapes[0]}
                f = extract(scores)
                xs = [p.x for p in fres.face_landmarks[0]]
                ys = [p.y for p in fres.face_landmarks[0]]
                box = (min(xs), min(ys), max(xs), max(ys))

            hres = hand_lm.detect_for_video(img, ts)
            hands = [[(p.x, p.y) for p in hand] for hand in hres.hand_landmarks]

            self._update(f, box, hands)
            time.sleep(0.02)
        cap.release()
        face_lm.close()
        hand_lm.close()

    def _update(self, f, box, hands):
        now = time.monotonic()
        with self._lock:
            if box is not None:
                self._face_box, self._face_box_t = box, now
            use_box = box if box is not None else (
                self._face_box if now - self._face_box_t < FACE_LOST_GRACE_S else None)
            n_hands = hands_on_head(hands, use_box)
            self._hands_n = n_hands
            self._debug = {"zone": head_zone(use_box) if use_box else None, "hands": hands}

            if f is not None or n_hands:
                self._last_seen = now

            if n_hands:
                if self._hands_since is None:
                    self._hands_since = now
            else:
                self._hands_since = None

            if f is not None:
                self._raw = f
                if now < self._cal_until:              # calibrating: collect samples only
                    self._cal_samples.append(f)
                    return
                if self._cal_samples:                  # calibration done: per-signal baseline
                    self._base = {k: median(s[k] for s in self._cal_samples) for k in f}
                    self._cal_samples = []
                deltas = {k: max(0.0, f[k] - self._base[k]) for k in f}
                c = min(1.0, GAIN * max(WEIGHTS[k] * deltas[k] for k in WEIGHTS))
                a = 0.3   # smoothing: higher = faster but twitchier
                self._concern = (1 - a) * self._concern + a * c
                self._smile = (1 - a) * self._smile + a * deltas["smile"]
            else:                                      # face not visible: let values fade
                self._concern *= 0.9
                self._smile *= 0.9

            if self._concern > CONCERN_T:
                if self._concern_since is None:
                    self._concern_since = now
            else:
                self._concern_since = None

            need = HANDS_HOLD_TWO_S if n_hands >= 2 else HANDS_HOLD_ONE_S
            stress_now = bool(self._hands_since and now - self._hands_since >= need)
            if stress_now:
                self._stress_hold_until = now + STRESS_RELEASE_S

            if stress_now or now < self._stress_hold_until:
                self._state = "stressed"
            elif self._smile > SMILE_T:
                self._state = "happy"
            elif self._concern_since and now - self._concern_since >= CONCERN_HOLD_S:
                self._state = "concerned"
            else:
                self._state = "calm"


COLORS = {"happy": (0, 200, 0), "concerned": (0, 165, 255), "stressed": (0, 0, 255),
          "calm": (255, 255, 255), "away": (128, 128, 128)}

if __name__ == "__main__":
    # Dev preview. The window runs in the main thread (required on macOS).
    cam = int(sys.argv[1]) if len(sys.argv) > 1 else None
    tracker = FaceTracker(camera_index=cam)
    tracker.start()
    started = time.monotonic()
    calibrated = False
    print("Press q to quit, c to recalibrate (relax your face first).")
    while True:
        frame = tracker.latest_frame()
        if tracker.camera_ok is False:
            break
        if frame is None:
            time.sleep(0.05)
            continue
        if not calibrated and time.monotonic() - started > 1.5:
            tracker.calibrate(5)
            calibrated = True
        snap = tracker.snapshot()
        view = cv2.flip(frame, 1)
        H, W = view.shape[:2]
        color = COLORS[snap["state"]]

        dbg = snap["debug"]                      # head zone + hand points (mirrored to match the view)
        if dbg["zone"]:
            zx0, zy0, zx1, zy1 = dbg["zone"]
            cv2.rectangle(view, (int((1 - zx1) * W), int(zy0 * H)),
                          (int((1 - zx0) * W), int(zy1 * H)), (255, 200, 0), 1)
        for pts in dbg["hands"]:
            for (x, y) in pts:
                cv2.circle(view, (int((1 - x) * W), int(y * H)), 3, (0, 255, 255), -1)

        cv2.rectangle(view, (0, 0), (W - 1, H - 1), color, 8)
        label = "CALIBRATING: relax your face..." if snap["calibrating"] else snap["state"].upper()
        cv2.putText(view, label, (20, 45), cv2.FONT_HERSHEY_SIMPLEX, 1.1, color, 3)
        cv2.putText(view, f"concern {snap['concern']} (needs > {CONCERN_T})   smile {snap['smile']} (needs > {SMILE_T})   hands on head: {snap['hands_on_head']}",
                    (20, 78), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
        y = 108
        for name, val in snap["raw"].items():
            cv2.putText(view, f"{name:7s} {val:.2f}", (20, y), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (200, 255, 200), 1)
            cv2.rectangle(view, (120, y - 9), (120 + int(val * 200), y), (200, 255, 200), -1)
            y += 22
        cv2.imshow("Panda face tracker (q quit, c recalibrate)", view)
        k = cv2.waitKey(1) & 0xFF
        if k == ord("q"):
            break
        if k == ord("c"):
            tracker.calibrate(5)
    tracker.stop()
    cv2.destroyAllWindows()