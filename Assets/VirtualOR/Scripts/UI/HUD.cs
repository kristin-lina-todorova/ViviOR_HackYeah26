// Virtual OR - heads-up display (IMGUI, no package dependencies).
using System.Collections.Generic;
using UnityEngine;

namespace VirtualOR
{
    public class HUD : MonoBehaviour
    {
        public App app;
        // palette (ViviOR brand book: Bright/Strong/Arty Sky/Sky Blue, white, gray on black; red/green/yellow kept as monitor semantics)
        static readonly Color Black = new Color32(4, 8, 14, 248), BlackSoft = new Color32(10, 18, 28, 245);   // near-opaque: UI alpha blends in linear space, so lower alpha washes out
        static readonly Color BrightBlue = new Color32(0, 88, 168, 255), StrongBlue = new Color32(0, 127, 188, 255), ArtySky = new Color32(47, 163, 219, 255);
        static readonly Color Sky = new Color32(106, 207, 255, 255), White = Color.white, Gray = new Color32(140, 140, 140, 255);
        static readonly Color Arterial = new Color32(226, 62, 52, 255), Monitor = new Color32(63, 209, 138, 255);
        Font reg, bold; bool ready;
        GUIStyle panel, panelSoft, title, h2, body, small, num, btn, btnPrimary, btnDanger, opt, optOn, chip;
        Texture2D white, ecgTex, chipPass, chipFail, gradient, stripes, wordmark, icon; Color32[] ecgPx;
        readonly List<Rect> uiRects = new List<Rect>();
        float W, H, uiScale = 1;
        Vector2 debriefScroll;

        void Init()
        {
            ready = true;
            reg = Resources.Load<Font>("VirtualOR/Fonts/Inter-Regular"); bold = Resources.Load<Font>("VirtualOR/Fonts/Inter-SemiBold");
            if (reg == null) reg = Resources.Load<Font>("VirtualOR/Fonts/AtkinsonHyperlegible-Regular");
            if (bold == null) bold = reg;
            wordmark = Resources.Load<Texture2D>("VirtualOR/UI/wordmark"); icon = Resources.Load<Texture2D>("VirtualOR/UI/icon");
            white = Tex(Color.white);
            gradient = U.MakeTex(64, 1, (u, v) => Color.Lerp(BrightBlue, ArtySky, u), false, TextureWrapMode.Clamp);
            stripes = Stripes(1024, 384);
            panel = Box(Card(Black, BrightBlue, 12, 1.5f), 18); panelSoft = Box(Card(BlackSoft, new Color(0, 0.345f, 0.66f, 0.6f), 10, 1f), 12);
            title = Text(bold, 24, White); h2 = Text(bold, 16, White); body = Text(reg, 15, White); small = Text(reg, 13, Gray); num = Text(bold, 30, Monitor);
            body.wordWrap = true; small.wordWrap = true; title.wordWrap = true;
            btn = Button(Card(Black, BrightBlue, 8, 1.5f), Card(new Color32(0, 40, 78, 240), ArtySky, 8, 1.5f), White);
            btnPrimary = Button(Card(StrongBlue, StrongBlue, 8, 0), Card(ArtySky, ArtySky, 8, 0), White);
            btnDanger = Button(Card(Black, Arterial, 8, 1.5f), Card(new Color32(70, 16, 14, 240), Arterial, 8, 1.5f), new Color32(255, 150, 140, 255));
            opt = Button(Card(Black, new Color(0, 0.345f, 0.66f, 0.7f), 8, 1f), Card(new Color32(0, 30, 58, 240), StrongBlue, 8, 1f), White); opt.font = reg; opt.alignment = TextAnchor.UpperLeft; opt.wordWrap = true; opt.padding = new RectOffset(14, 14, 10, 10); opt.richText = true;
            optOn = new GUIStyle(opt); optOn.normal.background = optOn.hover.background = optOn.active.background = Card(new Color32(0, 58, 112, 245), ArtySky, 8, 2f);
            chip = Text(bold, 12, White); chip.alignment = TextAnchor.MiddleCenter; chip.normal.background = Card(StrongBlue, StrongBlue, 10, 0); chip.border = new RectOffset(11, 11, 11, 11); chip.padding = new RectOffset(8, 8, 3, 3);
            chipPass = Card(StrongBlue, StrongBlue, 10, 0); chipFail = Card(Arterial, Arterial, 10, 0);
            ecgTex = new Texture2D(256, 56, TextureFormat.RGBA32, false); ecgPx = new Color32[256 * 56]; ecgTex.wrapMode = TextureWrapMode.Clamp;
        }

