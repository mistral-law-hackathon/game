using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

[Serializable] public class Villain { public string name, portrait, opening; }
[Serializable] public class Clue { public string id, label, card, text; }
[Serializable] public class LawCard { public string id, title, plain, example, law; public bool relevant; }
[Serializable] public class Claim { public string id, text; }
[Serializable] public class Room { public string id, title, intro; public int need; public Villain villain; public Clue[] clues; public LawCard[] cards; public Claim[] claims; public string[] questions; }
[Serializable] public class RoomsResp { public Room[] rooms; }
[Serializable] public class Msg { public string role, content; }
[Serializable] public class MentorReq { public string room, question; public string[] clues; public Msg[] history; }
[Serializable] public class MentorResp { public string answer, error; public string[] unlock; }
[Serializable] public class ObjectReq { public string room, claim, card, evidence; public string[] broken; }
[Serializable] public class ObjectResp { public bool correct, good_card, good_evidence, done; public string villain, mentor, card, error; }

public class LawGame : MonoBehaviour
{
    const float W = 1280, H = 720, Floor = 380, SceneTop = 48, PanelTop = 432;
    static readonly Color Navy = Hex("#1B2433"), Panel = Hex("#243044"), Line = Hex("#34435C"),
        Cream = Hex("#F2EADF"), Muted = Hex("#8C99AB"), Coral = Hex("#E8735A");

    enum Phase { Loading, Title, Play, Recap }
    class Say { public string who, text; public Color color; }
    class Learned { public string claim; public LawCard card; }

    Phase phase = Phase.Loading;
    Room[] rooms;
    int roomIndex;
    Room R => rooms[roomIndex];
    string loadError;

    // room state
    readonly List<Say> queue = new();
    float typed;
    readonly HashSet<string> evidence = new();
    readonly HashSet<string> cards = new();
    readonly HashSet<string> broken = new();
    readonly HashSet<string> asked = new();
    string selClaim, selCard, selEvidence, detail;
    bool doorOpen, busy, mentorOpen;
    float playerX = 90, walkTarget = -1, villainX = 1050, objectionFlash, toastTime;
    Clue pendingInspect;
    string toast;
    readonly List<Say> mentorLog = new();
    readonly List<Msg> mentorHistory = new();
    string mentorInput = "";
    Vector2 mentorScroll;

    // run state
    readonly List<Learned> learned = new();
    int mistakes;

    Texture2D white, circle;
    GUIStyle sTitle, sBig, sBody, sSmall, sLabel, sBtn, sBtnSel, sBtnDone, sAccent, sField, sChip;
    static readonly float[] ClueX = { 260, 500, 740 };

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    string Base => Application.platform != RuntimePlatform.WebGLPlayer || string.IsNullOrEmpty(Application.absoluteURL)
        ? "http://localhost:8080" : new Uri(Application.absoluteURL).GetLeftPart(UriPartial.Authority);

    void Start() { StartCoroutine(LoadRooms()); }

    IEnumerator LoadRooms()
    {
        using var req = UnityWebRequest.Get(Base + "/api/rooms");
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) { loadError = "Could not reach the game server: " + req.error; yield break; }
        rooms = JsonUtility.FromJson<RoomsResp>(req.downloadHandler.text).rooms;
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

    // ---------------- flow ----------------

    void EnterRoom(int i)
    {
        roomIndex = i;
        queue.Clear(); evidence.Clear(); cards.Clear(); broken.Clear(); asked.Clear();
        mentorLog.Clear(); mentorHistory.Clear();
        selClaim = selCard = selEvidence = detail = null;
        doorOpen = busy = mentorOpen = false;
        playerX = 90; walkTarget = -1; villainX = 1050; pendingInspect = null;
        Push("", R.intro, Muted);
        Push(R.villain.name, R.villain.opening, Coral);
        Push("Maitre Pocket", i == 0
            ? "To get out, break each of Gerard's claims. 1) Inspect objects to collect EVIDENCE. 2) Ask me questions to get LAW CARDS. 3) Pick a claim + a law + a piece of evidence, then press OBJECTION."
            : "Same method: collect evidence, ask me about the law, then object to each claim.", Cream);
        mentorLog.Add(new Say { who = "Maitre Pocket", text = "Ask me anything in plain words, or pick a question below.", color = Cream });
        phase = Phase.Play;
    }

    void Push(string who, string text, Color c) { queue.Add(new Say { who = who, text = text, color = c }); if (queue.Count == 1) typed = 0; }

