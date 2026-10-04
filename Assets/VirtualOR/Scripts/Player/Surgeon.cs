// Virtual OR - instruments and the first-person surgeon (camera, gloved animated hands, held instrument).
using System.Collections.Generic;
using UnityEngine;

namespace VirtualOR
{
    public enum Tool { None, PrepSponge, Scalpel, Cautery, Forceps, Scissors, Hemostat, Retractor, Swab, Penrose, Mesh, NeedleHolder }

    public class ToolDef
    {
        public Tool tool; public string name; public string use; public Vector3 trayPos; public float trayYaw; public float gripDist;
        public ToolDef(Tool t, string n, string u, Vector3 p, float yaw, float grip) { tool = t; name = n; use = u; trayPos = p; trayYaw = yaw; gripDist = grip; }
    }

    public static class Tools
    {
        public static readonly ToolDef[] All = {
            new ToolDef(Tool.PrepSponge, "Prep sponge (chlorhexidine 2% in 70% alcohol)", "skin antisepsis before draping", new Vector3(0.20f, 0.02f, 0.14f), 90, 0.17f),
            new ToolDef(Tool.Scalpel, "Scalpel, No. 10 blade", "the skin incision", new Vector3(0.20f, 0.02f, 0.06f), 90, 0.10f),
            new ToolDef(Tool.Cautery, "Diathermy pencil", "dissection through fat and haemostasis", new Vector3(0.20f, 0.02f, -0.02f), 90, 0.09f),
            new ToolDef(Tool.Forceps, "Toothed dissecting forceps", "grasping and handling tissue", new Vector3(0.20f, 0.02f, -0.10f), 90, 0.09f),
            new ToolDef(Tool.Scissors, "Metzenbaum scissors", "opening the external oblique aponeurosis", new Vector3(0.06f, 0.02f, 0.13f), 90, 0.10f),
            new ToolDef(Tool.Hemostat, "Artery forceps", "clamping vessels and holding tissue edges", new Vector3(0.06f, 0.02f, 0.03f), 90, 0.09f),
            new ToolDef(Tool.NeedleHolder, "Needle holder with 2-0 polypropylene", "suturing the mesh and closing", new Vector3(0.06f, 0.02f, -0.08f), 90, 0.10f),
            new ToolDef(Tool.Retractor, "Self-retaining retractor", "holding the wound open", new Vector3(-0.08f, 0.02f, 0.12f), 0, 0.06f),
            new ToolDef(Tool.Swab, "Gauze swab", "mopping blood to see the field", new Vector3(-0.08f, 0.02f, 0.02f), 0, 0.03f),
            new ToolDef(Tool.Penrose, "Penrose drain", "encircling and lifting the spermatic cord", new Vector3(-0.08f, 0.02f, -0.07f), 0, 0.03f),
            new ToolDef(Tool.Mesh, "Polypropylene mesh 7.5 x 15 cm", "tension-free reinforcement of the posterior wall", new Vector3(-0.19f, 0.02f, 0.06f), 0, 0.03f),
        };
        public static ToolDef Get(Tool t) { foreach (var d in All) if (d.tool == t) return d; return null; }
    }

    public class InstrumentKit
    {
        public readonly Dictionary<Tool, GameObject> trayItems = new Dictionary<Tool, GameObject>();
        public readonly Dictionary<Tool, GameObject> held = new Dictionary<Tool, GameObject>();
        VorModel source;
        Material steel, steelDark;

        public void Build(Transform tray, Transform heldParent)
        {
            steel = Mats.Std(new Color(0.70f, 0.72f, 0.74f), 0.62f, 1f); steelDark = Mats.Std(new Color(0.46f, 0.48f, 0.50f), 0.5f, 1f);   // satin (brushed) stainless
            source = VorLoader.Load("instruments", heldParent);
            source.root.SetActive(false);
            foreach (var m in source.materials) { Mats.SetColor(m, new Color(0.68f, 0.70f, 0.72f)); Mats.SetSurface(m, 0.6f, 1f); }
            foreach (var d in Tools.All)
            {
                var onTray = Make(d.tool); onTray.name = "tray_" + d.tool; onTray.transform.SetParent(tray, false);
                onTray.transform.localPosition = d.trayPos; onTray.transform.localRotation = Quaternion.Euler(0, d.trayYaw, 0);
                var col = onTray.AddComponent<BoxCollider>(); FitCollider(onTray, col);
                onTray.AddComponent<TrayItem>().tool = d.tool;
                trayItems[d.tool] = onTray;
                var h = Make(d.tool); h.name = "held_" + d.tool; h.transform.SetParent(heldParent, false); h.SetActive(false);
                held[d.tool] = h;
            }
        }

