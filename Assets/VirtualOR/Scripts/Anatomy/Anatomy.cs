// Virtual OR - inguinal anatomy. Real BodyParts3D meshes (external/internal oblique, transversus, rectus,
// inguinal ligament, hip bone, external iliac vessels) + simulated structures between real landmarks:
// external oblique aponeurosis (cuttable sheet), spermatic cord (rope), ilioinguinal nerve, indirect hernia sac,
// inferior epigastric vessels, polypropylene mesh (cloth) and sutures.
using System.Collections.Generic;
using UnityEngine;

namespace VirtualOR
{
    public static class TissueTex
    {
        public static Texture2D Muscle()
        {
            return U.MakeTex(256, 256, (u, v) =>
            {
                float s = 0.5f + 0.5f * Mathf.Sin(v * 220f + U.Noise(u * 8, v * 8, 3) * 6f);
                float n = U.Fbm(u * 6, v * 30, 9);
                Color c = Color.Lerp(new Color(0.44f, 0.12f, 0.11f), new Color(0.66f, 0.24f, 0.21f), n * 0.8f + s * 0.25f);
                return c;
            });
        }
        public static Texture2D Ligament()
        {
            return U.MakeTex(256, 256, (u, v) =>
            {
                float s = U.Noise(u * 4, v * 120, 4);
                return Color.Lerp(new Color(0.80f, 0.79f, 0.74f), new Color(0.93f, 0.92f, 0.88f), s);
            });
        }
        public static Texture2D Fat()
        {
            return U.MakeTex(256, 256, (u, v) =>
            {
                float c = U.Cells(u * 18, v * 18, 7); float edge = Mathf.SmoothStep(0.1f, 0.35f, c);
                float ves = Mathf.Abs(U.Fbm(u * 5, v * 5, 13) - 0.5f) < 0.012f ? 1f : 0f;
                Color lob = Color.Lerp(new Color(0.95f, 0.83f, 0.52f), new Color(0.89f, 0.74f, 0.42f), U.Noise(u * 40, v * 40, 2));
                Color sept = new Color(0.86f, 0.66f, 0.56f);
                Color col = Color.Lerp(lob, sept, (1 - edge) * 0.6f);
                return Color.Lerp(col, new Color(0.55f, 0.08f, 0.08f), ves * 0.8f);
            });
        }
        // wall: v=0 at skin surface. Epidermis + dermis band (~2.5 mm), then subcutaneous fat.
        public static Texture2D Wall(float texMeters)
        {
            float dermis = 0.0025f / texMeters;
            return U.MakeTex(128, 256, (u, v) =>
            {
                float vv = 1 - v; // Unity texture v origin bottom; we use uv.y = depth fraction
                vv = v;
                if (vv < 0.006f) return new Color(0.70f, 0.50f, 0.42f);
                if (vv < dermis) return Color.Lerp(new Color(0.86f, 0.55f, 0.52f), new Color(0.74f, 0.30f, 0.30f), U.Noise(u * 20, vv * 200, 5));
                float c = U.Cells(u * 10, vv * 30, 3); float edge = Mathf.SmoothStep(0.1f, 0.35f, c);
                Color lob = Color.Lerp(new Color(0.95f, 0.84f, 0.54f), new Color(0.88f, 0.72f, 0.42f), U.Noise(u * 30, vv * 80, 8));
                return Color.Lerp(lob, new Color(0.80f, 0.45f, 0.32f), (1 - edge) * 0.55f);
            }, true, TextureWrapMode.Repeat);
        }
        public static Texture2D Aponeurosis(Vector2 fibreDirUV, float aspect)
        {
            Vector2 d = new Vector2(fibreDirUV.x, fibreDirUV.y * aspect).normalized; Vector2 p = new Vector2(-d.y, d.x);
            return U.MakeTex(512, 512, (u, v) =>
            {
                float across = u * p.x + v * p.y, along = u * d.x + v * d.y;
                float f = 0.5f + 0.5f * Mathf.Sin(across * 900f + U.Noise(along * 30, across * 30, 2) * 4f);
                float n = U.Fbm(along * 8, across * 60, 5);
                Color c = Color.Lerp(new Color(0.74f, 0.75f, 0.76f), new Color(0.93f, 0.93f, 0.92f), f * 0.45f + n * 0.5f);
                float vessel = Mathf.Abs(U.Fbm(u * 3, v * 3, 31) - 0.5f) < 0.006f ? 0.6f : 0f;
                return Color.Lerp(c, new Color(0.7f, 0.2f, 0.2f), vessel);
            }, true, TextureWrapMode.Clamp);
        }
        public static Texture2D Cord()
        {
            return U.MakeTex(128, 256, (u, v) =>
            {
                float vein = Mathf.Abs(Mathf.Sin(u * 40f + U.Noise(u * 4, v * 6, 3) * 5f));
                float n = U.Fbm(u * 6, v * 10, 7);
                Color c = Color.Lerp(new Color(0.86f, 0.62f, 0.60f), new Color(0.95f, 0.80f, 0.76f), n);
                return Color.Lerp(c, new Color(0.45f, 0.22f, 0.40f), vein > 0.97f ? 0.6f : 0f);
            });
        }
        public static Texture2D Peritoneum()
        {
            return U.MakeTex(128, 128, (u, v) =>
            {
                float n = U.Fbm(u * 5, v * 5, 17);
                float ves = Mathf.Abs(U.Fbm(u * 3, v * 3, 41) - 0.5f) < 0.01f ? 1f : 0f;
                return Color.Lerp(Color.Lerp(new Color(0.84f, 0.84f, 0.88f), new Color(0.93f, 0.90f, 0.92f), n), new Color(0.75f, 0.25f, 0.3f), ves * 0.5f);
            });
        }
        public static Texture2D MeshGrid()
        {
            return U.MakeTex(256, 256, (u, v) =>
            {
                float gx = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 40f)), gy = Mathf.Abs(Mathf.Sin(v * Mathf.PI * 40f));
                float a = Mathf.Max(Mathf.SmoothStep(0.85f, 1f, gx), Mathf.SmoothStep(0.85f, 1f, gy));
                return new Color(0.92f, 0.94f, 0.96f, a * 0.95f);
            });
        }
    }

    // Simple XPBD rope (spermatic cord, hernia sac).
    public class Rope
    {
        public Vector3[] x, p, v, rest; public float[] w; public float[] seg;
        public float stiffBend = 0.25f; public float damping = 0.9f;
        public System.Func<int, Vector3, Vector3> constrain;   // per-particle positional constraint (floor/ceiling)
        public class Grab { public int i; public Vector3 target; public float k; }
        public readonly List<Grab> grabs = new List<Grab>();

        public Rope(List<Vector3> pts, bool pinFirst, bool pinLast)
        {
            int n = pts.Count; x = pts.ToArray(); p = pts.ToArray(); v = new Vector3[n]; rest = pts.ToArray(); w = new float[n]; seg = new float[n];
            for (int i = 0; i < n; i++) w[i] = 1;
            if (pinFirst) w[0] = 0; if (pinLast) w[n - 1] = 0;
            for (int i = 0; i + 1 < n; i++) seg[i] = Vector3.Distance(pts[i], pts[i + 1]);
        }

        public void Step(float dt)
        {
            int n = x.Length; const int ns = 4; float h = dt / ns;
            for (int s = 0; s < ns; s++)
            {
                for (int i = 0; i < n; i++) { p[i] = x[i]; if (w[i] > 0) x[i] += v[i] * h; }
                for (int it = 0; it < 2; it++)
                {
                    for (int i = 0; i + 1 < n; i++)
                    {
                        float ws = w[i] + w[i + 1]; if (ws == 0) continue;
                        Vector3 d = x[i + 1] - x[i]; float L = d.magnitude; if (L < 1e-9f) continue;
                        Vector3 c = d * ((L - seg[i]) / L / ws);
                        x[i] += c * w[i]; x[i + 1] -= c * w[i + 1];
                    }
                    for (int i = 1; i + 1 < n; i++)
                    {
                        if (w[i] == 0) continue;
                        Vector3 mid = (x[i - 1] + x[i + 1]) * 0.5f;
                        Vector3 restMid = (rest[i - 1] + rest[i + 1]) * 0.5f;
                        Vector3 want = mid + (rest[i] - restMid);
                        x[i] = Vector3.Lerp(x[i], want, stiffBend * 0.5f);
                    }
                    foreach (var g in grabs) if (g.i >= 0 && g.i < n && w[g.i] > 0) x[g.i] = Vector3.Lerp(x[g.i], g.target, g.k);
                    if (constrain != null) for (int i = 0; i < n; i++) if (w[i] > 0) x[i] = constrain(i, x[i]);
                }
                for (int i = 0; i < n; i++) v[i] = w[i] > 0 ? (x[i] - p[i]) / h * Mathf.Pow(damping, 1f / ns) : Vector3.zero;
            }
        }

        public int Nearest(Vector3 q) { int b = 0; float bd = float.MaxValue; for (int i = 0; i < x.Length; i++) { float d = (x[i] - q).sqrMagnitude; if (d < bd) { bd = d; b = i; } } return b; }

        public List<Vector3> Smooth(int sub)
        {
            var o = new List<Vector3>(); int n = x.Length;
            for (int i = 0; i + 1 < n; i++)
            {
                Vector3 p0 = x[Mathf.Max(0, i - 1)], p1 = x[i], p2 = x[i + 1], p3 = x[Mathf.Min(n - 1, i + 2)];
                for (int s = 0; s < sub; s++)
                {
                    float t = s / (float)sub, t2 = t * t, t3 = t2 * t;
                    o.Add(0.5f * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3));
                }
            }
            o.Add(x[n - 1]); return o;
        }
    }

    public class Stitch
    {
        public GameObject go; public Mesh mesh; public System.Func<Vector3> a, b; public float arch = 0.0025f;
        public void Update()
        {
            Vector3 pa = a(), pb = b();
            var pts = new List<Vector3>();
            Vector3 up = Vector3.up;
            for (int i = 0; i <= 8; i++) { float t = i / 8f; pts.Add(Vector3.Lerp(pa, pb, t) + up * Mathf.Sin(t * Mathf.PI) * arch); }
            U.Tube(pts, i => 0.00035f, 5, mesh);
        }
    }

    public class InguinalField : MonoBehaviour
    {
        // landmarks in field frame (Unity: x = patient right, y = cranial, z = anterior)
        public static readonly Vector2 DeepRing = new Vector2(0, 0), SupRing = new Vector2(-0.042f, -0.046f), PubicTubercle = new Vector2(-0.05f, -0.059f), ASIS = new Vector2(0.05f, 0.033f);
        public static readonly Vector2[] Ligament = {
            new Vector2(0.0517f,0.033f), new Vector2(0.0448f,0.0252f), new Vector2(0.0333f,0.0108f), new Vector2(0.0236f,-0.0015f), new Vector2(0.0133f,-0.0133f),
            new Vector2(0.0024f,-0.0252f), new Vector2(-0.0078f,-0.0347f), new Vector2(-0.0181f,-0.043f), new Vector2(-0.0286f,-0.0507f), new Vector2(-0.0393f,-0.0592f), new Vector2(-0.0457f,-0.0651f) };

        public Transform patient;
        public VorModel model;
        public readonly Dictionary<string, MeshCollider> colliders = new Dictionary<string, MeshCollider>();
        public readonly Dictionary<string, Renderer> parts = new Dictionary<string, Renderer>();
        public TissueSheet apo; public SkinSurface apoSurface;
        public Rope cord, sac;
        public GameObject cordGO, sacGO, nerveGO, meshGO;
        Mesh cordMesh, sacMesh, nerveMesh;
        public List<Vector3> nervePts = new List<Vector3>();
        public bool apoOpened, cordLifted, sacReduced, sacReducing, meshPlaced, meshFixed;
        float sacScale = 1f;
        public TissueCore meshCloth; public TissueSheet meshSheet;
        public readonly List<Stitch> stitches = new List<Stitch>();
        public int fieldLayer;
        Vector3 penroseTarget; Rope.Grab penroseGrabA, penroseGrabB;

        public Vector3 F2P(Vector3 f) { return transform.localPosition + f; }                 // field frame -> patient local
        public Vector3 F2W(Vector3 f) { return patient.TransformPoint(F2P(f)); }
        public Vector2 P2F(Vector2 p) { return p - (Vector2)transform.localPosition; }

        public void Build(Transform patientRoot, float skinZAtDeepRing)
        {
            patient = patientRoot;
            model = VorLoader.Load("field", transform);
            var muscle = TissueTex.Muscle(); var lig = TissueTex.Ligament();
            foreach (var mf in model.filters)
            {
                string n = mf.gameObject.name; var r = mf.GetComponent<MeshRenderer>();
                Material m;
                if (n.Contains("oblique") || n.Contains("transversus") || n.Contains("rectus")) m = Mats.Std(Color.white, 0.32f, 0, muscle);
                else if (n.Contains("ligament")) m = Mats.Std(Color.white, 0.38f, 0, lig);
                else if (n.Contains("artery")) m = Mats.Std(new Color(0.62f, 0.12f, 0.12f), 0.4f);
                else if (n.Contains("vein")) m = Mats.Std(new Color(0.30f, 0.20f, 0.36f), 0.4f);
                else m = Mats.Std(new Color(0.88f, 0.84f, 0.76f), 0.25f);
                r.sharedMaterial = m; parts[n] = r;
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; colliders[n] = mc;
                if (n == "ext_oblique" || n == "hip_bone" || n == "testis") { r.enabled = false; mf.gameObject.layer = 2; }  // aponeurosis is simulated; bone & testis out of view
            }
            // Position: external oblique front at the deep ring sits ~22 mm under the skin.
            float apoZ = FrontZ(new[] { "ext_oblique" }, DeepRing, 0.016f);
            var lp = transform.localPosition; lp.z = skinZAtDeepRing - 0.022f - apoZ; transform.localPosition = lp;
            Physics.SyncTransforms();
        }

        // z (field frame) of the first surface hit from the front for the given structures; fallback if missed.
        public float FrontZ(string[] names, Vector2 xy, float fallback, float startZ = 0.15f)
        {
            Vector3 o = F2W(new Vector3(xy.x, xy.y, startZ)), d = patient.TransformDirection(Vector3.back);
            float best = float.MaxValue; float z = fallback;
            foreach (var nme in names)
            {
                MeshCollider c; if (!colliders.TryGetValue(nme, out c)) continue;
                RaycastHit h; if (c.Raycast(new Ray(o, d), out h, 1f) && h.distance < best) { best = h.distance; z = startZ - h.distance; }
            }
            return z;
        }

        static readonly string[] Floor = { "int_oblique", "transversus", "inguinal_ligament", "rectus" };

        public SurfaceSample ApoSample(Vector2 pl)   // pl in patient-local xy
        {
            Vector2 f = P2F(pl);
            float z = FrontZ(new[] { "ext_oblique" }, f, -0.003f) + 0.0055f;
            float z2 = FrontZ(new[] { "ext_oblique" }, f + new Vector2(0.002f, 0), -0.003f) + 0.0055f, z3 = FrontZ(new[] { "ext_oblique" }, f + new Vector2(0, 0.002f), -0.003f) + 0.0055f;
            Vector3 n = Vector3.Cross(new Vector3(0, 0.002f, z3 - z), new Vector3(0.002f, 0, z2 - z)).normalized; if (n.z < 0) n = -n;
            return new SurfaceSample { pos = F2P(new Vector3(f.x, f.y, z)), normal = n, thickness = 0.0018f };
        }

        public Vector2 LigamentDir { get { return (Ligament[Ligament.Length - 1] - Ligament[0]).normalized; } }

        public void BuildSimulated(Rect window)
        {
            // ---- aponeurosis sheet (in patient-local space, same domain as the skin)
            var g = U.Child(patient, "aponeurosis"); apo = g.AddComponent<TissueSheet>();
            apo.core.domain = window; apo.core.spacing = 0.005f; apo.core.sample = ApoSample;
            apo.core.fibreDir = LigamentDir; apo.core.strainPar = 0.05f; apo.core.strainPerp = 0.012f; apo.core.tetherCompliance = 6e-5f; apo.core.scoreDepth = 0.5f;
            apo.wallTexScale = 0.004f;
            apo.surfaceMat = Mats.Tissue(TissueMaps.Aponeurosis(LigamentDir, window.height / window.width, window.width * 1000f), 1f, 0.9f);   // pearly fibre bundles
            apo.wallMat = Mats.Std(new Color(0.86f, 0.84f, 0.80f), 0.38f);
            apo.floorMat = Mats.Std(new Color(0.86f, 0.84f, 0.80f), 0.38f);
            apo.showFloor = false;
            apo.core.Init(); apo.Build(); apo.Refresh();

            // ---- spermatic cord: deep ring -> canal -> superficial ring -> towards scrotum (pinned outside field)
            var path = new List<Vector2> { DeepRing + new Vector2(0.004f, 0.004f), DeepRing, new Vector2(-0.01f, -0.012f), new Vector2(-0.022f, -0.025f), new Vector2(-0.033f, -0.036f), SupRing, new Vector2(-0.046f, -0.062f), new Vector2(-0.047f, -0.082f), new Vector2(-0.047f, -0.11f) };
            var pts = new List<Vector3>();
            var rs = U.Resample(new List<Vector2>(path), 0.0045f);
            float cordR = 0.0055f;
            foreach (var q in rs)
            {
                float zf = FrontZ(Floor, q, -0.002f, FrontZ(new[] { "ext_oblique" }, q, 0.012f) - 0.0005f);
                pts.Add(F2P(new Vector3(q.x, q.y, zf + cordR * 0.75f)));
            }
            cord = new Rope(pts, true, true); cord.stiffBend = 0.18f;
            var cordFloor = new float[pts.Count]; for (int i = 0; i < pts.Count; i++) cordFloor[i] = pts[i].z - cordR * 0.75f;
            cord.constrain = (i, p) =>
            {
                if (p.z < cordFloor[i] + cordR * 0.5f) p.z = cordFloor[i] + cordR * 0.5f;
                if (!apoOpened) { var s = ApoSample(new Vector2(p.x, p.y)); if (p.z > s.pos.z - cordR) p.z = s.pos.z - cordR; }
                return p;
            };
            cordGO = U.Child(patient, "spermatic_cord"); cordMesh = new Mesh(); U.AddMesh(cordGO, cordMesh, Mats.Std(Color.white, 0.38f, 0, TissueTex.Cord()));

            // ---- indirect sac: emerges at the deep ring, lies antero-medial to the cord
            var sp = new List<Vector3>();
            for (int i = 0; i < 9; i++) { var c = cord.x[Mathf.Min(cord.x.Length - 1, i + 1)]; sp.Add(c + new Vector3(-0.0045f, 0.0015f, 0.0035f)); }
            sac = new Rope(sp, true, false); sac.stiffBend = 0.3f;
            sac.constrain = (i, p) => { var c = cord.x[Mathf.Min(cord.x.Length - 1, i + 1)] + new Vector3(-0.0045f, 0.0015f, 0.0035f); return Vector3.Lerp(p, c, sacReducing ? 0f : 0.04f); };
            sacGO = U.Child(patient, "hernia_sac"); sacMesh = new Mesh(); U.AddMesh(sacGO, sacMesh, Mats.Std(new Color(0.95f, 0.93f, 0.97f), 0.45f, 0, TissueTex.Peritoneum()));

            // ---- ilioinguinal nerve and inferior epigastric vessels
            nerveGO = U.Child(patient, "ilioinguinal_nerve"); nerveMesh = new Mesh(); U.AddMesh(nerveGO, nerveMesh, Mats.Std(new Color(0.93f, 0.88f, 0.66f), 0.35f));
            float tz = FrontZ(Floor, new Vector2(-0.015f, 0.01f), -0.002f);
            AddVessel("inferior_epigastric_artery", new[] { new Vector3(-0.010f, -0.016f, tz), new Vector3(-0.015f, 0.0f, tz + 0.0005f), new Vector3(-0.022f, 0.02f, tz + 0.001f), new Vector3(-0.032f, 0.05f, tz + 0.001f) }, 0.0016f, new Color(0.70f, 0.10f, 0.10f));
            AddVessel("inferior_epigastric_vein", new[] { new Vector3(-0.0135f, -0.017f, tz), new Vector3(-0.019f, 0.0f, tz + 0.0005f), new Vector3(-0.026f, 0.02f, tz + 0.001f), new Vector3(-0.036f, 0.05f, tz + 0.001f) }, 0.0019f, new Color(0.22f, 0.16f, 0.38f));
            UpdateVisuals();
        }

        void AddVessel(string nm, Vector3[] f, float r, Color c)
        {
            var g = U.Child(patient, nm); var pts = new List<Vector3>(); foreach (var p in f) pts.Add(F2P(p));
            var sm = U.Resample(Flat(pts), 0.002f); var p3 = new List<Vector3>();
            // re-lift resampled 2D points to 3D by nearest original z
            foreach (var q in sm) { float z = pts[0].z; float bd = 9; foreach (var o in pts) { float d = Vector2.Distance(q, o); if (d < bd) { bd = d; z = o.z; } } p3.Add(new Vector3(q.x, q.y, z)); }
            var m = U.Tube(p3, i => r, 8); U.AddMesh(g, m, Mats.Std(c, 0.38f));
            var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = m;
        }
        static List<Vector2> Flat(List<Vector3> p) { var o = new List<Vector2>(); foreach (var v in p) o.Add(new Vector2(v.x, v.y)); return o; }

        public void Tick(float dt)
        {
            cord.Step(dt);
            if (!sacReduced) sac.Step(dt);
            if (sacReducing)
            {
                sacScale = Mathf.MoveTowards(sacScale, 0f, dt * 0.9f);
                Vector3 ring = cord.x[1];
                for (int i = 1; i < sac.x.Length; i++) sac.x[i] = Vector3.Lerp(sac.x[i], ring, dt * 3f);
                if (sacScale <= 0.001f) { sacReducing = false; sacReduced = true; sacGO.SetActive(false); }
            }
            if (meshCloth != null)
            {
                meshCloth.Step(dt);
            }
            UpdateVisuals();
            foreach (var s in stitches) s.Update();
        }

        void UpdateVisuals()
        {
            var cp = cord.Smooth(3);
            U.Tube(cp, i => 0.0055f * (1f - 0.15f * Mathf.Clamp01((i - cp.Count * 0.8f) / (cp.Count * 0.2f))), 14, cordMesh);
            if (!sacReduced)
            {
                var sp = sac.Smooth(3); int n = sp.Count;
                U.Tube(sp, i => { float t = i / (float)(n - 1); float r = Mathf.Lerp(0.0035f, 0.011f, Mathf.SmoothStep(0, 0.75f, t)); if (t > 0.8f) r *= Mathf.Sqrt(Mathf.Max(0.02f, 1f - (t - 0.8f) / 0.2f)); return r * sacScale; }, 16, sacMesh);
            }
            // nerve: enters laterally through internal oblique, then runs on the anterior surface of the cord
            nervePts.Clear();
            Vector3 lat = F2P(new Vector3(0.034f, 0.006f, FrontZ(Floor, new Vector2(0.034f, 0.006f), 0f) + 0.0012f));
            nervePts.Add(lat); nervePts.Add(Vector3.Lerp(lat, cord.x[3] + new Vector3(0, 0.002f, 0.0062f), 0.5f) + new Vector3(0, 0, 0.002f));
            for (int i = 3; i < cord.x.Length - 2; i++) nervePts.Add(cord.x[i] + new Vector3(0.0015f, 0.0015f, 0.0063f));
            U.Tube(nervePts, i => 0.0011f, 7, nerveMesh);
            if (meshSheet != null) meshSheet.Refresh();
        }

        // ------------------------------------------------------------ interactions
        public void OpenAponeurosis() { apoOpened = true; }

        public void LiftCord(bool on)
        {
            cordLifted = on;
            if (on)
            {
                int a = cord.x.Length / 3, b = cord.x.Length / 2;
                penroseGrabA = new Rope.Grab { i = a, target = cord.x[a] + new Vector3(0.006f, -0.004f, 0.016f), k = 0.08f };
                penroseGrabB = new Rope.Grab { i = b, target = cord.x[b] + new Vector3(0.006f, -0.004f, 0.016f), k = 0.08f };
                cord.grabs.Add(penroseGrabA); cord.grabs.Add(penroseGrabB);
                penroseTarget = (penroseGrabA.target + penroseGrabB.target) * 0.5f;
            }
            else { cord.grabs.Remove(penroseGrabA); cord.grabs.Remove(penroseGrabB); }
        }
        public Vector3 PenrosePoint { get { return penroseTarget; } }

        public void StartReduceSac() { if (!sacReduced) sacReducing = true; }

        public float SacFundusToRing()
        {
            if (sacReduced) return 0; return Vector3.Distance(sac.x[sac.x.Length - 1], cord.x[1]);
        }

        public void PlaceMesh(Vector3 centrePL)
        {
            if (meshCloth != null) return;
            meshPlaced = true;
            var g = U.Child(patient, "polypropylene_mesh"); meshSheet = g.AddComponent<TissueSheet>();
            meshCloth = meshSheet.core;
            // mesh lies on the posterior wall, long axis parallel to the inguinal ligament, medial end over the pubic tubercle
            Vector2 c = new Vector2(centrePL.x, centrePL.y); Vector2 ax = LigamentDir;
            meshCloth.domain = new Rect(-0.075f, -0.0375f, 0.15f, 0.075f);
            meshCloth.spacing = 0.006f;
            Vector2 pAx = new Vector2(-ax.y, ax.x); if (pAx.y < 0) pAx = -pAx;
            meshCloth.sample = q =>
            {
                Vector2 pl = c + ax * q.x + pAx * q.y;
                float z = FrontZ(Floor, P2F(pl), -0.003f, FrontZ(new[] { "ext_oblique" }, P2F(pl), 0.012f) - 0.0005f) + 0.0012f;
                return new SurfaceSample { pos = F2P(new Vector3(P2F(pl).x, P2F(pl).y, z)), normal = Vector3.forward, thickness = 0.0008f };
            };
            meshCloth.strainPar = 0f; meshCloth.strainPerp = 0f; meshCloth.tetherCompliance = 2e-4f; meshCloth.bendCompliance = 2e-5f;
            meshCloth.Init();
            for (int i = 0; i < meshCloth.w.Count; i++) meshCloth.w[i] = 1f;   // free edges (it is a loose mesh)
            // drop it from above so it settles onto the wall
            for (int i = 0; i < meshCloth.x.Length; i++) meshCloth.SetPos(i, meshCloth.x[i] + new Vector3(0, 0, 0.012f));
            meshCloth.ReloadWeights();
            var tex = TissueTex.MeshGrid();
            meshSheet.surfaceMat = Mats.Fade(Color.white, 0.3f, tex); meshSheet.wallMat = meshSheet.surfaceMat; meshSheet.floorMat = meshSheet.surfaceMat;
            meshSheet.showFloor = false; meshSheet.simulate = false; meshSheet.Build(); meshSheet.Refresh();
        }

        public Vector3 NearestLigamentPoint(Vector2 fxy, out float dist)
        {
            float best = float.MaxValue; Vector2 bp = Ligament[0];
            for (int i = 0; i + 1 < Ligament.Length; i++)
            {
                float t; float d = U.DistPointSeg(fxy, Ligament[i], Ligament[i + 1], out t);
                if (d < best) { best = d; bp = Vector2.Lerp(Ligament[i], Ligament[i + 1], t); }
            }
            dist = best;
            return new Vector3(bp.x, bp.y, FrontZ(new[] { "inguinal_ligament" }, bp, -0.002f));
        }

        public Stitch AddStitch(System.Func<Vector3> a, System.Func<Vector3> b, Color col)
        {
            var g = U.Child(patient, "stitch"); var s = new Stitch { go = g, mesh = new Mesh(), a = a, b = b };
            U.AddMesh(g, s.mesh, Mats.Std(col, 0.45f)); s.Update(); stitches.Add(s); return s;
        }
    }
}
