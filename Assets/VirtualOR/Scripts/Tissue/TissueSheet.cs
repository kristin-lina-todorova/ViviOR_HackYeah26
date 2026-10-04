// Virtual OR - renders a TissueCore: top surface, wound walls (dermis band over fat), and the uncut floor.
using System.Collections.Generic;
using UnityEngine;

namespace VirtualOR
{
    public enum TissueRegion { None, Surface, Wall, Floor }

    public struct TissueHit
    {
        public bool hit; public Vector3 point; public Vector3 normal; public float distance;
        public TissueRegion region; public Vector2 uv; public int cutIndex; public int particle;
    }

    public class TissueSheet : MonoBehaviour
    {
        public TissueCore core = new TissueCore();
        public Material surfaceMat, wallMat, floorMat;
        public float wallTexScale = 0.03f;     // metres covered by wall texture height (v 0..1)
        public bool showFloor = true;
        Mesh mesh; int builtVersion = -1;
        Vector3[] verts; Vector3[] norms; Vector2[] uvs;
        int nTop, wallStart, floorStart;
        int[] triTop, triWall, triFloor;
        // wall rows (fraction of the wall depth): dense near the skin for the dermis band, then the fat
        static readonly float[] Rows = { 0f, 0.04f, 0.10f, 0.22f, 0.38f, 0.55f, 0.72f, 0.88f, 1f };
        static int NR { get { return Rows.Length; } }
        float accum;
        public float timeScale = 1f;
        public bool simulate = true;

        public void Build()
        {
            mesh = new Mesh(); mesh.name = name + "_tissue"; mesh.MarkDynamic();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = gameObject.AddComponent<MeshRenderer>();
            r.sharedMaterials = new[] { surfaceMat, wallMat, floorMat };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
        }

        void LateUpdate()
        {
            if (core.uv.Count == 0) return;
            if (simulate)
            {
                accum += Mathf.Min(Time.deltaTime, 0.05f) * timeScale;
                int steps = 0;
                while (accum >= 1f / 60f && steps < 2) { core.Step(1f / 60f); accum -= 1f / 60f; steps++; }
                if (steps == 2) accum = 0;
            }
            Refresh();
        }

        public void Refresh()
        {
            if (mesh == null) Build();
            if (builtVersion != core.Version) RebuildTopology();
            UpdateVertices();
        }

        void RebuildTopology()
        {
            builtVersion = core.Version;
            nTop = core.uv.Count;
            int chain = core.chainL.Count;
            int wallVerts = chain > 1 ? chain * 2 * NR : 0;   // per cut point: L rows (top..bottom), then R rows
            int floorVerts = chain > 1 ? chain * 2 : 0;
            wallStart = nTop; floorStart = nTop + wallVerts;
            int total = nTop + wallVerts + floorVerts;
            verts = new Vector3[total]; norms = new Vector3[total]; uvs = new Vector2[total];
            triTop = (int[])core.tris.Clone();
            var tw = new List<int>(); var tf = new List<int>();
            if (chain > 1)
            {
                int per = 2 * NR;
                for (int k = 0; k + 1 < chain; k++)
                {
                    // orientation decided once in UpdateVertices by flipping index order if needed; build both sides here
                    for (int r = 0; r + 1 < NR; r++)
                    {
                        int a = wallStart + k * per + r, b = a + 1, c = a + per, d = c + 1;          // left wall
                        tw.Add(a); tw.Add(b); tw.Add(c); tw.Add(c); tw.Add(b); tw.Add(d);
                        a += NR; b += NR; c += NR; d += NR;                                          // right wall
                        tw.Add(a); tw.Add(c); tw.Add(b); tw.Add(c); tw.Add(d); tw.Add(b);
                    }
                    int fl = floorStart + k * 2, fr = fl + 1, fl2 = fl + 2, fr2 = fr + 2;
                    tf.Add(fl); tf.Add(fl2); tf.Add(fr); tf.Add(fr); tf.Add(fl2); tf.Add(fr2);
                }
            }
            triWall = tw.ToArray(); triFloor = tf.ToArray();
            mesh.Clear();
            mesh.vertices = verts; mesh.subMeshCount = 3;
            mesh.SetTriangles(triTop, 0, false); mesh.SetTriangles(triWall, 1, false); mesh.SetTriangles(triFloor, 2, false);
            orientChecked = false;
        }

        bool orientChecked;

