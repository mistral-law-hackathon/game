using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

[Serializable] public class Villain { public string name, portrait, opening; }
[Serializable] public class Clue { public string id, label, card, text; }
[Serializable] public class LawCard { public string id, title, plain, example, law; public bool relevant; }
[Serializable] public class Room { public string id, title, background, intro; public int need; public Villain villain; public Clue[] clues; public LawCard[] cards; }
[Serializable] public class RoomsResp { public Room[] rooms; }
[Serializable] public class Msg { public string role, content; }
[Serializable] public class MentorReq { public string room, question; public string[] clues; public Msg[] history; }
[Serializable] public class MentorResp { public string answer, error; public string[] unlock; }
[Serializable] public class ArgueReq { public string room, card, message; public string[] won; public Msg[] history; }
[Serializable] public class ArgueResp { public string reply, point, coach, fake_law, error; public int mood; public bool success, called_out_fake, door_open; public string[] won; }

public class LawGame : MonoBehaviour
{
    const float W = 1280, H = 720, RoomLen = 14f;
    enum Phase { Loading, Title, Play, Recap }
    enum Modal { None, Clue, Mentor, Cards }

    class Line { public string who, text; public Color color; }
    class ClueObj { public Clue clue; public int room; public Transform t; public Floaty f; }

    Phase phase = Phase.Loading;
    Modal modal = Modal.None;
    bool talking, showIntro;
    Room[] rooms;
    int roomIndex;
    Room R => rooms[roomIndex];
    string loadError;

    // world
    Camera cam;
    Transform player;
    CharacterController cc;
    Chibi playerChibi, mentorChibi;
    readonly List<Chibi> villains = new();
    readonly List<Door> doors = new();
    readonly List<ClueObj> clueObjs = new();
    ClueObj nearClue;
    bool nearVillain;

    // game state
    readonly List<Line> log = new();
    readonly List<Msg> villainHistory = new();
    readonly List<Msg> mentorHistory = new();
    readonly List<Line> mentorLog = new();
    readonly HashSet<string> cluesSeen = new();
    readonly HashSet<string> cardsUnlocked = new();
    readonly List<string> won = new();
    readonly List<LawCard> learned = new();
    int newCards;
    Clue openClue;
    string openCard, selectedCard, toast;
    float toastTime;
    string input = "", mentorInput = "";
    bool busy, doorOpen;
    int mood = 20, credibility = 100, bluffsCaught;
    Vector2 logScroll, mentorScroll, cardScroll, chipScroll;

    Texture2D white;
    GUIStyle sTitle, sBody, sSmall, sBtn, sBox, sField, sBubble, sName, sBig, sTag;
    float guiScale, guiOx, guiOy;

    static readonly Color cVillain = new(1f, 0.55f, 0.45f), cYou = new(0.55f, 0.8f, 1f), cMentor = new(0.5f, 0.9f, 0.65f), cGold = new(1f, 0.85f, 0.3f);

    string Base
    {
        get
        {
            if (Application.platform != RuntimePlatform.WebGLPlayer || string.IsNullOrEmpty(Application.absoluteURL))
                return "http://localhost:8080";
            return new Uri(Application.absoluteURL).GetLeftPart(UriPartial.Authority);
        }
    }

    void Start()
    {
        BuildWorld();
        StartCoroutine(LoadRooms());
    }

