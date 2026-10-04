// Virtual OR - procedural tissue maps for the wound: albedo + normal (from a height field) + smoothness.
// Reference: open groin incision photos - pale dermis band with a red papillary flush and punctate bleeders at the
// dermis/fat junction, glistening yellow fat lobules bulging between pink septa, Scarpa's fascia as a pale membrane,
// blood streaking down the walls; external oblique aponeurosis = pearly white with parallel fibre bundles.
using UnityEngine;

namespace VirtualOR
{
    public static class TissueMaps
    {
        public struct Set { public Texture2D albedo, normal, gloss; }
        public delegate void Sampler(float u, float v, out Color albedo, out float height, out float smooth);

        // Bake a sampler into three textures. The texture covers widthMm x heightMm of tissue; one unit of height = reliefMm.
        public static Set Bake(int w, int h, Sampler f, float reliefMm, float widthMm, float heightMm, TextureWrapMode wrapU, TextureWrapMode wrapV)
        {
            float bumpU = reliefMm / (2f * widthMm / w), bumpV = reliefMm / (2f * heightMm / h);   // central difference -> slope
            int n = w * h; var col = new Color32[n]; var hgt = new float[n]; var gl = new Color32[n];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    Color c; float ht, sm; f((x + 0.5f) / w, (y + 0.5f) / h, out c, out ht, out sm);
                    int i = y * w + x; col[i] = c; hgt[i] = ht; gl[i] = new Color32(0, 0, 0, (byte)(Mathf.Clamp01(sm) * 255));
                }
            var nrm = new Color32[n];
            bool ru = wrapU == TextureWrapMode.Repeat, rv = wrapV == TextureWrapMode.Repeat;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    int xl = ru ? (x + w - 1) % w : Mathf.Max(0, x - 1), xr = ru ? (x + 1) % w : Mathf.Min(w - 1, x + 1);
                    int yd = rv ? (y + h - 1) % h : Mathf.Max(0, y - 1), yu = rv ? (y + 1) % h : Mathf.Min(h - 1, y + 1);
                    float dx = (hgt[y * w + xr] - hgt[y * w + xl]) * bumpU, dy = (hgt[yu * w + x] - hgt[yd * w + x]) * bumpV;
                    var nn = new Vector3(-dx, -dy, 1f).normalized;
                    nrm[y * w + x] = new Color32((byte)((nn.x * 0.5f + 0.5f) * 255), (byte)((nn.y * 0.5f + 0.5f) * 255), (byte)((nn.z * 0.5f + 0.5f) * 255), 255);
                }
            var s = new Set();
            s.albedo = Tex(w, h, col, false, wrapU, wrapV); s.normal = Tex(w, h, nrm, true, wrapU, wrapV); s.gloss = Tex(w, h, gl, true, wrapU, wrapV);
            return s;
        }
        static Texture2D Tex(int w, int h, Color32[] px, bool linear, TextureWrapMode wu, TextureWrapMode wv)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, linear);
            t.SetPixels32(px); t.wrapModeU = wu; t.wrapModeV = wv; t.anisoLevel = 8; t.filterMode = FilterMode.Trilinear; t.Apply(true);
            return t;
        }
        static float SS(float a, float b, float x) { return Mathf.SmoothStep(0, 1, Mathf.InverseLerp(a, b, x)); }

        // ---- fat: soft rounded lobules of varied size (domain-warped cells), thin light septa with capillaries,
        //      wet highlights on the lobule domes
        static void Fat(float mmU, float mmV, out Color c, out float h, out float s, int seed)
        {
            float wu = mmU + (U.Noise(mmU * 0.30f, mmV * 0.30f, seed + 1) - 0.5f) * 0.9f, wv = mmV + (U.Noise(mmU * 0.30f, mmV * 0.30f, seed + 2) - 0.5f) * 0.9f;
            const float scale = 2.6f;
            int id; float e = U.CellEdge(wu / scale, wv / scale, seed, out id);
            float f1 = U.Cells(wu / scale, wv / scale, seed) / 0.8f;                 // same feature points as CellEdge
            float dome = (1 - Mathf.Min(1, f1 * f1)) * SS(0.0f, 0.14f, e);          // round lobule, 0 on the septa
            float tone = (id & 0xff) / 255f;
            Color lob = Color.Lerp(new Color(0.99f, 0.89f, 0.58f), new Color(0.95f, 0.80f, 0.47f), tone * 0.6f + U.Noise(mmU * 0.5f, mmV * 0.5f, seed) * 0.4f);
            Color sept = Color.Lerp(new Color(0.95f, 0.82f, 0.70f), new Color(0.90f, 0.68f, 0.60f), U.Noise(mmU * 0.8f, mmV * 0.8f, seed + 5));
            c = Color.Lerp(sept, lob, SS(0.01f, 0.09f, e));
            float cap = Mathf.Abs(U.Fbm(mmU * 0.10f, mmV * 0.10f, seed + 11) - 0.5f);   // a few capillaries crossing the septa
            if (cap < 0.006f) c = Color.Lerp(c, new Color(0.70f, 0.14f, 0.12f), (1 - cap / 0.006f) * (1 - dome * 0.85f) * 0.8f);
            c = Color.Lerp(c, new Color(0.92f, 0.55f, 0.42f), 0.05f);                  // faint blood-imbibed tint
            h = dome * 0.7f + U.Noise(mmU * 1.2f, mmV * 1.2f, seed + 6) * 0.12f;
            s = Mathf.Lerp(0.40f, 0.72f, dome);
        }

        // wound wall: u = 40 mm along the cut (repeat), v = 50 mm of depth from the skin surface (clamp)
        public static Set Wall() { return Bake(256, 1024, WallSample, 0.25f, 40f, 50f, TextureWrapMode.Repeat, TextureWrapMode.Clamp); }
        public const float WallDepthM = 0.05f;
        static void WallSample(float u, float v, out Color c, out float h, out float s)
        {
            float mmU = u * 40f, mm = v * 50f;
            Fat(mmU, mm, out c, out h, out s, 3);
            float scarpa = Mathf.Abs(mm - (13.5f + Mathf.Sin(mmU * 0.25f) * 0.8f + U.Noise(mmU * 0.3f, 0, 9) * 0.6f));   // Scarpa's fascia
            if (scarpa < 0.7f) { float k = SS(0.7f, 0.1f, scarpa); c = Color.Lerp(c, new Color(0.94f, 0.91f, 0.86f), k * 0.75f); h = Mathf.Lerp(h, 0.45f, k); s = Mathf.Lerp(s, 0.55f, k); }
            float dermEnd = 2.4f + U.Noise(mmU * 0.5f, 0, 4) * 0.4f;
            if (mm < dermEnd + 0.35f)
            {
                // epidermis line, papillary flush, reticular dermis
                Color derm = Color.Lerp(new Color(0.95f, 0.84f, 0.80f), new Color(0.90f, 0.70f, 0.66f), U.Noise(mmU * 1.5f, mm * 4f, 6));
                derm = Color.Lerp(derm, new Color(0.80f, 0.34f, 0.33f), SS(0.6f, 0.15f, mm) * 0.8f);
                if (mm < 0.15f) derm = new Color(0.72f, 0.52f, 0.44f);
                float k = SS(dermEnd + 0.35f, dermEnd - 0.05f, mm);
                c = Color.Lerp(c, derm, k); h = Mathf.Lerp(h, 0.55f + U.Noise(mmU * 3f, mm * 3f, 8) * 0.08f, k); s = Mathf.Lerp(s, 0.45f, k);
                // punctate dermal bleeders at the dermis/fat junction
                int id; float e = U.CellEdge(mmU / 1.6f, (mm - dermEnd) / 1.6f + 7f, 21, out id);
                if ((id & 7) == 0 && Mathf.Abs(mm - dermEnd) < 0.6f && e > 0.32f) { c = Color.Lerp(c, new Color(0.42f, 0.03f, 0.04f), 0.9f); s = 0.8f; }
            }
            // blood running down from the dermis in a few streaks
            float col = U.Noise(mmU * 0.55f + Mathf.Sin(mm * 0.35f) * 0.25f + (U.Noise(mmU * 0.3f, mm * 0.3f, 19) - 0.5f) * 0.4f, 0.5f, 17);
            float len = 3f + 14f * U.Noise(mmU * 0.2f, 1, 18);                                          // each run has its own length
            float streak = SS(0.68f, 0.86f, col) * SS(len, len * 0.4f, mm - dermEnd);
            if (mm > dermEnd - 0.3f && streak > 0.01f) { c = Color.Lerp(c, new Color(0.50f, 0.04f, 0.05f), streak * 0.85f); s = Mathf.Lerp(s, 0.78f, streak); h = Mathf.Lerp(h, 0.35f, streak * 0.6f); }
        }

        // uncut fat seen at the bottom of a partial-depth incision: u, v = 40 mm (repeat)
        public static Set FatFloor() { return Bake(512, 512, (float u, float v, out Color c, out float h, out float s) => Fat(u * 40f, v * 40f, out c, out h, out s, 5), 0.25f, 40f, 40f, TextureWrapMode.Repeat, TextureWrapMode.Repeat); }

        // external oblique aponeurosis over the window (clamp): pearly fibre bundles along fibreDir, a few small vessels
        public static Set Aponeurosis(Vector2 fibreDirUV, float aspect, float windowMm)
        {
            Vector2 d = new Vector2(fibreDirUV.x, fibreDirUV.y * aspect).normalized; Vector2 p = new Vector2(-d.y, d.x);
            return Bake(1024, 1024, (float u, float v, out Color c, out float h, out float s) =>
            {
                float across = (u * p.x + v * p.y) * windowMm, along = (u * d.x + v * d.y) * windowMm;
                float bundle = 0.5f + 0.5f * Mathf.Sin(across * 4.2f + U.Noise(along * 0.08f, across * 0.25f, 2) * 3f);   // ~1.5 mm bundles
                float fine = U.Noise(along * 0.1f, across * 3.5f, 4);
                float n = U.Noise(along * 0.05f, across * 0.05f, 5);
                c = Color.Lerp(new Color(0.78f, 0.80f, 0.82f), new Color(0.95f, 0.95f, 0.94f), bundle * 0.45f + fine * 0.25f + n * 0.3f);
                c = Color.Lerp(c, new Color(0.88f, 0.84f, 0.80f), 0.15f);
                float ves = Mathf.Abs(U.Fbm(u * 3, v * 3, 31) - 0.5f);
                if (ves < 0.006f) c = Color.Lerp(c, new Color(0.66f, 0.16f, 0.16f), (1 - ves / 0.006f) * 0.7f);
                h = bundle * 0.6f + fine * 0.25f; s = 0.5f + bundle * 0.2f;
            }, 0.3f, windowMm, windowMm, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
        }

        // skin micro-relief for the operative window (clamp): pores, fine creases along Langer's lines
        public static Texture2D SkinNormal(Vector2 langerUV, float wMm, float hMm)
        {
            Vector2 d = langerUV.normalized;
            var set = Bake(1024, 1024, (float u, float v, out Color c, out float h, out float s) =>
            {
                float x = u * wMm, y = v * hMm; float along = x * d.x + y * d.y, across = -x * d.y + y * d.x;
                int id; float pore = U.CellEdge(x / 0.9f, y / 0.9f, 41, out id);
                float crease = Mathf.Abs(Mathf.Sin(across * 2.3f + U.Noise(along * 0.2f, across * 0.2f, 43) * 2f));
                h = SS(0f, 0.25f, pore) * 0.5f + SS(0f, 0.25f, crease) * 0.35f + U.Noise(x * 2f, y * 2f, 44) * 0.15f;
                c = Color.white; s = 0;
            }, 0.12f, wMm, hMm, TextureWrapMode.Clamp, TextureWrapMode.Clamp);
            Object.Destroy(set.albedo); Object.Destroy(set.gloss);
            return set.normal;
        }
    }
}
