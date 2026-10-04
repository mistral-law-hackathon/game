"""Serves the WebGL build and proxies game dialogue to Mistral (the API key stays server-side)."""
import json
import os
import re
import urllib.parse
import urllib.request
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from rooms import BY_ID, public_rooms

ROOT = Path(__file__).resolve().parent.parent
WEB_DIR = ROOT / "webgl"


def load_env():
    env_file = ROOT / ".env"
    if env_file.exists():
        for line in env_file.read_text().splitlines():
            if "=" in line and not line.lstrip().startswith("#"):
                k, v = line.split("=", 1)
                os.environ.setdefault(k.strip(), v.strip())


load_env()
API_KEY = os.environ.get("MISTRAL_API_KEY", "")
MODEL = os.environ.get("MISTRAL_MODEL", "mistral-medium-latest")
GRADIUM_KEY = os.environ.get("GRADIUM_API_KEY", "")
# Gradium flagship voices: Marcus (boss), Marlowe (HR), Garrett (landlord), Declan (mentor)
VOICES = {"boss": "r2sIQdqqoqgRJuXw", "hr": "Bla6SbVMczYnOhfK", "landlord": "POBHtemksfWQbng0", "mentor": "I7GYfpcKbafFrYUv"}
TTS_CACHE = {}


def speech_text(text):
    t = text.replace("[...]", " ").replace("THE LAW SAYS", "The law says").replace("IN SIMPLE WORDS:", "In simple words:")
    t = t.replace("FOR YOU:", "For you:").replace("Art.", "Article").replace("EUR", "euros")
    t = re.sub(r"\s+", " ", t).strip()
    return t[:700]


def tts(who, text):
    voice = VOICES.get(who)
    if not voice or not GRADIUM_KEY or not text.strip():
        return None
    text = speech_text(text)
    key = (voice, text)
    if key not in TTS_CACHE:
        body = json.dumps({"text": text, "voice_id": voice, "output_format": "wav", "only_audio": True}).encode()
        req = urllib.request.Request("https://api.gradium.ai/api/post/speech/tts", data=body,
                                     headers={"x-api-key": GRADIUM_KEY, "Content-Type": "application/json"})
        with urllib.request.urlopen(req, timeout=45) as resp:
            if len(TTS_CACHE) > 200:
                TTS_CACHE.clear()
            TTS_CACHE[key] = resp.read()
    return TTS_CACHE[key]


