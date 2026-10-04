// Virtual OR - core utilities: materials, input, model loading.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualOR
{
    // ------------------------------------------------------------------ Materials
    public static class Mats
    {
        static Material baseOpaque, baseMatte, baseFade, baseUnlit, baseParticle;
        static bool urpChecked, urp;

        public static bool IsURP
        {
            get
            {
                if (!urpChecked) { urpChecked = true; urp = Shader.Find("Universal Render Pipeline/Lit") != null; }  // URP installed => assume active
                return urp;
            }
        }

        static Material Base(ref Material cache, string resource, string shaderName)
        {
            if (cache != null) return cache;
            var res = Resources.Load<Material>("VirtualOR/" + resource);
            if (res != null && !IsURP) cache = res;
            else
            {
                Shader sh = IsURP ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find(shaderName);
                if (sh == null) sh = Shader.Find("Standard");
                cache = new Material(sh);
            }
            return cache;
        }

        public static Material Std(Color c, float smooth = 0.4f, float metal = 0f, Texture tex = null)
        {
            var m = new Material(Base(ref baseOpaque, "VOR_Opaque", "Standard"));
            SetColor(m, c); SetTex(m, tex);
            m.SetFloat("_Glossiness", smooth); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            return m;
        }

        // cloth, paper, foam: diffuse only (no environment reflections, no specular highlights)
        public static Material Matte(Color c, Texture tex = null)
        {
            var m = new Material(Base(ref baseMatte, "VOR_Matte", "Standard"));
            SetColor(m, c); SetTex(m, tex);
            m.SetFloat("_Glossiness", 0.1f); m.SetFloat("_Smoothness", 0.1f); m.SetFloat("_Metallic", 0f);
            MakeMatte(m);
            return m;
        }
        public static void MakeMatte(Material m)
        {
            m.SetFloat("_Metallic", 0f); m.SetFloat("_Glossiness", 0.1f); m.SetFloat("_Smoothness", 0.1f);
            if (IsURP) { m.SetFloat("_SpecularHighlights", 0f); m.SetFloat("_EnvironmentReflections", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); m.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF"); return; }
            m.SetFloat("_GlossyReflections", 0f); m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_GLOSSYREFLECTIONS_OFF"); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        }
        public static void SetSurface(Material m, float smooth, float metal = 0f)
        {
            m.SetFloat("_Glossiness", smooth); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
        }

        // living tissue: albedo + tangent-space normal map + smoothness map (alpha); variant kept by Resources/VirtualOR/VOR_Tissue.mat
        static Material baseTissue;
        public static Material Tissue(TissueMaps.Set maps, float bump = 1f, float glossScale = 1f, Color? tint = null)
        {
            var m = new Material(Base(ref baseTissue, "VOR_Tissue", "Standard"));
            SetColor(m, tint ?? Color.white); SetTex(m, maps.albedo);
            m.SetFloat("_Metallic", 0f);
            if (maps.normal != null) { m.SetTexture("_BumpMap", maps.normal); m.SetFloat("_BumpScale", bump); m.EnableKeyword("_NORMALMAP"); }
            if (maps.gloss != null && !IsURP) { m.SetTexture("_MetallicGlossMap", maps.gloss); m.SetFloat("_GlossMapScale", glossScale); m.EnableKeyword("_METALLICGLOSSMAP"); }
            m.SetFloat("_Glossiness", 0.45f * glossScale); m.SetFloat("_Smoothness", 0.45f * glossScale);
            return m;
        }

        public static Material Fade(Color c, float smooth = 0.6f, Texture tex = null)
        {
            var m = new Material(Base(ref baseFade, "VOR_Fade", "Standard"));
            if (!IsURP)
            {
                m.SetFloat("_Mode", 3f);
                m.SetInt("_SrcBlend", (int)BlendMode.One);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 3000;
            }
            else
            {
                m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
                m.SetInt("_SrcBlend", (int)BlendMode.One); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 3000;
            }
            SetColor(m, c); SetTex(m, tex);
            m.SetFloat("_Glossiness", smooth); m.SetFloat("_Smoothness", smooth);
            return m;
        }

        public static Material Unlit(Texture tex, Color c)
        {
            Material m;
            if (IsURP) { var sh = Shader.Find("Universal Render Pipeline/Unlit"); m = new Material(sh != null ? sh : Shader.Find("Standard")); }
            else m = new Material(Base(ref baseUnlit, "VOR_Unlit", "Unlit/Texture"));
            SetColor(m, c); SetTex(m, tex);
            return m;
        }

        public static Material Particle(Texture tex)
        {
            var m = new Material(Base(ref baseParticle, "VOR_Particle", "Sprites/Default"));
            if (tex != null) m.mainTexture = tex;
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }
        public static void SetTex(Material m, Texture t)
        {
            if (t == null) return;
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
        }
        public static void SetGlossMap(Material m, Texture t, float scale = 1f)
        {
            if (IsURP) return;
            m.SetTexture("_MetallicGlossMap", t);
            m.EnableKeyword("_METALLICGLOSSMAP");
            m.SetFloat("_GlossMapScale", scale);
        }
        public static void SetEmission(Material m, Color c)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
    }

    // ------------------------------------------------------------------ Input (via IMGUI events, works with any input backend)
    public class InputState : MonoBehaviour
    {
        public static Vector2 Mouse;              // screen pixels, origin bottom-left
        public static bool LMB, RMB;
        public static bool LMBDown, LMBUp, RMBDown, RMBUp;
        public static Vector2 LookDelta, OrbitDelta;
        public static float Scroll;
        public static bool OverUI;                // set by HUD each frame
        static readonly HashSet<KeyCode> held = new HashSet<KeyCode>();
        static readonly HashSet<KeyCode> down = new HashSet<KeyCode>();
        static readonly HashSet<KeyCode> pendingDown = new HashSet<KeyCode>();
        static bool pLDown, pLUp, pRDown, pRUp; static Vector2 pLook, pOrbit; static float pScroll; static bool MMB;

        public static bool Key(KeyCode k) { return held.Contains(k); }
        public static bool KeyDown(KeyCode k) { return down.Contains(k); }

        void Awake() { useGUILayout = false; }

        void OnGUI()
        {
            Event e = Event.current;
            Mouse = new Vector2(e.mousePosition.x, Screen.height - e.mousePosition.y);
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0) { LMB = true; pLDown = true; }
                    if (e.button == 1) { RMB = true; pRDown = true; }
                    if (e.button == 2) MMB = true;
                    break;
                case EventType.MouseUp:
                    if (e.button == 0) { LMB = false; pLUp = true; }
                    if (e.button == 1) { RMB = false; pRUp = true; }
                    if (e.button == 2) MMB = false;
                    break;
                case EventType.MouseDrag:
                    if (MMB || e.button == 2 || ((RMB || e.button == 1) && (e.shift || e.alt))) pOrbit += e.delta;
                    else if (RMB || e.button == 1) pLook += e.delta;
                    break;
                case EventType.ScrollWheel:
                    pScroll += e.delta.y;
                    break;
                case EventType.KeyDown:
                    if (e.keyCode != KeyCode.None) { if (!held.Contains(e.keyCode)) pendingDown.Add(e.keyCode); held.Add(e.keyCode); }
                    break;
                case EventType.KeyUp:
                    if (e.keyCode != KeyCode.None) held.Remove(e.keyCode);
                    break;
            }
        }

        // Called first thing every frame by App (execution order handled there).
        public static void BeginFrame()
        {
            LMBDown = pLDown; LMBUp = pLUp; RMBDown = pRDown; RMBUp = pRUp; LookDelta = pLook; OrbitDelta = pOrbit; Scroll = pScroll;
            pLDown = pLUp = pRDown = pRUp = false; pLook = pOrbit = Vector2.zero; pScroll = 0f;
            down.Clear(); foreach (var k in pendingDown) down.Add(k); pendingDown.Clear();
        }

        public static void ReleaseAll() { held.Clear(); LMB = RMB = MMB = false; }
    }

    // ------------------------------------------------------------------ Small helpers
    public static class U
    {
        public static float Cross2(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }

        public static float DistPointSeg(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 ab = b - a; float l2 = ab.sqrMagnitude;
            t = l2 > 1e-12f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }

        public static float DistPointPoly(Vector2 p, List<Vector2> poly, out float arc, out int seg)
        {
            float best = float.MaxValue; arc = 0; seg = 0; float acc = 0;
            for (int i = 0; i + 1 < poly.Count; i++)
            {
                float t; float d = DistPointSeg(p, poly[i], poly[i + 1], out t);
                float len = Vector2.Distance(poly[i], poly[i + 1]);
                if (d < best) { best = d; arc = acc + t * len; seg = i; }
                acc += len;
            }
            return best;
        }

        public static float PolyLength(List<Vector2> poly)
        {
            float s = 0; for (int i = 0; i + 1 < poly.Count; i++) s += Vector2.Distance(poly[i], poly[i + 1]); return s;
        }

        // Resample polyline at fixed spacing (keeps endpoints).
        public static List<Vector2> Resample(List<Vector2> poly, float step)
        {
            var o = new List<Vector2>();
            if (poly.Count == 0) return o;
            o.Add(poly[0]);
            float total = PolyLength(poly);
            if (total < 1e-6f) return o;
            int n = Mathf.Max(1, Mathf.RoundToInt(total / step));
            float ds = total / n;
            int seg = 0; float segStart = 0;
            for (int k = 1; k < n; k++)
            {
                float target = k * ds;
                while (seg + 1 < poly.Count - 1 && segStart + Vector2.Distance(poly[seg], poly[seg + 1]) < target)
                { segStart += Vector2.Distance(poly[seg], poly[seg + 1]); seg++; }
                float L = Vector2.Distance(poly[seg], poly[seg + 1]);
                float t = L > 1e-9f ? (target - segStart) / L : 0;
                o.Add(Vector2.Lerp(poly[seg], poly[seg + 1], Mathf.Clamp01(t)));
            }
            o.Add(poly[poly.Count - 1]);
            return o;
        }

        // Value noise for procedural textures
        static int Hash(int x, int y, int s) { unchecked { int h = x * 374761393 + y * 668265263 + s * 982451653; h = (h ^ (h >> 13)) * 1274126177; return h ^ (h >> 16); } }
        // Worley F2-F1: ~0 on cell borders, larger towards cell centres (good for fat lobules, scales). Also returns the cell id.
        public static float CellEdge(float x, float y, int seed, out int id)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float b1 = 9, b2 = 9; id = 0;
            for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
                {
                    float px = xi + i + R(xi + i, yi + j, seed), py = yi + j + R(xi + i, yi + j, seed + 7);
                    float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                    if (d < b1) { b2 = b1; b1 = d; id = Hash(xi + i, yi + j, seed); } else if (d < b2) b2 = d;
                }
            return b2 - b1;
        }
        static float R(int x, int y, int s) { return (Hash(x, y, s) & 0xffff) / 65535f; }
        public static float Noise(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = R(xi, yi, seed), b = R(xi + 1, yi, seed), c = R(xi, yi + 1, seed), d = R(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }
        public static float Fbm(float x, float y, int seed, int oct = 4)
        {
            float s = 0, a = 0.5f, f = 1; for (int i = 0; i < oct; i++) { s += a * Noise(x * f, y * f, seed + i * 17); f *= 2.03f; a *= 0.5f; } return s;
        }
        // Worley-ish cellular distance (for fat lobules)
        public static float Cells(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y); float best = 9;
            for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
                {
                    float px = xi + i + R(xi + i, yi + j, seed), py = yi + j + R(xi + i, yi + j, seed + 7);
                    float d = (px - x) * (px - x) + (py - y) * (py - y); if (d < best) best = d;
                }
            return Mathf.Sqrt(best);
        }

        public static Texture2D MakeTex(int w, int h, Func<float, float, Color> f, bool mip = true, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, mip);
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f((x + 0.5f) / w, (y + 0.5f) / h);
            t.SetPixels32(px); t.wrapMode = wrap; t.Apply(mip); t.anisoLevel = 4;
            return t;
        }

        public static GameObject Child(Transform parent, string name)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false); return g;
        }

        public static MeshRenderer AddMesh(GameObject g, Mesh m, Material mat)
        {
            g.AddComponent<MeshFilter>().sharedMesh = m;
            var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; return r;
        }

        public static Mesh Tube(List<Vector3> pts, Func<int, float> radius, int sides, Mesh reuse = null)
        {
            var m = reuse ?? new Mesh();
            int n = pts.Count; if (n < 2) { m.Clear(); return m; }
            var v = new Vector3[n * sides]; var nr = new Vector3[n * sides]; var uv = new Vector2[n * sides];
            Vector3 prevN = Vector3.up;
            float acc = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 t = (pts[Mathf.Min(n - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]).normalized;
                if (t.sqrMagnitude < 1e-8f) t = Vector3.forward;
                Vector3 nn = Vector3.Cross(t, Vector3.Cross(prevN, t)); if (nn.sqrMagnitude < 1e-6f) nn = Vector3.Cross(t, Vector3.right); nn.Normalize(); prevN = nn;
                Vector3 b = Vector3.Cross(t, nn);
                if (i > 0) acc += Vector3.Distance(pts[i], pts[i - 1]);
                float r = radius(i);
                for (int s = 0; s < sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides; Vector3 d = nn * Mathf.Cos(a) + b * Mathf.Sin(a);
                    v[i * sides + s] = pts[i] + d * r; nr[i * sides + s] = d; uv[i * sides + s] = new Vector2(s / (float)sides, acc * 30f);
                }
            }
            var idx = new int[(n - 1) * sides * 6]; int k = 0;
            for (int i = 0; i + 1 < n; i++) for (int s = 0; s < sides; s++)
                {
                    int a = i * sides + s, b2 = i * sides + (s + 1) % sides, c = (i + 1) * sides + s, d = (i + 1) * sides + (s + 1) % sides;
                    idx[k++] = a; idx[k++] = c; idx[k++] = b2; idx[k++] = b2; idx[k++] = c; idx[k++] = d;
                }
            bool topo = reuse == null || reuse.vertexCount != v.Length;
            if (topo) m.Clear();
            m.vertices = v; m.normals = nr; m.uv = uv;
            if (topo) m.triangles = idx;
            m.RecalculateBounds();
            return m;
        }
    }

    // ------------------------------------------------------------------ VOR model format loader
    [Serializable] public class VorMesh { public string name; public int vertexCount; public int pos; public int nrm; public int uv; public int[] subOffsets; public int[] subCounts; public int[] subMaterials; public int skin; public int joints; public int weights; }
    [Serializable] public class VorMat { public string name; public float[] color; public string texture; public float metallic; public float smoothness; public int blend; public int unlit; }
    [Serializable] public class VorBone { public string name; public float[] pos; public float[] rot; public float[] scl; }
    [Serializable] public class VorClip { public string name; public int frames; public int fps; public int data; }
    [Serializable] public class VorHeader { public VorMesh[] meshes; public VorMat[] materials; public VorBone[] bones; public VorClip[] clips; public int bindposes; }

    public class VorModel
    {
        public GameObject root;
        public List<MeshFilter> filters = new List<MeshFilter>();
        public List<Mesh> meshes = new List<Mesh>();
        public SkinnedMeshRenderer skinned;
        public Transform[] bones;
        public Dictionary<string, float[]> clips = new Dictionary<string, float[]>();
        public Dictionary<string, int> clipFrames = new Dictionary<string, int>();
        public Material[] materials;
    }

    public static class VorLoader
    {
        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();

        public static Texture2D LoadTexture(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Texture2D t;
            if (texCache.TryGetValue(name, out t)) return t;
            var ta = Resources.Load<TextAsset>("VirtualOR/Models/" + name);
            if (ta == null) { Debug.LogWarning("[VirtualOR] missing texture " + name); return null; }
            t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            ImageConversion.LoadImage(t, ta.bytes, false);
            t.wrapMode = TextureWrapMode.Repeat; t.anisoLevel = 4; t.name = name;
            texCache[name] = t;
            return t;
        }

        public static VorModel Load(string name, Transform parent, bool readable = true)
        {
            var ta = Resources.Load<TextAsset>("VirtualOR/Models/" + name);
            if (ta == null) { Debug.LogError("[VirtualOR] model not found: Resources/VirtualOR/Models/" + name + ".bytes"); return null; }
            byte[] b = ta.bytes;
            if (b[0] != 'V' || b[1] != 'O' || b[2] != 'R' || b[3] != '1') { Debug.LogError("[VirtualOR] bad model file " + name); return null; }
            int hl = BitConverter.ToInt32(b, 4);
            string json = System.Text.Encoding.UTF8.GetString(b, 8, hl);
            var h = JsonUtility.FromJson<VorHeader>(json);
            int bs = 8 + hl;
            var model = new VorModel();
            model.root = new GameObject(name);
            model.root.transform.SetParent(parent, false);
            model.materials = new Material[h.materials.Length];
            for (int i = 0; i < h.materials.Length; i++)
            {
                var vm = h.materials[i];
                Color c = vm.color != null && vm.color.Length >= 4 ? new Color(vm.color[0], vm.color[1], vm.color[2], vm.color[3]) : Color.white;
                Texture2D tex = LoadTexture(vm.texture);
                Material m;
                if (vm.unlit == 1) m = Mats.Unlit(tex, c);
                else if (vm.blend == 1) m = Mats.Fade(c, vm.smoothness, tex);
                else m = Mats.Std(c, Mathf.Clamp(vm.smoothness, 0.05f, 0.9f), vm.metallic, tex);
                m.name = vm.name;
                model.materials[i] = m;
            }
            if (h.bones != null && h.bones.Length > 0)
            {
                model.bones = new Transform[h.bones.Length];
                var boneRoot = new GameObject("bones").transform; boneRoot.SetParent(model.root.transform, false);
                for (int i = 0; i < h.bones.Length; i++)
                {
                    var vb = h.bones[i];
                    var t = new GameObject(vb.name).transform; t.SetParent(boneRoot, false);
                    t.localPosition = new Vector3(vb.pos[0], vb.pos[1], vb.pos[2]);
                    t.localRotation = new Quaternion(vb.rot[0], vb.rot[1], vb.rot[2], vb.rot[3]);
                    t.localScale = new Vector3(vb.scl[0], vb.scl[1], vb.scl[2]);
                    model.bones[i] = t;
                }
                if (h.clips != null)
                    foreach (var c in h.clips)
                    {
                        int count = c.frames * h.bones.Length * 7;
                        var arr = new float[count];
                        Buffer.BlockCopy(b, bs + c.data, arr, 0, count * 4);
                        model.clips[c.name] = arr; model.clipFrames[c.name] = c.frames;
                    }
            }
            foreach (var vm in h.meshes)
            {
                int n = vm.vertexCount;
                var mesh = new Mesh(); mesh.name = vm.name;
                if (n > 65000) mesh.indexFormat = IndexFormat.UInt32;
                var fv = new float[n * 3]; Buffer.BlockCopy(b, bs + vm.pos, fv, 0, n * 12);
                var verts = new Vector3[n]; for (int i = 0; i < n; i++) verts[i] = new Vector3(fv[i * 3], fv[i * 3 + 1], fv[i * 3 + 2]);
                mesh.vertices = verts;
                if (vm.nrm >= 0)
                {
                    Buffer.BlockCopy(b, bs + vm.nrm, fv, 0, n * 12);
                    var nr = new Vector3[n]; for (int i = 0; i < n; i++) nr[i] = new Vector3(fv[i * 3], fv[i * 3 + 1], fv[i * 3 + 2]);
                    mesh.normals = nr;
                }
                if (vm.uv >= 0)
                {
                    var fu = new float[n * 2]; Buffer.BlockCopy(b, bs + vm.uv, fu, 0, n * 8);
                    var uv = new Vector2[n]; for (int i = 0; i < n; i++) uv[i] = new Vector2(fu[i * 2], fu[i * 2 + 1]);
                    mesh.uv = uv;
                }
                int subs = vm.subOffsets.Length;
                mesh.subMeshCount = subs;
                var mats = new Material[subs];
                for (int s = 0; s < subs; s++)
                {
                    var ui = new uint[vm.subCounts[s]]; Buffer.BlockCopy(b, bs + vm.subOffsets[s], ui, 0, vm.subCounts[s] * 4);
                    var ii = new int[ui.Length]; for (int k = 0; k < ui.Length; k++) ii[k] = (int)ui[k];
                    mesh.SetTriangles(ii, s, false);
                    mats[s] = model.materials[Mathf.Clamp(vm.subMaterials[s], 0, model.materials.Length - 1)];
                }
                if (vm.nrm < 0) mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                var go = new GameObject(vm.name); go.transform.SetParent(model.root.transform, false);
                if (vm.skin == 1 && model.bones != null)
                {
                    var js = new ushort[n * 4]; Buffer.BlockCopy(b, bs + vm.joints, js, 0, n * 8);
                    var ws = new float[n * 4]; Buffer.BlockCopy(b, bs + vm.weights, ws, 0, n * 16);
                    var bw = new BoneWeight[n];
                    for (int i = 0; i < n; i++)
                    {
                        bw[i].boneIndex0 = js[i * 4]; bw[i].boneIndex1 = js[i * 4 + 1]; bw[i].boneIndex2 = js[i * 4 + 2]; bw[i].boneIndex3 = js[i * 4 + 3];
                        bw[i].weight0 = ws[i * 4]; bw[i].weight1 = ws[i * 4 + 1]; bw[i].weight2 = ws[i * 4 + 2]; bw[i].weight3 = ws[i * 4 + 3];
                    }
                    mesh.boneWeights = bw;
                    var bp = new float[model.bones.Length * 16]; Buffer.BlockCopy(b, bs + h.bindposes, bp, 0, bp.Length * 4);
                    var poses = new Matrix4x4[model.bones.Length];
                    for (int j = 0; j < poses.Length; j++)
                    {
                        var M = new Matrix4x4();
                        for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) M[r, c] = bp[j * 16 + c * 4 + r]; // column-major
                        poses[j] = M;
                    }
                    mesh.bindposes = poses;
                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.sharedMesh = mesh; smr.bones = model.bones; smr.sharedMaterials = mats;
                    smr.rootBone = model.bones[0]; smr.updateWhenOffscreen = true;
                    model.skinned = smr;
                }
                else
                {
                    var mf = go.AddComponent<MeshFilter>(); mf.sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats;
                    model.filters.Add(mf);
                }
                model.meshes.Add(mesh);
            }
            return model;
        }

        // Pose all bones of a skinned model from a clip at a given time (bones stored flat, root-space transforms).
        public static void SampleClip(VorModel m, string clip, float time, bool loop, float weight = 1f)
        {
            float[] d; if (m.bones == null || !m.clips.TryGetValue(clip, out d)) return;
            int frames = m.clipFrames[clip]; int nb = m.bones.Length;
            float f = time * 30f;
            if (loop) { f = f % (frames - 1); if (f < 0) f += frames - 1; } else f = Mathf.Clamp(f, 0, frames - 1);
            int f0 = Mathf.FloorToInt(f), f1 = Mathf.Min(frames - 1, f0 + 1); float t = f - f0;
            for (int i = 0; i < nb; i++)
            {
                int a = (f0 * nb + i) * 7, b = (f1 * nb + i) * 7;
                var p = Vector3.Lerp(new Vector3(d[a], d[a + 1], d[a + 2]), new Vector3(d[b], d[b + 1], d[b + 2]), t);
                var q = Quaternion.Slerp(new Quaternion(d[a + 3], d[a + 4], d[a + 5], d[a + 6]), new Quaternion(d[b + 3], d[b + 4], d[b + 5], d[b + 6]), t);
                var tr = m.bones[i];
                if (weight >= 0.999f) { tr.localPosition = p; tr.localRotation = q; }
                else { tr.localPosition = Vector3.Lerp(tr.localPosition, p, weight); tr.localRotation = Quaternion.Slerp(tr.localRotation, q, weight); }
            }
        }
    }
}