    void Advance()
    {
        if (queue.Count == 0) return;
        if (typed < queue[0].text.Length) { typed = queue[0].text.Length; return; }
        queue.RemoveAt(0); typed = 0;
    }

    void Toast(string s) { toast = s; toastTime = 3f; }

    void Unlock(string id)
    {
        if (string.IsNullOrEmpty(id) || !cards.Add(id)) return;
        Toast("New law card: " + R.cards.First(c => c.id == id).title);
    }

    void Inspect(Clue c)
    {
        bool fresh = evidence.Add(c.id);
        Push("Evidence: " + c.label, c.text, Muted);
        if (fresh) Unlock(c.card);
    }

    void AskMentor(string q)
    {
        q = q.Trim();
        if (q.Length == 0 || busy) return;
        busy = true; mentorInput = ""; asked.Add(q);
        mentorLog.Add(new Say { who = "You", text = q, color = Muted });
        mentorScroll.y = float.MaxValue;
        var req = new MentorReq { room = R.id, question = q, clues = evidence.ToArray(), history = mentorHistory.ToArray() };
        mentorHistory.Add(new Msg { role = "user", content = q });
        StartCoroutine(Post<MentorResp>("/api/mentor", req, r =>
        {
            busy = false;
            var a = r == null || !string.IsNullOrEmpty(r.error) ? "Sorry, I lost my train of thought. Ask again?" : r.answer;
            mentorLog.Add(new Say { who = "Maitre Pocket", text = a, color = Cream });
            mentorHistory.Add(new Msg { role = "assistant", content = a });
            if (r?.unlock != null) foreach (var id in r.unlock) Unlock(id);
            mentorScroll.y = float.MaxValue;
        }));
    }

    void Object()
    {
        if (busy || selClaim == null || selCard == null || selEvidence == null) return;
        busy = true; objectionFlash = 1.2f;
        var claim = R.claims.First(c => c.id == selClaim);
        var req = new ObjectReq { room = R.id, claim = selClaim, card = selCard, evidence = selEvidence, broken = broken.ToArray() };
        StartCoroutine(Post<ObjectResp>("/api/object", req, r =>
        {
            busy = false;
            if (r == null || !string.IsNullOrEmpty(r.error)) { Push("", "Connection hiccup. Try again.", Muted); return; }
            Push(R.villain.name, r.villain, Coral);
            Push("Maitre Pocket", (r.correct ? "Objection sustained. " : r.good_card ? "Right law, wrong evidence. " : r.good_evidence ? "Good evidence, wrong law. " : "Objection overruled. ") + r.mentor, Cream);
            if (r.correct)
            {
                broken.Add(claim.id);
                learned.Add(new Learned { claim = claim.text, card = R.cards.First(c => c.id == r.card) });
                selClaim = selCard = selEvidence = null; detail = null;
            }
            else mistakes++;
            if (r.done) { doorOpen = true; Push("", "The door is open. Walk right to leave the room.", Muted); }
        }));
    }

    // ---------------- update ----------------

