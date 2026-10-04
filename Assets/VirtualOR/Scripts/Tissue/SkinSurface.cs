// Virtual OR - skin surface layers: antiseptic prep, marking, burns, and blood (flow on skin + wound pool).
using System.Collections.Generic;
using UnityEngine;

namespace VirtualOR
{
    public class Bleeder
    {
        public int cutIndex; public Vector2 uv; public float rate;  // ml/s
        public bool arterial; public bool sealed_; public float age; public string vessel;
        public GameObject blob;
    }

    public class SkinSurface
    {
        public const int Res = 384;      // texture resolution
        public const int G = 96;         // blood grid resolution
        public Texture2D albedo, gloss;
        public Rect domain;
        Color32[] basePx; Color32[] outPx; Color32[] glossPx;
        float[] prep, wet, marker, burn;   // per texel (Res x Res)
        float[] friction;                  // per texel scrub work
        public float[] blood = new float[G * G], clot = new float[G * G], height = new float[G * G];
        bool[] woundCell = new bool[G * G];
        public float pool;                 // ml of blood in the wound
        public float bloodLoss;            // ml total
        public float scrubSeconds;
        public readonly List<Bleeder> bleeders = new List<Bleeder>();
        public Color prepColor = new Color(0.78f, 0.38f, 0.12f);   // tinted chlorhexidine-alcohol
        bool dirty = true; float composeTimer;

        public void Init(Rect dom, Color skin, System.Func<Vector2, float> worldHeight)
        {
            domain = dom;
            int n = Res * Res;
            basePx = new Color32[n]; outPx = new Color32[n]; glossPx = new Color32[n];
            prep = new float[n]; wet = new float[n]; marker = new float[n]; burn = new float[n]; friction = new float[n];
            for (int y = 0; y < Res; y++)
                for (int x = 0; x < Res; x++)
                {
                    float u = x / (float)Res, v = y / (float)Res;
                    float wx = u * dom.width * 1000f, wy = v * dom.height * 1000f; // mm
                    float pores = U.Noise(wx * 1.6f, wy * 1.6f, 3); pores = pores > 0.82f ? (pores - 0.82f) * 2.2f : 0;
                    float mott = U.Fbm(wx * 0.05f, wy * 0.05f, 11) - 0.5f;
                    float fine = U.Noise(wx * 0.5f, wy * 0.5f, 5) - 0.5f;
                    float stub = U.Noise(wx * 2.4f, wy * 2.4f, 21) > 0.9f ? 0.12f : 0f;  // shaved hair follicles
                    Color c = skin;
                    c.r += mott * 0.07f + fine * 0.02f; c.g += mott * 0.05f + fine * 0.02f; c.b += mott * 0.04f;
                    c *= 1f - pores * 0.25f - stub;
                    c.a = 1; basePx[y * Res + x] = c;
                }
            albedo = new Texture2D(Res, Res, TextureFormat.RGBA32, true); albedo.wrapMode = TextureWrapMode.Clamp; albedo.anisoLevel = 4;
            gloss = new Texture2D(Res, Res, TextureFormat.RGBA32, true); gloss.wrapMode = TextureWrapMode.Clamp;
            for (int j = 0; j < G; j++) for (int i = 0; i < G; i++)
                    height[j * G + i] = worldHeight(new Vector2(dom.xMin + (i + 0.5f) / G * dom.width, dom.yMin + (j + 0.5f) / G * dom.height));
            Compose();
        }

        Vector2 ToTex(Vector2 uv) { return new Vector2((uv.x - domain.xMin) / domain.width * Res, (uv.y - domain.yMin) / domain.height * Res); }
        float PxPerM { get { return Res / domain.width; } }

