# Escape the Crazy Boss

An AI-native legal escape game (Unity 6, WebGL). You are a layperson trapped on your last day at work:
outsmart an absurd boss, a creepy HR manager and a greedy landlord by **learning** your rights.

Top-down 2D maps (office, HR, apartment) in a cartoon RPG style. Click the floor or use WASD to walk; click objects and characters to interact. Loop per room: inspect glowing objects for **evidence**, click Maitre Pocket to learn **law cards**, then talk to the villain and build an **objection** = claim + law + evidence. The server checks it against fixed rubrics in `server/rooms.py`; Mistral voices the villain reaction and the mentor explanation. Art lives in `Assets/Resources/Art/`.
collect **law cards** (some are decoys) → **play a card and argue** against the LLM villain → coach feedback → escape.
The final screen recaps what you learned.

## Layout
- `Assets/Scripts/LawGame.cs` - 2D scene, objection builder, dialogue and mentor UI (IMGUI), API calls.
- `Assets/Editor/BuildScript.cs` - generates the scene and builds WebGL.
- `server/rooms.py` - fixed scenarios, clues and law cards (source of truth for scoring).
- `server/server.py` - serves `webgl/` and proxies `/api/mentor` and `/api/object` to Mistral (key stays server-side).

## Run
```bash
cp .env.example .env   # put your MISTRAL_API_KEY in it
./build.sh             # headless WebGL build into ./webgl (needs an activated Unity license)
python3 server/server.py   # http://localhost:8080
```

Legal references are simplified (French/EU law) for education only; not legal advice.