        static void FitCollider(GameObject g, BoxCollider c)
        {
            var b = new Bounds(); bool first = true;
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                var lb = r.bounds; var mn = g.transform.InverseTransformPoint(lb.min); var mx = g.transform.InverseTransformPoint(lb.max);
                if (first) { b = new Bounds((mn + mx) / 2, Vector3.zero); first = false; }
                b.Encapsulate(mn); b.Encapsulate(mx);
            }
            c.center = b.center; c.size = b.size + Vector3.one * 0.012f;
        }

        // Instrument object: local +Z points to the working tip, origin at the tip.
        GameObject Make(Tool t)
        {
            var g = new GameObject(t.ToString());
            switch (t)
            {
                case Tool.Scalpel:
                    {
                        var h = Real("scalpel", g.transform, 0.075f, -0.038f);
                        var blade = new GameObject("blade"); blade.transform.SetParent(g.transform, false);
                        U.AddMesh(blade, BladeMesh(), Mats.Std(new Color(0.80f, 0.82f, 0.85f), 0.78f, 1f));
                        if (h == null) Cyl(g.transform, new Vector3(0, 0, -0.1f), 0.006f, 0.12f, steel);
                        break;
                    }
                case Tool.Forceps: if (Real("forceps", g.transform, 0.075f, 0) == null) Cyl(g.transform, new Vector3(0, 0, -0.07f), 0.004f, 0.14f, steel); break;
                case Tool.Scissors: if (Real("scissors", g.transform, 0.075f, 0) == null) Cyl(g.transform, new Vector3(0, 0, -0.07f), 0.004f, 0.14f, steel); break;
                case Tool.Hemostat: if (Real("hemostat", g.transform, 0.075f, 0) == null) Cyl(g.transform, new Vector3(0, 0, -0.07f), 0.004f, 0.14f, steel); break;
                case Tool.NeedleHolder:
                    {
                        if (Real("needle_holder", g.transform, 0.075f, 0) == null) Cyl(g.transform, new Vector3(0, 0, -0.07f), 0.004f, 0.14f, steel);
                        var n = Real("needle", g.transform, 0.045f, 0.004f); if (n != null) n.transform.localRotation *= Quaternion.Euler(0, 0, 90);
                        var thread = new GameObject("suture"); thread.transform.SetParent(g.transform, false);
                        var pts = new List<Vector3>(); for (int i = 0; i < 16; i++) pts.Add(new Vector3(Mathf.Sin(i * 0.4f) * 0.01f, -0.003f - i * 0.006f, -0.01f - i * 0.004f));
                        U.AddMesh(thread, U.Tube(pts, k => 0.00035f, 5), Mats.Std(new Color(0.12f, 0.18f, 0.55f), 0.4f));
                        break;
                    }
                case Tool.Cautery:
                    {
                        Cyl(g.transform, new Vector3(0, 0, -0.075f), 0.0065f, 0.13f, Mats.Std(new Color(0.86f, 0.88f, 0.90f), 0.3f));
                        var b1 = ORWorld.Prim(PrimitiveType.Cube, g.transform, new Vector3(0, 0, -0.006f), new Vector3(0.0025f, 0.0008f, 0.014f), steel);
                        ORWorld.Prim(PrimitiveType.Cube, g.transform, new Vector3(0, 0.0068f, -0.055f), new Vector3(0.004f, 0.002f, 0.012f), Mats.Std(new Color(0.90f, 0.74f, 0.12f), 0.3f));
                        ORWorld.Prim(PrimitiveType.Cube, g.transform, new Vector3(0, 0.0068f, -0.072f), new Vector3(0.004f, 0.002f, 0.012f), Mats.Std(new Color(0.20f, 0.35f, 0.78f), 0.3f));
                        var cable = new List<Vector3>(); for (int i = 0; i < 12; i++) cable.Add(new Vector3(0, -i * 0.004f, -0.14f - i * 0.02f));
                        var cg = new GameObject("cable"); cg.transform.SetParent(g.transform, false); U.AddMesh(cg, U.Tube(cable, k => 0.0025f, 6), Mats.Std(new Color(0.85f, 0.85f, 0.85f), 0.2f));
                        b1.name = "electrode";
                        break;
                    }
                case Tool.PrepSponge:
                    {
                        Cyl(g.transform, new Vector3(0, 0, -0.13f), 0.004f, 0.22f, Mats.Std(new Color(0.90f, 0.90f, 0.90f), 0.35f));
                        ORWorld.Prim(PrimitiveType.Cube, g.transform, new Vector3(0, 0.004f, -0.012f), new Vector3(0.03f, 0.016f, 0.03f), Mats.Std(new Color(0.85f, 0.45f, 0.15f), 0.35f));
                        break;
                    }
                case Tool.Retractor:
                    {
                        ORWorld.Prim(PrimitiveType.Cube, g.transform, new Vector3(0, 0.02f, 0), new Vector3(0.09f, 0.004f, 0.006f), steel);
                        for (int s = -1; s <= 1; s += 2)
                        {
                            ORWorld.Prim(PrimitiveType.Cube, g.transform, new Vector3(s * 0.035f, 0.004f, 0), new Vector3(0.004f, 0.034f, 0.006f), steel);
                            for (int k = -1; k <= 1; k++) ORWorld.Prim(PrimitiveType.Cube, g.transform, new Vector3(s * 0.031f, -0.012f, k * 0.005f), new Vector3(0.008f, 0.002f, 0.0015f), steelDark);
                        }
                        break;
                    }
                case Tool.Swab:
                    ORWorld.Prim(PrimitiveType.Cube, g.transform, new Vector3(0, 0.002f, -0.03f), new Vector3(0.05f, 0.006f, 0.06f), Mats.Matte(new Color(0.96f, 0.96f, 0.95f), ORWorld.DrapeTex()));
                    break;
                case Tool.Penrose:
                    {
                        var pts = new List<Vector3>(); for (int i = 0; i < 40; i++) { float a = i * 0.45f; pts.Add(new Vector3(Mathf.Cos(a) * 0.018f, i * 0.0004f, Mathf.Sin(a) * 0.018f - 0.02f)); }
                        var pg = new GameObject("drain"); pg.transform.SetParent(g.transform, false); U.AddMesh(pg, U.Tube(pts, k => 0.0025f, 8), Mats.Std(new Color(0.92f, 0.80f, 0.50f), 0.42f));
                        break;
                    }
                case Tool.Mesh:
                    ORWorld.Prim(PrimitiveType.Cube, g.transform, new Vector3(0, 0.002f, -0.04f), new Vector3(0.075f, 0.003f, 0.075f), Mats.Std(new Color(0.92f, 0.94f, 0.96f), 0.25f, 0, TissueTex.MeshGrid()));
                    break;
            }
            return g;
        }