        static Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
        // rounded card: fill + antialiased outline of width sw (brand cards: black with a thin blue stroke)
        static Texture2D Card(Color fill, Color stroke, int r, float sw)
        {
            int s = r * 2 + 4; var t = new Texture2D(s, s, TextureFormat.RGBA32, false); t.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < s; y++) for (int x = 0; x < s; x++)
                {
                    float dx = Mathf.Max(0, Mathf.Max(r + 1.5f - x, x - (s - 2.5f - r))), dy = Mathf.Max(0, Mathf.Max(r + 1.5f - y, y - (s - 2.5f - r)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r + 0.5f - d), inner = Mathf.Clamp01(r - sw + 0.5f - d);
                    var c = Color.Lerp(stroke, fill, inner); c.a = a * Mathf.Lerp(stroke.a, fill.a, inner); t.SetPixel(x, y, c);
                }
            t.Apply(); return t;
        }
        // full-screen version for the start screen: two parallel stripes running edge to edge, from low on the left
        // (yLeft, fraction of the height) to higher on the right (yRight); generated at the screen's pixel size so they stay crisp
        Texture2D screenStripes; int screenStripesW, screenStripesH;
        Texture2D ScreenStripes(int w, int h)
        {
            if (screenStripes != null && screenStripesW == w && screenStripesH == h) return screenStripes;
            if (screenStripes != null) Destroy(screenStripes);
            screenStripesW = w; screenStripesH = h;
            const float yLeft = 0.10f, yRight = 0.56f;            // texture v (0 = bottom)
            float sw = h * 0.024f, gap = h * 0.040f;              // stripe width, centre-to-centre spacing
            float k = (yRight - yLeft) * h / w, n = 1f / Mathf.Sqrt(1 + k * k);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float d = (y - (yLeft * h + k * x)) * n;
                    float a = Mathf.Clamp01(sw * 0.5f + 0.5f - Mathf.Abs(d)) + Mathf.Clamp01(sw * 0.5f + 0.5f - Mathf.Abs(d - gap));
                    if (a <= 0) continue;
                    var c = Color.Lerp(BrightBlue, StrongBlue, x / (float)w); c.a = Mathf.Clamp01(a); px[y * w + x] = c;
                }
            screenStripes = new Texture2D(w, h, TextureFormat.RGBA32, false); screenStripes.wrapMode = TextureWrapMode.Clamp;
            screenStripes.SetPixels32(px); screenStripes.Apply(false);
            return screenStripes;
        }

        // the brand's two parallel diagonal stripes (Bright Blue -> Strong Blue), transparent elsewhere
        static Texture2D Stripes(int w, int h)
        {
            float k = 0.40f, sw = h * 0.075f, gap = h * 0.11f, c0 = h * 0.12f;
            float n = 1f / Mathf.Sqrt(1 + k * k);
            return U.MakeTex(w, h, (u, v) =>
            {
                float x = u * w, y = v * h; float d = (y - k * x - c0) * n;
                float a = Mathf.Clamp01(sw * 0.5f + 0.5f - Mathf.Abs(d)) + Mathf.Clamp01(sw * 0.5f + 0.5f - Mathf.Abs(d - gap));
                var c = Color.Lerp(BrightBlue, StrongBlue, u); c.a = Mathf.Clamp01(a); return c;
            }, false, TextureWrapMode.Clamp);
        }
        static RectOffset Slice(Texture2D t) { int b = t.width / 2 - 1; return new RectOffset(b, b, b, b); }
        GUIStyle Box(Texture2D bg, int pad) { var s = new GUIStyle(); s.normal.background = bg; s.border = Slice(bg); s.padding = new RectOffset(pad, pad, pad, pad); return s; }
        GUIStyle Text(Font f, int size, Color c) { var s = new GUIStyle(); s.font = f; s.fontSize = size; s.normal.textColor = c; s.richText = true; return s; }
        GUIStyle Button(Texture2D n, Texture2D h, Color c)
        {
            var s = new GUIStyle(); s.font = bold; s.fontSize = 15; s.normal.background = n; s.hover.background = h; s.active.background = h; s.normal.textColor = s.hover.textColor = s.active.textColor = c;
            s.border = Slice(n); s.padding = new RectOffset(14, 14, 9, 9); s.alignment = TextAnchor.MiddleCenter; s.richText = true; return s;
        }
        void Logo(float x, float y, float w) { if (wordmark != null) GUI.DrawTexture(new Rect(x, y, w, w * wordmark.height / wordmark.width), wordmark, ScaleMode.ScaleToFit); }
        const string Tagline = "Your <color=#2FA3DB>first</color> patient should <color=#2FA3DB>never</color> be <color=#2FA3DB>real</color>";

