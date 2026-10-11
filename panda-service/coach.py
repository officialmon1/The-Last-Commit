import random
import time

MIN_SWITCH_S = 2.5            # least time between Panda changing what she says (stops flicker)
CALM_CLEAR_S = 6              # an unanswered offer fades after this many calm seconds
QUIET_AFTER_ANSWER_S = 20     # quiet time after the player accepts help
QUIET_AFTER_DISMISS_S = 45    # quiet time after the player says "no thanks"
CONCERN_NEEDS_SUPPORT = False # True = a concerned face also needs a failed run or a typing pause
IDLE_SUPPORT_S = 20           # seconds without typing that count as a "pause" (only if the above is True)

HELP_STRESSED = [
    "Rough patch? Want a hint?",
    "Hey. Want a little help with this one?",
    "Hmm. Might be time to try a different angle. Want a nudge?",
    "This one's putting up a fight, huh? I can help.",
    "This looks like a tough spot. Say the word and I'll chime in.",
    "You've been at this a while. Want me to take a look?",
]
HELP_CONCERNED = [
    "Not quite clicking yet? I can give you a hint.",
    "Want to look at this from another angle?",
    "I've got an idea, if you want to hear it.",
    "Stuck on something? I might be able to help.",
    "This part's tricky. Want a push in the right direction?",
    "I'm not doing much over here. Want a hint?",
]
PROGRESS = [  # (question, reply if they say yes)
    ("Oh? I think we're getting somewhere.", "Nice. Keep going."),
    ("There we go, something's starting to click.", "Good. Don't stop now."),
    ("Is that a good sign I'm seeing?", "I'll take it. Keep at it."),
    ("Hey, that looks like progress to me.", "Told you we had this."),
    ("Something's working. Am I right?", "There it is. Onward."),
]
SOLVED_BIG = [
    "You actually did it! I knew we'd crack that one.",
    "Ha! All that trouble, and you got there in the end.",
    "A perfect solution. I may have to share my bamboo with you.",
    "That was not easy, and you still pulled it off. Respect.",
    "Look at that, solved and solved well. I'm almost impressed.",
    "After all that? You earned this one.",
]
SOLVED_NORMAL = [
    "Nice work. One more gate cleared.",
    "There we go. On to the next one.",
    "Clean solution. Not bad at all.",
    "Solved. That wasn't so bad, was it?",
    "Good. Chalk up another one.",
    "Done and done. Onward.",
]


class Coach:
    def __init__(self):
        self.reset()

    def reset(self):
        self.failed_runs = 0
        self.solved = False
        self.quiet_until = 0.0
        self.current = None        # the offer Panda is currently showing
        self.switched_t = 0.0
        self.calm_since = None
        self.why = ""              # plain-English reason for what Panda is doing (shown on the page)

    # ---------- game events ----------
    def event(self, kind, face=None):
        """kind: failed | solved | accepted | dismissed | reset"""
        out = {"failed_runs": self.failed_runs, "celebrate": None, "text": None}
        now = time.monotonic()
        if kind == "reset":
            self.reset()
            out["failed_runs"] = 0
        elif kind == "failed":
            self.failed_runs += 1
            out["failed_runs"] = self.failed_runs
        elif kind == "accepted":
            self.quiet_until = now + QUIET_AFTER_ANSWER_S
            self.current = None
        elif kind == "dismissed":
            self.quiet_until = now + QUIET_AFTER_DISMISS_S
            self.current = None
        elif kind == "solved":
            self.solved = True
            self.current = None
            happy = bool(face and face.get("state") == "happy")
            out["celebrate"] = "big" if happy else "normal"
            out["text"] = random.choice(SOLVED_BIG if happy else SOLVED_NORMAL)
        return out

    # ---------- what should Panda say right now? ----------
    def _make_offer(self, key):
        if key == "stressed":
            return {"state": key, "kind": "help", "text": random.choice(HELP_STRESSED)}
        if key == "concerned":
            return {"state": key, "kind": "help", "text": random.choice(HELP_CONCERNED)}
        q, yes = random.choice(PROGRESS)
        return {"state": key, "kind": "progress", "text": q, "yes_text": yes}

    def _desired(self, face, idle_seconds):
        """Returns (offer key or None, reason)."""
        if face["calibrating"]:
            return None, "calibrating, hold a relaxed face"
        if not face["present"]:
            return None, "no face seen"
        s = face["state"]
        if s == "stressed":
            return "stressed", "you look stressed"
        if s == "concerned":
            if CONCERN_NEEDS_SUPPORT and self.failed_runs < 1 and idle_seconds < IDLE_SUPPORT_S:
                return None, "concerned, waiting for a failed run or a pause in typing"
            return "concerned", "you look concerned"
        if s == "happy":
            return "happy", "you look happy"
        return None, "you look calm, nothing to say"

    def decide(self, face, idle_seconds):
        """Return the offer Panda should be showing right now, or None.
        The same offer (same id) is returned until the state changes."""
        now = time.monotonic()
        if self.solved:
            self.current = None
            self.why = "solved, celebrating"
            return None
        if now < self.quiet_until:
            self.current = None
            self.why = f"quiet for {int(self.quiet_until - now) + 1}s (you just answered)"
            return None

        want, why = self._desired(face, idle_seconds)
        self.why = why
        cur_key = self.current["state"] if self.current else None

        if want == cur_key:                    # nothing changed
            self.calm_since = None
            return self.current

        if want is None:                       # calm again: keep the offer a few seconds, then fade it
            if self.current is None:
                return None
            if self.calm_since is None:
                self.calm_since = now
            if now - self.calm_since >= CALM_CLEAR_S:
                self.current = None
                self.calm_since = None
            else:
                self.why = "calm again, fading the offer"
            return self.current

        self.calm_since = None                 # the state changed to something new: update the bubble
        if self.current is not None and now - self.switched_t < MIN_SWITCH_S:
            return self.current                # too soon to switch again
        self.current = self._make_offer(want)
        self.current["id"] = int(time.time() * 1000)
        self.switched_t = now
        return self.current