        void Cyl(Transform p, Vector3 pos, float r, float len, Material m)
        {
            var c = ORWorld.Prim(PrimitiveType.Cylinder, p, pos, new Vector3(r * 2, len / 2, r * 2), m);
            c.transform.localRotation = Quaternion.Euler(90, 0, 0);
        }

        // Re-centre a real instrument mesh: long axis -> +Z, narrow (working) end at the origin.
        GameObject Real(string name, Transform parent, float scale, float tipOffset)
        {
            MeshFilter src = null; foreach (var f in source.filters) if (f.gameObject.name == name) src = f;
            if (src == null) return null;
            var v = src.sharedMesh.vertices; Vector3 mn = v[0], mx = v[0];
            foreach (var p in v) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            Vector3 size = mx - mn; bool alongX = size.x > size.z; float len = alongX ? size.x : size.z;
            // width at each end
            float w0 = 0, w1 = 0; int c0 = 0, c1 = 0; float mid = 0;
            foreach (var p in v)
            {
                float a = alongX ? p.x : p.z, o = alongX ? p.z : p.x; float lo = alongX ? mn.x : mn.z;
                float t = (a - lo) / len;
                if (t < 0.12f) { w0 += Mathf.Abs(o - (alongX ? (mn.z + mx.z) / 2 : (mn.x + mx.x) / 2)); c0++; }
                if (t > 0.88f) { w1 += Mathf.Abs(o - (alongX ? (mn.z + mx.z) / 2 : (mn.x + mx.x) / 2)); c1++; }
                mid += o;
            }
            w0 /= Mathf.Max(1, c0); w1 /= Mathf.Max(1, c1);
            bool tipAtMax = w1 < w0;
            Vector3 tip = (mn + mx) / 2; if (alongX) tip.x = tipAtMax ? mx.x : mn.x; else tip.z = tipAtMax ? mx.z : mn.z; tip.y = (mn.y + mx.y) / 2;
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            var inner = new GameObject("mesh"); inner.transform.SetParent(g.transform, false);
            inner.AddComponent<MeshFilter>().sharedMesh = src.sharedMesh; inner.AddComponent<MeshRenderer>().sharedMaterial = steel;
            // rotation that maps the instrument axis (pointing to the tip) onto +Z
            Vector3 axis = alongX ? (tipAtMax ? Vector3.right : Vector3.left) : (tipAtMax ? Vector3.forward : Vector3.back);
            Quaternion q = Quaternion.FromToRotation(axis, Vector3.forward);
            inner.transform.localRotation = q; inner.transform.localScale = Vector3.one * scale;
            inner.transform.localPosition = -(q * (tip * scale)) + new Vector3(0, 0, tipOffset);
            return g;
        }