        // ------------------------------------------------------------ prep
        public void PaintPrep(Vector2 uv, float radiusM, float movedM, float dt)
        {
            Vector2 c = ToTex(uv); float r = radiusM * PxPerM;
            int x0 = Mathf.Max(0, (int)(c.x - r)), x1 = Mathf.Min(Res - 1, (int)(c.x + r)), y0 = Mathf.Max(0, (int)(c.y - r)), y1 = Mathf.Min(Res - 1, (int)(c.y + r));
            float work = Mathf.Clamp01(movedM / 0.004f);
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c) / r; if (d > 1) continue;
                    float f = (1 - d * d);
                    int i = y * Res + x;
                    prep[i] = Mathf.Min(1, prep[i] + Mathf.Max(f, 0.35f) * dt * 9f);   // one pass is enough
                    wet[i] = Mathf.Min(1, wet[i] + f * dt * 6f);
                    friction[i] += f * work * dt;
                }
            if (movedM > 0.0005f) scrubSeconds += dt;
            dirty = true;
        }

        // finish the prep over the whole window (used once most of it has been scrubbed)
        public void CompletePrep()
        {
            for (int i = 0; i < prep.Length; i++) { if (prep[i] < 1) { prep[i] = 1; wet[i] = Mathf.Max(wet[i], 0.8f); } }
            dirty = true;
        }

        // fraction of the target rect (uv) covered with prep
        public float PrepCoverage(Rect area)
        {
            int tot = 0, ok = 0;
            for (int y = 0; y < Res; y += 2) for (int x = 0; x < Res; x += 2)
                {
                    Vector2 uv = new Vector2(domain.xMin + x / (float)Res * domain.width, domain.yMin + y / (float)Res * domain.height);
                    if (!area.Contains(uv)) continue; tot++; if (prep[y * Res + x] > 0.5f) ok++;
                }
            return tot == 0 ? 0 : ok / (float)tot;
        }
        public float MaxWetness()
        {
            float m = 0; for (int i = 0; i < wet.Length; i += 7) m = Mathf.Max(m, wet[i]); return m;
        }
        public void Dry(float dt, float rate)
        {
            bool any = false;
            for (int i = 0; i < wet.Length; i++) if (wet[i] > 0) { wet[i] = Mathf.Max(0, wet[i] - dt * rate); any = true; }
            if (any) dirty = true;
        }
        public float PrepAt(Vector2 uv) { Vector2 c = ToTex(uv); int x = Mathf.Clamp((int)c.x, 0, Res - 1), y = Mathf.Clamp((int)c.y, 0, Res - 1); return prep[y * Res + x]; }

        // ------------------------------------------------------------ marking & burns
        public void DrawLine(List<Vector2> pts, float widthM, bool dashed, float strength = 1f)
        {
            float acc = 0;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                float L = Vector2.Distance(pts[i], pts[i + 1]); int steps = Mathf.Max(1, (int)(L / 0.0005f));
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / (float)steps; float a = acc + L * t;
                    if (dashed && (a % 0.008f) > 0.005f) continue;
                    Stamp(marker, Vector2.Lerp(pts[i], pts[i + 1], t), widthM, strength);
                }
                acc += L;
            }
            dirty = true;
        }
        public void Burn(Vector2 uv, float r) { Stamp(burn, uv, r, 0.6f); dirty = true; }

        void Stamp(float[] layer, Vector2 uv, float rM, float strength)
        {
            Vector2 c = ToTex(uv); float r = Mathf.Max(0.8f, rM * PxPerM);
            int x0 = Mathf.Max(0, (int)(c.x - r - 1)), x1 = Mathf.Min(Res - 1, (int)(c.x + r + 1)), y0 = Mathf.Max(0, (int)(c.y - r - 1)), y1 = Mathf.Min(Res - 1, (int)(c.y + r + 1));
            for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c); if (d > r) continue;
                    int i = y * Res + x; layer[i] = Mathf.Min(1, layer[i] + strength * (1 - d / r * 0.6f));
                }
        }

        // ------------------------------------------------------------ blood
        int Cell(Vector2 uv)
        {
            int i = Mathf.Clamp((int)((uv.x - domain.xMin) / domain.width * G), 0, G - 1), j = Mathf.Clamp((int)((uv.y - domain.yMin) / domain.height * G), 0, G - 1);
            return j * G + i;
        }
        public void AddBloodAt(Vector2 uv, float ml) { blood[Cell(uv)] += ml / CellMl; dirty = true; }
        float CellMl { get { return (domain.width / G) * (domain.height / G) * 1e6f * 0.001f; } } // ml per mm of film on one cell

        public void MarkWound(TissueCore core)
        {
            for (int i = 0; i < woundCell.Length; i++) woundCell[i] = false;
            if (!core.HasCut) return;
            for (int k = 1; k < core.cut.Count - 1; k++)
            {
                float gap = core.Gap(k);
                if (gap < 0.0015f) continue;
                Vector2 c = core.cut[k]; float r = gap * 0.5f + 0.0005f;
                int ci = Cell(c); int cx = ci % G, cy = ci / G; int rr = Mathf.CeilToInt(r / (domain.width / G));
                for (int y = cy - rr; y <= cy + rr; y++) for (int x = cx - rr; x <= cx + rr; x++)
                    {
                        if (x < 0 || y < 0 || x >= G || y >= G) continue;
                        Vector2 cu = new Vector2(domain.xMin + (x + 0.5f) / G * domain.width, domain.yMin + (y + 0.5f) / G * domain.height);
                        if (Vector2.Distance(cu, c) <= r) woundCell[y * G + x] = true;
                    }
            }
        }

        public float WoundCapacityMl = 6f;

        public void StepBlood(float dt, TissueCore core)
        {
            // bleeders feed the wound pool
            foreach (var b in bleeders)
            {
                if (b.sealed_) continue;
                b.age += dt;
                float r = b.rate * (b.arterial ? (0.7f + 0.6f * Mathf.Max(0, Mathf.Sin(Time.time * 7.5f))) : 1f);
                pool += r * dt; bloodLoss += r * dt;
            }
            // overflow onto the skin at the lowest wound edge
            if (pool > WoundCapacityMl && core != null && core.HasCut)
            {
                float over = (pool - WoundCapacityMl) * Mathf.Min(1, dt * 2f); pool -= over;
                int lowK = 1; float lowH = float.MaxValue;
                for (int k = 1; k < core.cut.Count - 1; k++) { float hh = height[Cell(core.cut[k])]; if (hh < lowH) { lowH = hh; lowK = k; } }
                Vector2 dir = Vector2.zero;
                if (core.cut.Count > 2) { Vector2 t = core.cut[Mathf.Min(core.cut.Count - 1, lowK + 1)] - core.cut[Mathf.Max(0, lowK - 1)]; dir = new Vector2(-t.y, t.x).normalized; }
                Vector2 p1 = core.cut[lowK] + dir * (core.Gap(lowK) * 0.6f + 0.002f), p2 = core.cut[lowK] - dir * (core.Gap(lowK) * 0.6f + 0.002f);
                Vector2 spill = height[Cell(p1)] < height[Cell(p2)] ? p1 : p2;
                AddBloodAt(spill, over);
            }
            // gravity-driven film flow on skin
            float kf = Mathf.Min(0.24f, dt * 9f);
            var nb = new float[blood.Length];
            System.Array.Copy(blood, nb, blood.Length);
            bool any = false;
            for (int j = 0; j < G; j++)
                for (int i = 0; i < G; i++)
                {
                    int c = j * G + i; float bc = blood[c]; if (bc < 1e-4f) continue; any = true;
                    if (woundCell[c]) { pool += bc * CellMl; nb[c] -= bc; continue; }
                    float H = height[c] + bc * 0.001f;
                    float tot = 0; float d0 = 0, d1 = 0, d2 = 0, d3 = 0;
                    if (i > 0) d0 = Mathf.Max(0, H - height[c - 1] - blood[c - 1] * 0.001f);
                    if (i < G - 1) d1 = Mathf.Max(0, H - height[c + 1] - blood[c + 1] * 0.001f);
                    if (j > 0) d2 = Mathf.Max(0, H - height[c - G] - blood[c - G] * 0.001f);
                    if (j < G - 1) d3 = Mathf.Max(0, H - height[c + G] - blood[c + G] * 0.001f);
                    tot = d0 + d1 + d2 + d3; if (tot <= 0) continue;
                    float mobile = Mathf.Max(0, bc - 0.06f) * (1f - clot[c]);   // thin films stick to skin
                    float move = mobile * kf;
                    if (d0 > 0) nb[c - 1] += move * d0 / tot; if (d1 > 0) nb[c + 1] += move * d1 / tot;
                    if (d2 > 0) nb[c - G] += move * d2 / tot; if (d3 > 0) nb[c + G] += move * d3 / tot;
                    nb[c] -= move;
                }
            blood = nb;
            for (int c = 0; c < clot.Length; c++) if (blood[c] > 0.01f) clot[c] = Mathf.Min(1, clot[c] + dt / 90f);
            if (any) dirty = true;
        }

        public float Swab(Vector2 uv, float rM, float dt)
        {
            int c = Cell(uv); int cx = c % G, cy = c / G; int rr = Mathf.CeilToInt(rM / (domain.width / G)); float taken = 0;
            for (int y = cy - rr; y <= cy + rr; y++) for (int x = cx - rr; x <= cx + rr; x++)
                {
                    if (x < 0 || y < 0 || x >= G || y >= G) continue;
                    int i = y * G + x; float t = blood[i] * Mathf.Min(1, dt * 6f); blood[i] -= t; taken += t * CellMl;
                }
            dirty = true; return taken;
        }

        // ------------------------------------------------------------ compose
        public void Update(float dt)
        {
            composeTimer -= dt;
            if (dirty && composeTimer <= 0) { Compose(); composeTimer = 0.1f; dirty = false; }
        }

        public void Compose()
        {
            Color32 bl = new Color32(110, 8, 10, 255), bd = new Color32(62, 6, 8, 255), mk = new Color32(92, 40, 120, 255), ch = new Color32(40, 24, 14, 255);
            Color32 pc = prepColor;
            for (int y = 0; y < Res; y++)
            {
                int gy = Mathf.Min(G - 1, y * G / Res);
                for (int x = 0; x < Res; x++)
                {
                    int i = y * Res + x; Color32 c = basePx[i];
                    float p = prep[i];
                    if (p > 0) { float s = p * (0.70f + 0.08f * Mathf.Clamp01(friction[i] * 2f)); c = Color32.Lerp(c, Mul(c, pc), Mathf.Clamp01(s)); }   // even tint (no stripes)
                    if (marker[i] > 0) c = Color32.Lerp(c, mk, Mathf.Clamp01(marker[i]));
                    if (burn[i] > 0) c = Color32.Lerp(c, ch, Mathf.Clamp01(burn[i]));
                    int gx = Mathf.Min(G - 1, x * G / Res); int gi = gy * G + gx;
                    float b = blood[gi];
                    if (b > 0.01f) c = Color32.Lerp(c, Color32.Lerp(bl, bd, clot[gi]), Mathf.Clamp01(b * 4f));
                    outPx[i] = c;
                    float sm = 0.32f + wet[i] * 0.55f + Mathf.Clamp01(b * 4f) * 0.5f * (1 - clot[gi] * 0.5f);
                    glossPx[i] = new Color32(0, 0, 0, (byte)(Mathf.Clamp01(sm) * 255));
                }
            }
            albedo.SetPixels32(outPx); albedo.Apply(true);
            gloss.SetPixels32(glossPx); gloss.Apply(true);
        }
        static Color32 Mul(Color32 a, Color32 b) { return new Color32((byte)(a.r * b.r / 255), (byte)(a.g * b.g / 255), (byte)(a.b * b.b / 255), 255); }
    }
}