def mistral_json(system, messages, temperature=0.7):
    body = json.dumps({
        "model": MODEL,
        "temperature": temperature,
        "response_format": {"type": "json_object"},
        "messages": [{"role": "system", "content": system}] + messages,
    }).encode()
    req = urllib.request.Request(
        "https://api.mistral.ai/v1/chat/completions", data=body,
        headers={"Authorization": f"Bearer {API_KEY}", "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=45) as resp:
        content = json.loads(resp.read())["choices"][0]["message"]["content"]
    content = content.replace("\u20ac", " EUR").replace("\u2014", " - ").replace("\u2019", "'")
    return json.loads(content)


def cards_text(room):
    return "\n".join(
        f"- {c['id']} ({'APPLIES to this situation' if c['relevant'] else 'DECOY: does NOT apply here'}): "
        f"{c['title']} - {c['plain']} [{c['law']}] OFFICIAL TEXT: \"{c['text']}\"" for c in room["cards"])


def clean_history(history, limit=12):
    out = []
    for m in (history or [])[-limit:]:
        role = "assistant" if m.get("role") == "assistant" else "user"
        out.append({"role": role, "content": str(m.get("content", ""))[:800]})
    return out


def mentor(data):
    room = BY_ID[data["room"]]
    found = [c["text"] for c in room["clues"] if c["id"] in data.get("clues", [])]
    system = f"""You are Maitre Pocket, a seasoned, witty but kind elderly French lawyer who accompanies the player, in a comedic legal-education game.
The player is a layperson with NO legal knowledge. Teach with the OFFICIAL wording of the law, then translate it into very simple words.

ALWAYS answer in exactly this format (plain text, line breaks between parts, max 90 words total):
THE LAW SAYS (<article reference>): "<a short exact quote, copied word for word from the OFFICIAL TEXT of the most relevant card>"
IN SIMPLE WORDS: <one short sentence a 12-year-old understands>
FOR YOU: <one short sentence applying it to the player's facts, with numbers if any>

Situation: {room['intro']}
Villain: {room['villain']['persona']}
Facts the player discovered: {found or 'none yet'}

Law cards in this room:
{cards_text(room)}

Rules:
- Never invent articles or quotes: only quote the OFFICIAL TEXT given above.
- If the question is vague or off-topic, still use the format with the closest useful card, and end FOR YOU with a hint about what to ask next.
- "unlock": ids of the cards whose concept your answer actually explains (0-2). If you explain a DECOY, also say clearly why it does not fit this situation.
- If the villain just used a fake law, you may point out that it does not exist.
- This is simplified educational information, not legal advice; never mention that disclaimer unless asked.
Return ONLY JSON: {{"answer": string, "unlock": [card ids]}}"""
    msgs = clean_history(data.get("history")) + [{"role": "user", "content": str(data.get("question", ""))[:600]}]
    out = mistral_json(system, msgs, 0.5)
    valid = {c["id"] for c in room["cards"]}
    unlock = [i for i in out.get("unlock", []) if i in valid]
    for q in room["questions"]:
        if q["q"] == data.get("question") and q["unlock"] not in unlock:
            unlock.append(q["unlock"])
    return {"answer": str(out.get("answer", "")), "unlock": unlock}


def objection(data):
    room = BY_ID[data["room"]]
    claim = next(c for c in room["claims"] if c["id"] == data.get("claim"))
    card = next((c for c in room["cards"] if c["id"] == data.get("card")), None)
    clue = next((c for c in room["clues"] if c["id"] == data.get("evidence")), None)
    good_card = card is not None and card["id"] == claim["card"]
    good_ev = clue is not None and clue["id"] in claim["evidence"]
    correct = good_card and good_ev
    right_card = next(c for c in room["cards"] if c["id"] == claim["card"])
    broken = set(data.get("broken", [])) | ({claim["id"]} if correct else set())
    done = all(c["id"] in broken for c in room["claims"])
    verdict = ("CORRECT: law and evidence both fit." if correct else
               "WRONG LAW (evidence was fine)." if good_ev else
               "WRONG EVIDENCE (law was right)." if good_card else "WRONG LAW AND WRONG EVIDENCE.")
    system = f"""You voice two characters in a legal-education game for laypeople. Keep it short and clear.
VILLAIN: {room['villain']['persona']}
MENTOR: Maitre Pocket, a calm, kind lawyer who explains in plain language.

The villain claimed: "{claim['text']}"
The player objected with law card: {card['title'] + ' - ' + card['plain'] if card else 'none'}
and evidence: {clue['label'] + ' - ' + clue['text'] if clue else 'none'}
The correct answer was law "{right_card['title']}" ({right_card['law']}: "{right_card['text']}") with evidence "{', '.join(claim['evidence'])}".
Verdict (already decided, do not change it): {verdict}
{'This was the last claim: the villain gives up and opens the door.' if done else ''}

Write:
- "villain": the villain's in-character reaction, max 25 words. If correct: flustered, defeated on this point. If wrong: smug, mocking the bad objection.
- "mentor": max 50 words, very simple words. If correct: start with the article reference and a short exact quote from its official text in double quotes, then one sentence on how the evidence proves the claim is wrong. If wrong: say in one sentence why the chosen law or evidence does not fit, then one hint about what to look for, without giving the full answer.
Return ONLY JSON: {{"villain": string, "mentor": string}}"""
    try:
        out = mistral_json(system, [{"role": "user", "content": "Generate the lines."}], 0.7)
    except Exception:
        out = {}
    fallback_m = (f"Yes! {right_card['plain']}" if correct else
                  "Close, but that combination doesn't prove it. Check which law matches the claim and which clue shows the facts.")
    return {"correct": correct, "good_card": good_card, "good_evidence": good_ev, "done": done,
            "villain": str(out.get("villain") or ("Grr... fine, that point is yours." if correct else "Ha! Nice try.")),
            "mentor": str(out.get("mentor") or fallback_m),
            "card": right_card["id"] if correct else ""}


class Handler(SimpleHTTPRequestHandler):
    def __init__(self, *a, **kw):
        super().__init__(*a, directory=str(WEB_DIR), **kw)

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def send_json(self, obj, code=200):
        body = json.dumps(obj).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path.startswith("/api/rooms"):
            return self.send_json({"rooms": public_rooms()})
        if self.path.startswith("/api/tts"):
            q = urllib.parse.parse_qs(urllib.parse.urlparse(self.path).query)
            try:
                audio = tts(q.get("who", [""])[0], q.get("text", [""])[0])
            except Exception as e:
                self.log_error("tts error: %r", e)
                audio = None
            if not audio:
                return self.send_json({"error": "no audio"}, 503)
            self.send_response(200)
            self.send_header("Content-Type", "audio/wav")
            self.send_header("Content-Length", str(len(audio)))
            self.end_headers()
            self.wfile.write(audio)
            return
        return super().do_GET()

    def do_POST(self):
        routes = {"/api/mentor": mentor, "/api/object": objection}
        fn = routes.get(self.path)
        if not fn:
            return self.send_json({"error": "not found"}, 404)
        try:
            data = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}")
            self.send_json(fn(data))
        except Exception as e:  # keep the game alive on LLM/network errors
            self.log_error("api error: %r", e)
            self.send_json({"error": str(e)}, 500)


if __name__ == "__main__":
    port = int(os.environ.get("PORT", "8080"))
    print(f"Serving {WEB_DIR} on http://localhost:{port} (model {MODEL})")
    ThreadingHTTPServer(("0.0.0.0", port), Handler).serve_forever()