        static Mesh BladeMesh()
        {
            // No. 10 blade: curved cutting edge, ~40 mm, lying in the Y-Z plane, tip at origin
            var pts = new List<Vector3>(); var m = new Mesh();
            var outline = new List<Vector2>();
            for (int i = 0; i <= 12; i++) { float t = i / 12f; outline.Add(new Vector2(-0.0045f - Mathf.Sin(t * Mathf.PI * 0.5f) * 0.0035f, -t * 0.03f)); }   // belly (edge)
            outline.Add(new Vector2(-0.006f, -0.04f)); outline.Add(new Vector2(0.002f, -0.04f)); outline.Add(new Vector2(0.002f, -0.012f)); outline.Add(new Vector2(0f, 0f));
            var v = new List<Vector3>(); var t2 = new List<int>();
            v.Add(new Vector3(0, -0.002f, -0.02f));
            foreach (var p in outline) v.Add(new Vector3(0.0003f, p.x, p.y));
            for (int i = 1; i < v.Count - 1; i++) { t2.Add(0); t2.Add(i); t2.Add(i + 1); t2.Add(0); t2.Add(i + 1); t2.Add(i); }
            m.vertices = v.ToArray(); m.triangles = t2.ToArray(); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        public bool OnTray(Tool t) { GameObject g; return trayItems.TryGetValue(t, out g) && g.activeSelf; }
        public Vector3 TrayPos(Tool t) { GameObject g; return trayItems.TryGetValue(t, out g) ? g.transform.position : Vector3.zero; }
        public void ShowOnTray(Tool t, bool on) { GameObject g; if (trayItems.TryGetValue(t, out g)) g.SetActive(on); }
    }

    public class TrayItem : MonoBehaviour { public Tool tool; }

    public class SurgeonView
    {
        public Camera cam; public Transform rig;
        public float yaw, pitch; public Vector3 pos; public float fov = HomeFov; const float HomeFov = 45f; bool closeUp;
        public Vector3 home; Vector3 lookAtHome;
        VorModel hands; Transform handsAnchor; Transform gripR;
        string clip = "Idle"; float clipT; string nextClip; bool holding;
        public Tool heldTool = Tool.None; public GameObject heldGO;
        public Vector3? aimPoint; public bool pressing;
        public float toolLift = 0.012f;
        Vector3 handOffset, handOffsetVel;

