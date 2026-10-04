"""Serves the WebGL build and proxies game dialogue to Mistral (the API key stays server-side)."""
import json
import os
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
    return json.loads(content)


def cards_text(room):
    return "\n".join(
        f"- {c['id']} ({'APPLIES to this situation' if c['relevant'] else 'DECOY: does NOT apply here'}): "
        f"{c['title']} - {c['plain']} [{c['law']}]" for c in room["cards"])


def clean_history(history, limit=12):
    out = []
    for m in (history or [])[-limit:]:
        role = "assistant" if m.get("role") == "assistant" else "user"
        out.append({"role": role, "content": str(m.get("content", ""))[:800]})
    return out


def mentor(data):
    room = BY_ID[data["room"]]
    found = [c["text"] for c in room["clues"] if c["id"] in data.get("clues", [])]
    system = f"""You are Maitre Pocket, a tiny, witty but kind French lawyer living in the player's phone, in a comedic legal-education game.
The player is a layperson with NO legal knowledge. Your job is to TEACH: explain the law in plain, friendly language, relate it to the player's situation, and give a concrete everyday example. Max 90 words. No jargon unless you explain it.

Situation: {room['intro']}
Villain: {room['villain']['persona']}
Facts the player discovered: {found or 'none yet'}

Law cards in this room:
{cards_text(room)}

Rules:
- Answer the player's question. If they ask something vague, guide them toward one useful concept with a question back.
- "unlock": ids of the cards whose concept your answer actually explains (0-2). If you explain a DECOY, also say clearly why it does not fit this situation.
- If the villain just used a fake law, you may point out that it does not exist.
- This is simplified educational information, not legal advice; never mention that disclaimer unless asked.
Return ONLY JSON: {{"answer": string, "unlock": [card ids]}}"""
    msgs = clean_history(data.get("history")) + [{"role": "user", "content": str(data.get("question", ""))[:600]}]
    out = mistral_json(system, msgs, 0.5)
    valid = {c["id"] for c in room["cards"]}
    return {"answer": str(out.get("answer", "")), "unlock": [i for i in out.get("unlock", []) if i in valid]}


def argue(data):
    room = BY_ID[data["room"]]
    won = [i for i in data.get("won", []) if i in {c["id"] for c in room["cards"] if c["relevant"]}]
    card = next((c for c in room["cards"] if c["id"] == data.get("card")), None)
    card_txt = (f"The player PLAYED the law card '{card['id']}' ({'applies' if card['relevant'] else 'DECOY, does not apply'}): "
                f"{card['title']} - {card['plain']}") if card else "The player played no card this turn (free talk)."
    need = room["need"]
    system = f"""You run one confrontation in a comedic legal-education game. Two voices:
1) VILLAIN: {room['villain']['persona']} Stay in character, funny and exaggerated, max 55 words. Sometimes (about 1 turn in 3) bluff with an invented, absurd-but-official-sounding fake law to intimidate.
2) COACH (Maitre Pocket, kind mentor): judges the player's latest move and TEACHES. Max 45 words, plain language.

Situation: {room['intro']}
Law cards in this room:
{cards_text(room)}
Points already won by the player: {won}. Points needed to escape: {need}.
{card_txt}

Judging the player's latest message (be lenient on wording - they are beginners - but strict on whether the law fits):
- success = true only if the argument uses a card that APPLIES (played or clearly described) AND connects it to the facts of the situation, and that card is not already won.
- point = id of that card if success, else "".
- If they used a DECOY or a vague claim ("this is illegal!"), success=false and the coach explains why it does not work and which idea to explore instead (without giving the full answer).
- called_out_fake = true if the player correctly says a law the villain invented does not exist.
- If success makes won points reach {need}, the villain's reply is a dramatic, funny SURRENDER and he/she opens the door.
- mood: 0-100, how cornered/desperate the villain is now.
Return ONLY JSON: {{"reply": string, "mood": int, "success": bool, "point": string, "coach": string, "fake_law": string (the fake law the villain invented in this reply, or ""), "called_out_fake": bool}}"""
    msgs = clean_history(data.get("history")) + [{"role": "user", "content": str(data.get("message", ""))[:600]}]
    out = mistral_json(system, msgs, 0.8)
    point = out.get("point", "") if out.get("success") else ""
    if point not in {c["id"] for c in room["cards"] if c["relevant"]} or point in won:
        point = ""
    if point:
        won.append(point)
    return {
        "reply": str(out.get("reply", "")), "mood": max(0, min(100, int(out.get("mood", 50) or 0))),
        "success": bool(point), "point": point, "coach": str(out.get("coach", "")),
        "fake_law": str(out.get("fake_law", "") or ""), "called_out_fake": bool(out.get("called_out_fake")),
        "won": won, "door_open": len(won) >= need,
    }


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
        return super().do_GET()

    def do_POST(self):
        routes = {"/api/mentor": mentor, "/api/argue": argue}
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
