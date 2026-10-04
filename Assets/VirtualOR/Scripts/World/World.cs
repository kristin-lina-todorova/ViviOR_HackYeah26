// Virtual OR - the operating room: photogrammetry scan, table, patient, drapes, Mayo tray, lights and team.
using System.Collections.Generic;
using UnityEngine;

namespace VirtualOR
{
    public class ORWorld
    {
        // Operative window in patient-local coordinates (x = patient's right, y = towards head, z = anterior).
        public static readonly Rect Window = new Rect(-0.035f, 0.80f, 0.20f, 0.1875f);
        public static readonly Vector2 Groin = new Vector2(0.066f, 0.885f);

        public Transform root, patient, table, tray;
        public MeshCollider patientCollider; public List<MeshCollider> tableColliders = new List<MeshCollider>();
        Mesh bodyMesh; Vector3[] bodyBase, bodyWork; int[] breathIdx; float[] breathChest, breathBelly; int[] bodyTris;
        Transform drip; float dripT;
        public GameObject drapes, cape; BreathingCover capeBreath, drapeBreath; Mesh drapeMesh; Vector3[] drapeFinal, drapeStart; float drapeT = -1f; public bool draped;
        public Light lamp;
        public Transform assistantT; Vector3 assistantBaseScale, assistantBasePos;
        public float assistantLean; float leanNow;   // 0..1: assistant leans in over the table while working
        public Vector3 GroinWorld;
        public float skinZAtGroin;
        public Color skinTone = new Color(0.80f, 0.60f, 0.50f);   // sampled from the patient texture around the window

