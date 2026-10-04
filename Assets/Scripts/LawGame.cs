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
    const float W = 1280, H = 720;
    enum Screen_ { Loading, Title, Room, Recap }
    enum Tab { Evidence, Cards, Mentor }

    class Line { public string who, text; public Color color; }

    Screen_ screen = Screen_.Loading;
    Tab tab = Tab.Evidence;
    Room[] rooms;
    int roomIndex;
    Room R => rooms[roomIndex];
    string loadError;

    readonly List<Line> log = new();
    readonly List<Msg> villainHistory = new();
    readonly List<Msg> mentorHistory = new();
    readonly List<Line> mentorLog = new();
    readonly HashSet<string> cluesSeen = new();
    readonly HashSet<string> cardsUnlocked = new();
    readonly List<string> won = new();
    readonly List<LawCard> learned = new();
    readonly List<string> newCardFlash = new();
    string openClue, openCard, selectedCard;
    string input = "", mentorInput = "";
    bool busy, doorOpen;
    int mood = 20, credibility = 100, bluffsCaught;
    float shake;
    Vector2 logScroll, mentorScroll, sideScroll;

    readonly Dictionary<string, Texture2D> tex = new();
    Texture2D white;
    GUIStyle sTitle, sBody, sSmall, sBtn, sBox, sField, sBubble, sName, sBig;

    string Base
    {
        get
        {
            if (Application.platform != RuntimePlatform.WebGLPlayer || string.IsNullOrEmpty(Application.absoluteURL))
                return "http://localhost:8080";
            var u = new Uri(Application.absoluteURL);
            return u.GetLeftPart(UriPartial.Authority);
        }
    }

    void Start() { StartCoroutine(LoadRooms()); }

    Texture2D T(string name)
    {
        if (!tex.TryGetValue(name, out var t)) tex[name] = t = Resources.Load<Texture2D>("Art/" + name);
        return t;
    }

    IEnumerator LoadRooms()
    {
        using var req = UnityWebRequest.Get(Base + "/api/rooms");
        yield return req.SendWebRequest();
        if (req.result != UnityWebRequest.Result.Success) { loadError = "Could not reach the game server: " + req.error; yield break; }
        rooms = JsonUtility.FromJson<RoomsResp>(req.downloadHandler.text).rooms;
        screen = Screen_.Title;
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

    void EnterRoom(int i)
    {
        roomIndex = i;
        log.Clear(); villainHistory.Clear(); mentorHistory.Clear(); mentorLog.Clear();
        cluesSeen.Clear(); cardsUnlocked.Clear(); won.Clear(); newCardFlash.Clear();
        openClue = openCard = selectedCard = null;
        input = mentorInput = "";
        doorOpen = false; mood = 20; tab = Tab.Evidence;
        Say("", R.intro, new Color(0.85f, 0.85f, 0.9f));
        Say(R.villain.name, R.villain.opening, new Color(1f, 0.55f, 0.45f));
        villainHistory.Add(new Msg { role = "assistant", content = R.villain.opening });
        mentorLog.Add(new Line { who = "Maitre Pocket", text = "Psst! I'm your pocket lawyer. Inspect the evidence first, then ask me anything, like \"Can he really do that?\". I'll explain the law in plain words and give you law cards to play.", color = new Color(0.55f, 0.9f, 0.7f) });
        screen = Screen_.Room;
    }

    void Say(string who, string text, Color c)
    {
        log.Add(new Line { who = who, text = text, color = c });
        logScroll.y = float.MaxValue;
    }

    void Unlock(string cardId)
    {
        if (string.IsNullOrEmpty(cardId) || !cardsUnlocked.Add(cardId)) return;
        newCardFlash.Add(cardId);
        var c = R.cards.First(x => x.id == cardId);
        Say("New law card", c.title, new Color(1f, 0.85f, 0.3f));
    }

    void InspectClue(Clue c)
    {
        openClue = c.id;
        if (cluesSeen.Add(c.id)) Unlock(c.card);
    }

    void AskMentor()
    {
        var q = mentorInput.Trim();
        if (q.Length == 0 || busy) return;
        mentorInput = ""; busy = true;
        mentorLog.Add(new Line { who = "You", text = q, color = new Color(0.7f, 0.85f, 1f) });
        mentorScroll.y = float.MaxValue;
        var req = new MentorReq { room = R.id, question = q, clues = cluesSeen.ToArray(), history = mentorHistory.ToArray() };
        mentorHistory.Add(new Msg { role = "user", content = q });
        StartCoroutine(Post<MentorResp>("/api/mentor", req, r =>
        {
            busy = false;
            var text = r == null || !string.IsNullOrEmpty(r.error) ? "Hmm, my connection to the law library dropped. Ask me again?" : r.answer;
            mentorLog.Add(new Line { who = "Maitre Pocket", text = text, color = new Color(0.55f, 0.9f, 0.7f) });
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
        Say("You" + (card != null ? "  [plays: " + card.title + "]" : ""), m, new Color(0.7f, 0.85f, 1f));
        var req = new ArgueReq { room = R.id, card = selectedCard ?? "", message = m, won = won.ToArray(), history = villainHistory.ToArray() };
        villainHistory.Add(new Msg { role = "user", content = m });
        StartCoroutine(Post<ArgueResp>("/api/argue", req, r =>
        {
            busy = false;
            if (r == null || !string.IsNullOrEmpty(r.error)) { Say("", "(The villain is momentarily speechless - network hiccup. Try again.)", Color.gray); return; }
            Say(R.villain.name, r.reply, new Color(1f, 0.55f, 0.45f));
            villainHistory.Add(new Msg { role = "assistant", content = r.reply });
            mood = r.mood;
            if (r.called_out_fake) { bluffsCaught++; Say("Bluff busted!", "You spotted a law that doesn't exist. Villains love to sound official.", new Color(1f, 0.85f, 0.3f)); }
            if (r.success)
            {
                won.Clear(); won.AddRange(r.won);
                var c = R.cards.First(x => x.id == r.point);
                if (!learned.Contains(c)) learned.Add(c);
                shake = 0.6f;
                selectedCard = null;
            }
            else if (card != null) credibility = Mathf.Max(0, credibility - 10);
            if (!string.IsNullOrEmpty(r.coach)) Say("Maitre Pocket (coach)", r.coach, new Color(0.55f, 0.9f, 0.7f));
            if (!string.IsNullOrEmpty(r.fake_law) && !r.called_out_fake)
                Say("", "Suspicious... \"" + r.fake_law + "\" - does that law really exist? Ask Maitre Pocket, or call the bluff!", new Color(0.8f, 0.7f, 1f));
            if (r.door_open) { doorOpen = true; Say("", "THE DOOR IS OPEN!", new Color(0.5f, 1f, 0.5f)); }
        }));
    }

    void Update() { if (shake > 0) shake -= Time.deltaTime; }

    // ---------------- UI ----------------

    void Styles()
    {
        if (sBody != null) return;
        white = Texture2D.whiteTexture;
        Texture2D Solid(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
        sTitle = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, wordWrap = true, normal = { textColor = Color.white } };
        sBig = new GUIStyle(sTitle) { fontSize = 56, alignment = TextAnchor.MiddleCenter };
        sBody = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true, richText = true, normal = { textColor = Color.white } };
        sSmall = new GUIStyle(sBody) { fontSize = 14 };
        sName = new GUIStyle(sBody) { fontStyle = FontStyle.Bold, fontSize = 15 };
        sBtn = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = true, normal = { background = Solid(new Color(0.25f, 0.3f, 0.55f)), textColor = Color.white }, hover = { background = Solid(new Color(0.35f, 0.42f, 0.75f)), textColor = Color.white }, active = { background = Solid(new Color(0.15f, 0.2f, 0.4f)), textColor = Color.white } };
        sBox = new GUIStyle(GUI.skin.box) { normal = { background = Solid(new Color(0.05f, 0.06f, 0.12f, 0.86f)) }, padding = new RectOffset(12, 12, 10, 10) };
        sBubble = new GUIStyle(sBox) { normal = { background = Solid(new Color(1, 1, 1, 0.07f)) }, padding = new RectOffset(10, 10, 6, 8) };
        sField = new GUIStyle(GUI.skin.textField) { fontSize = 17, wordWrap = true, padding = new RectOffset(8, 8, 8, 8), normal = { background = Solid(new Color(0.92f, 0.93f, 0.97f)), textColor = Color.black }, focused = { background = Solid(Color.white), textColor = Color.black } };
    }

    void OnGUI()
    {
        Styles();
        float s = Mathf.Min(Screen.width / W, Screen.height / H);
        float ox = (Screen.width - W * s) / 2, oy = (Screen.height - H * s) / 2;
        GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white); GUI.color = Color.white;
        GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0), Quaternion.identity, new Vector3(s, s, 1));
        switch (screen)
        {
            case Screen_.Loading: DrawLoading(); break;
            case Screen_.Title: DrawTitle(); break;
            case Screen_.Room: DrawRoom(); break;
            case Screen_.Recap: DrawRecap(); break;
        }
    }

    void Bg(string name, float dim)
    {
        var t = T(name);
        if (t) GUI.DrawTexture(new Rect(0, 0, W, H), t, ScaleMode.ScaleAndCrop);
        GUI.color = new Color(0, 0, 0, dim); GUI.DrawTexture(new Rect(0, 0, W, H), white); GUI.color = Color.white;
    }

    void DrawLoading()
    {
        GUI.Label(new Rect(0, 0, W, H), loadError ?? "Loading the courthouse...", new GUIStyle(sBody) { alignment = TextAnchor.MiddleCenter, fontSize = 24 });
    }

    void DrawTitle()
    {
        Bg("bg_office", 0.55f);
        GUI.Label(new Rect(0, 40, W, 80), "ESCAPE THE CRAZY BOSS", sBig);
        GUI.Label(new Rect(0, 110, W, 40), "A legal escape game: learn your rights by outsmarting absurd villains", new GUIStyle(sBody) { alignment = TextAnchor.MiddleCenter, fontSize = 22 });
        var m = T("mentor"); if (m) GUI.DrawTexture(new Rect(120, 190, 340, 340), m, ScaleMode.ScaleToFit);
        GUI.Box(new Rect(500, 180, 660, 370), "", sBox);
        GUILayout.BeginArea(new Rect(525, 195, 610, 350));
        GUILayout.Label("It's 9 p.m. on your last day. Your boss, HR and your landlord all want something from you. You know nothing about law... yet.", sBody);
        GUILayout.Space(10);
        GUILayout.Label("<b>1. Discover</b>  Inspect the evidence in each room.", sBody);
        GUILayout.Label("<b>2. Learn</b>  Ask Maitre Pocket, your pocket lawyer, anything in plain words. Good questions unlock law cards.", sBody);
        GUILayout.Label("<b>3. Apply</b>  Play a card and argue in your own words. Careful: some cards don't apply here!", sBody);
        GUILayout.Label("<b>4. Spot bluffs</b>  Villains invent fake laws. Call them out.", sBody);
        GUILayout.Label("<b>5. Escape</b>  Win enough legal points to open each door.", sBody);
        GUILayout.EndArea();
        if (GUI.Button(new Rect(W / 2 - 160, 590, 320, 70), "START YOUR ESCAPE", sBtn)) EnterRoom(0);
    }

    void DrawRoom()
    {
        Bg(R.background, 0.35f);
        // top bar
        GUI.Box(new Rect(0, 0, W, 56), "", sBox);
        GUI.Label(new Rect(20, 8, 600, 40), R.title, new GUIStyle(sTitle) { fontSize = 24 });
        GUI.Label(new Rect(640, 14, 620, 30), $"Legal points: {won.Count}/{R.need}     Credibility: {credibility}%     Room {roomIndex + 1}/{rooms.Length}", new GUIStyle(sBody) { alignment = TextAnchor.MiddleRight, fontSize = 18 });

        // villain column
        float sx = shake > 0 ? Mathf.Sin(Time.time * 60) * 10 * shake : 0;
        GUI.Box(new Rect(16, 70, 300, 420), "", sBox);
        var p = T(R.villain.portrait); if (p) GUI.DrawTexture(new Rect(30 + sx, 80, 272, 272), p, ScaleMode.ScaleToFit);
        GUI.Label(new Rect(30, 356, 272, 30), R.villain.name, new GUIStyle(sName) { fontSize = 20, alignment = TextAnchor.MiddleCenter });
        GUI.Label(new Rect(30, 392, 272, 24), "Cornered-o-meter", new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter });
        GUI.color = new Color(1, 1, 1, 0.15f); GUI.DrawTexture(new Rect(40, 420, 252, 22), white);
        GUI.color = Color.Lerp(new Color(0.3f, 0.8f, 0.3f), new Color(1f, 0.25f, 0.2f), mood / 100f);
        GUI.DrawTexture(new Rect(40, 420, 252 * mood / 100f, 22), white); GUI.color = Color.white;
        GUI.Label(new Rect(30, 448, 272, 40), mood < 40 ? "Smug and confident" : mood < 75 ? "Starting to sweat" : "Panicking!", new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter });

        if (doorOpen)
        {
            bool last = roomIndex == rooms.Length - 1;
            if (GUI.Button(new Rect(16, 504, 300, 90), last ? "ESCAPE!\nSee what you learned" : "DOOR OPEN!\nNext room >", sBtn))
            { if (last) screen = Screen_.Recap; else EnterRoom(roomIndex + 1); }
        }

        DrawDialogue(new Rect(330, 70, 560, 636));
        DrawSide(new Rect(904, 70, 360, 636));
    }

    void DrawDialogue(Rect r)
    {
        GUI.Box(r, "", sBox);
        var logRect = new Rect(r.x + 8, r.y + 8, r.width - 16, r.height - 150);
        GUILayout.BeginArea(logRect);
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

        float y = r.yMax - 136;
        var sel = selectedCard != null ? R.cards.First(c => c.id == selectedCard) : null;
        GUI.Label(new Rect(r.x + 12, y, r.width - 24, 24), sel != null ? $"Card in hand: <b>{sel.title}</b>  (click it again in Law cards to drop)" : "Tip: pick a law card in the Law cards tab, then explain how it applies to the facts.", sSmall);
        bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "arg";
        if (enter) { Argue(); Event.current.Use(); }
        GUI.SetNextControlName("arg");
        GUI.enabled = !doorOpen;
        input = GUI.TextArea(new Rect(r.x + 12, y + 28, r.width - 150, 96), input, 600, sField);
        if (GUI.Button(new Rect(r.xMax - 128, y + 28, 116, 96), busy ? "..." : "ARGUE!", sBtn)) Argue();
        GUI.enabled = true;
    }

    void DrawSide(Rect r)
    {
        GUI.Box(r, "", sBox);
        string[] names = { "Evidence", "Law cards" + (newCardFlash.Count > 0 ? " (" + newCardFlash.Count + " new)" : ""), "Ask mentor" };
        for (int i = 0; i < 3; i++)
        {
            var br = new Rect(r.x + 8 + i * 116, r.y + 8, 112, 40);
            GUI.backgroundColor = (int)tab == i ? new Color(1f, 0.8f, 0.3f) : Color.white;
            if (GUI.Button(br, names[i], new GUIStyle(sBtn) { fontSize = 14 })) { tab = (Tab)i; if (tab == Tab.Cards) newCardFlash.Clear(); }
            GUI.backgroundColor = Color.white;
        }
        var inner = new Rect(r.x + 8, r.y + 56, r.width - 16, r.height - 64);
        if (tab == Tab.Mentor) { DrawMentor(inner); return; }
        GUILayout.BeginArea(inner);
        sideScroll = GUILayout.BeginScrollView(sideScroll);
        if (tab == Tab.Evidence)
        {
            GUILayout.Label("Click on things to investigate. Each clue hides a legal idea.", sSmall);
            foreach (var c in R.clues)
            {
                if (GUILayout.Button((cluesSeen.Contains(c.id) ? "[seen] " : "[?] ") + c.label, sBtn, GUILayout.Height(44))) InspectClue(c);
                if (openClue == c.id) { GUILayout.BeginVertical(sBubble); GUILayout.Label(c.text, sBody); GUILayout.EndVertical(); }
                GUILayout.Space(6);
            }
        }
        else
        {
            if (cardsUnlocked.Count == 0) GUILayout.Label("No law cards yet. Inspect evidence or ask Maitre Pocket a question.", sBody);
            foreach (var c in R.cards.Where(c => cardsUnlocked.Contains(c.id)))
            {
                bool isWon = won.Contains(c.id);
                GUI.backgroundColor = selectedCard == c.id ? new Color(1f, 0.8f, 0.3f) : isWon ? new Color(0.4f, 1f, 0.4f) : Color.white;
                if (GUILayout.Button((isWon ? "[won] " : "") + c.title, sBtn, GUILayout.Height(48))) openCard = openCard == c.id ? null : c.id;
                GUI.backgroundColor = Color.white;
                if (openCard == c.id)
                {
                    GUILayout.BeginVertical(sBubble);
                    GUILayout.Label(c.plain, sBody);
                    GUILayout.Label("<i>Example: " + c.example + "</i>", sSmall);
                    GUILayout.Label("<color=#ffd75e>" + c.law + "</color>", sSmall);
                    if (!isWon && !doorOpen && GUILayout.Button(selectedCard == c.id ? "Put back in hand" : "Play this card", sBtn, GUILayout.Height(36)))
                        selectedCard = selectedCard == c.id ? null : c.id;
                    GUILayout.EndVertical();
                }
                GUILayout.Space(6);
            }
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawMentor(Rect r)
    {
        var m = T("mentor"); if (m) GUI.DrawTexture(new Rect(r.x, r.y, 90, 90), m, ScaleMode.ScaleToFit);
        GUI.Label(new Rect(r.x + 96, r.y + 10, r.width - 96, 80), "<b>Maitre Pocket</b>\nAsk anything, in plain words. No question is silly.", sSmall);
        GUILayout.BeginArea(new Rect(r.x, r.y + 96, r.width, r.height - 210));
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
        bool enter = Event.current.type == EventType.KeyDown && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) && GUI.GetNameOfFocusedControl() == "ask";
        if (enter) { AskMentor(); Event.current.Use(); }
        GUI.SetNextControlName("ask");
        mentorInput = GUI.TextArea(new Rect(r.x, r.yMax - 108, r.width - 90, 104), mentorInput, 600, sField);
        if (GUI.Button(new Rect(r.xMax - 84, r.yMax - 108, 84, 104), busy ? "..." : "ASK", sBtn)) AskMentor();
    }

    void DrawRecap()
    {
        Bg("bg_exit", 0.55f);
        GUI.Label(new Rect(0, 24, W, 70), "YOU ESCAPED!", sBig);
        GUI.Label(new Rect(0, 92, W, 30), $"Credibility {credibility}%   -   Bluffs busted: {bluffsCaught}   -   Concepts learned: {learned.Count}", new GUIStyle(sBody) { alignment = TextAnchor.MiddleCenter, fontSize = 20 });
        GUI.Box(new Rect(140, 140, 1000, 470), "", sBox);
        GUILayout.BeginArea(new Rect(160, 150, 960, 450));
        sideScroll = GUILayout.BeginScrollView(sideScroll);
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
        GUILayout.Label("<i>Remember: this is a simplified game. For a real situation, talk to a lawyer, a union or a free legal aid service (\"Maison de justice et du droit\").</i>", sSmall);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        if (GUI.Button(new Rect(W / 2 - 140, 630, 280, 64), "PLAY AGAIN", sBtn))
        { learned.Clear(); credibility = 100; bluffsCaught = 0; EnterRoom(0); }
    }
}