        // ---- crisp text: draw with an identity matrix at screen-pixel size instead of magnifying the scaled GUI
        bool inScroll;
        readonly Dictionary<GUIStyle, GUIStyle> scaled = new Dictionary<GUIStyle, GUIStyle>(); float scaledFor = -1;
        static readonly System.Text.RegularExpressions.Regex SizeTag = new System.Text.RegularExpressions.Regex("<size=(\\d+)>");
        bool Native { get { return !inScroll && Mathf.Abs(uiScale - 1) > 0.01f; } }
        Rect Px(Rect r) { return new Rect(r.x * uiScale, r.y * uiScale, r.width * uiScale, r.height * uiScale); }
        GUIStyle Sc(GUIStyle s)
        {
            if (scaledFor != uiScale || scaled.Count > 256) { scaled.Clear(); scaledFor = uiScale; }
            GUIStyle o; if (scaled.TryGetValue(s, out o)) return o;
            o = new GUIStyle(s); o.fontSize = Mathf.RoundToInt(s.fontSize * uiScale);
            o.padding = new RectOffset(Mathf.RoundToInt(s.padding.left * uiScale), Mathf.RoundToInt(s.padding.right * uiScale), Mathf.RoundToInt(s.padding.top * uiScale), Mathf.RoundToInt(s.padding.bottom * uiScale));
            scaled[s] = o; return o;
        }
        string ScTags(string t) { return t == null || t.IndexOf("<size=") < 0 ? t : SizeTag.Replace(t, m => "<size=" + Mathf.RoundToInt(int.Parse(m.Groups[1].Value) * uiScale) + ">"); }
        void SLabel(Rect r, string t, GUIStyle s)
        {
            if (!Native) { GUI.Label(r, t, s); return; }
            var m = GUI.matrix; GUI.matrix = Matrix4x4.identity; GUI.Label(Px(r), ScTags(t), Sc(s)); GUI.matrix = m;
        }
        bool SButton(Rect r, string t, GUIStyle s)
        {
            if (!Native) return GUI.Button(r, t, s);
            var m = GUI.matrix; GUI.matrix = Matrix4x4.identity; bool b = GUI.Button(Px(r), ScTags(t), Sc(s)); GUI.matrix = m; return b;
        }
        void SBox(Rect r, GUIContent c, GUIStyle s)
        {
            if (!Native) { GUI.Box(r, c, s); return; }
            var m = GUI.matrix; GUI.matrix = Matrix4x4.identity; GUI.Box(Px(r), c, Sc(s)); GUI.matrix = m;
        }

        Rect Panel(Rect r, GUIStyle st = null) { SBox(r, GUIContent.none, st ?? panel); uiRects.Add(r); return r; }
        float Label(float x, float y, float w, string t, GUIStyle s) { float h = s.CalcHeight(new GUIContent(t), w); SLabel(new Rect(x, y, w, h), t, s); return h; }