    IEnumerator LoadRooms()
    {
        using var req = UnityWebRequest.Get(Base + "/api/rooms");
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) { loadError = "Could not reach the game server: " + req.error; yield break; }
        rooms = JsonUtility.FromJson<RoomsResp>(req.downloadHandler.text).rooms;
        BuildRooms();
        Physics.SyncTransforms();
        phase = Phase.Title;
    }

    IEnumerator Post<T>(string path, object body, Action<T> done) where T : class
    {
        using var req = new UnityWebRequest(Base + path, "POST");
        req.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(body)));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();
        T res = null;
        try { if (!string.IsNullOrEmpty(req.downloadHandler.text)) res = JsonUtility.FromJson<T>(req.downloadHandler.text); } catch { }
        done(res);
    }

    // ---------------- world building ----------------

    void BuildWorld()
    {
        cam = Camera.main;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Art.Hex("#BFE6FF");
        cam.fieldOfView = 45;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Art.Hex("#E8EEFF");
        RenderSettings.ambientEquatorColor = Art.Hex("#C9C2D9");
        RenderSettings.ambientGroundColor = Art.Hex("#8C7F99");
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 0.9f;
        sun.color = Art.Hex("#FFF4E0");
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.55f;
        sun.transform.rotation = Quaternion.Euler(55, -30, 0);

        playerChibi = Chibi.Make("player", new Vector3(0, 0.3f, -4.5f), 0);
        player = playerChibi.transform;
        cc = player.gameObject.AddComponent<CharacterController>();
        cc.center = new Vector3(0, 1f, 0); cc.height = 2f; cc.radius = 0.45f;
        mentorChibi = Chibi.Make("mentor", player.position + new Vector3(-1, 1, 0), 0);
        mentorChibi.transform.localScale = Vector3.one * 0.45f;
        mentorChibi.gameObject.AddComponent<Follower>().target = player;
        cam.transform.position = player.position + new Vector3(0, 9, -8);
    }

    static readonly string[] Floors = { "#CFE3F2", "#E6D9F5", "#FBE3CF" };
    static readonly string[] WallsC = { "#8FB8DE", "#B39DDB", "#F4A988" };
    static readonly string[] Kinds = { "boss", "hr", "landlord" };

    void BuildRooms()
    {
        var root = new GameObject("World").transform;
        int n = rooms.Length;
        for (int i = 0; i < n; i++)
        {
            float z0 = i * RoomLen;
            Color floor = Art.Hex(Floors[i % 3]), wall = Art.Hex(WallsC[i % 3]);
            Art.Prim(PrimitiveType.Cube, root, new Vector3(0, -0.1f, z0), new Vector3(14, 0.2f, RoomLen), floor, true);
            // checker rug
            Art.Prim(PrimitiveType.Cylinder, root, new Vector3(0, 0.01f, z0 + 1), new Vector3(6, 0.01f, 6), Color.Lerp(floor, Color.white, 0.5f));
            Art.Prim(PrimitiveType.Cube, root, new Vector3(-7, 1.25f, z0), new Vector3(0.3f, 2.5f, RoomLen), wall, true);
            Art.Prim(PrimitiveType.Cube, root, new Vector3(7, 1.25f, z0), new Vector3(0.3f, 2.5f, RoomLen), wall, true);
            if (i == 0) Art.Prim(PrimitiveType.Cube, root, new Vector3(0, 1.25f, z0 - 7), new Vector3(14, 2.5f, 0.3f), wall, true);
            float zb = z0 + 7;
            Art.Prim(PrimitiveType.Cube, root, new Vector3(-4.1f, 1.25f, zb), new Vector3(5.8f, 2.5f, 0.3f), wall, true);
            Art.Prim(PrimitiveType.Cube, root, new Vector3(4.1f, 1.25f, zb), new Vector3(5.8f, 2.5f, 0.3f), wall, true);
            var door = Art.Prim(PrimitiveType.Cube, root, new Vector3(0, 1.25f, zb), new Vector3(2.4f, 2.5f, 0.25f), Art.Hex("#8D5A3B"), true);
            Art.Prim(PrimitiveType.Sphere, door.transform, new Vector3(0.35f, 0, -0.6f), new Vector3(0.06f, 0.06f, 0.6f), cGold);
            doors.Add(door.AddComponent<Door>());

            Decor(root, i, z0);

            var v = Chibi.Make(Kinds[i % 3], new Vector3(0, 0, z0 + 4.6f), 180);
            v.gameObject.AddComponent<CapsuleCollider>().center = Vector3.up;
            villains.Add(v);

            Vector3[] spots = { new(-4.8f, 0, z0 + 0.5f), new(4.8f, 0, z0 + 0.5f), new(-4.2f, 0, z0 - 4.5f) };
            for (int c = 0; c < rooms[i].clues.Length && c < spots.Length; c++) MakeClue(root, rooms[i].clues[c], i, spots[c]);
        }
        // outside: freedom garden
        float zo = n * RoomLen + 4;
        Art.Prim(PrimitiveType.Cube, root, new Vector3(0, -0.12f, zo), new Vector3(20, 0.2f, 10), Art.Hex("#9BDE7E"), true);
        for (int t = 0; t < 6; t++)
        {
            float x = (t % 2 == 0 ? -1 : 1) * (4 + t);
            Art.Prim(PrimitiveType.Cylinder, root, new Vector3(x, 0.6f, zo + 2), new Vector3(0.3f, 0.6f, 0.3f), Art.Hex("#8D6E63"));
            Art.Prim(PrimitiveType.Sphere, root, new Vector3(x, 1.8f, zo + 2), new Vector3(1.6f, 1.6f, 1.6f), Art.Hex("#5FBF5F"));
        }
    }

    void Furniture(Transform root, string kind, Vector3 p, float yaw = 0)
    {
        var g = new GameObject(kind).transform; g.SetParent(root); g.position = p; g.rotation = Quaternion.Euler(0, yaw, 0);
        switch (kind)
        {
            case "desk":
                Art.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.75f, 0), new Vector3(2f, 0.12f, 1f), Art.Hex("#F5F0E6"), true);
                foreach (var x in new[] { -0.9f, 0.9f }) Art.Prim(PrimitiveType.Cube, g, new Vector3(x, 0.35f, 0), new Vector3(0.1f, 0.7f, 0.9f), Art.Hex("#B0A99A"));
                Art.Prim(PrimitiveType.Cube, g, new Vector3(0, 1.15f, 0.2f), new Vector3(0.8f, 0.5f, 0.06f), Art.Hex("#37474F"));
                Art.Prim(PrimitiveType.Cube, g, new Vector3(0, 1.15f, 0.16f), new Vector3(0.7f, 0.4f, 0.02f), Art.Hex("#80DEEA"));
                break;
            case "plant":
                Art.Prim(PrimitiveType.Cylinder, g, new Vector3(0, 0.3f, 0), new Vector3(0.5f, 0.3f, 0.5f), Art.Hex("#E57373"), true);
                Art.Prim(PrimitiveType.Sphere, g, new Vector3(0, 0.95f, 0), new Vector3(0.9f, 0.9f, 0.9f), Art.Hex("#66BB6A"));
                break;
            case "cabinet":
                Art.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.8f, 0), new Vector3(0.8f, 1.6f, 0.7f), Art.Hex("#90A4AE"), true);
                for (int d = 0; d < 3; d++) Art.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.35f + d * 0.5f, -0.36f), new Vector3(0.3f, 0.06f, 0.04f), Art.Hex("#ECEFF1"));
                break;
            case "sofa":
                Art.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.3f, 0), new Vector3(2.4f, 0.5f, 1f), Art.Hex("#FF8A65"), true);
                Art.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.75f, 0.4f), new Vector3(2.4f, 0.8f, 0.25f), Art.Hex("#FF7043"));
                break;
            case "box":
                Art.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.35f, 0), new Vector3(0.7f, 0.7f, 0.7f), Art.Hex("#D7B98E"), true);
                Art.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.71f, 0), new Vector3(0.72f, 0.02f, 0.15f), Art.Hex("#C49A6C"));
                break;
            case "cooler":
                Art.Prim(PrimitiveType.Cube, g, new Vector3(0, 0.55f, 0), new Vector3(0.5f, 1.1f, 0.5f), Color.white, true);
                Art.Prim(PrimitiveType.Cylinder, g, new Vector3(0, 1.4f, 0), new Vector3(0.4f, 0.3f, 0.4f), Art.Hex("#81D4FA"));
                break;
        }
    }

    void Decor(Transform root, int i, float z0)
    {
        switch (i % 3)
        {
            case 0:
                Furniture(root, "desk", new Vector3(-3.5f, 0, z0 + 3.5f)); Furniture(root, "desk", new Vector3(3.5f, 0, z0 + 3.5f));
                Furniture(root, "desk", new Vector3(3.5f, 0, z0 - 2.5f), 180); Furniture(root, "cooler", new Vector3(6.2f, 0, z0 - 6f));
                Furniture(root, "plant", new Vector3(-6.2f, 0, z0 + 6.2f)); Furniture(root, "plant", new Vector3(6.2f, 0, z0 + 6.2f));
                break;
            case 1:
                Furniture(root, "cabinet", new Vector3(-6.3f, 0, z0 + 5.5f)); Furniture(root, "cabinet", new Vector3(-6.3f, 0, z0 + 4.6f));
                Furniture(root, "cabinet", new Vector3(6.3f, 0, z0 - 2f)); Furniture(root, "desk", new Vector3(3.2f, 0, z0 + 3.8f));
                Furniture(root, "plant", new Vector3(6.2f, 0, z0 + 6.2f)); Furniture(root, "plant", new Vector3(-6.2f, 0, z0 - 6.2f));
                break;
            default:
                Furniture(root, "sofa", new Vector3(-3.8f, 0, z0 + 4.5f)); Furniture(root, "box", new Vector3(4f, 0, z0 + 4.5f));
                Furniture(root, "box", new Vector3(4.8f, 0, z0 + 3.6f)); Furniture(root, "box", new Vector3(4.4f, 0.7f, z0 + 4.1f));
                Furniture(root, "plant", new Vector3(6.2f, 0, z0 - 6.2f));
                break;
        }
    }

    void MakeClue(Transform root, Clue clue, int room, Vector3 pos)
    {
        var g = new GameObject("Clue_" + clue.id).transform; g.SetParent(root); g.position = pos;
        var holder = new GameObject("obj").transform; holder.SetParent(g, false);
        Color col = clue.id switch { "postit" => Art.Hex("#FFEB3B"), "poster" or "flyer" => Art.Hex("#FF80AB"), "chats" => Art.Hex("#4FC3F7"), "shredder" => Art.Hex("#78909C"), _ => Color.white };
        if (clue.id == "shredder") Art.Prim(PrimitiveType.Cube, holder, Vector3.zero, new Vector3(0.6f, 0.6f, 0.4f), col);
        else
        {
            Art.Prim(PrimitiveType.Cube, holder, Vector3.zero, new Vector3(0.55f, 0.75f, 0.04f), col);
            for (int l = 0; l < 3; l++) Art.Prim(PrimitiveType.Cube, holder, new Vector3(0, 0.2f - l * 0.15f, -0.03f), new Vector3(0.38f, 0.04f, 0.01f), Art.Hex("#90A4AE"));
        }
        Art.Prim(PrimitiveType.Cylinder, g, new Vector3(0, 0.3f, 0), new Vector3(0.5f, 0.3f, 0.5f), Art.Hex("#ECEFF1"), true);
        var ring = Art.Prim(PrimitiveType.Cylinder, g, new Vector3(0, 0.02f, 0), new Vector3(1.3f, 0.02f, 1.3f), cGold).transform;
        var f = g.gameObject.AddComponent<Floaty>(); f.baseY = 1.1f; f.SetRing(ring);
        clueObjs.Add(new ClueObj { clue = clue, room = room, t = g, f = f });
    }

    // ---------------- game flow ----------------

    void EnterRoom(int i)
    {
        roomIndex = i;
        log.Clear(); villainHistory.Clear(); mentorHistory.Clear(); mentorLog.Clear();
        cluesSeen.Clear(); cardsUnlocked.Clear(); won.Clear();
        newCards = 0; openClue = null; openCard = selectedCard = null;
        input = mentorInput = "";
        doorOpen = false; mood = 20; talking = false; modal = Modal.None; showIntro = true;
        Say("", R.intro, new Color(0.85f, 0.85f, 0.9f));
        Say(R.villain.name, R.villain.opening, cVillain);
        villainHistory.Add(new Msg { role = "assistant", content = R.villain.opening });
        mentorLog.Add(new Line { who = "Maitre Pocket", text = "Psst! I'm your pocket lawyer. Look around for glowing clues, then ask me anything, like \"Can he really do that?\". I'll explain the law in plain words and give you law cards.", color = cMentor });
        phase = Phase.Play;
    }

    void Restart()
    {
        learned.Clear(); credibility = 100; bluffsCaught = 0;
        foreach (var d in doors) d.open = false;
        foreach (var c in clueObjs) c.f.seen = false;
        foreach (var v in villains) v.cheer = false;
        cc.enabled = false; player.position = new Vector3(0, 0.3f, -4.5f); cc.enabled = true;
        EnterRoom(0);
    }

    void Say(string who, string text, Color c) { log.Add(new Line { who = who, text = text, color = c }); logScroll.y = float.MaxValue; }

    void Toast(string s) { toast = s; toastTime = 3.5f; }

    void Unlock(string cardId)
    {
        if (string.IsNullOrEmpty(cardId) || !cardsUnlocked.Add(cardId)) return;
        newCards++;
        var c = R.cards.First(x => x.id == cardId);
        Toast("New law card: " + c.title);
    }

    void InspectClue(ClueObj c)
    {
        openClue = c.clue; modal = Modal.Clue;
        c.f.seen = true;
        if (cluesSeen.Add(c.clue.id)) Unlock(c.clue.card);
    }

    void AskMentor()
    {
        var q = mentorInput.Trim();
        if (q.Length == 0 || busy) return;
        mentorInput = ""; busy = true;
        mentorLog.Add(new Line { who = "You", text = q, color = cYou });
        mentorScroll.y = float.MaxValue;
        var req = new MentorReq { room = R.id, question = q, clues = cluesSeen.ToArray(), history = mentorHistory.ToArray() };
        mentorHistory.Add(new Msg { role = "user", content = q });
        StartCoroutine(Post<MentorResp>("/api/mentor", req, r =>
        {
            busy = false;
            var text = r == null || !string.IsNullOrEmpty(r.error) ? "Hmm, my connection to the law library dropped. Ask me again?" : r.answer;
            mentorLog.Add(new Line { who = "Maitre Pocket", text = text, color = cMentor });
            mentorHistory.Add(new Msg { role = "assistant", content = text });
            if (r?.unlock != null) foreach (var id in r.unlock) Unlock(id);
            mentorScroll.y = float.MaxValue;
        }));
    }

    void Argue()
    {
        var m = input.Trim();
        if (busy || doorOpen) return;
        var card = selectedCard != null ? R.cards.First(c => c.id == selectedCard) : null;
        if (m.Length == 0 && card == null) return;
        if (m.Length == 0) m = "I invoke this: " + card.title + ".";
        input = ""; busy = true;
        Say("You" + (card != null ? "  [plays: " + card.title + "]" : ""), m, cYou);
        var req = new ArgueReq { room = R.id, card = selectedCard ?? "", message = m, won = won.ToArray(), history = villainHistory.ToArray() };
        villainHistory.Add(new Msg { role = "user", content = m });
        StartCoroutine(Post<ArgueResp>("/api/argue", req, r =>
        {
            busy = false;
            if (r == null || !string.IsNullOrEmpty(r.error)) { Say("", "(The villain is momentarily speechless - network hiccup. Try again.)", Color.gray); return; }
            Say(R.villain.name, r.reply, cVillain);
            villainHistory.Add(new Msg { role = "assistant", content = r.reply });
            mood = r.mood;
            if (r.called_out_fake) { bluffsCaught++; Say("Bluff busted!", "You spotted a law that doesn't exist. Villains love to sound official.", cGold); }
            if (r.success)
            {
                won.Clear(); won.AddRange(r.won);
                var c = R.cards.First(x => x.id == r.point);
                if (!learned.Contains(c)) learned.Add(c);
                selectedCard = null;
                Toast("Legal point won: " + c.title);
            }
            else if (card != null) credibility = Mathf.Max(0, credibility - 10);
            if (!string.IsNullOrEmpty(r.coach)) Say("Maitre Pocket (coach)", r.coach, cMentor);
            if (!string.IsNullOrEmpty(r.fake_law) && !r.called_out_fake)
                Say("", "Suspicious... \"" + r.fake_law + "\" - does that law really exist? Ask Maitre Pocket, or call the bluff!", new Color(0.8f, 0.7f, 1f));
            if (r.door_open)
            {
                doorOpen = true; doors[roomIndex].open = true; villains[roomIndex].cheer = true;
                Say("", "THE DOOR IS OPEN! Press Esc to leave and walk through.", new Color(0.5f, 1f, 0.5f));
            }
        }));
    }

    // ---------------- per-frame ----------------

    void Update()
    {
        if (toastTime > 0) toastTime -= Time.deltaTime;
        if (phase != Phase.Play) { OrbitCamera(); return; }

        bool locked = talking || modal != Modal.None;
        Vector3 move = Vector3.zero;
        if (!locked)
        {
            move = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
            if (move.sqrMagnitude > 1) move.Normalize();
            if (move.sqrMagnitude > 0.01f) { showIntro = false; player.rotation = Quaternion.Slerp(player.rotation, Quaternion.LookRotation(move), Time.deltaTime * 12); }
        }
        cc.Move((move * 5.5f + Vector3.down * 9f) * Time.deltaTime);
        if (player.position.y < -1) { cc.enabled = false; player.position = new Vector3(player.position.x, 0.3f, player.position.z); cc.enabled = true; }
        playerChibi.body.gameObject.SetActive(!talking);
        mentorChibi.body.gameObject.SetActive(!talking);
        playerChibi.speed01 = move.magnitude;

        // nearby interactables
        nearClue = clueObjs.Where(c => c.room == roomIndex && Flat(c.t.position - player.position) < 2.0f).OrderBy(c => Flat(c.t.position - player.position)).FirstOrDefault();
        nearVillain = !doorOpen && Flat(villains[roomIndex].transform.position - player.position) < 2.6f;

        if (!locked && Input.GetKeyDown(KeyCode.E))
        {
            if (nearVillain) { talking = true; showIntro = false; }
            else if (nearClue != null) InspectClue(nearClue);
        }
        if (!locked && Input.GetKeyDown(KeyCode.M)) modal = Modal.Mentor;
        if (!locked && Input.GetKeyDown(KeyCode.C)) { modal = Modal.Cards; newCards = 0; }
        if (Input.GetKeyDown(KeyCode.Escape)) { if (modal != Modal.None) modal = Modal.None; else talking = false; }

        // walking into the next room
        if (doorOpen && player.position.z > roomIndex * RoomLen + 7.6f)
        {
            if (roomIndex + 1 < rooms.Length) EnterRoom(roomIndex + 1);
            else phase = Phase.Recap;
        }

        // villains face the player
        var v = villains[roomIndex].transform;
        var d = player.position - v.position; d.y = 0;
        if (d.sqrMagnitude > 0.01f && !doorOpen) v.rotation = Quaternion.Slerp(v.rotation, Quaternion.LookRotation(d), Time.deltaTime * 3);

        Vector3 camPos, look;
        if (talking)
        {
            var vf = v.forward;
            var right = -Vector3.Cross(Vector3.up, vf);
            camPos = v.position + vf * 5f + Vector3.up * 2f + right * 1.6f;
            look = v.position + Vector3.up * 1.1f + right * 1.6f;
        }
        else
        {
            camPos = player.position + new Vector3(0, 8.5f, -7.5f);
            look = player.position + Vector3.up * 0.8f;
        }
        cam.transform.position = Vector3.Lerp(cam.transform.position, camPos, Time.deltaTime * 4);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(look - cam.transform.position), Time.deltaTime * 6);
    }

    void OrbitCamera()
    {
        float t = Time.time * 0.15f;
        Vector3 center = phase == Phase.Recap ? new Vector3(0, 0, rooms.Length * RoomLen + 2) : new Vector3(0, 0, 1);
        if (phase == Phase.Recap)
        {
            var target = new Vector3(0, 0, rooms.Length * RoomLen + 3);
            cc.enabled = false; player.position = target; cc.enabled = true;
            playerChibi.cheer = true;
        }
        var pos = center + new Vector3(Mathf.Sin(t) * 9, 6, -Mathf.Cos(t) * 9);
        cam.transform.position = Vector3.Lerp(cam.transform.position, pos, Time.deltaTime * 2);
        cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(center + Vector3.up - cam.transform.position), Time.deltaTime * 3);
    }

    static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }

    // ---------------- UI ----------------

    void Styles()
    {
        if (sBody != null) return;
        white = Texture2D.whiteTexture;
        Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
        sTitle = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, wordWrap = true, richText = true, normal = { textColor = Color.white } };
        sBig = new GUIStyle(sTitle) { fontSize = 60, alignment = TextAnchor.MiddleCenter };
        sBody = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, richText = true, normal = { textColor = Color.white } };
        sSmall = new GUIStyle(sBody) { fontSize = 14 };
        sName = new GUIStyle(sBody) { fontStyle = FontStyle.Bold, fontSize = 15 };
        sTag = new GUIStyle(sBody) { fontStyle = FontStyle.Bold, fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { background = Solid(new Color(0.1f, 0.1f, 0.2f, 0.8f)), textColor = Color.white }, padding = new RectOffset(10, 10, 4, 4) };
        Color b0 = Art.Hex("#FF7AA2"), b1 = Art.Hex("#FF98B8"), b2 = Art.Hex("#E05C86");
        sBtn = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = true, normal = { background = Solid(b0), textColor = Color.white }, hover = { background = Solid(b1), textColor = Color.white }, active = { background = Solid(b2), textColor = Color.white } };
        sBox = new GUIStyle(GUI.skin.box) { normal = { background = Solid(new Color(0.12f, 0.1f, 0.22f, 0.9f)) }, padding = new RectOffset(12, 12, 10, 10) };
        sBubble = new GUIStyle(sBox) { normal = { background = Solid(new Color(1, 1, 1, 0.08f)) }, padding = new RectOffset(10, 10, 6, 8) };
        sField = new GUIStyle(GUI.skin.textField) { fontSize = 17, wordWrap = true, padding = new RectOffset(8, 8, 8, 8), normal = { background = Solid(new Color(0.95f, 0.94f, 0.98f)), textColor = Color.black }, focused = { background = Solid(Color.white), textColor = Color.black } };
    }

    void OnGUI()
    {
        Styles();
        guiScale = Mathf.Min(Screen.width / W, Screen.height / H);
        guiOx = (Screen.width - W * guiScale) / 2; guiOy = (Screen.height - H * guiScale) / 2;
        GUI.matrix = Matrix4x4.TRS(new Vector3(guiOx, guiOy, 0), Quaternion.identity, new Vector3(guiScale, guiScale, 1));
        switch (phase)
        {
            case Phase.Loading: GUI.Label(new Rect(0, 0, W, H), loadError ?? "Loading the courthouse...", new GUIStyle(sBody) { alignment = TextAnchor.MiddleCenter, fontSize = 26, normal = { textColor = Art.Hex("#333355") } }); break;
            case Phase.Title: DrawTitle(); break;
            case Phase.Play: DrawPlay(); break;
            case Phase.Recap: DrawRecap(); break;
        }
    }

    bool Key(KeyCode k) => Event.current.type == EventType.KeyDown && Event.current.keyCode == k;

    Vector2 ToGui(Vector3 world)
    {
        var p = cam.WorldToScreenPoint(world);
        return new Vector2((p.x - guiOx) / guiScale, (Screen.height - p.y - guiOy) / guiScale);
    }

    void WorldTag(Vector3 world, string text)
    {
        if (Vector3.Dot(world - cam.transform.position, cam.transform.forward) < 0) return;
        var p = ToGui(world);
        var size = sTag.CalcSize(new GUIContent(text));
        GUI.Label(new Rect(p.x - size.x / 2, p.y - size.y, size.x, size.y), text, sTag);
    }

    void DrawTitle()
    {
        GUI.color = new Color(0, 0, 0, 0.35f); GUI.DrawTexture(new Rect(0, 0, W, H), white); GUI.color = Color.white;
        GUI.Label(new Rect(0, 60, W, 90), "ESCAPE THE CRAZY BOSS", sBig);
        GUI.Label(new Rect(0, 140, W, 40), "A cozy legal escape game: learn your rights by outsmarting cute-but-terrible villains", new GUIStyle(sBody) { alignment = TextAnchor.MiddleCenter, fontSize = 22 });
        GUI.Box(new Rect(340, 210, 600, 300), "", sBox);
        GUILayout.BeginArea(new Rect(365, 225, 550, 280));
        GUILayout.Label("It's 9 p.m. on your last day. Your boss, HR and your landlord all want something from you. You know nothing about law... yet.", sBody);
        GUILayout.Space(10);
        GUILayout.Label("<b>Move</b>  WASD / arrow keys", sBody);
        GUILayout.Label("<b>Investigate</b>  walk to a glowing clue and press E", sBody);
        GUILayout.Label("<b>Learn</b>  press M to ask Maitre Pocket, your tiny pocket lawyer", sBody);
        GUILayout.Label("<b>Argue</b>  walk to the villain, press E, play a law card", sBody);
        GUILayout.Label("<b>Escape</b>  win enough legal points to open each door", sBody);
        GUILayout.EndArea();
        if (GUI.Button(new Rect(W / 2 - 160, 560, 320, 72), "START YOUR ESCAPE", new GUIStyle(sBtn) { fontSize = 22 })) EnterRoom(0);
    }

    void DrawPlay()
    {
        if (!talking && modal == Modal.None)
        {
            WorldTag(villains[roomIndex].transform.position + Vector3.up * 2.6f, R.villain.name + (nearVillain ? "   [E] Talk" : ""));
            foreach (var c in clueObjs.Where(c => c.room == roomIndex))
                if (c == nearClue || !c.f.seen) WorldTag(c.t.position + Vector3.up * 1.9f, (c == nearClue ? "[E] " : c.f.seen ? "" : "? ") + c.clue.label);
            WorldTag(mentorChibi.transform.position + Vector3.up * 1.2f, "Maitre Pocket [M]");
        }

        // HUD
        GUI.Box(new Rect(0, 0, W, 52), "", sBox);
        GUI.Label(new Rect(20, 8, 600, 40), R.title, new GUIStyle(sTitle) { fontSize = 23 });
        GUI.Label(new Rect(560, 12, 700, 30), $"Legal points: {won.Count}/{R.need}     Credibility: {credibility}%     Room {roomIndex + 1}/{rooms.Length}", new GUIStyle(sBody) { alignment = TextAnchor.MiddleRight, fontSize = 18 });

        if (!talking)
        {
            if (GUI.Button(new Rect(20, H - 70, 230, 52), "Ask Maitre Pocket [M]", sBtn)) modal = Modal.Mentor;
            if (GUI.Button(new Rect(260, H - 70, 230, 52), "Law cards [C]" + (newCards > 0 ? $"  ({newCards} new)" : ""), sBtn)) { modal = Modal.Cards; newCards = 0; }
            GUI.Label(new Rect(510, H - 62, 700, 40), doorOpen ? "<b>The door is open - walk through it!</b>" : "WASD to move  -  E to interact  -  glowing rings are clues", new GUIStyle(sTag) { alignment = TextAnchor.MiddleLeft });
        }
        if (showIntro && !talking && modal == Modal.None)
        {
            GUI.Box(new Rect(W / 2 - 330, 80, 660, 140), "", sBox);
            GUI.Label(new Rect(W / 2 - 310, 92, 620, 120), "<b>" + R.title + "</b>\n" + R.intro, sBody);
        }

        if (talking) DrawTalk();
        switch (modal)
        {
            case Modal.Clue: DrawClue(); break;
            case Modal.Mentor: DrawMentor(new Rect(20, 66, 560, 640)); break;
            case Modal.Cards: DrawCards(new Rect(20, 66, 560, 640)); break;
        }

        if (toastTime > 0 && !string.IsNullOrEmpty(toast))
        {
            var sz = sTag.CalcSize(new GUIContent(toast));
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01(toastTime));
            GUI.Label(new Rect(W / 2 - sz.x / 2 - 10, H - 140, sz.x + 20, 44), toast, new GUIStyle(sTag) { fontSize = 20, normal = { background = sTag.normal.background, textColor = cGold } });
            GUI.color = Color.white;
        }
    }

    void DrawClue()
    {
        var r = new Rect(W / 2 - 280, 190, 560, 320);
        GUI.Box(r, "", sBox);
        GUILayout.BeginArea(new Rect(r.x + 20, r.y + 16, r.width - 40, r.height - 30));
        GUILayout.Label("Clue: " + openClue.label, sTitle);
        GUILayout.Space(8);
        GUILayout.Label(openClue.text, new GUIStyle(sBody) { fontSize = 19 });
        GUILayout.FlexibleSpace();
        var card = R.cards.First(c => c.id == openClue.card);
        GUILayout.Label("<color=#ffd75e>This clue hints at a law card: " + card.title + "</color>\nNot sure what it means? Ask Maitre Pocket!", sSmall);
        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Ask Maitre Pocket", sBtn, GUILayout.Height(44))) { modal = Modal.Mentor; mentorInput = "What does the " + openClue.label.ToLower() + " mean for me legally?"; }
        if (GUILayout.Button("Got it (Esc)", sBtn, GUILayout.Height(44))) modal = Modal.None;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    void DrawTalk()
    {
        var r = new Rect(680, 66, 580, 640);
        GUI.Box(r, "", sBox);
        GUI.Label(new Rect(r.x + 14, r.y + 8, 300, 30), R.villain.name, new GUIStyle(sTitle) { fontSize = 22, normal = { textColor = cVillain } });
        GUI.Label(new Rect(r.x + 14, r.y + 40, 140, 22), "Cornered-o-meter", sSmall);
        GUI.color = new Color(1, 1, 1, 0.15f); GUI.DrawTexture(new Rect(r.x + 150, r.y + 44, 200, 16), white);
        GUI.color = Color.Lerp(new Color(0.4f, 0.85f, 0.4f), new Color(1f, 0.3f, 0.3f), mood / 100f);
        GUI.DrawTexture(new Rect(r.x + 150, r.y + 44, 200 * mood / 100f, 16), white); GUI.color = Color.white;
        if (GUI.Button(new Rect(r.xMax - 210, r.y + 10, 95, 48), "Ask\nPocket", new GUIStyle(sBtn) { fontSize = 14 })) modal = Modal.Mentor;
        if (GUI.Button(new Rect(r.xMax - 108, r.y + 10, 95, 48), "Leave\n(Esc)", new GUIStyle(sBtn) { fontSize = 14 })) talking = false;

        GUILayout.BeginArea(new Rect(r.x + 8, r.y + 70, r.width - 16, r.height - 270));
        logScroll = GUILayout.BeginScrollView(logScroll);
        foreach (var l in log)
        {
            GUILayout.BeginVertical(sBubble);
            if (!string.IsNullOrEmpty(l.who)) { GUI.contentColor = l.color; GUILayout.Label(l.who, sName); GUI.contentColor = Color.white; }
            GUILayout.Label(l.text, sBody);
            GUILayout.EndVertical();
            GUILayout.Space(4);
        }
        if (busy) GUILayout.Label("<i>...thinking...</i>", sBody);
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        float y = r.yMax - 194;
        GUI.Label(new Rect(r.x + 12, y, r.width - 24, 22), "Your law cards (pick one, then explain how it fits the facts):", sSmall);
        var hand = R.cards.Where(c => cardsUnlocked.Contains(c.id)).ToList();
        GUILayout.BeginArea(new Rect(r.x + 12, y + 24, r.width - 24, 56));
        chipScroll = GUILayout.BeginScrollView(chipScroll, GUILayout.Height(56));
        GUILayout.BeginHorizontal();
        if (hand.Count == 0) GUILayout.Label("<i>No cards yet - inspect clues or ask Maitre Pocket.</i>", sSmall);
        foreach (var c in hand)
        {
            bool isWon = won.Contains(c.id);
            GUI.backgroundColor = selectedCard == c.id ? cGold : isWon ? new Color(0.5f, 1f, 0.5f) : Color.white;
            GUI.enabled = !isWon && !doorOpen;
            if (GUILayout.Button((isWon ? "[won] " : "") + c.title, new GUIStyle(sBtn) { fontSize = 13 }, GUILayout.Width(170), GUILayout.Height(44)))
                selectedCard = selectedCard == c.id ? null : c.id;
            GUI.enabled = true; GUI.backgroundColor = Color.white;
        }
        GUILayout.EndHorizontal();
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        if (modal == Modal.None && (Key(KeyCode.Return) || Key(KeyCode.KeypadEnter)) && GUI.GetNameOfFocusedControl() == "arg") { Argue(); Event.current.Use(); }
        GUI.SetNextControlName("arg");
        GUI.enabled = !doorOpen && modal == Modal.None;
        input = GUI.TextArea(new Rect(r.x + 12, y + 88, r.width - 150, 96), input, 600, sField);
        if (GUI.Button(new Rect(r.xMax - 128, y + 88, 116, 96), busy ? "..." : "ARGUE!", new GUIStyle(sBtn) { fontSize = 22 })) Argue();
        GUI.enabled = true;
    }

    void DrawCards(Rect r)
    {
        GUI.Box(r, "", sBox);
        GUI.Label(new Rect(r.x + 14, r.y + 10, 400, 34), "Your law cards", sTitle);
        if (GUI.Button(new Rect(r.xMax - 110, r.y + 10, 96, 40), "Close", sBtn)) modal = Modal.None;
        GUILayout.BeginArea(new Rect(r.x + 10, r.y + 60, r.width - 20, r.height - 70));
        cardScroll = GUILayout.BeginScrollView(cardScroll);
        if (cardsUnlocked.Count == 0) GUILayout.Label("No law cards yet. Inspect clues or ask Maitre Pocket a question.", sBody);
        foreach (var c in R.cards.Where(c => cardsUnlocked.Contains(c.id)))
        {
            GUILayout.BeginVertical(sBubble);
            GUILayout.Label("<b>" + (won.Contains(c.id) ? "[won] " : "") + c.title + "</b>", new GUIStyle(sBody) { fontSize = 19 });
            GUILayout.Label(c.plain, sBody);
            GUILayout.Label("<i>Example: " + c.example + "</i>", sSmall);
            GUILayout.Label("<color=#ffd75e>" + c.law + "</color>", sSmall);
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawMentor(Rect r)
    {
        GUI.Box(r, "", sBox);
        GUI.Label(new Rect(r.x + 14, r.y + 8, 400, 34), "Maitre Pocket", new GUIStyle(sTitle) { normal = { textColor = cMentor } });
        GUI.Label(new Rect(r.x + 14, r.y + 42, 420, 22), "Ask anything, in plain words. No question is silly.", sSmall);
        if (GUI.Button(new Rect(r.xMax - 110, r.y + 10, 96, 40), "Close", sBtn)) modal = Modal.None;
        GUILayout.BeginArea(new Rect(r.x + 8, r.y + 72, r.width - 16, r.height - 190));
        mentorScroll = GUILayout.BeginScrollView(mentorScroll);
        foreach (var l in mentorLog)
        {
            GUILayout.BeginVertical(sBubble);
            GUI.contentColor = l.color; GUILayout.Label(l.who, sName); GUI.contentColor = Color.white;
            GUILayout.Label(l.text, sBody);
            GUILayout.EndVertical();
            GUILayout.Space(4);
        }
        if (busy) GUILayout.Label("<i>...flipping through the Code...</i>", sBody);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        if ((Key(KeyCode.Return) || Key(KeyCode.KeypadEnter)) && GUI.GetNameOfFocusedControl() == "ask") { AskMentor(); Event.current.Use(); }
        GUI.SetNextControlName("ask");
        mentorInput = GUI.TextArea(new Rect(r.x + 12, r.yMax - 110, r.width - 120, 98), mentorInput, 600, sField);
        if (GUI.Button(new Rect(r.xMax - 100, r.yMax - 110, 88, 98), busy ? "..." : "ASK", new GUIStyle(sBtn) { fontSize = 20 })) AskMentor();
    }

    void DrawRecap()
    {
        GUI.Label(new Rect(0, 20, W, 80), "YOU ESCAPED!", sBig);
        GUI.Label(new Rect(0, 92, W, 30), $"Credibility {credibility}%   -   Bluffs busted: {bluffsCaught}   -   Concepts learned: {learned.Count}", new GUIStyle(sTag) { fontSize = 20 });
        GUI.Box(new Rect(190, 140, 900, 470), "", sBox);
        GUILayout.BeginArea(new Rect(210, 150, 860, 450));
        cardScroll = GUILayout.BeginScrollView(cardScroll);
        GUILayout.Label("What you learned today", sTitle);
        foreach (var c in learned)
        {
            GUILayout.BeginVertical(sBubble);
            GUILayout.Label("<b>" + c.title + "</b>  <color=#ffd75e>(" + c.law + ")</color>", sBody);
            GUILayout.Label(c.plain, sBody);
            GUILayout.Label("<i>In real life: " + c.example + "</i>", sSmall);
            GUILayout.EndVertical();
            GUILayout.Space(6);
        }
        GUILayout.Label("<i>This is a simplified game. For a real situation, talk to a lawyer, a union or a free legal aid service (\"Maison de justice et du droit\").</i>", sSmall);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        if (GUI.Button(new Rect(W / 2 - 140, 630, 280, 64), "PLAY AGAIN", new GUIStyle(sBtn) { fontSize = 22 })) Restart();
    }
}
