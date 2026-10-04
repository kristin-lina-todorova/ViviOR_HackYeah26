// Virtual OR - labelled, clickable theatre equipment: anaesthesia machine + vitals monitor, ventilator, anaesthesia cart,
// theatre lamp, case computer, diathermy generator, operating table and the sterile instrument tray.
// Built from primitives in the scene palette (white bodies, teal accents); screens show live data via TextMesh + LineRenderer.
using System.Collections.Generic;
using UnityEngine;

namespace VirtualOR
{
    public class Equipment : MonoBehaviour
    {
        public static readonly List<Equipment> All = new List<Equipment>();
        public string label, action;          // action: what a click does (shown on hover)
        public Vector3 labelOffset;           // world-space offset of the floating label from the root
        public System.Action onClick;
        public readonly List<Material> glow = new List<Material>();
        bool hovered;

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }
        public Vector3 LabelPos { get { return transform.position + labelOffset; } }
        public void SetHover(bool on)
        {
            if (on == hovered) return; hovered = on;
            foreach (var m in glow) Mats.SetEmission(m, on ? new Color(0.08f, 0.30f, 0.34f) : Color.black);
        }
    }

    public class EquipmentRoom
    {
        static readonly Color White = new Color(0.93f, 0.95f, 0.95f), Teal = new Color(0.16f, 0.60f, 0.64f), Dark = new Color(0.12f, 0.14f, 0.16f);
        static readonly Color Green = new Color(0.25f, 0.95f, 0.55f), Cyan = new Color(0.35f, 0.85f, 1f), Yellow = new Color(1f, 0.88f, 0.30f), Red = new Color(1f, 0.30f, 0.25f), Grey = new Color(0.62f, 0.68f, 0.72f);
        Font font; Material teal, dark, steel, screenMat;
        Vector3 eye;
        ORWorld world;
        // live screen elements
        TextMesh hr, spo2, nibp, etco2, alarm, ventTxt, diaCoag, caseStep, caseNext, caseFoot, caseClock;
        LineRenderer ecgLine, plethLine, pawLine;
        readonly float[] pleth = new float[64], paw = new float[64]; int ring; float beatAge = 1, sample, textT, alarmT;
        int lampLevel = 1; static readonly float[] LampIntensity = { 1.4f, 2.0f, 2.6f }; static readonly string[] LampName = { "low", "medium", "high" };

        public void Build(ORWorld w, Vector3 eyePos)
        {
            world = w; eye = eyePos;
            font = Resources.Load<Font>("VirtualOR/Fonts/Inter-SemiBold");
            teal = Mats.Std(Teal, 0.3f); dark = Mats.Std(Dark, 0.3f); steel = Mats.Std(new Color(0.70f, 0.72f, 0.74f), 0.55f, 1f);
            screenMat = Mats.Unlit(U.MakeTex(4, 4, (u, v) => new Color(0.02f, 0.05f, 0.08f), false), Color.white);
            var root = w.root;
            AnaesthesiaMachine(root, new Vector3(-0.60f, 0, -1.70f));
            Ventilator(root, new Vector3(0.28f, 0, -1.80f));
            AnaesthesiaCart(root, new Vector3(-1.12f, 0, -1.38f));
            CaseComputer(root, new Vector3(-0.98f, 0, -0.80f));
            Diathermy(root, new Vector3(-0.86f, 0, 0.30f));
            Lamp(root);
            Table(w);
            Tray(w);
        }

        // ------------------------------------------------------------ helpers
        Equipment Device(Transform parent, string name, Vector3 pos, string label, string action, float labelY)
        {
            var g = U.Child(parent, name); g.transform.position = pos;
            Vector3 f = eye - pos; f.y = 0; g.transform.rotation = Quaternion.LookRotation(f.normalized);   // front (+z) faces the surgeon
            var e = g.AddComponent<Equipment>(); e.label = label; e.action = action; e.labelOffset = Vector3.up * labelY;
            return e;
        }
        Material Body(Equipment e) { var m = Mats.Std(White, 0.35f); e.glow.Add(m); return m; }
        static GameObject Box(Transform p, Vector3 pos, Vector3 size, Material m) { var g = ORWorld.Prim(PrimitiveType.Cube, p, pos, size, m); g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; return g; }
        static GameObject Cyl(Transform p, Vector3 pos, float r, float h, Material m) { return ORWorld.Prim(PrimitiveType.Cylinder, p, pos, new Vector3(r * 2, h * 0.5f, r * 2), m); }
        void Wheels(Transform p, float w, float d)
        {
            foreach (var sx in new[] { -1, 1 }) foreach (var sz in new[] { -1, 1 })
                    ORWorld.Prim(PrimitiveType.Sphere, p, new Vector3(sx * (w * 0.5f - 0.04f), 0.035f, sz * (d * 0.5f - 0.04f)), Vector3.one * 0.07f, dark);
        }
        static void Collider(Equipment e)
        {
            var b = new Bounds(); bool first = true;
            foreach (var r in e.GetComponentsInChildren<MeshRenderer>()) { if (r.GetComponent<TextMesh>() != null) continue; if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            var c = e.gameObject.AddComponent<BoxCollider>();
            c.center = e.transform.InverseTransformPoint(b.center);
            var s = e.transform.InverseTransformVector(b.size); c.size = new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        }
        // a screen: dark quad facing the surgeon's eye; returns its transform (local XY = screen plane, origin at centre)
        Transform Screen(Transform parent, Vector3 localPos, float w, float h)
        {
            var g = U.Child(parent, "screen"); g.transform.localPosition = localPos;
            g.transform.rotation = Quaternion.LookRotation(g.transform.position - eye);   // quad faces -Z: towards the eye
            var bezel = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.Destroy(bezel.GetComponent<Collider>()); bezel.transform.SetParent(g.transform, false);
            bezel.transform.localPosition = new Vector3(0, 0, 0.018f); bezel.transform.localScale = new Vector3(w + 0.03f, h + 0.03f, 0.03f); bezel.GetComponent<Renderer>().sharedMaterial = dark;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(q.GetComponent<Collider>()); q.transform.SetParent(g.transform, false);
            q.transform.localScale = new Vector3(w, h, 1); q.GetComponent<Renderer>().sharedMaterial = screenMat;
            return g.transform;
        }
        TextMesh Text(Transform scr, Vector3 pos, string s, float height, Color c, TextAnchor anchor = TextAnchor.UpperLeft)
        {
            var g = U.Child(scr, "text"); g.transform.localPosition = pos + new Vector3(0, 0, -0.002f);
            var t = g.AddComponent<TextMesh>(); t.font = font; t.fontSize = 64; t.characterSize = height * 10f / 64f; t.anchor = anchor; t.color = c; t.text = s;
            g.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return t;
        }
        LineRenderer Trace(Transform scr, Color c, int n)
        {
            var g = U.Child(scr, "trace"); g.transform.localPosition = new Vector3(0, 0, -0.002f);
            var l = g.AddComponent<LineRenderer>(); l.useWorldSpace = false; l.positionCount = n; l.widthMultiplier = 0.0035f;
            l.material = Mats.Particle(null); l.startColor = l.endColor = c; l.numCornerVertices = 0; l.alignment = LineAlignment.TransformZ;
            return l;
        }
        App app { get { return App.Instance; } }
        void Say(string who, string line) { if (app != null && app.procedure != null) app.procedure.Say(who, line); }

        // ------------------------------------------------------------ devices
        void AnaesthesiaMachine(Transform root, Vector3 pos)
        {
            var e = Device(root, "anaesthesia_machine", pos, "Anaesthesia machine", "Ask the anaesthetist how the patient is", 1.95f); var t = e.transform; var body = Body(e);
            Box(t, new Vector3(0, 0.07f, 0), new Vector3(0.66f, 0.08f, 0.56f), dark); Wheels(t, 0.66f, 0.56f);
            Box(t, new Vector3(0, 0.58f, 0), new Vector3(0.62f, 0.96f, 0.52f), body);
            Box(t, new Vector3(0, 0.80f, 0.005f), new Vector3(0.63f, 0.035f, 0.525f), teal);
            Box(t, new Vector3(0, 1.075f, 0.02f), new Vector3(0.68f, 0.03f, 0.58f), body);                     // worktop
            Box(t, new Vector3(0, 1.30f, -0.18f), new Vector3(0.62f, 0.44f, 0.16f), body);                     // gas module
            Box(t, new Vector3(0, 1.30f, -0.098f), new Vector3(0.56f, 0.36f, 0.005f), dark);                   // front panel
            Cyl(t, new Vector3(-0.17f, 1.36f, -0.09f), 0.035f, 0.02f, teal).transform.localRotation = Quaternion.Euler(90, 0, 0);
            Cyl(t, new Vector3(-0.07f, 1.36f, -0.09f), 0.035f, 0.02f, teal).transform.localRotation = Quaternion.Euler(90, 0, 0);
            Box(t, new Vector3(0.14f, 1.30f, -0.07f), new Vector3(0.08f, 0.16f, 0.07f), Mats.Std(new Color(0.95f, 0.78f, 0.15f), 0.3f));   // sevoflurane vaporiser
            Cyl(t, new Vector3(0.24f, 1.30f, -0.07f), 0.03f, 0.16f, Mats.Std(new Color(0.85f, 0.88f, 0.95f), 0.4f));                         // CO2 absorber
            Cyl(t, new Vector3(0.24f, 1.62f, -0.12f), 0.012f, 0.40f, steel);                                     // monitor pole
            var mon = Screen(t, new Vector3(0.24f, 1.72f, -0.08f), 0.46f, 0.30f);
            ecgLine = Trace(mon, Green, 64); plethLine = Trace(mon, Cyan, 64);
            Text(mon, new Vector3(-0.215f, 0.135f, 0), "II", 0.022f, Grey);
            Text(mon, new Vector3(0.10f, 0.135f, 0), "HR", 0.022f, Green);
            hr = Text(mon, new Vector3(0.215f, 0.135f, 0), "72", 0.075f, Green, TextAnchor.UpperRight);
            Text(mon, new Vector3(0.10f, 0.03f, 0), "SpO2", 0.022f, Cyan);
            spo2 = Text(mon, new Vector3(0.215f, 0.03f, 0), "99", 0.06f, Cyan, TextAnchor.UpperRight);
            Text(mon, new Vector3(-0.215f, -0.065f, 0), "NIBP", 0.02f, Grey);
            nibp = Text(mon, new Vector3(-0.215f, -0.088f, 0), "124/78", 0.045f, Color.white);
            Text(mon, new Vector3(0.10f, -0.065f, 0), "EtCO2", 0.02f, Yellow);
            etco2 = Text(mon, new Vector3(0.215f, -0.088f, 0), "36", 0.045f, Yellow, TextAnchor.UpperRight);
            alarm = Text(mon, new Vector3(-0.215f, 0.105f, 0), "", 0.024f, Red);
            Collider(e);
            e.onClick = () =>
            {
                var v = app.vitals; float ebl = app.procedure.ebl;
                if (v.sys < 105 || v.hr > 95) Say("Anaesthetist", "He's getting tachycardic, BP " + Mathf.RoundToInt(v.sys) + "/" + Mathf.RoundToInt(v.dia) + ". About " + Mathf.RoundToInt(ebl) + " ml down. Control the bleeding, I'll give fluid from the cart.");
                else Say("Anaesthetist", "Stable. Heart rate " + Mathf.RoundToInt(v.hr) + ", BP " + Mathf.RoundToInt(v.sys) + "/" + Mathf.RoundToInt(v.dia) + ", sats " + Mathf.RoundToInt(v.spo2) + "%. Carry on.");
            };
        }

        void Ventilator(Transform root, Vector3 pos)
        {
            var e = Device(root, "ventilator", pos, "Ventilator", "Check the ventilation settings", 1.70f); var t = e.transform; var body = Body(e);
            Box(t, new Vector3(0, 0.06f, 0), new Vector3(0.50f, 0.06f, 0.46f), dark); Wheels(t, 0.50f, 0.46f);
            Box(t, new Vector3(0, 0.52f, 0), new Vector3(0.42f, 0.86f, 0.40f), body);
            Box(t, new Vector3(0, 0.70f, 0.005f), new Vector3(0.43f, 0.03f, 0.405f), teal);
            Box(t, new Vector3(0, 0.45f, 0.202f), new Vector3(0.30f, 0.30f, 0.005f), Mats.Std(new Color(0.85f, 0.88f, 0.90f), 0.35f));   // bellows window
            Cyl(t, new Vector3(-0.12f, 0.30f, 0.23f), 0.02f, 0.06f, teal).transform.localRotation = Quaternion.Euler(90, 0, 0);
            Cyl(t, new Vector3(0.12f, 0.30f, 0.23f), 0.02f, 0.06f, Mats.Std(new Color(0.3f, 0.5f, 0.9f), 0.3f)).transform.localRotation = Quaternion.Euler(90, 0, 0);
            var scr = Screen(t, new Vector3(0, 1.18f, 0.05f), 0.32f, 0.22f);
            pawLine = Trace(scr, Yellow, 64);
            Text(scr, new Vector3(-0.15f, 0.10f, 0), "VCV", 0.024f, Teal + new Color(0.3f, 0.3f, 0.3f));
            ventTxt = Text(scr, new Vector3(-0.15f, -0.035f, 0), "", 0.02f, Color.white);
            Collider(e);
            e.onClick = () => { var v = app.vitals; Say("Anaesthetist", "Volume control, rate " + Mathf.RoundToInt(v.rr) + ", tidal volume 500, PEEP 5, FiO2 50%. EtCO2 " + Mathf.RoundToInt(v.etco2) + "."); };
        }

        float lastFluid = -99;
        void AnaesthesiaCart(Transform root, Vector3 pos)
        {
            var e = Device(root, "anaesthesia_cart", pos, "Anaesthesia cart", "Emergency drugs and IV fluid", 1.12f); var t = e.transform; var body = Body(e);
            Box(t, new Vector3(0, 0.06f, 0), new Vector3(0.58f, 0.05f, 0.48f), dark); Wheels(t, 0.58f, 0.48f);
            Box(t, new Vector3(0, 0.50f, 0), new Vector3(0.56f, 0.82f, 0.46f), body);
            Box(t, new Vector3(0, 0.925f, 0), new Vector3(0.60f, 0.03f, 0.50f), teal);
            Color[] tags = { new Color(0.85f, 0.2f, 0.2f), new Color(0.95f, 0.75f, 0.2f), new Color(0.3f, 0.6f, 0.9f), Teal, Teal };
            for (int i = 0; i < 5; i++)
            {
                float y = 0.80f - i * 0.15f;
                Box(t, new Vector3(0, y, 0.232f), new Vector3(0.52f, 0.13f, 0.006f), Mats.Std(new Color(0.88f, 0.91f, 0.92f), 0.35f));
                Box(t, new Vector3(0, y, 0.24f), new Vector3(0.20f, 0.018f, 0.012f), Mats.Std(tags[i], 0.3f));
            }
            Cyl(t, new Vector3(0.20f, 1.25f, -0.15f), 0.008f, 0.64f, steel);                                     // drip stand + fluid bag
            Box(t, new Vector3(0.20f, 1.50f, -0.15f), new Vector3(0.09f, 0.16f, 0.03f), Mats.Fade(new Color(0.85f, 0.92f, 0.95f, 0.6f), 0.5f));
            Collider(e);
            e.onClick = () =>
            {
                var v = app.vitals;
                if (Time.time - lastFluid < 20) { Say("Anaesthetist", "Bolus is still running."); return; }
                if (v.sys < 110 || v.hr > 90 || app.procedure.ebl > 120) { lastFluid = Time.time; v.fluid += 500; Say("Anaesthetist", "Giving 500 ml Hartmann's stat. Find the bleeder."); }
                else Say("Anaesthetist", "Adrenaline, atropine, suxamethonium and airway kit are in the cart. Nothing needed right now.");
            };
        }

        void CaseComputer(Transform root, Vector3 pos)
        {
            var e = Device(root, "case_computer", pos, "Case computer", "Read the patient and case details", 1.95f); var t = e.transform;
            var body = Body(e);
            foreach (var a in new[] { 0f, 90f }) Box(t, new Vector3(0, 0.03f, 0), new Vector3(0.60f, 0.03f, 0.06f), dark).transform.localRotation = Quaternion.Euler(0, a, 0);
            Wheels(t, 0.58f, 0.58f);
            Cyl(t, new Vector3(0, 0.70f, 0), 0.02f, 1.30f, steel);
            Box(t, new Vector3(0, 1.00f, 0), new Vector3(0.36f, 0.04f, 0.26f), body);                             // keyboard shelf
            Box(t, new Vector3(0, 1.025f, 0.03f), new Vector3(0.32f, 0.01f, 0.12f), dark);
            var scr = Screen(t, new Vector3(0, 1.58f, 0.04f), 0.62f, 0.38f);
            Text(scr, new Vector3(-0.29f, 0.17f, 0), "Open inguinal hernia repair (Lichtenstein), right", 0.022f, Teal + new Color(0.35f, 0.35f, 0.35f));
            caseClock = Text(scr, new Vector3(0.29f, 0.17f, 0), "", 0.022f, Grey, TextAnchor.UpperRight);
            caseStep = Text(scr, new Vector3(-0.29f, 0.125f, 0), "", 0.04f, Color.white);
            caseNext = Text(scr, new Vector3(-0.29f, 0.055f, 0), "", 0.024f, Grey);
            caseFoot = Text(scr, new Vector3(-0.29f, -0.11f, 0), "", 0.021f, Grey);
            Collider(e);
            e.onClick = () =>
            {
                var p = app.procedure;
                Say("Case notes", "James Harlow, 46, ASA 1, no allergies. Right inguinal hernia, consented and marked. Antibiotic " + (p.checks[4] ? (p.antibioticAsked ? "given." : "not given.") : "not yet confirmed."));
            };
        }

        void Diathermy(Transform root, Vector3 pos)
        {
            var e = Device(root, "diathermy", pos, "Diathermy generator", "Change the coagulation power", 1.15f); var t = e.transform; var body = Body(e);
            Box(t, new Vector3(0, 0.06f, 0), new Vector3(0.50f, 0.05f, 0.42f), dark); Wheels(t, 0.50f, 0.42f);
            Box(t, new Vector3(0, 0.42f, 0), new Vector3(0.46f, 0.66f, 0.40f), body);
            Box(t, new Vector3(0, 0.77f, 0), new Vector3(0.50f, 0.03f, 0.44f), teal);
            Box(t, new Vector3(0, 0.86f, -0.01f), new Vector3(0.44f, 0.15f, 0.36f), Mats.Std(new Color(0.86f, 0.90f, 0.92f), 0.35f));   // generator
            var g = U.Child(t, "panel"); g.transform.localPosition = new Vector3(0, 0.86f, 0.175f); g.transform.localRotation = Quaternion.Euler(0, 180, 0);   // quad/text face -Z -> towards +z (front)
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.Destroy(q.GetComponent<Collider>()); q.transform.SetParent(g.transform, false); q.transform.localScale = new Vector3(0.40f, 0.11f, 1); q.GetComponent<Renderer>().sharedMaterial = screenMat;
            Text(g.transform, new Vector3(-0.19f, 0.045f, 0), "CUT", 0.018f, Yellow);
            Text(g.transform, new Vector3(-0.19f, 0.02f, 0), "30 W", 0.04f, Yellow);
            Text(g.transform, new Vector3(0.02f, 0.045f, 0), "COAG", 0.018f, Cyan);
            diaCoag = Text(g.transform, new Vector3(0.02f, 0.02f, 0), "35 W", 0.04f, Cyan);
            Collider(e);
            e.onClick = () =>
            {
                var p = app.procedure; p.coagW = p.coagW >= 45 ? 25 : p.coagW + 10;
                Say("Assistant", "Coag set to " + p.coagW + " watts." + (p.coagW >= 45 ? " That's high, watch the skin edges." : p.coagW <= 25 ? " Low, it will take longer to seal." : ""));
            };
        }

        void Lamp(Transform root)
        {
            var L = world.lamp; if (L == null) return;
            var e = U.Child(root, "theatre_lamp").AddComponent<Equipment>(); e.label = "Operating room lamp"; e.action = "Change the brightness"; e.labelOffset = Vector3.up * 0.30f;
            e.transform.position = L.transform.position; e.transform.rotation = L.transform.rotation;   // +z points at the field
            var t = e.transform; var body = Body(e);
            var head = Cyl(t, new Vector3(0, 0, -0.06f), 0.30f, 0.07f, body); head.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var bulb = Mats.Std(Color.white, 0.6f); Mats.SetEmission(bulb, new Color(1f, 0.98f, 0.92f) * 1.6f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3; var b = Cyl(t, new Vector3(Mathf.Cos(a) * 0.17f, Mathf.Sin(a) * 0.17f, -0.022f), 0.065f, 0.01f, bulb); b.transform.localRotation = Quaternion.Euler(90, 0, 0);
            }
            Cyl(t, new Vector3(0, 0, 0.0f), 0.03f, 0.08f, teal).transform.localRotation = Quaternion.Euler(90, 0, 0);   // sterile handle
            foreach (var r in t.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // arm up to the ceiling
            Vector3 top = new Vector3(t.position.x - 0.4f, 3.0f, t.position.z + 0.2f), elbow = new Vector3(top.x, t.position.y + 0.35f, top.z);
            var arm = U.Child(root, "lamp_arm").transform;
            Link(arm, top + Vector3.up * 0.2f, elbow, 0.025f); Link(arm, elbow, t.position - t.forward * 0.10f, 0.02f);
            Collider(e);
            e.onClick = () =>
            {
                lampLevel = (lampLevel + 1) % LampIntensity.Length; L.intensity = LampIntensity[lampLevel]; L.transform.LookAt(world.GroinWorld);
                Say("Assistant", "Light " + LampName[lampLevel] + ", focused on the field.");
            };
        }
        void Link(Transform p, Vector3 a, Vector3 b, float r)
        {
            var g = Cyl(p, (a + b) * 0.5f, r, (b - a).magnitude, Mats.Std(White, 0.35f));
            g.transform.position = (a + b) * 0.5f; g.transform.up = (b - a).normalized; g.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Table(ORWorld w)
        {
            var g = U.Child(w.root, "table_pick"); g.transform.position = new Vector3(0, 0.40f, w.GroinWorld.z + 0.10f);
            var c = g.AddComponent<BoxCollider>(); c.size = new Vector3(0.50f, 0.78f, 1.80f);
            var e = g.AddComponent<Equipment>(); e.label = "Operating table"; e.action = "Locked, height set for you"; e.labelOffset = new Vector3(0.35f, 0.15f, 0.55f);
            e.onClick = () => Say("Assistant", "Table's locked, brakes on. Height is right for you.");
        }

        void Tray(ORWorld w)
        {
            var g = U.Child(w.tray, "tray_pick");
            var c = g.AddComponent<BoxCollider>(); c.size = new Vector3(0.56f, 0.012f, 0.40f);
            var e = g.AddComponent<Equipment>(); e.label = "Sterile instruments"; e.action = "Click an instrument to take it"; e.labelOffset = new Vector3(0, 0.16f, 0);
            e.onClick = () => Say("Assistant", "Click the instrument you need on the tray.");
        }

        // ------------------------------------------------------------ live screens
        public void Tick(float dt, Vitals v, Procedure p)
        {
            // waveforms
            if (v.beat) beatAge = 0; beatAge += dt;
            sample += dt;
            while (sample > 0.05f)
            {
                sample -= 0.05f; ring = (ring + 1) % 64;
                pleth[ring] = Mathf.Exp(-Mathf.Pow((beatAge - 0.28f) / 0.11f, 2)) + 0.35f * Mathf.Exp(-Mathf.Pow((beatAge - 0.52f) / 0.08f, 2));
                paw[ring] = Mathf.Max(0, v.breath);
            }
            if (ecgLine != null)
                for (int i = 0; i < 64; i++)
                {
                    float x = -0.215f + i / 63f * 0.30f;
                    ecgLine.SetPosition(i, new Vector3(x, 0.065f + v.EcgAt(i * 4) * 0.05f, 0));
                    plethLine.SetPosition(i, new Vector3(x, -0.035f + pleth[(ring + 1 + i) % 64] * 0.035f, 0));
                }
            if (pawLine != null)
                for (int i = 0; i < 64; i++) pawLine.SetPosition(i, new Vector3(-0.15f + i / 63f * 0.30f, 0.0f + paw[(ring + 1 + i) % 64] * 0.06f, 0));
            // numbers at 4 Hz
            textT -= dt; alarmT += dt;
            bool alarmOn = v.hr > 100 || v.sys < 100 || v.spo2 < 94;
            if (alarm != null) alarm.text = alarmOn && (alarmT % 1f) < 0.6f ? (v.hr > 100 ? "! TACHYCARDIA" : v.sys < 100 ? "! HYPOTENSION" : "! DESATURATION") : "";
            if (textT > 0) return; textT = 0.25f;
            if (hr != null) { hr.text = Mathf.RoundToInt(v.hr).ToString(); hr.color = v.hr > 100 ? Red : Green; spo2.text = Mathf.RoundToInt(v.spo2).ToString(); nibp.text = Mathf.RoundToInt(v.sys) + "/" + Mathf.RoundToInt(v.dia); nibp.color = v.sys < 100 ? Red : Color.white; etco2.text = Mathf.RoundToInt(v.etco2).ToString(); }
            if (ventTxt != null) ventTxt.text = "RR " + Mathf.RoundToInt(v.rr) + "    VT 500\nPEEP 5   FiO2 50%";
            if (diaCoag != null) diaCoag.text = p.coagW + " W";
            if (caseStep != null)
            {
                int n = Procedure.Steps.Length - 1, k = Mathf.Clamp(p.stepIndex, 0, n - 1);
                caseStep.text = p.running || p.finished ? "Step " + (k + 1) + "/" + n + "  " + p.Step.title : "Awaiting start";
                caseNext.text = k + 1 < n ? "Next: " + Procedure.Steps[k + 1].title : "Last step";
                int s = (int)p.time; caseClock.text = (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
                caseFoot.text = "James Harlow, 46  ·  ASA 1  ·  No known allergies\nBlood loss " + Mathf.RoundToInt(p.ebl) + " ml  ·  Antibiotic " + (p.checks[4] ? (p.antibioticAsked ? "given" : "not given") : "pending");
            }
        }
    }
}