        public void Build(Transform parent, Vector3 eye, Vector3 lookAt)
        {
            rig = U.Child(parent, "surgeon_view").transform;
            var cg = U.Child(rig, "Main Camera"); cg.tag = "MainCamera";
            cam = cg.AddComponent<Camera>(); cam.nearClipPlane = 0.01f; cam.farClipPlane = 40f; cam.fieldOfView = fov; cam.allowHDR = true; cam.allowMSAA = true;
            cg.AddComponent<AudioListener>();
            home = eye; lookAtHome = lookAt; pos = eye; LookAt(lookAt);
            handsAnchor = U.Child(cam.transform, "hands_anchor").transform; handsAnchor.localPosition = new Vector3(0, -1.62f, 0);
            hands = VorLoader.Load("hands", handsAnchor);
            if (hands != null && hands.skinned != null)
            {
                var mats = hands.skinned.sharedMaterials;
                if (mats.Length > 0) { Mats.SetColor(mats[0], ORWorld.Latex); mats[0].mainTexture = null; Mats.SetSurface(mats[0], 0.4f); }  // latex surgical gloves (no skin texture: no nails/veins)
                if (mats.Length > 1) { Mats.SetColor(mats[1], ORWorld.GownColor); mats[1].mainTexture = null; Mats.MakeMatte(mats[1]); }   // plain gown fabric, not the skin texture // gown sleeves
                foreach (var b in hands.bones) { if (b.name.Contains("f_index03R") || b.name.Contains("f_index.03.R")) gripR = b; }
                if (gripR == null) foreach (var b in hands.bones) if (b.name.Contains("handR")) gripR = b;
                hands.skinned.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        public void ShowHands(bool on) { if (hands != null && hands.skinned != null) hands.skinned.enabled = on; }

        public void LookAt(Vector3 p)
        {
            Vector3 d = (p - pos).normalized; yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; pitch = -Mathf.Asin(d.y) * Mathf.Rad2Deg;
        }
        public void Home() { pos = home; LookAt(lookAtHome); fov = HomeFov; closeUp = false; }

        // orbit the eye around the operative field on a sphere, staying on the surgeon's side and above the patient
        void Orbit(Vector2 d)
        {
            Vector3 o = pos - lookAtHome; float dist = o.magnitude;
            float az = Mathf.Atan2(o.x, o.z) * Mathf.Rad2Deg + d.x * 0.25f;
            float el = Mathf.Asin(Mathf.Clamp(o.y / dist, -1, 1)) * Mathf.Rad2Deg + d.y * 0.25f;
            az = Mathf.Clamp(az, 25f, 155f); el = Mathf.Clamp(el, 30f, 86f);
            float c = Mathf.Cos(el * Mathf.Deg2Rad);
            pos = lookAtHome + new Vector3(Mathf.Sin(az * Mathf.Deg2Rad) * c, Mathf.Sin(el * Mathf.Deg2Rad), Mathf.Cos(az * Mathf.Deg2Rad) * c) * dist;
            LookAt(lookAtHome);
        }

        public void Update(float dt, float groinZ)
        {
            // orbit the field: middle drag or Shift/Alt + right drag;  V: close-up over the field
            if (InputState.OrbitDelta != Vector2.zero) Orbit(InputState.OrbitDelta);
            if (InputState.KeyDown(KeyCode.V))
            {
                if (closeUp) Home();
                else { closeUp = true; pos = lookAtHome + new Vector3(0.20f, 0.52f, -0.03f); LookAt(lookAtHome); fov = 34f; }
            }
            // look: right mouse drag or arrow keys
            yaw += InputState.LookDelta.x * 0.18f; pitch += InputState.LookDelta.y * 0.18f;
            if (InputState.Key(KeyCode.LeftArrow)) yaw -= 60 * dt; if (InputState.Key(KeyCode.RightArrow)) yaw += 60 * dt;
            if (InputState.Key(KeyCode.UpArrow)) pitch -= 45 * dt; if (InputState.Key(KeyCode.DownArrow)) pitch += 45 * dt;
            pitch = Mathf.Clamp(pitch, -30, 80);
            // move: WASD in a small standing area beside the table
            Vector3 f = Quaternion.Euler(0, yaw, 0) * Vector3.forward, r = Quaternion.Euler(0, yaw, 0) * Vector3.right; Vector3 mv = Vector3.zero;
            if (InputState.Key(KeyCode.W)) mv += f; if (InputState.Key(KeyCode.S)) mv -= f; if (InputState.Key(KeyCode.D)) mv += r; if (InputState.Key(KeyCode.A)) mv -= r;
            if (mv != Vector3.zero)
            {
                pos += mv * dt * 0.6f;
                pos.x = Mathf.Clamp(pos.x, 0.30f, 1.1f); pos.z = Mathf.Clamp(pos.z, groinZ - 0.7f, groinZ + 0.8f);
            }
            // lean in (loupes) with scroll / +/-
            fov = Mathf.Clamp(fov + InputState.Scroll * 1.6f, 20f, 60f);
            if (InputState.Key(KeyCode.Equals) || InputState.Key(KeyCode.KeypadPlus)) fov = Mathf.Max(20, fov - 30 * dt);
            if (InputState.Key(KeyCode.Minus) || InputState.Key(KeyCode.KeypadMinus)) fov = Mathf.Min(60, fov + 30 * dt);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, dt * 8);
            rig.position = pos; cam.transform.localPosition = Vector3.zero; cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            AnimateHands(dt);
        }

        void AnimateHands(float dt)
        {
            if (hands == null || hands.bones == null) return;
            clipT += dt;
            if (clip == "Take_Start" && clipT > 0.7f) { clip = "Take_Loop"; clipT = 0; }
            if (clip == "Take_Stop" && clipT > 1.5f) { clip = "Idle"; clipT = 0; }
            VorLoader.SampleClip(hands, clip, clipT, clip == "Idle" || clip == "Take_Loop");
            // move hands so the right hand sits at the instrument handle
            Vector3 target = Vector3.zero;
            if (heldGO != null && heldGO.activeSelf && gripR != null)
            {
                var d = Tools.Get(heldTool);
                Vector3 grip = heldGO.transform.position - heldGO.transform.forward * (d != null ? d.gripDist : 0.08f);
                Vector3 cur = gripR.position - handsAnchor.TransformVector(handOffset);
                target = handsAnchor.InverseTransformVector(grip - cur);
                target = Vector3.ClampMagnitude(target, 0.38f);
            }
            handOffset = Vector3.SmoothDamp(handOffset, target, ref handOffsetVel, 0.08f);
            handsAnchor.localPosition = new Vector3(0, -1.62f, 0) + handOffset;
        }

        public void Take(Tool t, GameObject heldObj)
        {
            if (heldGO != null) heldGO.SetActive(false);
            heldTool = t; heldGO = heldObj; if (heldGO != null) heldGO.SetActive(t != Tool.None);
            clip = t == Tool.None ? "Take_Stop" : "Take_Start"; clipT = 0;
        }

        // Place the held instrument: tip on the aim point (slightly lifted unless pressing), pointing away from the hand.
        public void PoseTool(float dt)
        {
            if (heldGO == null || !heldGO.activeSelf) return;
            Transform t = heldGO.transform;
            Vector3 shoulder = cam.transform.TransformPoint(new Vector3(0.10f, -0.30f, 0.05f));
            Vector3 tip;
            if (aimPoint.HasValue)
            {
                Vector3 n = (cam.transform.position - aimPoint.Value).normalized;
                tip = aimPoint.Value + n * (pressing ? 0.0005f : toolLift);
            }
            else tip = cam.transform.TransformPoint(new Vector3(0.06f, -0.12f, 0.42f));
            Vector3 dir = (tip - shoulder).normalized;
            Quaternion want = Quaternion.LookRotation(dir, cam.transform.up);
            if (heldTool == Tool.Scalpel) want = want * Quaternion.Euler(0, 0, 0);
            t.position = Vector3.Lerp(t.position, tip, Mathf.Min(1, dt * 22));
            t.rotation = Quaternion.Slerp(t.rotation, want, Mathf.Min(1, dt * 14));
        }

        public Ray MouseRay() { return cam.ScreenPointToRay(new Vector3(InputState.Mouse.x, InputState.Mouse.y, 0)); }
    }
}
