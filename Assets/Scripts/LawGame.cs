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
    const float W = 1280, H = 720, Side = 200, K = 1080f / 1536f;
    static readonly Color Wood = Hex("#5B3A22"), WoodDark = Hex("#43291A"), Paper = Hex("#FFF4DC"), Ink = Hex("#3A2A1A"),
        Brown = Hex("#7A5230"), Green = Hex("#5DAA3C"), Red = Hex("#C8553D"), Soft = Hex("#8A7457"), Gold = Hex("#FFD34D");

    class Layout { public Vector2[] clueAt, clueStand; public Vector2 villain, exit, exitStand, start; public Rect walk; public Rect[] blocks; }
    static readonly Dictionary<string, Layout> Layouts = new()
    {
        ["boss"] = new Layout
        {
            clueAt = new[] { V(368, 175), V(742, 95), V(1070, 115) }, clueStand = new[] { V(300, 330), V(720, 330), V(1070, 280) },
            villain = V(1180, 610), exit = V(1380, 860), exitStand = V(1380, 945), start = V(300, 720),
            walk = new Rect(100, 270, 1340, 685), blocks = new[] { new Rect(1010, 290, 340, 240), new Rect(30, 780, 250, 150), new Rect(1380, 440, 130, 300) }
        },
        ["hr"] = new Layout
        {
            clueAt = new[] { V(400, 120), V(718, 150), V(1100, 110) }, clueStand = new[] { V(400, 290), V(718, 330), V(1100, 265) },
            villain = V(1200, 660), exit = V(1460, 800), exitStand = V(1400, 880), start = V(500, 820),
            walk = new Rect(110, 230, 1310, 740), blocks = new[] { new Rect(565, 90, 300, 200), new Rect(1015, 320, 390, 240), new Rect(70, 600, 300, 330) }
        },
        ["landlord"] = new Layout
        {
            clueAt = new[] { V(322, 105), V(760, 170), V(1100, 100) }, clueStand = new[] { V(330, 330), V(760, 345), V(1040, 310) },
            villain = V(1040, 700), exit = V(1410, 740), exitStand = V(1300, 840), start = V(500, 760),
            walk = new Rect(70, 290, 1350, 615), blocks = new[] { new Rect(1110, 240, 300, 270), new Rect(30, 380, 260, 300), new Rect(50, 720, 130, 140), new Rect(1260, 420, 140, 160) }
        },
    };
    static Vector2 V(float x, float y) => new(x, y);

    enum Phase { Loading, Play }
    enum Modal { None, Intro, Info, Villain, Mentor, Recap }
    enum Act { None, Clue, Villain, Mentor, Exit }

    Phase phase = Phase.Loading;
    Modal modal = Modal.Intro;
    Room[] rooms; int roomIndex; string loadError;
    Room R => rooms[roomIndex];
    Layout L => Layouts[R.id];

    readonly HashSet<string> evidence = new(), cards = new(), broken = new(), asked = new();
    readonly List<(string claim, LawCard card)> learned = new();
    readonly List<(bool me, string text)> chat = new();
    readonly List<Msg> history = new();
    int mistakes;
    bool talked, doorOpen, busy;
    Vector2 player, mentor, target; bool moving; Act pending; int pendingClue; bool faceLeft;
    float walkTime;

    string infoTitle, infoBody, infoExtra;
    string villainLine, mentorLine; int verdict; // 0 none, 1 sustained, 2 overruled
    string selClaim, selCard, selEvidence, mentorInput = "";
    Vector2 chatScroll, recapScroll;

    Texture2D white, circle, diamond, arrow, mapTex, playerTex, mentorTex; readonly Dictionary<string, Texture2D> villainTex = new();
    GUIStyle sText, sSmall, sHead, sTitle, sSide, sSideHead, sBtn, sBtnSel, sBtnGo, sBtnOff, sField, sTip;

    static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }
    string Base => Application.platform != RuntimePlatform.WebGLPlayer || string.IsNullOrEmpty(Application.absoluteURL)
        ? "http://localhost:8080" : new Uri(Application.absoluteURL).GetLeftPart(UriPartial.Authority);
    string VName => R.villain.name.Split(',')[0];

    void Start()
    {
        playerTex = Resources.Load<Texture2D>("Art/player");
        mentorTex = Resources.Load<Texture2D>("Art/mentor");
        foreach (var n in new[] { "boss", "hr", "landlord" }) villainTex[n] = Resources.Load<Texture2D>("Art/" + n);
        StartCoroutine(LoadRooms());
    }

    IEnumerator LoadRooms()
    {
        using var req = UnityWebRequest.Get(Base + "/api/rooms");
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) { loadError = "Could not reach the game server: " + req.error; yield break; }
        rooms = JsonUtility.FromJson<RoomsResp>(req.downloadHandler.text).rooms;
        EnterRoom(0);
        phase = Phase.Play;
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

    // ---------------- game flow ----------------

    void EnterRoom(int i)
    {
        roomIndex = i;
        evidence.Clear(); cards.Clear(); broken.Clear(); asked.Clear(); chat.Clear(); history.Clear();
        talked = doorOpen = busy = moving = false; pending = Act.None;
        selClaim = selCard = selEvidence = null; verdict = 0; mentorLine = null;
        mapTex = Resources.Load<Texture2D>("Art/map_" + R.id);
        player = L.start; mentor = player + V(-80, 10);
        chat.Add((false, "Bonjour! I'm Maitre Pocket, your pocket lawyer. Ask me anything in plain words, or click a question below."));
        modal = Modal.Intro;
    }

    void Info(string title, string body, string extra = null) { infoTitle = title; infoBody = body; infoExtra = extra; modal = Modal.Info; }

    void DoAct(Act a, int clue)
    {
        switch (a)
        {
            case Act.Clue:
                var c = R.clues[clue];
                string extra = null;
                if (evidence.Add(c.id) && !string.IsNullOrEmpty(c.card) && cards.Add(c.card))
                {
                    var card = R.cards.First(x => x.id == c.card);
                    extra = "NEW LAW CARD: " + card.title + "\n" + card.plain;
                }
                Info("EVIDENCE: " + c.label.ToUpper(), c.text, extra);
                break;
            case Act.Villain:
                if (!talked) { talked = true; villainLine = R.villain.opening; }
                modal = Modal.Villain; break;
            case Act.Mentor: modal = Modal.Mentor; break;
            case Act.Exit:
                if (!doorOpen) Info("THE DOOR IS LOCKED", $"{VName} won't let you leave. Break {R.need} of the {R.claims.Length} claims first: talk to {VName} and OBJECT.");
                else if (roomIndex + 1 < rooms.Length) EnterRoom(roomIndex + 1);
                else { modal = Modal.Recap; }
                break;
        }
    }

    void AskMentor(string q)
    {
        q = q.Trim();
        if (q.Length == 0 || busy) return;
        busy = true; mentorInput = ""; asked.Add(q);
        chat.Add((true, q)); chatScroll.y = 99999;
        var req = new MentorReq { room = R.id, question = q, clues = evidence.ToArray(), history = history.ToArray() };
        history.Add(new Msg { role = "user", content = q });
        StartCoroutine(Post<MentorResp>("/api/mentor", req, r =>
        {
            busy = false;
            var a = r == null || !string.IsNullOrEmpty(r.error) ? "Sorry, I lost my train of thought. Ask again?" : r.answer;
            history.Add(new Msg { role = "assistant", content = a });
            var got = new List<string>();
            if (r?.unlock != null) foreach (var id in r.unlock) if (cards.Add(id)) got.Add(R.cards.First(x => x.id == id).title);
            if (got.Count > 0) a += "\n\nNEW LAW CARD: " + string.Join(", ", got);
            chat.Add((false, a)); chatScroll.y = 99999;
        }));
    }

    void Object()
    {
        if (busy || selClaim == null || selCard == null || selEvidence == null) return;
        busy = true;
        var claim = R.claims.First(c => c.id == selClaim);
        var req = new ObjectReq { room = R.id, claim = selClaim, card = selCard, evidence = selEvidence, broken = broken.ToArray() };
        StartCoroutine(Post<ObjectResp>("/api/object", req, r =>
        {
            busy = false;
            if (r == null || !string.IsNullOrEmpty(r.error)) { mentorLine = "Connection hiccup. Try again."; verdict = 2; return; }
            villainLine = r.villain; mentorLine = r.mentor;
            verdict = r.correct ? 1 : 2;
            if (!r.correct) { mistakes++; mentorLine = (r.good_card ? "Right law, but that evidence doesn't prove it. " : r.good_evidence ? "Good evidence, but that law doesn't fit this claim. " : "") + mentorLine; }
            else
            {
                broken.Add(claim.id);
                learned.Add((claim.text, R.cards.First(c => c.id == r.card)));
                selClaim = selCard = selEvidence = null;
            }
            if (r.done && !doorOpen) { doorOpen = true; mentorLine += "\n\nThe exit door is now open!"; }
        }));
    }

    (string text, Vector2 at) Next()
    {
        if (!talked) return ($"Talk to {VName} to hear his claims.", L.villain);
        if (doorOpen) return ("The door is open! Walk to the exit.", L.exit);
        for (int i = 0; i < R.clues.Length; i++)
            if (!evidence.Contains(R.clues[i].id)) return ($"Search the glowing objects for evidence ({evidence.Count}/{R.clues.Length} found).", L.clueAt[i]);
        if (asked.Count == 0) return ("Ask Maitre Pocket (the little lawyer next to you) what the law says.", mentor);
        return ($"Go back to {VName} and OBJECT: pick his claim + a law + your proof.", L.villain);
    }

    // ---------------- movement ----------------

    bool Walkable(Vector2 p) => L.walk.Contains(p) && !L.blocks.Any(b => b.Contains(p));

    void Update()
    {
        if (phase == Phase.Play && modal != Modal.None && modal != Modal.Recap)
        {
            bool esc = Input.GetKeyDown(KeyCode.Escape);
            bool ok = modal is Modal.Info or Modal.Intro && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space));
            if (esc || ok) modal = Modal.None;
        }
        if (phase != Phase.Play || modal != Modal.None) { walkTime = 0; return; }
        var dir = new Vector2(Input.GetAxisRaw("Horizontal"), -Input.GetAxisRaw("Vertical"));
        if (dir != Vector2.zero) { moving = false; pending = Act.None; }
        else if (moving)
        {
            var d = target - player;
            if (d.magnitude < 8) { Arrive(); }
            else dir = d.normalized;
        }
        if (dir != Vector2.zero)
        {
            var step = dir.normalized * 420 * Time.deltaTime;
            var before = player;
            if (Walkable(player + step)) player += step;
            else if (Walkable(player + V(step.x, 0))) player += V(step.x, 0);
            else if (Walkable(player + V(0, step.y))) player += V(0, step.y);
            if (Mathf.Abs(step.x) > 0.5f) faceLeft = step.x < 0;
            if ((player - before).sqrMagnitude < 0.01f && moving) Arrive();
            walkTime += Time.deltaTime;
        }
        else walkTime = 0;
        mentor = Vector2.Lerp(mentor, player + V(faceLeft ? 80 : -80, 10), Time.deltaTime * 4);
        if (Input.GetKeyDown(KeyCode.E)) { var (a, c, p) = HitNear(player, 170); if (a != Act.None) DoAct(a, c); }
    }

    void Arrive()
    {
        moving = false;
        var a = pending; pending = Act.None;
        if (a == Act.None) return;
        var spot = a == Act.Clue ? L.clueStand[pendingClue] : a == Act.Villain ? L.villain : a == Act.Exit ? L.exitStand : player;
        if ((spot - player).magnitude < 220) DoAct(a, pendingClue);
    }

    (Act, int, Vector2) HitNear(Vector2 p, float r)
    {
        for (int i = 0; i < R.clues.Length; i++)
            if ((L.clueStand[i] - p).magnitude < r || (L.clueAt[i] - p).magnitude < r) return (Act.Clue, i, L.clueStand[i]);
        if ((L.villain - p).magnitude < r) return (Act.Villain, 0, L.villain);
        if ((L.exitStand - p).magnitude < r) return (Act.Exit, 0, L.exitStand);
        return (Act.None, 0, p);
    }

    (Act act, int clue, string label) HitAt(Vector2 p)
    {
        if (Rect.MinMaxRect(mentor.x - 45, mentor.y - 130, mentor.x + 45, mentor.y).Contains(p)) return (Act.Mentor, 0, "Ask Maitre Pocket");
        if (Rect.MinMaxRect(L.villain.x - 70, L.villain.y - 200, L.villain.x + 70, L.villain.y).Contains(p)) return (Act.Villain, 0, "Talk to " + VName);
        for (int i = 0; i < R.clues.Length; i++)
            if ((L.clueAt[i] - p).magnitude < 95) return (Act.Clue, i, (evidence.Contains(R.clues[i].id) ? "Look again: " : "Inspect: ") + R.clues[i].label);
        if ((L.exit - p).magnitude < 110) return (Act.Exit, 0, doorOpen ? "Leave the room" : "Exit (locked)");
        return (Act.None, 0, null);
    }

    // ---------------- drawing helpers ----------------

    static Texture2D Shape(Func<float, float, float> a)
    {
        var t = new Texture2D(64, 64) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(a((x + 0.5f) / 64f * 2 - 1, (y + 0.5f) / 64f * 2 - 1))));
        t.Apply(); return t;
    }
    static Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }

    void Styles()
    {
        if (sText != null) return;
        white = Texture2D.whiteTexture;
        circle = Shape((x, y) => (1 - Mathf.Sqrt(x * x + y * y)) * 32);
        diamond = Shape((x, y) => (1 - Mathf.Abs(x) - Mathf.Abs(y) * 0.5f) * 32);
        arrow = Shape((x, y) => Mathf.Min((y + 1) * 0.5f * 1 - Mathf.Abs(x) * 0.9f + 0.05f, 1) * 32);
        sText = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, richText = true, normal = { textColor = Ink } };
        sSmall = new GUIStyle(sText) { fontSize = 14, normal = { textColor = Soft } };
        sHead = new GUIStyle(sText) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = Brown } };
        sTitle = new GUIStyle(sText) { fontSize = 26, fontStyle = FontStyle.Bold };
        sSide = new GUIStyle(sText) { fontSize = 13, normal = { textColor = Paper } };
        sSideHead = new GUIStyle(sSide) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = Gold } };
        GUIStyle Btn(Color bg, Color fg, Color hov) => new(GUI.skin.button)
        {
            fontSize = 14, wordWrap = true, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(10, 8, 6, 6), border = new RectOffset(0, 0, 0, 0),
            normal = { background = Solid(bg), textColor = fg }, hover = { background = Solid(hov), textColor = fg }, active = { background = Solid(hov), textColor = fg }
        };
        sBtn = Btn(Hex("#F3E2BF"), Ink, Hex("#EBD3A3"));
        sBtnSel = Btn(Green, Color.white, Hex("#6DBA4B"));
        sBtnGo = new GUIStyle(Btn(Red, Color.white, Hex("#D96650"))) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold };
        sBtnOff = Btn(Hex("#E6DCC8"), Hex("#A89A80"), Hex("#E6DCC8"));
        sField = new GUIStyle(GUI.skin.textField) { fontSize = 15, padding = new RectOffset(8, 8, 8, 8), normal = { background = Solid(Color.white), textColor = Ink }, focused = { background = Solid(Color.white), textColor = Ink } };
        sTip = new GUIStyle(sText) { fontSize = 14, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
    }

    void Box(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, white); GUI.color = Color.white; }
    void Panel(Rect r) { Box(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), Brown); Box(r, Paper); }
    void Tex(Rect r, Texture t, Color c) { GUI.color = c; GUI.DrawTexture(r, t); GUI.color = Color.white; }
    Vector2 S(Vector2 p) => new(Side + p.x * K, p.y * K);
    Vector2 FromScreen(Vector2 s) => new((s.x - Side) / K, s.y / K);
    bool Btn(Rect r, string t, GUIStyle s = null) => GUI.Button(r, t, s ?? sBtn);
    GUIStyle Center(GUIStyle s) => new(s) { alignment = TextAnchor.MiddleCenter };

    void Sprite(Texture2D t, Vector2 feet, float heightImg, bool flip, float bob = 0)
    {
        if (t == null) return;
        float h = heightImg * K, w = h * t.width / t.height;
        var p = S(feet);
        Tex(new Rect(p.x - w * 0.45f, p.y - 8, w * 0.9f, 16), circle, new Color(0, 0, 0, 0.25f));
        var r = new Rect(p.x - w / 2, p.y - h - bob, w, h);
        if (flip) GUI.DrawTextureWithTexCoords(r, t, new Rect(1, 0, -1, 1)); else GUI.DrawTexture(r, t);
    }

    // ---------------- GUI ----------------

    void OnGUI()
    {
        Styles();
        float s = Mathf.Min(Screen.width / W, Screen.height / H);
        Box(new Rect(0, 0, Screen.width, Screen.height), WoodDark);
        GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - W * s) / 2, (Screen.height - H * s) / 2, 0), Quaternion.identity, new Vector3(s, s, 1));
        if (phase == Phase.Loading) { GUI.Label(new Rect(0, 0, W, H), loadError ?? "Loading...", Center(sSide)); return; }

        DrawMap();
        DrawSide();
        switch (modal)
        {
            case Modal.Intro: DrawIntro(); break;
            case Modal.Info: DrawInfo(); break;
            case Modal.Villain: DrawVillain(); break;
            case Modal.Mentor: DrawMentor(); break;
            case Modal.Recap: DrawRecap(); break;
        }
    }

    void DrawMap()
    {
        GUI.DrawTexture(new Rect(Side, 0, W - Side, H), mapTex);
        var e = Event.current;
        var mouse = FromScreen(e.mousePosition);
        bool onMap = e.mousePosition.x > Side && modal == Modal.None;

        for (int i = 0; i < R.clues.Length; i++)
        {
            if (evidence.Contains(R.clues[i].id)) continue;
            var p = S(L.clueAt[i]); float pulse = 46 + Mathf.Sin(Time.time * 4 + i) * 8;
            Tex(new Rect(p.x - pulse, p.y - pulse, pulse * 2, pulse * 2), circle, new Color(1, 0.9f, 0.4f, 0.45f));
            var b = new Rect(p.x - 13, p.y - 62 + Mathf.Sin(Time.time * 3 + i) * 3, 26, 26);
            Tex(b, circle, Color.white);
            GUI.Label(b, "?", new GUIStyle(Center(sText)) { fontStyle = FontStyle.Bold, fontSize = 16 });
        }
        if (doorOpen) { var p = S(L.exit); float pulse = 60 + Mathf.Sin(Time.time * 4) * 8; Tex(new Rect(p.x - pulse, p.y - pulse, pulse * 2, pulse * 2), circle, new Color(0.4f, 1, 0.4f, 0.45f)); }

        bool walking = walkTime > 0;
        var actors = new List<(float y, Action draw)>
        {
            (L.villain.y, () => Sprite(villainTex[R.id], L.villain, 200, false, doorOpen ? 0 : Mathf.Abs(Mathf.Sin(Time.time * 2)) * 3)),
            (player.y, () => Sprite(playerTex, player, 175, faceLeft, walking ? Mathf.Abs(Mathf.Sin(walkTime * 12)) * 6 : 0)),
            (mentor.y, () => Sprite(mentorTex, mentor, 120, false, Mathf.Sin(Time.time * 2.5f) * 4 + 6)),
        };
        foreach (var a in actors.OrderBy(a => a.y)) a.draw();

        var vp = S(L.villain);
        Label(new Vector2(vp.x, vp.y + 14), VName, Red);
        var mp = S(mentor);
        Label(new Vector2(mp.x, mp.y + 12), "Maitre Pocket", Brown);
        // plumbob
        var pp = S(player);
        Tex(new Rect(pp.x - 10, pp.y - 175 * K - 34 + Mathf.Sin(Time.time * 3) * 3, 20, 30), diamond, Green);

        if (modal == Modal.None)
        {
            var (_, at) = Next();
            var tp = S(at); float bob = Mathf.Abs(Mathf.Sin(Time.time * 4)) * 10;
            float top = at == L.villain ? 200 * K + 40 : at == mentor ? 130 * K + 30 : 90;
            Tex(new Rect(tp.x - 18, tp.y - top - bob, 36, 30), arrow, Gold);
        }

        if (!onMap) return;
        var hit = HitAt(mouse);
        if (hit.label != null)
        {
            var sz = sTip.CalcSize(new GUIContent(hit.label));
            var r = new Rect(e.mousePosition.x + 14, e.mousePosition.y - 34, sz.x + 20, 28);
            Box(r, new Color(0.15f, 0.1f, 0.05f, 0.85f)); GUI.Label(r, hit.label, sTip);
        }
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (hit.act == Act.Mentor) DoAct(Act.Mentor, 0);
            else if (hit.act != Act.None)
            {
                pending = hit.act; pendingClue = hit.clue;
                target = hit.act == Act.Clue ? L.clueStand[hit.clue] : hit.act == Act.Villain ? L.villain + V(-110, 20) : L.exitStand;
                moving = true;
            }
            else { target = mouse; moving = true; pending = Act.None; }
            e.Use();
        }
    }

    void Label(Vector2 at, string t, Color c)
    {
        var st = new GUIStyle(sTip) { fontSize = 12 };
        var sz = st.CalcSize(new GUIContent(t));
        var r = new Rect(at.x - sz.x / 2 - 8, at.y, sz.x + 16, 20);
        Box(r, new Color(c.r, c.g, c.b, 0.9f)); GUI.Label(r, t, st);
    }

    void DrawSide()
    {
        Box(new Rect(0, 0, Side, H), Wood);
        Box(new Rect(Side - 3, 0, 3, H), WoodDark);
        float x = 14, w = Side - 28, y = 14;
        GUI.Label(new Rect(x, y, w, 18), $"ROOM {roomIndex + 1} OF {rooms.Length}", sSideHead); y += 18;
        var title = R.title.Contains(" - ") ? R.title.Substring(R.title.IndexOf(" - ") + 3) : R.title;
        GUI.Label(new Rect(x, y, w, 26), title, new GUIStyle(sSide) { fontSize = 17, fontStyle = FontStyle.Bold }); y += 34;

        GUI.Label(new Rect(x, y, w, 18), "WHAT TO DO NOW", sSideHead); y += 20;
        var next = Next().text;
        float nh = sText.CalcHeight(new GUIContent(next), w - 16) * 0.85f + 16;
        Box(new Rect(x, y, w, nh), Paper);
        Box(new Rect(x, y, 4, nh), Green);
        GUI.Label(new Rect(x + 10, y + 6, w - 14, nh - 8), next, new GUIStyle(sText) { fontSize = 14, fontStyle = FontStyle.Bold });
        y += nh + 16;

        GUI.Label(new Rect(x, y, w, 18), $"CLAIMS TO BREAK  {broken.Count}/{R.need}", sSideHead); y += 20;
        foreach (var c in R.claims)
        {
            bool done = broken.Contains(c.id);
            var txt = talked ? (done ? "BROKEN - " : "") + "\"" + c.text + "\"" : "???";
            var st = new GUIStyle(sSide) { fontSize = 12, normal = { textColor = done ? Hex("#9FD98A") : Paper } };
            float h = st.CalcHeight(new GUIContent(txt), w - 14);
            Tex(new Rect(x, y + 3, 9, 9), circle, done ? Green : Soft);
            GUI.Label(new Rect(x + 14, y, w - 14, h), txt, st); y += h + 6;
        }
        y += 10;
        GUI.Label(new Rect(x, y, w, 18), "YOUR CASE FILE", sSideHead); y += 20;
        GUI.Label(new Rect(x, y, w, 20), $"Evidence found:  {evidence.Count}/{R.clues.Length}", sSide); y += 20;
        GUI.Label(new Rect(x, y, w, 20), $"Law cards:  {cards.Count}/{R.cards.Length}", sSide); y += 30;

        GUI.enabled = modal == Modal.None;
        if (Btn(new Rect(x, H - 112, w, 40), "Ask Maitre Pocket", Center(sBtnSel))) DoAct(Act.Mentor, 0);
        if (Btn(new Rect(x, H - 64, w, 40), "How to play", Center(sBtn))) modal = Modal.Intro;
        GUI.enabled = true;
    }

    Rect Dim()
    {
        Box(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.45f));
        var r = new Rect(240, 40, 1000, 640); Panel(r); return r;
    }

    void DrawIntro()
    {
        var r = new Rect(330, 80, 820, 560);
        Box(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.45f)); Panel(r);
        float x = r.x + 40, w = r.width - 80, y = r.y + 30;
        if (roomIndex == 0) { GUI.Label(new Rect(x, y, w, 36), "ESCAPE THE CRAZY BOSS", Center(sTitle)); y += 40; }
        GUI.Label(new Rect(x, y, w, 26), R.title, Center(new GUIStyle(sHead) { fontSize = 18 })); y += 34;
        float ih = sText.CalcHeight(new GUIContent(R.intro), w);
        GUI.Label(new Rect(x, y, w, ih), R.intro, sText); y += ih + 20;
        GUI.Label(new Rect(x, y, w, 20), "HOW TO WIN", sHead); y += 26;
        string[] steps =
        {
            "Search the glowing objects (?) to collect EVIDENCE.",
            "Click Maitre Pocket, the little lawyer next to you, to learn the LAW. Each answer can give you a law card.",
            $"Talk to {VName}. Pick one of his claims + the law that contradicts it + the evidence that proves it, then press OBJECTION!",
        };
        for (int i = 0; i < 3; i++)
        {
            Tex(new Rect(x, y, 34, 34), circle, Green);
            GUI.Label(new Rect(x, y, 34, 34), (i + 1).ToString(), new GUIStyle(sTip) { fontSize = 18 });
            float h = Mathf.Max(34, sText.CalcHeight(new GUIContent(steps[i]), w - 50));
            GUI.Label(new Rect(x + 50, y + 4, w - 50, h), steps[i], sText); y += h + 12;
        }
        GUI.Label(new Rect(x, y + 4, w, 20), "Click on the floor to walk (or use WASD / arrow keys). Mistakes are fine: the mentor explains them.", sSmall);
        if (Btn(new Rect(r.center.x - 110, r.yMax - 70, 220, 50), roomIndex == 0 && !talked && evidence.Count == 0 ? "START" : "GOT IT", new GUIStyle(sBtnSel) { alignment = TextAnchor.MiddleCenter, fontSize = 20, fontStyle = FontStyle.Bold })) modal = Modal.None;
    }

    void DrawInfo()
    {
        float bodyH = sText.CalcHeight(new GUIContent(infoBody), 560);
        float exH = infoExtra == null ? 0 : sText.CalcHeight(new GUIContent(infoExtra), 540) + 30;
        var r = new Rect(390, 0, 640, 150 + bodyH + exH); r.y = (H - r.height) / 2;
        Box(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.45f)); Panel(r);
        GUI.Label(new Rect(r.x + 40, r.y + 24, 560, 26), infoTitle, sHead);
        GUI.Label(new Rect(r.x + 40, r.y + 54, 560, bodyH), infoBody, sText);
        if (infoExtra != null)
        {
            var er = new Rect(r.x + 40, r.y + 66 + bodyH, 560, exH - 14);
            Box(er, Hex("#E3F2D6")); Box(new Rect(er.x, er.y, 4, er.height), Green);
            GUI.Label(new Rect(er.x + 14, er.y + 8, 540, er.height - 8), infoExtra, sText);
        }
        if (Btn(new Rect(r.center.x - 80, r.yMax - 64, 160, 44), "OK", new GUIStyle(sBtnSel) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold })) modal = Modal.None;
    }

    void DrawVillain()
    {
        var r = Dim();
        var vt = villainTex[R.id];
        float pw = 230, ph = Mathf.Min(330, pw * vt.height / vt.width); pw = ph * vt.width / vt.height;
        GUI.DrawTexture(new Rect(r.x + 130 - pw / 2, r.y + 390 - ph, pw, ph), vt);
        Label(new Vector2(r.x + 130, r.y + 400), R.villain.name, Red);
        float x = r.x + 270, w = r.width - 300, y = r.y + 24;

        // speech bubble
        float bh = Mathf.Max(60, sText.CalcHeight(new GUIContent(villainLine), w - 30) + 24);
        Box(new Rect(x, y, w, bh), Color.white); Box(new Rect(x, y, 4, bh), Red);
        GUI.Label(new Rect(x + 16, y + 10, w - 30, bh - 12), busy ? "..." : villainLine, sText);
        y += bh + 12;

        if (verdict != 0 && mentorLine != null)
        {
            var head = verdict == 1 ? "OBJECTION SUSTAINED - Maitre Pocket explains:" : "OBJECTION OVERRULED - Maitre Pocket explains:";
            float mh = sText.CalcHeight(new GUIContent(mentorLine), w - 90) + 40;
            Box(new Rect(x, y, w, mh), verdict == 1 ? Hex("#E3F2D6") : Hex("#F8E0D8"));
            GUI.DrawTexture(new Rect(x + 8, y + 8, 50, 50 * mentorTex.height / mentorTex.width > 70 ? 70 : 50), mentorTex, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(x + 70, y + 8, w - 80, 20), head, new GUIStyle(sHead) { normal = { textColor = verdict == 1 ? Green : Red } });
            GUI.Label(new Rect(x + 70, y + 30, w - 90, mh - 30), mentorLine, sText);
            y += mh + 12;
        }

        // builder
        GUI.Label(new Rect(x, y, w, 20), "BUILD YOUR OBJECTION", new GUIStyle(sHead) { fontSize = 15 }); y += 24;
        float cw = (w - 24) / 3, top = y, bottom = r.yMax - 80;
        string[] heads = { "1. HIS CLAIM", "2. THE LAW THAT SAYS NO", "3. YOUR PROOF" };
        for (int col = 0; col < 3; col++)
        {
            float cx = x + col * (cw + 12), cy = top;
            GUI.Label(new Rect(cx, cy, cw, 20), heads[col], sHead); cy += 22;
            if (col == 0)
                foreach (var c in R.claims)
                {
                    bool done = broken.Contains(c.id);
                    if (Btn(new Rect(cx, cy, cw, 54), (done ? "BROKEN: " : "") + "\"" + c.text + "\"", done ? sBtnOff : selClaim == c.id ? sBtnSel : sBtn) && !done) selClaim = c.id;
                    cy += 58;
                }
            else if (col == 1)
            {
                foreach (var c in R.cards.Where(c => cards.Contains(c.id)))
                {
                    if (Btn(new Rect(cx, cy, cw, 40), c.title, selCard == c.id ? sBtnSel : sBtn)) selCard = c.id;
                    cy += 44;
                }
                if (cards.Count < R.cards.Length) GUI.Label(new Rect(cx, cy, cw, 60), cards.Count == 0 ? "No law cards yet. Ask Maitre Pocket or inspect objects." : "Missing a law? Ask Maitre Pocket.", sSmall);
            }
            else
            {
                foreach (var c in R.clues.Where(c => evidence.Contains(c.id)))
                {
                    if (Btn(new Rect(cx, cy, cw, 40), c.label, selEvidence == c.id ? sBtnSel : sBtn)) selEvidence = c.id;
                    cy += 44;
                }
                if (evidence.Count < R.clues.Length) GUI.Label(new Rect(cx, cy, cw, 60), evidence.Count == 0 ? "No evidence yet. Search the glowing objects in the room." : "More evidence is hidden in the room.", sSmall);
            }
        }

        // selected card explanation
        var sel = selCard != null ? R.cards.First(c => c.id == selCard) : null;
        if (sel != null) GUI.Label(new Rect(x, bottom - 40, w - 470, 110), "<b>" + sel.title + ":</b> " + sel.plain, new GUIStyle(sSmall) { fontSize = 13, normal = { textColor = Ink } });

        if (Btn(new Rect(r.xMax - 450, r.yMax - 64, 130, 44), "Ask mentor", Center(sBtn))) modal = Modal.Mentor;
        if (Btn(new Rect(r.xMax - 310, r.yMax - 64, 100, 44), "Leave", Center(sBtn))) modal = Modal.None;
        bool ready = selClaim != null && selCard != null && selEvidence != null && !busy;
        int n = (selClaim != null ? 1 : 0) + (selCard != null ? 1 : 0) + (selEvidence != null ? 1 : 0);
        if (Btn(new Rect(r.xMax - 200, r.yMax - 64, 180, 44), busy ? "..." : ready ? "OBJECTION!" : $"pick {3 - n} more", ready ? sBtnGo : Center(sBtnOff)) && ready) Object();
    }

    void DrawMentor()
    {
        var r = Dim();
        float ph = 300, pw = ph * mentorTex.width / mentorTex.height;
        GUI.DrawTexture(new Rect(r.x + 130 - pw / 2, r.y + 70, pw, ph), mentorTex);
        Label(new Vector2(r.x + 130, r.y + 390), "Maitre Pocket", Brown);
        GUI.Label(new Rect(r.x + 30, r.y + 420, 200, 120), "Your pocket lawyer. Ask him anything in plain words. Good questions give you law cards.", sSmall);
        float x = r.x + 270, w = r.width - 300;
        GUI.Label(new Rect(x, r.y + 20, w, 22), "ASK MAITRE POCKET", new GUIStyle(sHead) { fontSize = 15 });
        if (Btn(new Rect(r.xMax - 110, r.y + 14, 90, 32), "Close", Center(sBtn))) modal = Modal.None;

        var area = new Rect(x, r.y + 52, w, 380);
        Box(area, Hex("#F7EBD0"));
        GUILayout.BeginArea(new Rect(area.x + 10, area.y + 10, area.width - 20, area.height - 20));
        chatScroll = GUILayout.BeginScrollView(chatScroll);
        foreach (var (me, text) in chat)
        {
            GUILayout.BeginHorizontal();
            if (me) GUILayout.FlexibleSpace();
            var st = new GUIStyle(sText) { fontSize = 15, padding = new RectOffset(12, 12, 8, 8), normal = { background = Solid(me ? Hex("#DCEFD0") : Color.white), textColor = Ink } };
            GUILayout.Label(text, st, GUILayout.MaxWidth(w * 0.78f));
            if (!me) GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);
        }
        if (busy) GUILayout.Label("Maitre Pocket is thinking...", sSmall);
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        float y = area.yMax + 10;
        GUI.Label(new Rect(x, y, w, 18), "SUGGESTED QUESTIONS", sHead); y += 22;
        foreach (var q in R.questions.Where(q => !asked.Contains(q)).Take(3))
        {
            if (Btn(new Rect(x, y, w, 28), q, new GUIStyle(sBtn) { fontSize = 13, padding = new RectOffset(10, 8, 2, 2) }) && !busy) AskMentor(q);
            y += 32;
        }
        var e = Event.current;
        if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "ask") { AskMentor(mentorInput); e.Use(); }
        GUI.SetNextControlName("ask");
        mentorInput = GUI.TextField(new Rect(x, r.yMax - 58, w - 110, 40), mentorInput, 300, sField);
        if (string.IsNullOrEmpty(mentorInput)) GUI.Label(new Rect(x + 10, r.yMax - 52, w - 120, 30), "Type your own question...", sSmall);
        if (Btn(new Rect(r.xMax - 130, r.yMax - 58, 100, 40), "ASK", new GUIStyle(sBtnSel) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold })) AskMentor(mentorInput);
    }

    void DrawRecap()
    {
        var r = Dim();
        GUI.Label(new Rect(r.x, r.y + 24, r.width, 40), "YOU ESCAPED!", Center(sTitle));
        GUI.Label(new Rect(r.x, r.y + 66, r.width, 24), $"{learned.Count} claims broken  -  {mistakes} overruled objections (that's how you learn)", Center(sSmall));
        GUILayout.BeginArea(new Rect(r.x + 50, r.y + 110, r.width - 100, r.height - 190));
        recapScroll = GUILayout.BeginScrollView(recapScroll);
        GUILayout.Label("WHAT YOU LEARNED", sHead);
        foreach (var (claim, card) in learned)
        {
            GUILayout.Space(10);
            GUILayout.Label("They said: \"" + claim + "\"", sSmall);
            GUILayout.Label("<b>" + card.title + "</b>  <color=#8A7457>" + card.law + "</color>", sText);
            GUILayout.Label(card.plain, new GUIStyle(sText) { fontSize = 15 });
        }
        GUILayout.Space(14);
        GUILayout.Label("Simplified for learning. For a real case, talk to a lawyer or a free legal aid service.", sSmall);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        if (Btn(new Rect(r.center.x - 110, r.yMax - 66, 220, 48), "PLAY AGAIN", new GUIStyle(sBtnSel) { alignment = TextAnchor.MiddleCenter, fontSize = 18, fontStyle = FontStyle.Bold }))
        { learned.Clear(); mistakes = 0; EnterRoom(0); }
    }
}