        void OnGUI()
        {
            if (!ready) Init();
            float sc = Mathf.Clamp(Screen.height / 860f, 0.8f, 1.6f); uiScale = sc;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(sc, sc, 1));
            W = Screen.width / sc; H = Screen.height / sc;
            if (Event.current.type == EventType.Layout) uiRects.Clear();
            switch (app.state)
            {
                case AppState.Loading: DrawLoading(); break;
                case AppState.Menu: DrawMenu(); break;
                case AppState.Running: DrawRunning(); break;
                case AppState.Debrief: DrawDebrief(); break;
            }
            if (Event.current.type == EventType.Repaint)
            {
                bool over = false; Vector2 m = Event.current.mousePosition; foreach (var r in uiRects) if (r.Contains(m)) over = true;
                InputState.OverUI = over;
            }
        }

        // ------------------------------------------------------------ loading
        void DrawLoading()
        {
            GUI.color = Color.black; GUI.DrawTexture(new Rect(0, 0, W, H), white); GUI.color = Color.white;
            float ts = Mathf.Min(1f, 1600f / Mathf.Max(1, Screen.width));   // cap the generated size; it is a soft shape anyway
            GUI.DrawTexture(new Rect(0, 0, W, H), ScreenStripes(Mathf.RoundToInt(Screen.width * ts), Mathf.RoundToInt(Screen.height * ts)));
            float cx = W / 2, cy = H / 2 - 60;
            if (icon != null) GUI.DrawTexture(new Rect(cx - 56, cy - 120, 112, 112), icon);
            var tl = new GUIStyle(body); tl.font = reg; tl.fontSize = 22; tl.alignment = TextAnchor.UpperCenter;
            SLabel(new Rect(cx - 300, cy + 12, 600, 34), Tagline, tl);
            var st = new GUIStyle(small); st.alignment = TextAnchor.UpperCenter; SLabel(new Rect(cx - 220, cy + 56, 440, 20), app.loadingText, st);
            GUI.color = new Color(1, 1, 1, 0.12f); GUI.DrawTexture(new Rect(cx - 220, cy + 84, 440, 4), white); GUI.color = Color.white;
            GUI.DrawTexture(new Rect(cx - 220, cy + 84, 440 * app.loadingProgress, 4), gradient);
        }

        // ------------------------------------------------------------ menu
        float menuH;   // content height measured last frame, so the panel hugs its content
        void DrawMenu()
        {
            float pw = 470, x = 28, y = 28;
            var r = Panel(new Rect(x, y, pw, menuH > 0 ? Mathf.Min(menuH, H - 56) : H - 56));
            float cx = x + 22, cw = pw - 44; float cy = y + 22;
            GUI.BeginGroup(r); GUI.color = new Color(1, 1, 1, 0.35f); GUI.DrawTexture(new Rect(pw * 0.35f, -60, pw, pw * 0.375f), stripes); GUI.color = Color.white; GUI.EndGroup();
            Logo(cx, cy, 230); cy += 230 * 195f / 1050f + 16;
            cy += Label(cx, cy, cw, Tagline, h2) + 6;
            cy += Label(cx, cy, cw, "Make your first mistakes on a simulation, not on a patient. Every error is caught, explained and scored.", small) + 18;
            cy += Label(cx, cy, cw, "Operation", h2) + 8;
            cy += Option(cx, cy, cw, "<b>Open inguinal hernia repair</b>\n<size=13>Lichtenstein tension-free mesh, right side</size>", true, true) + 6;
            cy += Option(cx, cy, cw, "Open appendicectomy  <size=12>(planned module)</size>", false, false) + 16;
            cy += Label(cx, cy, cw, "Mode", h2) + 8;
            string[] names = { "Guided", "Training", "Exam" };
            string[] desc = { "Every step explained, targets and the right instrument highlighted.", "Short instructions, errors explained as they happen. H shows a hint (costs 2 points).", "Step titles only. Errors are logged silently and shown in the debrief. Pass: 80 points and no critical error." };
            for (int i = 0; i < 3; i++)
            {
                float oy = cy; float oh = Option(cx, cy, cw, "<b>" + names[i] + "</b>\n<size=13>" + desc[i] + "</size>", (int)app.mode == i, true);
                if (lastOptionClicked) app.mode = (Mode)i;
                cy += oh + 6;
            }
            cy += 8;
            string pat = "<color=#FFFFFF><b>Patient</b></color>  James Harlow, 46. Reducible right inguinal hernia for 8 months, aching on lifting. Day case, general anaesthesia, ASA 1.";
            float ph = small.CalcHeight(new GUIContent(pat), cw - 24) + 22;
            SBox(new Rect(cx, cy, cw, ph), GUIContent.none, panelSoft);
            Label(cx + 12, cy + 11, cw - 24, pat, small);
            cy += ph + 14;
            if (SButton(new Rect(cx, cy, cw, 46), "Start operation", btnPrimary)) app.StartOperation();
            cy += 46 + 22;
            if (Event.current.type == EventType.Repaint) menuH = cy - y;
            DrawControls();
        }

        // a menu option sized to its text (height returned); click state in lastOptionClicked
        bool lastOptionClicked;
        float Option(float x, float y, float w, string text, bool on, bool enabled)
        {
            var st = on ? optOn : opt;
            float h = Mathf.Max(40, st.CalcHeight(new GUIContent(text), w) + 6);
            GUI.enabled = enabled; lastOptionClicked = SButton(new Rect(x, y, w, h), text, st); GUI.enabled = true;
            return h;
        }

        // controls cheat-sheet in its own card, bottom right
        void DrawControls()
        {
            string[,] k = { { "Left click", "use the instrument, or pick one from the tray" }, { "Right-drag / arrows", "look around" }, { "Shift + right-drag", "orbit the operative field" },
                            { "Scroll", "zoom" }, { "V", "close-up of the field" }, { "F", "recentre" }, { "Tab", "label the equipment" }, { "Q", "hand the instrument back" }, { "E", "ask the assistant" }, { "W A S D", "step" } };
            float w = 380, lh = 21, h = 44 + k.GetLength(0) * lh, x = W - w - 28, y = H - h - 28;
            Panel(new Rect(x, y, w, h), panelSoft);
            Label(x + 16, y + 12, w - 32, "Controls", h2);
            var key = new GUIStyle(small); key.normal.textColor = White; key.font = bold;
            for (int i = 0; i < k.GetLength(0); i++)
            {
                float ly = y + 40 + i * lh;
                SLabel(new Rect(x + 16, ly, 140, lh), k[i, 0], key);
                SLabel(new Rect(x + 160, ly, w - 176, lh), k[i, 1], small);
            }
        }

        // ------------------------------------------------------------ running
        void DrawRunning()
        {
            var p = app.procedure; var s = p.Step;
            // step card
            float x = 16, y = 16, w = 410;
            string text = p.mode == Mode.Guided ? s.guided : p.mode == Mode.Training ? s.brief + "  Press H for a hint." : "";
            float th = title.CalcHeight(new GUIContent(s.title), w - 36), bh = string.IsNullOrEmpty(text) ? 0 : body.CalcHeight(new GUIContent(text), w - 36);
            float extra = (s.id == "drape" || s.id == "count") ? 52 : 0;
            if (p.dryLeft > 0) extra += 24;
            var r = Panel(new Rect(x, y, w, 36 + th + bh + 58 + extra));
            float cy = y + 16;
            if (wordmark != null) GUI.DrawTexture(new Rect(x + w - 18 - 86, cy + 1, 86, 16), wordmark, ScaleMode.ScaleToFit);
            Label(x + 18, cy, w - 36, "<color=#2FA3DB>Step " + (p.stepIndex + 1) + " of " + (Procedure.Steps.Length - 1) + "</color>     " + p.mode + " mode", small); cy += 22;
            cy += Label(x + 18, cy, w - 36, s.title, title) + 6;
            if (bh > 0) cy += Label(x + 18, cy, w - 36, text, body) + 8;
            var held = app.view.heldTool;
            Label(x + 18, cy, w - 36, held == Tool.None ? "Hands free. Click an instrument on the tray to the right of the patient." : "In hand: <color=#6ACFFF><b>" + Tools.Get(held).name + "</b></color>   (Q to return it)", small); cy += 24;
            if (p.dryLeft > 0) { int sec = Mathf.CeilToInt(p.dryLeft); Label(x + 18, cy, w - 36, "<color=#6ACFFF>Prep drying: " + sec / 60 + ":" + (sec % 60).ToString("00") + " left before incision or diathermy</color>", small); cy += 24; }
            if (s.id == "drape" && SButton(new Rect(x + 18, cy + 4, 170, 38), "Apply drapes", btnPrimary)) p.ApplyDrapes();
            if (s.id == "count" && SButton(new Rect(x + 18, cy + 4, 170, 38), "Request count", btnPrimary)) p.RequestCount();

            DrawVitals();
            DrawToasts();
            // caption
            if (p.captionT > 0 && !string.IsNullOrEmpty(p.caption))
            {
                float cw2 = Mathf.Min(560, W - 40); float ch = body.CalcHeight(new GUIContent(p.caption), cw2 - 28) + 20;
                var cr = new Rect(W / 2 - cw2 / 2, H - ch - 70, cw2, ch); SBox(cr, GUIContent.none, panelSoft);
                var cs = new GUIStyle(body); cs.alignment = TextAnchor.MiddleCenter; SLabel(new Rect(cr.x + 14, cr.y + 10, cw2 - 28, ch - 20), p.caption, cs);
            }
            // score & controls
            var sr = Panel(new Rect(W - 236, H - 92, 220, 76), panelSoft);
            if (p.mode == Mode.Exam) Label(sr.x + 14, sr.y + 12, 192, "<b>Exam</b>\nScore is revealed at the end.", small);
            else
            {
                Label(sr.x + 14, sr.y + 8, 100, "<size=28><b>" + p.score + "</b></size>", body);
                Label(sr.x + 84, sr.y + 14, 130, p.errors.Count + (p.errors.Count == 1 ? " error" : " errors") + "\n" + Clock(p.time), small);
            }
            if (SButton(new Rect(W - 236, H - 132, 110, 32), app.audioMuted ? "Sound off" : "Sound on", btn)) app.ToggleSound();
            uiRects.Add(new Rect(W - 236, H - 132, 110, 32));
            if (SButton(new Rect(W - 120, H - 132, 104, 32), "Assistant  <size=11>E</size>", p.showAssistant ? btnPrimary : btn)) p.showAssistant = !p.showAssistant;
            uiRects.Add(new Rect(W - 120, H - 132, 104, 32));
            // equipment labels (always in guided mode, Tab in the others), then the hover tooltip
            if ((p.mode == Mode.Guided || InputState.Key(KeyCode.Tab)) && !p.showChecklist && !p.showQuestion) DrawEquipmentLabels();
            DrawHover();
            if (p.showChecklist) DrawChecklist();
            if (p.showQuestion) DrawQuestion();
            if (p.showAssistant) DrawAssistant();
        }

        static string Clock(float t) { int s = (int)t; return (s / 60).ToString("00") + ":" + (s % 60).ToString("00"); }

        void DrawVitals()
        {
            var v = app.vitals; var p = app.procedure;
            float w = 250, x = W - w - 16, y = 16;
            Panel(new Rect(x, y, w, 236));
            var g = new GUIStyle(num); Label(x + 18, y + 12, 100, Mathf.RoundToInt(v.hr).ToString(), g);
            Label(x + 18, y + 48, 100, "HR /min", small);
            // ECG trace
            for (int i = 0; i < ecgPx.Length; i++) ecgPx[i] = new Color32(0, 0, 0, 0);
            int prevY = -1;
            for (int i = 0; i < 256; i++)
            {
                int yy = Mathf.Clamp(Mathf.RoundToInt(18 + v.EcgAt(i) * 30), 0, 55);
                int a = prevY < 0 ? yy : Mathf.Min(prevY, yy), b = prevY < 0 ? yy : Mathf.Max(prevY, yy);
                for (int k = a; k <= b; k++) ecgPx[k * 256 + i] = new Color32(63, 209, 138, (byte)(80 + i * 175 / 256));
                prevY = yy;
            }
            ecgTex.SetPixels32(ecgPx); ecgTex.Apply(false);
            GUI.DrawTexture(new Rect(x + 108, y + 14, 126, 52), ecgTex);
            float ry = y + 76;
            Row(x, ry, w, "SpO2", Mathf.RoundToInt(v.spo2) + " %", Sky); ry += 26;
            Row(x, ry, w, "NIBP", Mathf.RoundToInt(v.sys) + "/" + Mathf.RoundToInt(v.dia), White); ry += 26;
            Row(x, ry, w, "EtCO2", Mathf.RoundToInt(v.etco2) + " mmHg", new Color(0.94f, 0.86f, 0.35f)); ry += 26;
            Row(x, ry, w, "Blood loss", Mathf.RoundToInt(p.ebl) + " ml", p.ebl > 100 ? Arterial : White); ry += 26;
            Row(x, ry, w, "Wound pool", (p.surf != null ? p.surf.pool.ToString("0.0") : "0") + " ml", White); ry += 26;
            Row(x, ry, w, "Time", Clock(p.time), Gray);
        }
        void Row(float x, float y, float w, string k, string v, Color c)
        {
            Label(x + 18, y, 110, k, small); var s = new GUIStyle(h2); s.normal.textColor = c; s.alignment = TextAnchor.UpperRight; SLabel(new Rect(x + 110, y - 2, w - 128, 24), v, s);
        }

        void DrawToasts()
        {
            var p = app.procedure; float y = H - 20; float w = Mathf.Min(430, W * 0.4f);
            for (int i = p.toasts.Count - 1; i >= 0 && i >= p.toasts.Count - 4; i--)
            {
                var t = p.toasts[i];
                float h = h2.CalcHeight(new GUIContent(t.title), w - 40) + body.CalcHeight(new GUIContent(t.body), w - 40) + 30;
                y -= h + 8;
                var r = new Rect(16, y, w, h); var a = Mathf.Clamp01(t.t * 2);
                GUI.color = new Color(1, 1, 1, a); SBox(r, GUIContent.none, panel);
                GUI.color = t.good ? ArtySky : Arterial; GUI.DrawTexture(new Rect(r.x, r.y + 8, 4, h - 16), white); GUI.color = new Color(1, 1, 1, a);
                var ts = new GUIStyle(h2); ts.normal.textColor = t.good ? White : new Color(1f, 0.55f, 0.5f); ts.wordWrap = true;
                float hy = Label(r.x + 20, r.y + 12, w - 40, t.title, ts);
                Label(r.x + 20, r.y + 16 + hy, w - 40, t.body, body);
                GUI.color = Color.white; uiRects.Add(r);
            }
        }

        // floating labels over the theatre equipment, like the infographic callouts
        void DrawEquipmentLabels()
        {
            var cam = app.view != null ? app.view.cam : null; if (cam == null) return;
            var hov = app.procedure.hover.kind == PickKind.Equipment ? app.procedure.hover.equip : null;
            var st = new GUIStyle(small); st.normal.textColor = White; st.alignment = TextAnchor.MiddleCenter; st.wordWrap = false; st.font = bold; st.fontSize = 12;
            foreach (var e in Equipment.All)
            {
                if (e == hov) continue;
                Vector3 sp = cam.WorldToScreenPoint(e.LabelPos); if (sp.z < 0.2f) continue;
                float x = sp.x / uiScale, y = (Screen.height - sp.y) / uiScale;
                if (x < 0 || x > W || y < 0 || y > H) continue;
                float w = st.CalcSize(new GUIContent(e.label)).x + 20; var r = new Rect(x - w / 2, y - 13, w, 26);
                SBox(r, GUIContent.none, panelSoft); GUI.color = ArtySky; GUI.DrawTexture(new Rect(r.x, r.yMax - 2, r.width, 2), white); GUI.color = Color.white;
                SLabel(r, e.label, st);
            }
        }

        void DrawHover()
        {
            var h = app.procedure.hover; string label = null;
            if (h.kind == PickKind.Tray) { var d = Tools.Get(h.tray); label = "<b>" + d.name + "</b>\n<size=12>for " + d.use + "</size>"; }
            else if (h.kind == PickKind.Equipment && h.equip != null) label = "<b>" + h.equip.label + "</b>\n<size=12>click: " + h.equip.action.ToLower() + "</size>";
            else if (app.procedure.mode == Mode.Guided)
            {
                switch (h.kind)
                {
                    case PickKind.Cord: label = "Spermatic cord"; break;
                    case PickKind.Sac: label = "Indirect hernia sac"; break;
                    case PickKind.Nerve: label = "Ilioinguinal nerve"; break;
                    case PickKind.Apo: label = h.th.region == TissueRegion.Surface ? "External oblique aponeurosis" : null; break;
                    case PickKind.Skin: label = h.th.region == TissueRegion.Surface ? null : h.th.region == TissueRegion.Wall ? "Wound edge: dermis over subcutaneous fat" : "Subcutaneous fat (not yet divided)"; break;
                    case PickKind.MeshImplant: label = "Polypropylene mesh"; break;
                    case PickKind.Structure:
                        if (h.structure.Contains("int_oblique")) label = "Internal oblique"; else if (h.structure.Contains("ligament")) label = "Inguinal ligament (shelving edge)";
                        else if (h.structure.Contains("transversus")) label = "Posterior wall (transversalis / transversus)"; else if (h.structure.Contains("epigastric")) label = "Inferior epigastric vessels";
                        else if (h.structure.Contains("rectus")) label = "Rectus sheath"; break;
                }
            }
            if (label == null) return;
            Vector2 m = Event.current.mousePosition; float w = 240; float hh = small.CalcHeight(new GUIContent(label), w - 20) + 16;
            var r = new Rect(m.x + 18, m.y + 14, w, hh); if (r.xMax > W) r.x = m.x - w - 10; if (r.yMax > H) r.y = m.y - hh - 10;
            SBox(r, GUIContent.none, panelSoft); var st = new GUIStyle(small); st.normal.textColor = White; SLabel(new Rect(r.x + 10, r.y + 8, w - 20, hh), label, st);
        }

        void DrawChecklist()
        {
            var p = app.procedure; float w = 560, h = 470; var r = Panel(new Rect(W / 2 - w / 2, H / 2 - h / 2, w, h));
            float x = r.x + 24, y = r.y + 20, cw = w - 48;
            y += Label(x, y, cw, "WHO surgical safety checklist: time-out", title) + 4;
            y += Label(x, y, cw, "Read each item aloud with the team. Confirm only what is true.", small) + 14;
            for (int i = 0; i < p.checklist.Length; i++)
            {
                bool done = p.checks[i];
                string item = i == 4 && !done ? "Antibiotic prophylaxis: <color=#FF8A80>NOT yet given</color> (anaesthetic chart)" : p.checklist[i];
                if (i == 4 && done) item = p.antibioticAsked ? "Antibiotic prophylaxis: cefazolin 2 g IV given now" : "Antibiotic prophylaxis: proceeded without";
                Label(x, y + 8, cw - 210, (done ? "<color=#3FD18A>✓</color>  " : "     ") + item, body);
                if (!done)
                {
                    if (i < 4) { if (SButton(new Rect(x + cw - 120, y, 120, 36), "Confirmed", btn)) p.ConfirmCheck(i, false); }
                    else
                    {
                        if (SButton(new Rect(x + cw - 200, y, 200, 36), "Give cefazolin now", btnPrimary)) p.ConfirmCheck(4, true);
                        if (SButton(new Rect(x + cw - 200, y + 42, 200, 32), "Proceed without", btnDanger)) p.ConfirmCheck(4, false);
                    }
                }
                y += i == 4 ? 84 : 52;
            }
        }

        // instrument picker for the assistant surgeon: names only, the trainee decides what to hand over
        void DrawAssistant()
        {
            var p = app.procedure; var tools = Procedure.AssistantTools;
            int cols = 2, rows = (tools.Length + 1) / 2; float w = 520, bh = 38, h = 116 + rows * (bh + 8);
            var r = Panel(new Rect(W / 2 - w / 2, H / 2 - h / 2, w, h));
            float x = r.x + 22, y = r.y + 18, cw = (w - 44 - 10) / cols;
            y += Label(x, y, w - 44, "Ask the assistant", title) + 2;
            y += Label(x, y, w - 44, "Hand him an instrument. He uses it as a first assistant would; the decisions stay yours.", small) + 12;
            for (int i = 0; i < tools.Length; i++)
            {
                var d = Tools.Get(tools[i]); string name = d != null ? d.name.Split('(')[0].Split(',')[0].Trim() : tools[i].ToString();
                var br = new Rect(x + (i % cols) * (cw + 10), y + (i / cols) * (bh + 8), cw, bh);
                if (SButton(br, name, opt)) p.AskAssistant(tools[i]);
            }
            if (SButton(new Rect(r.xMax - 22 - 110, r.yMax - 18 - 34, 110, 34), "Cancel  <size=11>Esc</size>", btn)) p.showAssistant = false;
        }

        void DrawQuestion()
        {
            var p = app.procedure; float w = 560, h = 330; var r = Panel(new Rect(W / 2 - w / 2, H / 2 - h / 2, w, h));
            float x = r.x + 24, y = r.y + 20, cw = w - 48;
            y += Label(x, y, cw, "Anatomy question", title) + 6;
            y += Label(x, y, cw, "The sac lies inside the cord, antero-medially, and comes out of the deep ring lateral to the inferior epigastric vessels. What kind of hernia is this?", body) + 16;
            string[] a = { "Indirect inguinal hernia", "Direct inguinal hernia", "Femoral hernia" };
            for (int i = 0; i < 3; i++) { if (SButton(new Rect(x, y, cw, 44), a[i], opt)) p.AnswerQuestion(i); y += 52; }
        }

        // ------------------------------------------------------------ debrief
        void DrawDebrief()
        {
            var p = app.procedure; float w = Mathf.Min(720, W - 40), h = Mathf.Min(640, H - 40);
            var r = Panel(new Rect(W / 2 - w / 2, H / 2 - h / 2, w, h));
            float x = r.x + 26, y = r.y + 22, cw = w - 52;
            y += Label(x, y, cw, "Debrief: open inguinal hernia repair", title) + 8;
            Label(x, y, 140, "<size=54><b>" + p.score + "</b></size>", body);
            bool pass = p.Passed;
            var cs = new GUIStyle(chip); cs.normal.background = pass ? chipPass : chipFail;
            SLabel(new Rect(x + 140, y + 12, 120, 26), pass ? "Passed" : "Not passed", cs);
            Label(x + 140, y + 44, cw - 140, p.mode + " mode.  Time " + Clock(p.time) + ".  Blood loss " + Mathf.RoundToInt(p.ebl) + " ml.  Swabs used " + p.swabsUsed + ".  Hints " + p.hintsUsed + ".", small);
            y += 84;
            int crit = 0; foreach (var e in p.errors) if (e.critical) crit++;
            y += Label(x, y, cw, p.errors.Count == 0 ? "No errors. Clean operation." : p.errors.Count + " errors, " + crit + " critical. Each one explained:", h2) + 8;
            float listH = h - (y - r.y) - 80;
            float inner = 0; foreach (var e in p.errors) inner += body.CalcHeight(new GUIContent(e.why), cw - 40) + 56;
            inScroll = true;
            debriefScroll = GUI.BeginScrollView(new Rect(x, y, cw, listH), debriefScroll, new Rect(0, 0, cw - 20, Mathf.Max(listH, inner)));
            float ly = 0;
            foreach (var e in p.errors)
            {
                float eh = body.CalcHeight(new GUIContent(e.why), cw - 40) + 48;
                SBox(new Rect(0, ly, cw - 20, eh), GUIContent.none, panelSoft);
                GUI.color = e.critical ? Arterial : ArtySky; GUI.DrawTexture(new Rect(0, ly + 8, 4, eh - 16), white); GUI.color = Color.white;
                Label(14, ly + 8, cw - 60, "<b>" + e.title + "</b>  <color=#8C8C8C><size=12>" + e.step + ", " + Clock(e.time) + ", −" + e.penalty + (e.critical ? ", critical" : "") + "</size></color>", body);
                Label(14, ly + 32, cw - 60, e.why, small);
                ly += eh + 8;
            }
            GUI.EndScrollView(); inScroll = false;
            float by = r.yMax - 64;
            if (SButton(new Rect(x, by, 200, 44), "Repeat operation", btnPrimary)) app.Restart(true);
            if (SButton(new Rect(x + 214, by, 160, 44), "Main menu", btn)) app.Restart(false);
        }
    }
}
