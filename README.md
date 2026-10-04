# Escape the Crazy Boss

An AI-native legal escape game (Unity 6, WebGL). You are a layperson trapped on your last day at work:
outsmart an absurd boss, a creepy HR manager and a greedy landlord by **learning** your rights.

3D top-down world (WASD to walk, E to interact). Loop per room: **inspect evidence** → **ask Maitre Pocket** (LLM mentor, plain-language explanations) →
collect **law cards** (some are decoys) → **play a card and argue** against the LLM villain → coach feedback → escape.
The final screen recaps what you learned.

## Layout
- `Assets/Scripts/LawGame.cs` - 3D world, player movement, camera, IMGUI overlays and API calls.
- `Assets/Scripts/World.cs` - cute chibi characters, props, doors (all built from primitives in code).
- `Assets/Editor/BuildScript.cs` - generates the scene and builds WebGL.
- `server/rooms.py` - fixed scenarios, clues and law cards (source of truth for scoring).
- `server/server.py` - serves `webgl/` and proxies `/api/mentor` and `/api/argue` to Mistral (key stays server-side).

## Run
```bash
cp .env.example .env   # put your MISTRAL_API_KEY in it
./build.sh             # headless WebGL build into ./webgl (needs an activated Unity license)
python3 server/server.py   # http://localhost:8080
```

Legal references are simplified (French/EU law) for education only; not legal advice.