    void Update()
    {
        if (toastTime > 0) toastTime -= Time.deltaTime;
        if (objectionFlash > 0) objectionFlash -= Time.deltaTime;
        if (phase != Phase.Play) return;
        if (queue.Count > 0)
        {
            typed += Time.deltaTime * 70;
            if (!mentorOpen && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))) Advance();
            return;
        }
        float dir = mentorOpen ? 0 : Input.GetAxisRaw("Horizontal");
        if (dir != 0) { walkTarget = -1; pendingInspect = null; }
        else if (walkTarget >= 0)
        {
            dir = Mathf.Sign(walkTarget - playerX);
            if (Mathf.Abs(walkTarget - playerX) < 6) { walkTarget = -1; dir = 0; if (pendingInspect != null) { Inspect(pendingInspect); pendingInspect = null; } }
        }
        playerX = Mathf.Clamp(playerX + dir * 340 * Time.deltaTime, 60, doorOpen ? 1300 : 960);
        if (doorOpen) villainX = Mathf.MoveTowards(villainX, 1180, Time.deltaTime * 200);
        if (!mentorOpen && Input.GetKeyDown(KeyCode.E)) { var c = NearClue(); if (c != null) Inspect(c); }
        if (doorOpen && playerX > 1240)
        {
            if (roomIndex + 1 < rooms.Length) EnterRoom(roomIndex + 1); else phase = Phase.Recap;
        }
    }

    Clue NearClue()
    {
        for (int i = 0; i < R.clues.Length && i < ClueX.Length; i++) if (Mathf.Abs(ClueX[i] - playerX) < 70) return R.clues[i];
        return null;
    }

    // ---------------- drawing ----------------

    void Styles()
    {
        if (sBody != null) return;
        white = Texture2D.whiteTexture;
        circle = new Texture2D(64, 64) { filterMode = FilterMode.Bilinear };
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32, 32));
            circle.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(31.5f - d)));
        }
        circle.Apply();
        Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
        sBody = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true, richText = true, normal = { textColor = Cream } };
        sSmall = new GUIStyle(sBody) { fontSize = 14, normal = { textColor = Muted } };
        sLabel = new GUIStyle(sBody) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = Muted } };
        sTitle = new GUIStyle(sBody) { fontSize = 22, fontStyle = FontStyle.Bold };
        sBig = new GUIStyle(sBody) { fontSize = 54, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        sAccent = new GUIStyle(sBody) { fontSize = 64, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter, normal = { textColor = Coral } };
        GUIStyle Btn(Color bg, Color fg, Color hover) => new(GUI.skin.button)
        {
            fontSize = 15, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(12, 10, 6, 6),
            normal = { background = Solid(bg), textColor = fg }, hover = { background = Solid(hover), textColor = fg }, active = { background = Solid(hover), textColor = fg }
        };
        sBtn = Btn(Panel, Cream, Line);
        sBtnSel = Btn(Coral, Navy, Hex("#F08A73"));
        sBtnDone = Btn(Navy, Muted, Navy);
        sChip = new GUIStyle(Btn(Navy, Cream, Line)) { fontSize = 13 };
        sField = new GUIStyle(GUI.skin.textField) { fontSize = 15, padding = new RectOffset(8, 8, 8, 8), normal = { background = Solid(Line), textColor = Cream }, focused = { background = Solid(Line), textColor = Cream } };
    }

    void Rect(float x, float y, float w, float h, Color c) { GUI.color = c; GUI.DrawTexture(new Rect(x, y, w, h), white); GUI.color = Color.white; }
    void Circle(float cx, float cy, float w, float h, Color c) { GUI.color = c; GUI.DrawTexture(new Rect(cx - w / 2, cy - h / 2, w, h), circle); GUI.color = Color.white; }

    void OnGUI()
    {
        Styles();
        float s = Mathf.Min(Screen.width / W, Screen.height / H);
        Rect(0, 0, Screen.width, Screen.height, Navy);
        GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - W * s) / 2, (Screen.height - H * s) / 2, 0), Quaternion.identity, new Vector3(s, s, 1));
        switch (phase)
        {
            case Phase.Loading: GUI.Label(new Rect(0, 0, W, H), loadError ?? "Loading...", new GUIStyle(sBody) { alignment = TextAnchor.MiddleCenter }); break;
            case Phase.Title: DrawTitle(); break;
            case Phase.Play: DrawPlay(); break;
            case Phase.Recap: DrawRecap(); break;
        }
    }

    void Figure(float x, float floor, float k, Color body, string kind, float bob)
    {
        float y = floor - bob;
        Circle(x, y - 45 * k, 72 * k, 92 * k, body);
        Circle(x, y - 112 * k, 62 * k, 62 * k, body);
        float face = kind == "player" ? 8 : -8;
        Circle(x - 11 * k + face * k, y - 114 * k, 8 * k, 8 * k, Navy);
        Circle(x + 11 * k + face * k, y - 114 * k, 8 * k, 8 * k, Navy);
        switch (kind)
        {
            case "boss": Rect(x - 5 * k, y - 82 * k, 10 * k, 34 * k, Navy); break;
            case "hr": Rect(x - 24 * k, y - 116 * k, 48 * k, 3 * k, Navy); break;
            case "landlord": Rect(x - 34 * k, y - 140 * k, 68 * k, 6 * k, Navy); Rect(x - 20 * k, y - 168 * k, 40 * k, 30 * k, Navy); break;
            case "mentor": Rect(x - 12 * k, y - 80 * k, 24 * k, 12 * k, Cream); break;
        }
    }

    void DrawTitle()
    {
        GUI.Label(new Rect(0, 110, W, 70), "ESCAPE THE CRAZY BOSS", sBig);
        Rect(W / 2 - 60, 190, 120, 4, Coral);
        GUI.Label(new Rect(0, 210, W, 30), "Learn your rights by objecting to absurd villains.", new GUIStyle(sSmall) { fontSize = 20, alignment = TextAnchor.MiddleCenter });
        string[] steps = { "THEIR CLAIM", "+  A LAW", "+  EVIDENCE", "=  OBJECTION" };
        string[] sub = { "\"I don't pay overtime.\"", "Overtime must be paid", "Payslip: 47h worked, 35h paid", "Claim broken" };
        for (int i = 0; i < 4; i++)
        {
            float x = 160 + i * 250;
            Rect(x, 290, 220, 110, i == 3 ? Coral : Panel);
            GUI.Label(new Rect(x + 16, 302, 200, 24), steps[i], new GUIStyle(sLabel) { fontSize = 14, normal = { textColor = i == 3 ? Navy : Muted } });
            GUI.Label(new Rect(x + 16, 330, 196, 60), sub[i], new GUIStyle(sBody) { fontSize = 16, normal = { textColor = i == 3 ? Navy : Cream } });
        }
        GUI.Label(new Rect(0, 430, W, 30), "Inspect objects to find evidence. Ask your mentor to learn the law. Then object.", new GUIStyle(sSmall) { fontSize = 17, alignment = TextAnchor.MiddleCenter });
        if (GUI.Button(new Rect(W / 2 - 120, 500, 240, 56), "START", new GUIStyle(sBtnSel) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold })) EnterRoom(0);
    }

    void DrawPlay()
    {
        // top bar
        GUI.Label(new Rect(24, 12, 600, 30), R.title.ToUpper(), new GUIStyle(sLabel) { fontSize = 15 });
        GUI.Label(new Rect(680, 12, 576, 30), $"CLAIMS BROKEN  {broken.Count}/{R.claims.Length}", new GUIStyle(sLabel) { fontSize = 15, alignment = TextAnchor.UpperRight });

        DrawScene();
        if (queue.Count > 0) DrawDialogue(); else DrawBuilder();
        if (mentorOpen) DrawMentor();

        if (objectionFlash > 0)
        {
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01(objectionFlash * 2));
            GUI.Label(new Rect(0, 150, W, 120), "OBJECTION!", sAccent);
            GUI.color = Color.white;
        }
        if (toastTime > 0 && toast != null)
        {
            GUI.color = new Color(1, 1, 1, Mathf.Clamp01(toastTime));
            var sz = sBody.CalcSize(new GUIContent(toast));
            Rect(W / 2 - sz.x / 2 - 16, 60, sz.x + 32, 36, Coral);
            GUI.Label(new Rect(W / 2 - sz.x / 2, 66, sz.x + 10, 30), toast, new GUIStyle(sBody) { fontSize = 16, normal = { textColor = Navy } });
            GUI.color = Color.white;
        }
    }

    void DrawScene()
    {
        Rect(0, SceneTop, W, Floor - SceneTop, Panel);
        Rect(0, Floor, W, 2, Line);
        // door
        Rect(1150, Floor - 190, 90, 190, doorOpen ? Navy : Line);
        if (!doorOpen) Circle(1225, Floor - 95, 10, 10, Muted);
        else GUI.Label(new Rect(1150, Floor - 220, 90, 24), "EXIT  >", new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Coral } });

        var near = queue.Count == 0 ? NearClue() : null;
        for (int i = 0; i < R.clues.Length && i < ClueX.Length; i++)
        {
            var c = R.clues[i]; float x = ClueX[i];
            Rect(x - 60, Floor - 70, 120, 8, Line);
            Rect(x - 52, Floor - 62, 6, 62, Line); Rect(x + 46, Floor - 62, 6, 62, Line);
            Rect(x - 16, Floor - 112, 32, 42, evidence.Contains(c.id) ? Muted : Cream);
            if (!evidence.Contains(c.id)) Circle(x, Floor - 132 + Mathf.Sin(Time.time * 3 + i) * 4, 12, 12, Coral);
            GUI.Label(new Rect(x - 90, Floor + 8, 180, 22), (near == c ? "[E] " : "") + c.label, new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = near == c ? Coral : Muted } });
            var hit = new Rect(x - 70, Floor - 150, 140, 190);
            if (queue.Count == 0 && !mentorOpen && Event.current.type == EventType.MouseDown && hit.Contains(Event.current.mousePosition))
            { walkTarget = x; pendingInspect = c; Event.current.Use(); }
        }

        bool walking = walkTarget >= 0 || Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0;
        float bob = walking ? Mathf.Abs(Mathf.Sin(Time.time * 12)) * 6 : 0;
        string vk = R.id == "boss" ? "boss" : R.id == "hr" ? "hr" : "landlord";
        Figure(villainX, Floor, 1.15f, Coral, vk, doorOpen ? 0 : Mathf.Abs(Mathf.Sin(Time.time * 2)) * 3);
        GUI.Label(new Rect(villainX - 100, Floor + 8, 200, 22), R.villain.name, new GUIStyle(sLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Coral } });
        Figure(playerX, Floor, 1f, Cream, "player", bob);
        Figure(playerX - 70, Floor - 70 + Mathf.Sin(Time.time * 2.5f) * 6, 0.45f, Muted, "mentor", 0);
        if (queue.Count == 0)
            GUI.Label(new Rect(24, SceneTop + 12, 700, 24), doorOpen ? "Walk right to leave ->" : "A / D to walk  -  click or press E on an object to inspect it", sSmall);
    }

    void DrawDialogue()
    {
        var l = queue[0];
        Rect(0, PanelTop, W, H - PanelTop, Navy);
        Rect(80, PanelTop + 30, 4, 140, l.color);
        GUI.Label(new Rect(104, PanelTop + 26, 900, 28), string.IsNullOrEmpty(l.who) ? "" : l.who.ToUpper(), new GUIStyle(sLabel) { fontSize = 15, normal = { textColor = l.color } });
        int n = Mathf.Min(l.text.Length, (int)typed);
        GUI.Label(new Rect(104, PanelTop + 58, 1080, 160), l.text.Substring(0, n), new GUIStyle(sBody) { fontSize = 22 });
        GUI.Label(new Rect(104, H - 46, 1080, 24), queue.Count > 1 ? $"click to continue  ({queue.Count - 1} more)" : "click to continue", sSmall);
        if (!mentorOpen && Event.current.type == EventType.MouseDown && Event.current.mousePosition.y > SceneTop) { Advance(); Event.current.Use(); }
    }

    void DrawBuilder()
    {
        Rect(0, PanelTop, W, H - PanelTop, Navy);
        GUI.Label(new Rect(24, PanelTop + 6, 900, 24), "BUILD YOUR OBJECTION:  pick 1 claim  +  1 law  +  1 piece of evidence", new GUIStyle(sLabel) { fontSize = 14, normal = { textColor = Cream } });
        float y0 = PanelTop + 36, colW = 380, bh = 40;
        string[] heads = { "1  WHAT " + R.villain.name.Split(',')[0].ToUpper() + " CLAIMS", "2  YOUR LAW CARDS", "3  YOUR EVIDENCE" };
        for (int col = 0; col < 3; col++)
        {
            float x = 24 + col * (colW + 22);
            GUI.Label(new Rect(x, y0, colW, 20), heads[col], sLabel);
            float y = y0 + 24;
            if (col == 0)
                foreach (var c in R.claims)
                {
                    bool done = broken.Contains(c.id);
                    var st = done ? sBtnDone : selClaim == c.id ? sBtnSel : sBtn;
                    if (GUI.Button(new Rect(x, y, colW, bh), (done ? "BROKEN  " : "") + "\"" + c.text + "\"", st) && !done) { selClaim = selClaim == c.id ? null : c.id; }
                    y += bh + 6;
                }
            else if (col == 1)
            {
                foreach (var c in R.cards.Where(c => cards.Contains(c.id)))
                {
                    if (GUI.Button(new Rect(x, y, colW, 34), c.title, selCard == c.id ? sBtnSel : sBtn)) { selCard = selCard == c.id ? null : c.id; detail = selCard == null ? null : c.title + ": " + c.plain + "  (" + c.law + ")"; }
                    y += 38;
                }
                int locked = R.cards.Length - cards.Count;
                if (locked > 0) { GUI.Label(new Rect(x, y, colW, 40), $"{locked} card(s) left to discover. Inspect objects or ask the mentor.", sSmall); }
            }
            else
            {
                foreach (var c in R.clues.Where(c => evidence.Contains(c.id)))
                {
                    if (GUI.Button(new Rect(x, y, colW, 34), c.label, selEvidence == c.id ? sBtnSel : sBtn)) { selEvidence = selEvidence == c.id ? null : c.id; detail = selEvidence == null ? null : c.label + ": " + c.text; }
                    y += 38;
                }
                if (evidence.Count < R.clues.Length) GUI.Label(new Rect(x, y, colW, 40), "Inspect objects in the room to collect evidence.", sSmall);
            }
        }
        Rect(24, H - 64, W - 48, 1, Line);
        GUI.Label(new Rect(24, H - 58, 820, 54), detail ?? "Tip: click a law card or a piece of evidence to read what it means.", new GUIStyle(sSmall) { fontSize = 14, normal = { textColor = detail != null ? Cream : Muted } });
        if (GUI.Button(new Rect(860, H - 54, 170, 44), "ASK MENTOR", new GUIStyle(sBtn) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold })) mentorOpen = !mentorOpen;
        bool ready = selClaim != null && selCard != null && selEvidence != null && !busy;
        int picked = (selClaim != null ? 1 : 0) + (selCard != null ? 1 : 0) + (selEvidence != null ? 1 : 0);
        if (GUI.Button(new Rect(1040, H - 54, 216, 44), busy ? "..." : ready ? "OBJECTION!" : $"OBJECTION  {picked}/3", new GUIStyle(ready ? sBtnSel : sBtn) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 18, normal = { background = (ready ? sBtnSel : sBtn).normal.background, textColor = ready ? Navy : Muted } }) && ready) Object();
    }

    void DrawMentor()
    {
        var r = new Rect(780, SceneTop + 8, 476, Floor - SceneTop + 40);
        Rect(r.x, r.y, r.width, r.height, Navy);
        Rect(r.x, r.y, 4, r.height, Cream);
        GUI.Label(new Rect(r.x + 20, r.y + 10, 300, 24), "MAITRE POCKET  -  your mentor", new GUIStyle(sLabel) { fontSize = 14, normal = { textColor = Cream } });
        if (GUI.Button(new Rect(r.xMax - 70, r.y + 8, 60, 26), "close", new GUIStyle(sChip) { alignment = TextAnchor.MiddleCenter })) mentorOpen = false;
        GUILayout.BeginArea(new Rect(r.x + 20, r.y + 42, r.width - 30, 190));
        mentorScroll = GUILayout.BeginScrollView(mentorScroll);
        foreach (var m in mentorLog)
        {
            GUILayout.Label(m.who.ToUpper(), new GUIStyle(sLabel) { normal = { textColor = m.color == Cream ? Cream : Muted } });
            GUILayout.Label(m.text, new GUIStyle(sBody) { fontSize = 15, normal = { textColor = m.color } });
            GUILayout.Space(6);
        }
        if (busy) GUILayout.Label("thinking...", sSmall);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        float y = r.y + 238;
        foreach (var q in R.questions.Where(q => !asked.Contains(q)).Take(3))
        {
            if (GUI.Button(new Rect(r.x + 20, y, r.width - 30, 26), q, sChip)) AskMentor(q);
            y += 30;
        }
        bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "ask";
        if (enter) { AskMentor(mentorInput); Event.current.Use(); }
        GUI.SetNextControlName("ask");
        mentorInput = GUI.TextField(new Rect(r.x + 20, r.yMax - 46, r.width - 110, 36), mentorInput, 300, sField);
        if (GUI.Button(new Rect(r.xMax - 82, r.yMax - 46, 72, 36), "ASK", new GUIStyle(sBtnSel) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold })) AskMentor(mentorInput);
    }

    void DrawRecap()
    {
        GUI.Label(new Rect(0, 40, W, 70), "YOU ESCAPED", sBig);
        Rect(W / 2 - 60, 112, 120, 4, Coral);
        GUI.Label(new Rect(0, 124, W, 28), $"{learned.Count} claims broken  -  {mistakes} overruled objections", new GUIStyle(sSmall) { fontSize = 17, alignment = TextAnchor.MiddleCenter });
        GUILayout.BeginArea(new Rect(180, 170, 920, 450));
        mentorScroll = GUILayout.BeginScrollView(mentorScroll);
        GUILayout.Label("WHAT YOU LEARNED", sLabel);
        foreach (var l in learned)
        {
            GUILayout.Space(8);
            GUILayout.Label("They said: \"" + l.claim + "\"", sSmall);
            GUILayout.Label("<b>" + l.card.title + "</b>  <color=#8C99AB>" + l.card.law + "</color>", sBody);
            GUILayout.Label(l.card.plain, new GUIStyle(sBody) { fontSize = 15 });
        }
        GUILayout.Space(12);
        GUILayout.Label("Simplified for learning. For a real case, talk to a lawyer or a free legal aid service.", sSmall);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        if (GUI.Button(new Rect(W / 2 - 110, 640, 220, 50), "PLAY AGAIN", new GUIStyle(sBtnSel) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold }))
        { learned.Clear(); mistakes = 0; EnterRoom(0); }
    }
}