        void UpdateVertices()
        {
            var x = core.x; var d = core.domain;
            for (int i = 0; i < nTop; i++)
            {
                verts[i] = x[i];
                uvs[i] = new Vector2((core.uv[i].x - d.xMin) / d.width, (core.uv[i].y - d.yMin) / d.height);
                norms[i] = Vector3.zero;
            }
            var t = triTop;
            for (int k = 0; k < t.Length; k += 3)
            {
                Vector3 n = Vector3.Cross(x[t[k + 1]] - x[t[k]], x[t[k + 2]] - x[t[k]]);
                norms[t[k]] += n; norms[t[k + 1]] += n; norms[t[k + 2]] += n;
            }
            for (int i = 0; i < nTop; i++) norms[i] = norms[i].sqrMagnitude > 0 ? norms[i].normalized : core.restN[i];
            int chain = core.chainL.Count;
            if (chain > 1)
            {
                float acc = 0;
                for (int k = 0; k < chain; k++)
                {
                    int L = core.chainL[k], R = core.chainR[k];
                    if (k > 0) acc += Vector2.Distance(core.cut[k], core.cut[k - 1]);
                    float depthM = core.depth[k] * core.thick[L];
                    Vector3 nl = norms[L], nr = norms[R];
                    Vector3 across = x[R] - x[L]; if (across.sqrMagnitude < 1e-10f) across = Vector3.Cross(nl, (x[core.chainL[Mathf.Min(chain - 1, k + 1)]] - x[core.chainL[Mathf.Max(0, k - 1)]])).normalized * 1e-4f;
                    Vector3 an = across.normalized;
                    // walls slope inward slightly toward the floor (fat bulges into the wound), but never across the gap:
                    // each wall may come in at most ~30% of the gape, so the wound always reads as open from above
                    float maxIn = across.magnitude * 0.30f;
                    Vector3 lb = x[L] - core.restN[L] * depthM + an * Mathf.Min(depthM * 0.18f, maxIn);
                    Vector3 rb = x[R] - core.restN[R] * depthM - an * Mathf.Min(depthM * 0.18f, maxIn);
                    int b = wallStart + k * 2 * NR;
                    for (int r = 0; r < NR; r++)
                    {
                        float rt = Rows[r], dm = depthM * rt;
                        // fat bulges into the wound (more in the middle of the wall), lobular unevenness, slight rounding of the skin lip
                        float fat = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.002f, 0.005f, dm)) * (r == NR - 1 ? 0.3f : 1f);
                        float lobL = (U.Noise(acc * 380f, dm * 380f, 3) - 0.5f) * 0.0016f, lobR = (U.Noise(acc * 380f, dm * 380f, 9) - 0.5f) * 0.0016f;
                        float bulge = depthM * 0.10f * Mathf.Sin(rt * Mathf.PI) * fat, lip = r == 1 ? 0.00025f : 0f;
                        float slopeIn = Mathf.Min(depthM * 0.18f, maxIn) * rt;
                        float inL = Mathf.Clamp(bulge + lobL * fat + lip, -0.002f, Mathf.Max(0, maxIn - slopeIn));
                        float inR = Mathf.Clamp(bulge + lobR * fat + lip, -0.002f, Mathf.Max(0, maxIn - slopeIn));
                        verts[b + r] = Vector3.Lerp(x[L], lb, rt) + an * inL;
                        verts[b + NR + r] = Vector3.Lerp(x[R], rb, rt) - an * inR;
                        norms[b + r] = an; norms[b + NR + r] = -an;
                        float vv = dm / wallTexScale;
                        uvs[b + r] = new Vector2(acc * 25f, vv); uvs[b + NR + r] = new Vector2(acc * 25f + 0.37f, vv);
                    }
                    int f = floorStart + k * 2;
                    bool open = core.depth[k] >= 0.985f || !showFloor;
                    verts[f] = open ? lb - core.restN[L] * 0.0005f : lb; verts[f + 1] = open ? lb - core.restN[L] * 0.0005f : rb;
                    norms[f] = core.restN[L]; norms[f + 1] = core.restN[R];
                    uvs[f] = new Vector2(acc * 25f, 0); uvs[f + 1] = new Vector2(acc * 25f, Vector3.Distance(lb, rb) * 25f);
                }
                if (!orientChecked) orientChecked = OrientWalls();
                // smooth wall normals from the (oriented) faces, so the bulges shade
                int w0 = wallStart, w1 = floorStart;
                var wn = new Vector3[w1 - w0];
                for (int i = 0; i < triWall.Length; i += 3)
                {
                    int ia = triWall[i], ib = triWall[i + 1], ic = triWall[i + 2];
                    Vector3 fn = Vector3.Cross(verts[ib] - verts[ia], verts[ic] - verts[ia]);
                    wn[ia - w0] += fn; wn[ib - w0] += fn; wn[ic - w0] += fn;
                }
                for (int i = 0; i < wn.Length; i++)
                {
                    var want = norms[w0 + i];
                    if (wn[i].sqrMagnitude > 1e-14f) { var nn = wn[i].normalized; norms[w0 + i] = Vector3.Dot(nn, want) < 0 ? -nn : nn; }
                }
            }
            mesh.vertices = verts; mesh.normals = norms; mesh.uv = uvs;
            mesh.RecalculateTangents();   // normal-mapped tissue materials
            mesh.RecalculateBounds();
        }

        // Make sure wall and floor triangles face into the wound (towards the viewer looking into the incision).
        // Decide the winding by summing over ALL wall (floor) triangles: single triangles at the ends of the cut are
        // degenerate (zero depth) and gave a random answer, which turned every wall back-facing (invisible).
        // Returns false while the walls are still too shallow to decide; it is retried next frame.
        bool OrientWalls()
        {
            if (triWall.Length < 6) return false;
            float vote = Vote(triWall);
            if (Mathf.Abs(vote) < 1e-12f) return false;
            if (vote < 0) { Flip(triWall); mesh.SetTriangles(triWall, 1, false); }
            if (triFloor.Length >= 3)
            {
                float vf = Vote(triFloor);
                if (vf < 0) { Flip(triFloor); mesh.SetTriangles(triFloor, 2, false); }
            }
            return true;
        }
        float Vote(int[] T)
        {
            float sum = 0;
            for (int i = 0; i < T.Length; i += 3)
            {
                Vector3 a = verts[T[i]], b = verts[T[i + 1]], c = verts[T[i + 2]];
                sum += Vector3.Dot(Vector3.Cross(b - a, c - a), norms[T[i]]);
            }
            return sum;
        }
        static void Flip(int[] t) { for (int i = 0; i < t.Length; i += 3) { int s = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = s; } }

        // ------------------------------------------------------------ raycast (local space math; ray in world)
        public TissueHit Raycast(Ray worldRay, float maxDist = 3f)
        {
            var res = new TissueHit();
            if (verts == null) return res;
            var tr = transform;
            Vector3 o = tr.InverseTransformPoint(worldRay.origin), dir = tr.InverseTransformDirection(worldRay.direction).normalized;
            float best = maxDist; int bestTri = -1; int bestSet = -1; Vector3 bestBc = Vector3.zero;
            TestSet(triTop, 0, o, dir, ref best, ref bestTri, ref bestSet, ref bestBc);
            TestSet(triWall, 1, o, dir, ref best, ref bestTri, ref bestSet, ref bestBc);
            if (showFloor) TestSet(triFloor, 2, o, dir, ref best, ref bestTri, ref bestSet, ref bestBc);
            if (bestTri < 0) return res;
            int[] T = bestSet == 0 ? triTop : bestSet == 1 ? triWall : triFloor;
            int ia = T[bestTri], ib = T[bestTri + 1], ic = T[bestTri + 2];
            res.hit = true; res.distance = best;
            Vector3 lp = o + dir * best;
            res.point = tr.TransformPoint(lp);
            res.normal = tr.TransformDirection(Vector3.Cross(verts[ib] - verts[ia], verts[ic] - verts[ia]).normalized);
            if (bestSet == 0)
            {
                res.region = TissueRegion.Surface;
                res.uv = core.uv[ia] * bestBc.x + core.uv[ib] * bestBc.y + core.uv[ic] * bestBc.z;
                res.particle = bestBc.x > bestBc.y ? (bestBc.x > bestBc.z ? ia : ic) : (bestBc.y > bestBc.z ? ib : ic);
                float dd; res.cutIndex = core.HasCut ? core.NearestCutIndex(res.uv, out dd) : -1;
            }
            else
            {
                int vi = bestBc.x > bestBc.y ? (bestBc.x > bestBc.z ? ia : ic) : (bestBc.y > bestBc.z ? ib : ic);
                int baseIdx = bestSet == 1 ? wallStart : floorStart; int per = bestSet == 1 ? 2 * NR : 2;
                int k = Mathf.Clamp((vi - baseIdx) / per, 0, core.cut.Count - 1);
                res.region = bestSet == 1 ? TissueRegion.Wall : TissueRegion.Floor;
                res.cutIndex = k; res.uv = core.cut[k];
                int lane = (vi - baseIdx) % per;
                res.particle = (bestSet == 1 && lane >= NR) ? core.chainR[k] : core.chainL[k];
            }
            return res;
        }

        void TestSet(int[] T, int set, Vector3 o, Vector3 d, ref float best, ref int bestTri, ref int bestSet, ref Vector3 bestBc)
        {
            for (int i = 0; i < T.Length; i += 3)
            {
                Vector3 a = verts[T[i]], b = verts[T[i + 1]], c = verts[T[i + 2]];
                Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, p); if (det > -1e-12f && det < 1e-12f) continue;
                float inv = 1f / det; Vector3 tv = o - a;
                float u = Vector3.Dot(tv, p) * inv; if (u < 0 || u > 1) continue;
                Vector3 q = Vector3.Cross(tv, e1);
                float v = Vector3.Dot(d, q) * inv; if (v < 0 || u + v > 1) continue;
                float t = Vector3.Dot(e2, q) * inv;
                if (t > 1e-5f && t < best) { best = t; bestTri = i; bestSet = set; bestBc = new Vector3(1 - u - v, u, v); }
            }
        }
    }
}
