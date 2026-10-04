# Game

Unity 6 (6000.0.84f1) project targeting WebGL. Right now it is a single scene that shows "Hello, World!".

## Layout
- `Assets/Scenes/Main.unity`: main scene (camera + `HelloWorld`)
- `Assets/Scripts/`: runtime C# scripts
- `Assets/Editor/BuildScript.cs`: headless build entry point (`BuildScript.BuildWebGL`) and the menu items **Game → Build WebGL** and **Game → Regenerate Main Scene**

## Open in the editor
Open this folder in Unity Hub with editor **6000.0.84f1** and the **WebGL Build Support** module installed.

## Build (headless)
```bash
./build.sh          # outputs ./webgl, log in build.log
```

## Run locally
```bash
cd webgl && python3 -m http.server 8080   # open http://localhost:8080
```