        // Built in stages (each yield = one frame of the loading screen); the string is the next stage's caption.
        public IEnumerable<string> Build(Transform parent)
        {
            root = U.Child(parent, "OR").transform;
            // ---- room (photo-scanned Charité OR; unlit, baked lighting)
            var room = VorLoader.Load("room", root);
            if (room != null)
            {
                foreach (var r in room.root.GetComponentsInChildren<Renderer>()) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }   // unlit scan: keep it out of the lamp's shadow map
                TintRoom(room, RoomTint, FloorTint);
            }
            yield return "Positioning the patient on the table";
            // ---- operating table (model's top is rotated and off-centre: align with world Z)
            var tg = U.Child(root, "table").transform; table = tg;
            var tm = VorLoader.Load("table", tg);
            tm.root.transform.localPosition = new Vector3(1.185f, 0, 1.139f);
            tg.localScale = Vector3.one * 0.22f; tg.localRotation = Quaternion.Euler(0, -29.16f, 0); tg.localPosition = new Vector3(0, 0, -0.31f);
            foreach (var m in tm.materials) { Mats.SetSurface(m, 0.3f, 0.25f); Mats.SetColor(m, new Color(0.80f, 0.94f, 0.94f)); }   // padded table + brushed steel, not chrome
            foreach (var mf in tm.filters) { mf.gameObject.layer = 2; var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; tableColliders.Add(mc); }
            // ---- patient (supine, head towards -Z, right side towards +X where the surgeon stands)
            patient = U.Child(root, "patient").transform;
            patient.localPosition = new Vector3(0, 0.975f, 0.50f); patient.localRotation = Quaternion.Euler(-90, 0, 0);
            var pm = VorLoader.Load("patient", patient);
            if (pm.materials.Length > 0) Mats.SetSurface(pm.materials[0], 0.28f);   // skin: soft sheen, not plastic
            MeshFilter body = pm.filters[0]; foreach (var f in pm.filters) if (f.sharedMesh.vertexCount > body.sharedMesh.vertexCount) body = f;
            var colGO = U.Child(patient, "patient_collider"); colGO.layer = 2; patientCollider = colGO.AddComponent<MeshCollider>(); patientCollider.sharedMesh = body.sharedMesh;
            bodyMesh = Object.Instantiate(body.sharedMesh); bodyMesh.MarkDynamic(); body.sharedMesh = bodyMesh;
            bodyBase = bodyMesh.vertices; bodyWork = (Vector3[])bodyBase.Clone();
            bodyTris = body.sharedMesh.GetTriangles(0);
            SampleSkinTone(body);
            CutPatient(false);
            // breathing weights
            var idx = new List<int>(); var wc = new List<float>(); var wb = new List<float>();
            for (int i = 0; i < bodyBase.Length; i++)
            {
                float chest, belly; BreathWeights(bodyBase[i], out chest, out belly);
                if (chest > 0 || belly > 0) { idx.Add(i); wc.Add(chest); wb.Add(belly); }
            }
            breathIdx = idx.ToArray(); breathChest = wc.ToArray(); breathBelly = wb.ToArray();
            Physics.SyncTransforms();
            // skin height at groin
            RaycastHit h;
            Vector3 o = patient.TransformPoint(new Vector3(Groin.x, Groin.y, 0.5f));
            if (patientCollider.Raycast(new Ray(o, patient.TransformDirection(Vector3.back)), out h, 1f)) skinZAtGroin = patient.InverseTransformPoint(h.point).z; else skinZAtGroin = 0.1f;
            GroinWorld = patient.TransformPoint(new Vector3(Groin.x, Groin.y, skinZAtGroin));
            yield return "Covering the patient";
            // ---- anaesthesia screen
            screen = BuildEtherScreen(patient.localPosition.z - 1.40f); screen.SetActive(false);   // put up during draping
            // ---- cape over the patient, only the operative window exposed
            BuildCape();
            yield return "Connecting the anaesthetic circuit";
            // ---- anaesthetised patient: airway + breathing circuit, IV drip, theatre cap
            BuildPatientLines();
            // ---- Mayo tray over the legs
            BuildTray();
            // ---- lights
            RoomReflections();
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.78f, 0.80f, 0.84f); RenderSettings.ambientEquatorColor = new Color(0.55f, 0.55f, 0.54f); RenderSettings.ambientGroundColor = new Color(0.30f, 0.29f, 0.27f);
            var dl = U.Child(root, "fill").AddComponent<Light>(); dl.type = LightType.Directional; dl.intensity = 0.4f; dl.color = new Color(1f, 0.97f, 0.92f);
            dl.transform.rotation = Quaternion.Euler(55, 150, 0); dl.shadows = LightShadows.None;
            lamp = U.Child(root, "surgical_light").AddComponent<Light>(); lamp.type = LightType.Spot; lamp.range = 4f; lamp.spotAngle = 42f; lamp.intensity = 2.0f; lamp.color = new Color(1f, 0.985f, 0.95f);
            lamp.shadows = LightShadows.Soft; lamp.shadowBias = 0.002f; lamp.shadowNormalBias = 0.2f; lamp.shadowResolution = UnityEngine.Rendering.LightShadowResolution.High;
            lamp.transform.position = GroinWorld + new Vector3(0.20f, 1.35f, 0.05f); lamp.transform.LookAt(GroinWorld);
            yield return "Scrubbing the team in";
            // ---- team
            BuildTeam();
        }

        GameObject screen;

        void CutPatient(bool armsTucked)
        {
            var keep = new List<int>(bodyTris.Length);
            for (int t = 0; t < bodyTris.Length; t += 3)
            {
                Vector3 c = (bodyBase[bodyTris[t]] + bodyBase[bodyTris[t + 1]] + bodyBase[bodyTris[t + 2]]) / 3f;
                bool inWindow = c.x > Window.xMin && c.x < Window.xMax && c.y > Window.yMin && c.y < Window.yMax && c.z > -0.02f;
                bool arm = armsTucked && Mathf.Abs(c.x) > 0.245f && c.y < 1.47f;
                if (inWindow || arm) continue;
                keep.Add(bodyTris[t]); keep.Add(bodyTris[t + 1]); keep.Add(bodyTris[t + 2]);
            }
            bodyMesh.SetTriangles(keep.ToArray(), 0, true);
        }

        // average patient-texture colour on the skin around the operative window, so the simulated skin matches the body
        void SampleSkinTone(MeshFilter body)
        {
            var tex = body.GetComponent<Renderer>().sharedMaterial.mainTexture as Texture2D; var uv = bodyMesh.uv;
            if (tex == null || !tex.isReadable || uv == null || uv.Length != bodyBase.Length) return;
            Color sum = Color.black; int n = 0; var near = new Rect(Window.x - 0.05f, Window.y - 0.05f, Window.width + 0.10f, Window.height + 0.10f);
            for (int i = 0; i < bodyBase.Length; i++)
            {
                var p = bodyBase[i]; if (!near.Contains(new Vector2(p.x, p.y)) || p.z < 0) continue;
                var c = tex.GetPixelBilinear(uv[i].x, uv[i].y);
                if (c.r < 0.3f || c.r - c.b < 0.06f) continue;   // skin only (skip underwear, hair, shadows)
                sum += c; n++;
            }
            if (n > 20) { skinTone = sum / n; skinTone.a = 1; }
        }

        // Anaesthesia ("ether") screen: two poles clamped to the table rails at the shoulders, a crossbar, and a drape
        // hanging from it as a wall between the anaesthetist's area (head, airway) and the sterile field. Below the bar
        // the sheet drops until it meets the patient / table, then lies forward over the chest; past the table it hangs down.
        GameObject BuildEtherScreen(float zLine)
        {
            var g = U.Child(root, "anaesthesia_screen");
            const float bar = 1.57f, poleX = 0.43f, x0 = -0.80f, x1 = 0.80f, len = 1.25f;
            var steel = Mats.Std(new Color(0.72f, 0.74f, 0.76f), 0.5f, 1f);
            foreach (var sx in new[] { -1f, 1f })
            {
                Prim(PrimitiveType.Cylinder, g.transform, new Vector3(sx * poleX, (0.80f + bar) * 0.5f, zLine), new Vector3(0.022f, (bar - 0.80f) * 0.5f, 0.022f), steel);
                Prim(PrimitiveType.Cube, g.transform, new Vector3(sx * poleX, 0.82f, zLine), new Vector3(0.05f, 0.06f, 0.05f), steel);   // rail clamp
            }
            var cb = Prim(PrimitiveType.Cylinder, g.transform, new Vector3(0, bar, zLine), new Vector3(0.018f, poleX + 0.02f, 0.018f), steel); cb.transform.localRotation = Quaternion.Euler(0, 0, 90);
            // surface under the screen line: patient / table height (+ drape and breathing clearance), floor-ish past the table
            float Surface(float x)
            {
                float h = 0.55f; var ray = new Ray(new Vector3(x, 2.5f, zLine + 0.02f), Vector3.down); RaycastHit hit;
                if (Mathf.Abs(x) < 0.36f)
                {
                    if (patientCollider.Raycast(ray, out hit, 3f)) h = Mathf.Max(h, hit.point.y + 0.035f);
                    foreach (var tc in tableColliders) if (tc.Raycast(ray, out hit, 3f)) h = Mathf.Max(h, hit.point.y + 0.03f);
                }
                return h;
            }
            int nx = 64, nv = 50; float du = (x1 - x0) / nx, dv = len / nv;
            var surf = new float[nx + 1]; for (int i = 0; i <= nx; i++) surf[i] = Surface(x0 + i * du);
            for (int pass = 0; pass < 4; pass++) for (int i = 1; i < nx; i++) surf[i] = Mathf.Max(surf[i], (surf[i - 1] + surf[i + 1]) * 0.5f - 0.004f);   // fabric bridges small dips
            var v = new List<Vector3>(); var uv = new List<Vector2>();
            for (int j = 0; j <= nv + 2; j++) for (int i = 0; i <= nx; i++)
                {
                    float x = x0 + i * du, ax = Mathf.Abs(x);
                    float top = ax <= poleX ? bar : bar - (ax - poleX) * 1.6f;                         // falls off the bar ends
                    Vector3 p;
                    if (j < 2) p = new Vector3(x, top - 0.035f + j * 0.03f, zLine - 0.045f + j * 0.03f);   // flap folded back over the bar
                    else
                    {
                        float drop = top - surf[i], d = Mathf.Min((j - 2) * dv, drop + 0.12f);   // only ~12 cm lies on the chest; never reaches the field
                        float fold = Mathf.Sin(x * 23f + d * 3f) * 0.012f * Mathf.Clamp01(d / 0.3f) + Mathf.Sin(x * 61f) * 0.003f;
                        if (d <= drop) p = new Vector3(x, top - d, zLine + 0.012f + fold);
                        else p = new Vector3(x + fold * 0.3f, surf[i] + Mathf.Sin(x * 17f) * 0.004f, zLine + 0.012f + fold + (d - drop));   // lies forward over the chest
                    }
                    v.Add(p); uv.Add(new Vector2(i * du * 6f, j * dv * 6f));
                }
            int n = v.Count, row = nx + 1; var tri = new List<int>();
            for (int j = 0; j < nv + 2; j++) for (int i = 0; i < nx; i++)
                {
                    int a = j * row + i, b = a + 1, c = a + row, d = c + 1;
                    tri.Add(a); tri.Add(b); tri.Add(c); tri.Add(b); tri.Add(d); tri.Add(c);
                }
            // double-sided: the anaesthetist sees the back
            var vv = new List<Vector3>(v); vv.AddRange(v); var uu = new List<Vector2>(uv); uu.AddRange(uv);
            int tc0 = tri.Count; for (int k = 0; k < tc0; k += 3) { tri.Add(tri[k] + n); tri.Add(tri[k + 2] + n); tri.Add(tri[k + 1] + n); }
            var mesh = new Mesh(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vv); mesh.SetUVs(0, uu); mesh.SetTriangles(tri, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            var sheet = U.Child(g.transform, "screen_drape");
            sheet.AddComponent<MeshFilter>().sharedMesh = mesh;
            sheet.AddComponent<MeshRenderer>().sharedMaterial = Mats.Matte(DrapeBlue, DrapeTex());
            return g;
        }

        // ------------------------------------------------------------ anaesthetised patient
        // Anaesthesia machine common-gas port, matches EquipmentRoom's machine at (-0.60, 0, -1.70) facing the surgeon.
        static readonly Vector3 CircuitPort = new Vector3(-0.44f, 1.02f, -1.47f);

        void BuildPatientLines()
        {
            // face landmarks from the body mesh (patient-local: +z anterior, +y towards the head)
            Vector3 nose = new Vector3(0, 1.62f, 0.10f); float best = -1;
            for (int i = 0; i < bodyBase.Length; i++) { var p = bodyBase[i]; if (Mathf.Abs(p.x) < 0.025f && p.y > 1.45f && p.y < 1.85f && p.z > best) { best = p.z; nose = p; } }
            Vector3 mouthL = new Vector3(0, nose.y - 0.040f, nose.z - 0.016f);
            Vector3 W(Vector3 pl) { return root.InverseTransformPoint(patient.TransformPoint(pl)); }
            Vector3 up = root.InverseTransformDirection(patient.TransformDirection(Vector3.forward)), headward = root.InverseTransformDirection(patient.TransformDirection(Vector3.up));
            var g = U.Child(root, "anaesthesia_lines").transform;
            Material clear = Mats.Std(new Color(0.84f, 0.92f, 0.95f), 0.5f), white = Mats.Std(new Color(0.93f, 0.95f, 0.96f), 0.35f), blue = Mats.Std(new Color(0.30f, 0.55f, 0.85f), 0.35f);
            // endotracheal tube out of the mouth, catheter mount, HME filter, Y-piece
            Vector3 m = W(mouthL), a = m + up * 0.07f, b = a + headward * 0.10f + up * 0.02f, f0 = b + headward * 0.02f, f1 = f0 + headward * 0.05f, y = f1 + headward * 0.03f;
            Tube(g, new[] { m - up * 0.01f, m + up * 0.03f, a, a + headward * 0.03f, b }, 0.0045f, clear, 8);
            Cyl(g, (f0 + f1) * 0.5f, f1 - f0, 0.022f, white);                                                     // HME filter
            Cyl(g, (b + f0) * 0.5f, f0 - b, 0.009f, blue);                                                        // catheter mount
            Cyl(g, (f1 + y) * 0.5f, y - f1, 0.012f, clear);                                                       // Y-piece
            // inspiratory / expiratory limbs (corrugated) to the machine
            Vector3 side = Vector3.Cross(headward, up).normalized;
            foreach (var sgn in new[] { -1f, 1f })
            {
                Vector3 s0 = y + side * sgn * 0.018f, port = CircuitPort + side * sgn * 0.03f;
                Vector3 mid = Vector3.Lerp(s0, port, 0.5f) + Vector3.down * 0.25f;   // sags between patient and machine
                var pts = Spline(new[] { s0, s0 + headward * 0.12f, mid, port + Vector3.up * 0.10f, port }, 24);
                var tg = U.Child(g, "circuit_limb"); U.AddMesh(tg, U.Tube(pts, i => 0.011f + 0.0016f * Mathf.Sin(i * 2.6f), 10), white);
            }
            // IV pole with two bags (Hartmann's, saline), drip chamber and a line to a cannula in the left hand
            Vector3 pole = new Vector3(-0.62f, 0, -1.28f);   // anaesthetist side, clear of the case computer and the screen pole
            Cyl(g, pole + Vector3.up * 0.95f, Vector3.up * 1.90f, 0.012f, Mats.Std(new Color(0.72f, 0.74f, 0.76f), 0.5f, 1f));
            for (int k = 0; k < 5; k++) { float an = k * Mathf.PI * 0.4f; var leg = new Vector3(Mathf.Cos(an), 0, Mathf.Sin(an)) * 0.26f; Cyl(g, pole + leg * 0.5f + Vector3.up * 0.04f, leg, 0.01f, clear); }
            Cyl(g, pole + Vector3.up * 1.88f, Vector3.right * 0.30f, 0.008f, Mats.Std(new Color(0.72f, 0.74f, 0.76f), 0.5f, 1f));
            var bag = Mats.Fade(new Color(0.88f, 0.94f, 0.97f, 0.55f), 0.6f);
            for (int k = 0; k < 2; k++)
            {
                Vector3 hook = pole + new Vector3(k == 0 ? -0.12f : 0.12f, 1.86f, 0);
                var bg = Prim(PrimitiveType.Cube, g, hook + Vector3.down * 0.12f, new Vector3(0.11f, 0.20f, 0.035f), bag);
                Prim(PrimitiveType.Cube, g, hook + Vector3.down * 0.10f + Vector3.forward * 0.019f, new Vector3(0.07f, 0.05f, 0.002f), Mats.Std(k == 0 ? new Color(0.95f, 0.95f, 0.95f) : new Color(0.75f, 0.85f, 0.95f), 0.3f));
            }
            Vector3 chamber = pole + new Vector3(-0.12f, 1.62f, 0);
            Cyl(g, chamber, Vector3.up * 0.06f, 0.008f, clear);
            var dg = Prim(PrimitiveType.Sphere, g, chamber + Vector3.up * 0.02f, Vector3.one * 0.004f, Mats.Fade(new Color(0.8f, 0.9f, 1f, 0.8f), 0.8f));
            var dp = U.Child(g, "drip_anchor").transform; dp.localPosition = chamber + Vector3.up * 0.02f; dg.transform.SetParent(dp, false); dg.transform.localPosition = Vector3.zero; drip = dg.transform;
            Vector3 hand = W(new Vector3(-0.27f, 0.98f, 0.04f)), rail = W(new Vector3(-0.30f, 1.42f, 0.0f));
            var line = Spline(new[] { chamber + Vector3.down * 0.04f, chamber + Vector3.down * 0.5f + Vector3.right * 0.05f, rail + Vector3.down * 0.05f, Vector3.Lerp(rail, hand, 0.5f) + Vector3.down * 0.02f, hand }, 20);
            var lg = U.Child(g, "iv_line"); U.AddMesh(lg, U.Tube(line, i => 0.0022f, 6), clear);
            Prim(PrimitiveType.Cube, g, hand + up * 0.012f, new Vector3(0.025f, 0.008f, 0.03f), Mats.Std(new Color(0.95f, 0.75f, 0.2f), 0.35f));   // cannula hub + dressing
            // theatre cap: bouffant shell over the top and back of the head, leaving the face free
            Vector3 hmin = new Vector3(9, 9, 9), hmax = -hmin;
            for (int i = 0; i < bodyBase.Length; i++) { var p = bodyBase[i]; if (p.y > nose.y - 0.02f && Mathf.Abs(p.x) < 0.13f) { hmin = Vector3.Min(hmin, p); hmax = Vector3.Max(hmax, p); } }
            if (hmax.x > hmin.x) Cap(U.Child(patient, "theatre_cap"), (hmin + hmax) * 0.5f, (hmax - hmin) * 0.5f * 1.07f);
            foreach (var r in g.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static List<Vector3> Spline(Vector3[] c, int perSeg)
        {
            var o = new List<Vector3>();
            for (int s = 0; s < c.Length - 1; s++)
                for (int k = 0; k < perSeg; k++)
                {
                    float t = k / (float)perSeg; Vector3 p0 = c[Mathf.Max(0, s - 1)], p1 = c[s], p2 = c[s + 1], p3 = c[Mathf.Min(c.Length - 1, s + 2)];
                    o.Add(0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t));
                }
            o.Add(c[c.Length - 1]); return o;
        }
        static void Tube(Transform g, Vector3[] c, float r, Material m, int sides) { var t = U.Child(g, "tube"); U.AddMesh(t, U.Tube(Spline(c, 8), i => r, sides), m); }
        static void Cyl(Transform g, Vector3 centre, Vector3 axis, float r, Material m)
        {
            var c = Prim(PrimitiveType.Cylinder, g, centre, new Vector3(r * 2, axis.magnitude * 0.5f, r * 2), m); c.transform.up = axis.normalized;
        }
        static void Cap(GameObject go, Vector3 centre, Vector3 rad)
        {
            const int NU = 32, NV = 18; var v = new List<Vector3>(); var keep = new List<bool>(); var tri = new List<int>();
            for (int j = 0; j <= NV; j++) for (int i = 0; i <= NU; i++)
                {
                    float th = j / (float)NV * Mathf.PI, ph = i / (float)NU * Mathf.PI * 2;
                    var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));   // y = towards the crown
                    float ruffle = 1 + 0.035f * Mathf.Sin(ph * 14) * Mathf.Sin(th);
                    v.Add(centre + Vector3.Scale(d, rad) * ruffle); keep.Add(d.z < 0.15f || d.y > 0.55f);               // skip the face (+z) below the hairline
                }
            for (int j = 0; j < NV; j++) for (int i = 0; i < NU; i++)
                {
                    int a = j * (NU + 1) + i, b = a + 1, c = a + NU + 1, d = c + 1;
                    if (keep[a] && keep[b] && keep[c]) { tri.Add(a); tri.Add(b); tri.Add(c); }
                    if (keep[b] && keep[d] && keep[c]) { tri.Add(b); tri.Add(d); tri.Add(c); }
                }
            var mesh = new Mesh(); mesh.SetVertices(v); mesh.SetTriangles(tri, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            U.AddMesh(go, mesh, Mats.Matte(new Color(0.42f, 0.62f, 0.82f), DrapeTex()));
        }

        void BuildTray()
        {
            tray = U.Child(root, "mayo_tray").transform; tray.localPosition = new Vector3(0.05f, 1.20f, 0.30f);
            var steel = Mats.Std(new Color(0.72f, 0.74f, 0.76f), 0.5f, 1f);
            var towel = Mats.Matte(new Color(0.22f, 0.58f, 0.61f), DrapeTex());
            Prim(PrimitiveType.Cube, tray, new Vector3(0, 0, 0), new Vector3(0.56f, 0.012f, 0.40f), steel);
            Prim(PrimitiveType.Cube, tray, new Vector3(0, 0.008f, 0), new Vector3(0.54f, 0.004f, 0.38f), towel);
            Prim(PrimitiveType.Cylinder, tray, new Vector3(-0.33f, -0.53f, 0), new Vector3(0.028f, 0.53f, 0.028f), steel);
            Prim(PrimitiveType.Cube, tray, new Vector3(-0.30f, -0.012f, 0), new Vector3(0.09f, 0.02f, 0.05f), steel);
        }

        public static GameObject Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(t); g.transform.SetParent(parent, false); g.transform.localPosition = pos; g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            var c = g.GetComponent<Collider>(); if (c != null) Object.Destroy(c);
            return g;
        }

        // light teal for the room's walls/ceiling/cabinets
        public static readonly Color DrapeBlue = new Color(0.30f, 0.66f, 0.68f);   // sterile drapes + anaesthesia screen
        public static readonly Color RoomTint = new Color(0.52f, 0.78f, 0.80f), FloorTint = new Color(0.17f, 0.45f, 0.49f);   // teal theatre (walls / floor)

        // Recolour neutral, light pixels of the room scan towards `tint`, keeping the baked shading.
        // Coloured equipment and the dark floor are left alone. (Unlit/Texture ignores _Color, so edit the texture.)
        static void TintRoom(VorModel room, Color tint, Color floorTint)
        {
            var done = new HashSet<Texture2D>();
            foreach (var m in room.materials)
            {
                var src = m.mainTexture as Texture2D;
                if (src == null || done.Contains(src) || !src.isReadable) continue;
                done.Add(src);
                var px = src.GetPixels32();
                for (int i = 0; i < px.Length; i++)
                {
                    float r = px[i].r / 255f, g = px[i].g / 255f, b = px[i].b / 255f;
                    float mx = Mathf.Max(r, Mathf.Max(g, b)), mn = Mathf.Min(r, Mathf.Min(g, b));
                    float sat = mx > 1e-4f ? (mx - mn) / mx : 0f;
                    float lum = 0.2126f * r + 0.7152f * g + 0.0722f * b;
                    float neutral = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.10f, 0.28f, sat));
                    float w = neutral * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.22f, 0.50f, lum));                                   // light walls, ceiling, cabinets
                    float wf = neutral * 0.85f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.05f, 0.12f, lum)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.24f, 0.40f, lum)));   // grey floor
                    if (w <= 0 && wf <= 0) continue;
                    Color t = w >= wf ? tint * (lum / 0.85f) : floorTint * (lum / 0.30f); float a = Mathf.Max(w, wf);
                    px[i].r = (byte)(Mathf.Clamp01(Mathf.Lerp(r, t.r, a)) * 255f);
                    px[i].g = (byte)(Mathf.Clamp01(Mathf.Lerp(g, t.g, a)) * 255f);
                    px[i].b = (byte)(Mathf.Clamp01(Mathf.Lerp(b, t.b, a)) * 255f);
                }
                src.SetPixels32(px); src.Apply(true);
            }
        }

        // clean surgical-gown blue for the assistant surgeon
        public static readonly Color GownColor = new Color(0.90f, 0.94f, 0.95f);   // white surgical gown

        // The PPE scan's gown is crumpled paper with patch seams baked into the texture. Replace blue (gown) pixels
        // with an even fabric colour, keeping only broad shading; skin, mask straps, visor and badges are left alone.
        static void CleanScrubs(VorModel model, Color cloth)
        {
            var done = new HashSet<Texture2D>();
            foreach (var m in model.materials)
            {
                var tex = m.mainTexture as Texture2D;
                if (tex == null || done.Contains(tex) || !tex.isReadable) continue;
                done.Add(tex);
                int w = tex.width, h = tex.height; var px = tex.GetPixels32();
                // broad shading: average luminance on a coarse grid, bilinearly sampled
                const int G = 24; var sum = new float[G * G]; var cnt = new float[G * G]; float tot = 0, n = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    float r = px[i].r / 255f, g = px[i].g / 255f, b = px[i].b / 255f;
                    if (b - r < 0.08f) continue;
                    float l = 0.2126f * r + 0.7152f * g + 0.0722f * b; int c = (i / w) * G / h * G + (i % w) * G / w;
                    sum[c] += l; cnt[c]++; tot += l; n++;
                }
                if (n == 0) continue;
                float mean = tot / n;
                for (int c = 0; c < sum.Length; c++) sum[c] = cnt[c] > 0 ? sum[c] / cnt[c] : mean;
                for (int i = 0; i < px.Length; i++)
                {
                    float r = px[i].r / 255f, g = px[i].g / 255f, b = px[i].b / 255f;
                    float wgt = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.05f, 0.16f, b - Mathf.Max(r, g * 0.85f)));
                    if (wgt <= 0) continue;
                    int x = i % w, y = i / w;
                    float gx = Mathf.Clamp((x + 0.5f) * G / w - 0.5f, 0, G - 1.001f), gy = Mathf.Clamp((y + 0.5f) * G / h - 0.5f, 0, G - 1.001f);
                    int x0 = (int)gx, y0 = (int)gy; float fx = gx - x0, fy = gy - y0;
                    float sl = Mathf.Lerp(Mathf.Lerp(sum[y0 * G + x0], sum[y0 * G + x0 + 1], fx), Mathf.Lerp(sum[(y0 + 1) * G + x0], sum[(y0 + 1) * G + x0 + 1], fx), fy);
                    float shade = Mathf.Lerp(1f, Mathf.Clamp(sl / mean, 0.7f, 1.3f), 0.35f) * (1f + (U.Noise(x * 0.9f, y * 0.9f, 7) - 0.5f) * 0.04f);
                    px[i].r = (byte)(Mathf.Clamp01(Mathf.Lerp(r, cloth.r * shade, wgt)) * 255f);
                    px[i].g = (byte)(Mathf.Clamp01(Mathf.Lerp(g, cloth.g * shade, wgt)) * 255f);
                    px[i].b = (byte)(Mathf.Clamp01(Mathf.Lerp(b, cloth.b * shade, wgt)) * 255f);
                }
                tex.SetPixels32(px); tex.Apply(true);
            }
        }

        // latex surgical gloves: everyone except the patient wears them
        public static readonly Color Latex = new Color(0.93f, 0.87f, 0.72f);

        // Paint latex gloves into a scan's texture: skin-coloured texels of the triangles whose vertices are on the hands
        // (isHand, model space) become glove colour, keeping the finger shading.
        static void Glove(VorModel model, System.Func<Vector3, bool> isHand, Color latex)
        {
            if (model == null || model.meshes.Count == 0) return;
            var tex = model.materials[0].mainTexture as Texture2D;
            if (tex == null || !tex.isReadable) return;
            var mesh = model.meshes[0]; var v = mesh.vertices; var uv = mesh.uv; var tri = mesh.GetTriangles(0);
            if (uv == null || uv.Length != v.Length) return;
            var hand = new bool[v.Length]; for (int i = 0; i < v.Length; i++) hand[i] = isHand(v[i]);
            int w = tex.width, h = tex.height; var px = tex.GetPixels32(); var mark = new bool[px.Length];
            for (int t = 0; t < tri.Length; t += 3)
            {
                int i0 = tri[t], i1 = tri[t + 1], i2 = tri[t + 2];
                if ((hand[i0] ? 1 : 0) + (hand[i1] ? 1 : 0) + (hand[i2] ? 1 : 0) < 2) continue;
                Vector2 a = new Vector2(uv[i0].x * w, uv[i0].y * h), b = new Vector2(uv[i1].x * w, uv[i1].y * h), c = new Vector2(uv[i2].x * w, uv[i2].y * h);
                float d = U.Cross2(b - a, c - a); if (Mathf.Abs(d) < 1e-6f) continue;
                int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))) - 1), x1 = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))) + 1);
                int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))) - 1), y1 = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))) + 1);
                if ((x1 - x0) * (y1 - y0) > 40000) continue;
                float tol = 1.5f / Mathf.Max(1f, Mathf.Sqrt(Mathf.Abs(d)));   // ~1 texel of slack against seams
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                    {
                        var q = new Vector2(x + 0.5f, y + 0.5f);
                        float l1 = U.Cross2(c - b, q - b) / d, l2 = U.Cross2(a - c, q - c) / d, l3 = 1 - l1 - l2;
                        if (l1 >= -tol && l2 >= -tol && l3 >= -tol) mark[y * w + x] = true;
                    }
            }
            float tot = 0, n = 0;
            for (int i = 0; i < px.Length; i++) if (mark[i] && px[i].r - px[i].b > 15) { tot += (0.2126f * px[i].r + 0.7152f * px[i].g + 0.0722f * px[i].b) / 255f; n++; }
            if (n == 0) return;
            float mean = tot / n;
            for (int i = 0; i < px.Length; i++)
            {
                if (!mark[i]) continue;
                float r = px[i].r / 255f, g = px[i].g / 255f, bl = px[i].b / 255f;
                float wgt = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.03f, 0.10f, r - bl));
                if (wgt <= 0) continue;
                float lum = 0.2126f * r + 0.7152f * g + 0.0722f * bl;
                float shade = Mathf.Lerp(1f, Mathf.Clamp(lum / mean, 0.6f, 1.2f), 0.6f);
                px[i].r = (byte)(Mathf.Clamp01(Mathf.Lerp(r, latex.r * shade, wgt)) * 255f);
                px[i].g = (byte)(Mathf.Clamp01(Mathf.Lerp(g, latex.g * shade, wgt)) * 255f);
                px[i].b = (byte)(Mathf.Clamp01(Mathf.Lerp(bl, latex.b * shade, wgt)) * 255f);
            }
            tex.SetPixels32(px); tex.Apply(true);
        }

        // Dim, neutral reflection environment shaped like the room (light-teal walls, bright ceiling, dark floor)
        // instead of the default bright sky, which made every surface look like wet plastic.
        static void RoomReflections()
        {
            const int S = 16; var cube = new Cubemap(S, TextureFormat.RGBA32, false);
            Color ceil = new Color(0.62f, 0.68f, 0.68f), wall = new Color(0.42f, 0.52f, 0.51f), floor = new Color(0.16f, 0.17f, 0.17f);
            foreach (CubemapFace f in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
            {
                var px = new Color[S * S];
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                    {
                        float u = (x + 0.5f) / S * 2 - 1, v = (y + 0.5f) / S * 2 - 1; float dy;
                        if (f == CubemapFace.PositiveY) dy = 1; else if (f == CubemapFace.NegativeY) dy = -1; else dy = -v;   // side faces: row 0 is the top
                        float up = dy / Mathf.Sqrt(1 + (f == CubemapFace.PositiveY || f == CubemapFace.NegativeY ? u * u + v * v : u * u + v * v));
                        px[y * S + x] = up >= 0 ? Color.Lerp(wall, ceil, Mathf.SmoothStep(0, 1, up)) : Color.Lerp(wall, floor, Mathf.SmoothStep(0, 1, -up * 1.5f));
                    }
                cube.SetPixels(px, f);
            }
            cube.Apply();
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
            RenderSettings.reflectionIntensity = 0.6f;
        }

        static Texture2D drapeTex;
        public static Texture2D DrapeTex()
        {
            if (drapeTex != null) return drapeTex;
            drapeTex = U.MakeTex(256, 256, (u, v) =>
            {
                float n = U.Noise(u * 256, v * 256, 1) * 0.08f + U.Fbm(u * 8, v * 8, 2) * 0.1f;
                float weave = ((int)(u * 256) + (int)(v * 256)) % 3 == 0 ? 0.03f : 0f;
                float g = 0.86f + n - weave; return new Color(g, g, g);
            });
            return drapeTex;
        }

        // ------------------------------------------------------------ drapes
        // Fabric heightfield over patient + table (world space), falling off the sides and the foot end,
        // with a hole over the operative window. Submesh 0 = fabric, 1 = border around the window.
        // lift: clearance above the body, dilate: fabric tent radius (cells), holeInset: hole shrink from the window edge.
        Mesh CoverMesh(float halfW, float lift, int dilate, float holeInset, out Vector3[] verts)
        {
            float s = 0.0125f, x0 = -0.835f, x1 = 0.84f, z0 = -1.0f, z1 = 0.9125f;
            int nx = Mathf.RoundToInt((x1 - x0) / s), nz = Mathf.RoundToInt((z1 - z0) / s);
            float top = 0.80f + 0.035f, zEnd = 0.80f;
            var H = new float[(nx + 1) * (nz + 1)];
            for (int j = 0; j <= nz; j++) for (int i = 0; i <= nx; i++)
                {
                    float x = x0 + i * s, z = z0 + j * s; int k = j * (nx + 1) + i;
                    float hh = top;
                    if (Mathf.Abs(x) <= halfW && z <= zEnd)
                    {
                        var ray = new Ray(new Vector3(x, 2.5f, z), Vector3.down); RaycastHit hit;
                        if (patientCollider.Raycast(ray, out hit, 3f)) hh = Mathf.Max(hh, hit.point.y);
                        foreach (var tc in tableColliders) if (tc.Raycast(ray, out hit, 3f)) hh = Mathf.Max(hh, hit.point.y);
                    }
                    H[k] = hh;
                }
            // dilate + relax (fabric tension)
            var C = (float[])H.Clone();
            for (int j = 0; j <= nz; j++) for (int i = 0; i <= nx; i++)
                {
                    float m = C[j * (nx + 1) + i];
                    for (int dj = -dilate; dj <= dilate; dj++) for (int di = -dilate; di <= dilate; di++) { int ii = Mathf.Clamp(i + di, 0, nx), jj = Mathf.Clamp(j + dj, 0, nz); m = Mathf.Max(m, C[jj * (nx + 1) + ii]); }
                    H[j * (nx + 1) + i] = m + lift;
                }
            var B = (float[])H.Clone();
            for (int pass = 0; pass < 14; pass++)
            {
                C = (float[])H.Clone();
                for (int j = 1; j < nz; j++) for (int i = 1; i < nx; i++)
                    {
                        int k = j * (nx + 1) + i;
                        float avg = (C[k - 1] + C[k + 1] + C[k - nx - 1] + C[k + nx + 1]) * 0.25f;
                        H[k] = Mathf.Max(B[k] - lift / 3f, avg);
                    }
            }
            verts = new Vector3[H.Length]; var uv = new Vector2[H.Length];
            for (int j = 0; j <= nz; j++) for (int i = 0; i <= nx; i++)
                {
                    int k = j * (nx + 1) + i; float x = x0 + i * s, z = z0 + j * s; float y = H[k];
                    float ox = Mathf.Max(0, Mathf.Abs(x) - halfW), oz = Mathf.Max(0, z - zEnd), o = Mathf.Max(ox, oz);
                    if (o > 0)
                    {
                        float fall = Mathf.Min(1, o / 0.06f);
                        float fold = Mathf.Sin((ox > oz ? z : x) * 38f) * 0.012f * fall;
                        y = top + lift - 0.002f - (o * 0.6f + fall * fall * 0.1f) - o * o * 40f; y = Mathf.Max(y, 0.06f);
                        if (ox > 0) { x = x - Mathf.Sign(x) * Mathf.Max(0, ox - 0.035f) * 0.92f; z += fold; }
                        if (oz > 0) { z = z - Mathf.Max(0, oz - 0.035f) * 0.92f; x += fold; }
                    }
                    verts[k] = new Vector3(x, y, z); uv[k] = new Vector2(i * s * 6f, j * s * 6f);
                }
            // hole over the operative window + border submesh
            var wx0 = patient.TransformPoint(new Vector3(Window.xMin, Window.yMax, 0)); var wx1 = patient.TransformPoint(new Vector3(Window.xMax, Window.yMin, 0));
            float hx0 = Mathf.Min(wx0.x, wx1.x), hx1 = Mathf.Max(wx0.x, wx1.x), hz0 = Mathf.Min(wx0.z, wx1.z), hz1 = Mathf.Max(wx0.z, wx1.z);
            var tri = new List<int>(); var border = new List<int>();
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++)
                {
                    float cx = x0 + (i + 0.5f) * s, cz = z0 + (j + 0.5f) * s;
                    bool hole = cx > hx0 + holeInset && cx < hx1 - holeInset && cz > hz0 + holeInset && cz < hz1 - holeInset;
                    if (hole) continue;
                    bool rim = cx > hx0 - 0.02f && cx < hx1 + 0.02f && cz > hz0 - 0.02f && cz < hz1 + 0.02f;
                    int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                    var l = rim ? border : tri;
                    l.Add(a); l.Add(c); l.Add(b); l.Add(b); l.Add(c); l.Add(d);
                }
            var mesh = new Mesh(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts; mesh.uv = uv; mesh.subMeshCount = 2;
            mesh.SetTriangles(tri.ToArray(), 0); mesh.SetTriangles(border.ToArray(), 1);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // ventilation weights for a patient-local point (chest and belly rise along patient +z, anterior surface only)
        static void BreathWeights(Vector3 p, out float chest, out float belly)
        {
            float front = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.02f, 0.10f, p.z)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.17f, 0.26f, Mathf.Abs(p.x))));
            chest = front * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.05f, 1.28f, p.y)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.42f, 1.52f, p.y)));
            belly = front * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.95f, 1.08f, p.y)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(1.18f, 1.3f, p.y)));
        }
        const float ChestRise = 0.010f, BellyRise = 0.007f;

        // a cover (cape/drapes) that rises and falls with the patient's chest, so the breathing body never pokes through it
        class BreathingCover
        {
            readonly Mesh mesh; readonly Vector3[] rest, work; readonly int[] idx; readonly float[] wc, wb; readonly Vector3 up;
            public BreathingCover(Mesh m, Vector3[] restVerts, Transform space, Transform patient)
            {
                mesh = m; rest = restVerts; work = (Vector3[])restVerts.Clone();
                up = space.InverseTransformVector(patient.TransformVector(Vector3.forward));
                var li = new List<int>(); var lc = new List<float>(); var lb = new List<float>();
                for (int i = 0; i < rest.Length; i++)
                {
                    float c, b; BreathWeights(patient.InverseTransformPoint(space.TransformPoint(rest[i])), out c, out b);
                    if (c > 0 || b > 0) { li.Add(i); lc.Add(c); lb.Add(b); }
                }
                idx = li.ToArray(); wc = lc.ToArray(); wb = lb.ToArray();
            }
            public Vector3 Offset(int k, float bc, float bb) { return up * (wc[k] * ChestRise * bc + wb[k] * BellyRise * bb); }
            public int[] Indices { get { return idx; } }
            public void Apply(float bc, float bb)
            {
                for (int k = 0; k < idx.Length; k++) work[idx[k]] = rest[idx[k]] + Offset(k, bc, bb);
                mesh.vertices = work;
            }
        }

        // ------------------------------------------------------------ patient cape (non-sterile cover until the sterile drapes land)
        void BuildCape()
        {
            Vector3[] v;
            var mesh = CoverMesh(0.40f, 0.006f, 1, 0.002f, out v);
            cape = U.Child(root, "patient_cape");
            cape.AddComponent<MeshFilter>().sharedMesh = mesh; mesh.MarkDynamic();
            capeBreath = new BreathingCover(mesh, v, root, patient);
            var cloth = Mats.Matte(new Color(0.66f, 0.87f, 0.87f), DrapeTex());   // light aqua sheet
            cape.AddComponent<MeshRenderer>().sharedMaterials = new[] { cloth, cloth };
        }

        // ------------------------------------------------------------ drapes
        public void ApplyDrapes()
        {
            if (drapes != null) return;
            CutPatient(true);
            Physics.SyncTransforms();
            Vector3[] verts;
            drapeMesh = CoverMesh(0.37f, 0.012f, 2, 0.006f, out verts); drapeMesh.MarkDynamic();
            drapeFinal = verts; drapeStart = new Vector3[verts.Length];
            drapeBreath = new BreathingCover(drapeMesh, verts, root, patient);
            for (int k = 0; k < verts.Length; k++) drapeStart[k] = new Vector3(verts[k].x * 1.15f, 1.75f + Mathf.Sin(verts[k].x * 6f) * 0.03f, verts[k].z * 1.05f);
            drapeMesh.vertices = drapeStart; drapeMesh.RecalculateNormals(); drapeMesh.RecalculateBounds();
            drapes = U.Child(root, "drapes");
            drapes.AddComponent<MeshFilter>().sharedMesh = drapeMesh;
            var r = drapes.AddComponent<MeshRenderer>();
            r.sharedMaterials = new[] { Mats.Matte(DrapeBlue, DrapeTex()), Mats.Std(new Color(0.72f, 0.89f, 0.89f), 0.3f, 0, DrapeTex()) };   // nonwoven drape + adhesive film border
            drapeT = 0f;
            screen.SetActive(true);
        }

        // ------------------------------------------------------------ team
        void BuildTeam()
        {
            // assistant surgeon: opposite side (static scan, procedural sway)
            var ah = U.Child(root, "assistant_surgeon").transform; assistantT = ah;
            var sm = VorLoader.Load("surgeon", ah);
            if (sm != null) { if (sm.materials.Length > 0) Mats.MakeMatte(sm.materials[0]); CleanScrubs(sm, GownColor); Glove(sm, p => p.y > 0.55f && p.y < 0.9f && p.z > 0.12f, Latex); }
            ah.localScale = Vector3.one * 1.06f; assistantBaseScale = ah.localScale;
            ah.position = new Vector3(-0.62f, 0.01f, GroinWorld.z + 0.05f); assistantBasePos = ah.position;
            ah.rotation = Quaternion.LookRotation(new Vector3(1f, 0, -0.1f));
        }

        public void Tick(float dt, float breath, Vector3 camPos)
        {
            // patient ventilation (12/min)
            float bc = breath, bb = breath * 0.7f;
            for (int k = 0; k < breathIdx.Length; k++) { int i = breathIdx[k]; var p = bodyBase[i]; p.z += breathChest[k] * ChestRise * bc + breathBelly[k] * BellyRise * bb; bodyWork[i] = p; }
            bodyMesh.vertices = bodyWork;
            if (cape != null && cape.activeSelf) capeBreath.Apply(bc, bb);
            // drapes falling into place
            if (drapeT >= 0 && drapeT < 1)
            {
                drapeT = Mathf.Min(1, drapeT + dt / 1.6f);
                float e = 1 - Mathf.Pow(1 - drapeT, 3);
                var v = new Vector3[drapeFinal.Length];
                for (int k = 0; k < v.Length; k++)
                {
                    float lag = Mathf.Clamp01(e * 1.25f - Mathf.Abs(drapeFinal[k].x) * 0.3f);
                    v[k] = Vector3.Lerp(drapeStart[k], drapeFinal[k], lag) + Vector3.up * Mathf.Sin(lag * Mathf.PI) * 0.02f;
                }
                var bi = drapeBreath.Indices;
                for (int k = 0; k < bi.Length; k++) v[bi[k]] += drapeBreath.Offset(k, bc, bb) * Mathf.Clamp01(e * 1.25f - Mathf.Abs(drapeFinal[bi[k]].x) * 0.3f);
                drapeMesh.vertices = v; drapeMesh.RecalculateNormals(); drapeMesh.RecalculateBounds();
                if (drapeT >= 1) { draped = true; if (cape != null) cape.SetActive(false); }
            }
            else if (draped) drapeBreath.Apply(bc, bb);
            // IV drip chamber: a drop every ~1.2 s
            if (drip != null) { dripT = (dripT + dt / 1.2f) % 1f; drip.localPosition = new Vector3(0, -0.006f - dripT * dripT * 0.03f, 0); drip.gameObject.SetActive(dripT < 0.85f); }
            // team
            if (assistantT != null)
            {
                float t = Time.time;
                assistantT.localScale = new Vector3(assistantBaseScale.x * (1 + Mathf.Sin(t * 1.3f) * 0.004f), assistantBaseScale.y, assistantBaseScale.z * (1 + Mathf.Sin(t * 1.3f) * 0.006f));
                leanNow = Mathf.MoveTowards(leanNow, assistantLean, dt * 1.6f); float e = Mathf.SmoothStep(0, 1, leanNow);
                assistantT.rotation = Quaternion.LookRotation(new Vector3(1f, 0, -0.1f)) * Quaternion.Euler(Mathf.Sin(t * 0.3f) * 1.2f + 3f + e * 9f, Mathf.Sin(t * 0.21f) * 4f * (1 - e), Mathf.Sin(t * 0.37f) * 0.8f);
                assistantT.position = assistantBasePos + new Vector3(0.06f, 0, 0) * e;
            }
        }
    }
}
