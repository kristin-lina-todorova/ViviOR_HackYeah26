// Virtual OR - soft tissue core.
// A pre-tensioned membrane (skin+subcutis, or aponeurosis) simulated with XPBD.
// Cutting re-triangulates the rest domain so the blade path becomes mesh edges, then splits the mesh along it.
// Residual tension (anisotropic, along Langer's lines / fibre direction) makes the wound gape by itself.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualOR
{
    public struct SurfaceSample
    {
        public Vector3 pos; public Vector3 normal; public float thickness;
    }

    public static class Delaunay
    {
        struct Tri { public int a, b, c; public double cx, cy, r2; }

        static Tri Make(int a, int b, int c, List<Vector2> P)
        {
            double ax = P[a].x, ay = P[a].y, bx = P[b].x, by = P[b].y, cx = P[c].x, cy = P[c].y;
            // enforce CCW
            double cr = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            if (cr < 0) { int t = b; b = c; c = t; double tx = bx, ty = by; bx = cx; by = cy; cx = tx; cy = ty; }
            double d = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
            var tr = new Tri { a = a, b = b, c = c };
            if (Math.Abs(d) < 1e-18) { tr.cx = ax; tr.cy = ay; tr.r2 = double.MaxValue; return tr; }
            double a2 = ax * ax + ay * ay, b2 = bx * bx + by * by, c2 = cx * cx + cy * cy;
            tr.cx = (a2 * (by - cy) + b2 * (cy - ay) + c2 * (ay - by)) / d;
            tr.cy = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d;
            tr.r2 = (ax - tr.cx) * (ax - tr.cx) + (ay - tr.cy) * (ay - tr.cy);
            return tr;
        }

        public static int[] Triangulate(List<Vector2> pts)
        {
            int n = pts.Count;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var p in pts) { minX = Mathf.Min(minX, p.x); minY = Mathf.Min(minY, p.y); maxX = Mathf.Max(maxX, p.x); maxY = Mathf.Max(maxY, p.y); }
            float dm = Mathf.Max(maxX - minX, maxY - minY); Vector2 mid = new Vector2((minX + maxX) / 2, (minY + maxY) / 2);
            var P = new List<Vector2>(pts);
            P.Add(new Vector2(mid.x - 20 * dm, mid.y - dm)); P.Add(new Vector2(mid.x, mid.y + 20 * dm)); P.Add(new Vector2(mid.x + 20 * dm, mid.y - dm));
            var tris = new List<Tri>(n * 2 + 8) { Make(n, n + 1, n + 2, P) };
            var edgeCount = new Dictionary<long, int>();
            var edgeDir = new Dictionary<long, long>();
            var keep = new List<Tri>(n * 2 + 8);
            for (int i = 0; i < n; i++)
            {
                double px = P[i].x, py = P[i].y;
                edgeCount.Clear(); edgeDir.Clear(); keep.Clear();
                foreach (var t in tris)
                {
                    double dx = px - t.cx, dy = py - t.cy;
                    if (dx * dx + dy * dy < t.r2 * (1 - 1e-12))
                    {
                        AddEdge(edgeCount, edgeDir, t.a, t.b); AddEdge(edgeCount, edgeDir, t.b, t.c); AddEdge(edgeCount, edgeDir, t.c, t.a);
                    }
                    else keep.Add(t);
                }
                tris.Clear(); tris.AddRange(keep);
                foreach (var kv in edgeCount)
                {
                    if (kv.Value != 1) continue;
                    long dir = edgeDir[kv.Key]; int ea = (int)(dir >> 32), eb = (int)(dir & 0xffffffff);
                    tris.Add(Make(ea, eb, i, P));
                }
            }
            var o = new List<int>(tris.Count * 3);
            foreach (var t in tris) { if (t.a >= n || t.b >= n || t.c >= n) continue; o.Add(t.a); o.Add(t.b); o.Add(t.c); }
            return o.ToArray();
        }

        static void AddEdge(Dictionary<long, int> cnt, Dictionary<long, long> dir, int a, int b)
        {
            long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            int c; cnt.TryGetValue(k, out c); cnt[k] = c + 1;
            dir[k] = ((long)a << 32) | (uint)b;
        }
    }

    public class TissueCore
    {
        // ---------------- configuration
        public Rect domain;
        public float spacing = 0.004f;
        public Func<Vector2, SurfaceSample> sample;
        public Vector2 fibreDir = new Vector2(1, 0);   // Langer's lines / collagen fibre direction (uv space)
        public float strainPar = 0.17f, strainPerp = 0.05f;   // residual tension, higher ALONG Langer's lines (so cuts across them gape more)
        public float stretchCompliance = 2e-7f, bendCompliance = 6e-6f, tetherCompliance = 1.2e-4f;
        public float damping = 0.92f;
        public int substeps = 4;
        public float scoreDepth = 0.25f;   // cut depth (fraction) below which the deep layer still bridges the wound

        // ---------------- particles
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Vector3> rest = new List<Vector3>(), restN = new List<Vector3>();
        public Vector3[] x = new Vector3[0];            // current positions (read by renderers)
        Vector3[] vel = new Vector3[0];
        public readonly List<float> w = new List<float>(), thick = new List<float>();
        // flat solver arrays
        float[] px = new float[0], py = new float[0], pz = new float[0], qx = new float[0], qy = new float[0], qz = new float[0], vx = new float[0], vy = new float[0], vz = new float[0], iw = new float[0];
        float[] rx = new float[0], ry = new float[0], rz = new float[0], nx_ = new float[0], ny_ = new float[0], nz_ = new float[0], tk = new float[0];
        public readonly List<int> side = new List<int>();     // +1 left copy, -1 right copy, 0 normal / tip
        public int[] tris = new int[0];
        List<Vector2> gridUV = new List<Vector2>();
        List<bool> gridPinned = new List<bool>();
        List<bool> pinned = new List<bool>();

        // ---------------- constraints
        int[] ca = new int[0], cb = new int[0]; float[] crest = new float[0], ccomp = new float[0];

        // ---------------- cut state
        public readonly List<Vector2> cut = new List<Vector2>();   // resampled cut polyline (uv)
        public readonly List<int> chainL = new List<int>(), chainR = new List<int>();
        public readonly List<float> depth = new List<float>();      // per cut point, 0..1 (fraction of thickness)
        public bool HasCut { get { return chainL.Count > 1; } }
        public int Version { get; private set; }                    // increments on topology change

        public class Attachment { public int i; public Vector3 target; public float compliance; public bool active = true; }
        public readonly List<Attachment> attachments = new List<Attachment>();
        public class Suture { public int a, b; public float rest; public float compliance; }
        public readonly List<Suture> sutures = new List<Suture>();

        // ---------------------------------------------------------------- setup
        public void Init()
        {
            gridUV.Clear(); gridPinned.Clear();
            int nx = Mathf.Max(2, Mathf.RoundToInt(domain.width / spacing)), ny = Mathf.Max(2, Mathf.RoundToInt(domain.height / spacing));
            float sx = domain.width / nx, sy = domain.height / ny;
            var rnd = new System.Random(7);
            for (int j = 0; j <= ny; j++)
                for (int i = 0; i <= nx; i++)
                {
                    bool edge = i == 0 || j == 0 || i == nx || j == ny;
                    float jx = edge ? 0 : (float)(rnd.NextDouble() - 0.5) * spacing * 0.004f;
                    float jy = edge ? 0 : (float)(rnd.NextDouble() - 0.5) * spacing * 0.004f;
                    gridUV.Add(new Vector2(domain.xMin + i * sx + jx, domain.yMin + j * sy + jy));
                    gridPinned.Add(edge);
                }
            Rebuild(new List<Vector2>(), null);
        }

        // ---------------------------------------------------------------- cutting
        // Sets (replaces) the cut polyline. Points are in uv. depth0 = depth applied to newly created cut points.
        public void SetCut(List<Vector2> poly, float depth0)
        {
            var clipped = new List<Vector2>();
            float m = spacing * 1.6f;
            foreach (var p in poly)
            {
                if (p.x < domain.xMin + m || p.x > domain.xMax - m || p.y < domain.yMin + m || p.y > domain.yMax - m) { if (clipped.Count > 0) break; else continue; }
                clipped.Add(p);
            }
            if (clipped.Count < 2 || U.PolyLength(clipped) < spacing * 1.5f) return;
            var pts = ResampleFixed(clipped, spacing * 0.7f);
            Rebuild(pts, depth0);
        }

        static List<Vector2> ResampleFixed(List<Vector2> poly, float step)
        {
            var o = new List<Vector2> { poly[0] };
            float carry = 0;
            for (int i = 0; i + 1 < poly.Count; i++)
            {
                Vector2 a = poly[i], b = poly[i + 1]; float L = Vector2.Distance(a, b);
                float s = step - carry;
                while (s <= L) { o.Add(Vector2.Lerp(a, b, s / L)); s += step; }
                carry = L - (s - step);
            }
            Vector2 end = poly[poly.Count - 1];
            if (Vector2.Distance(o[o.Count - 1], end) < step * 0.35f && o.Count > 1) o[o.Count - 1] = end; else o.Add(end);
            return o;
        }

        // Rebuild topology with the given cut polyline. Transfers simulation state from the old mesh.
        void Rebuild(List<Vector2> cutPts, float? depth0)
        {
            // ---- old state snapshot
            var oUV = new List<Vector2>(uv); var oX = (Vector3[])x.Clone(); var oV = (Vector3[])vel.Clone(); var oSide = new List<int>(side);
            var oTris = tris; var oDepth = new List<float>(depth); var oCut = new List<Vector2>(cut);
            var oAttach = new List<Attachment>(attachments);
            var attachUV = new List<Vector2>(); foreach (var a in oAttach) attachUV.Add(a.i < oUV.Count ? oUV[a.i] : Vector2.zero);
            var attachSide = new List<int>(); foreach (var a in oAttach) attachSide.Add(a.i < oSide.Count ? oSide[a.i] : 0);

            // ---- points
            var pts = new List<Vector2>(); var pin = new List<bool>();
            float rm = spacing * 0.75f;
            for (int i = 0; i < gridUV.Count; i++)
            {
                if (cutPts.Count > 1) { float arc; int seg; if (U.DistPointPoly(gridUV[i], cutPts, out arc, out seg) < rm) continue; }
                pts.Add(gridUV[i]); pin.Add(gridPinned[i]);
            }
            int cutStart = pts.Count;
            foreach (var c in cutPts) { pts.Add(c); pin.Add(false); }
            int[] T = Delaunay.Triangulate(pts);

            // ---- split along cut
            var sideArr = new List<int>(); for (int i = 0; i < pts.Count; i++) sideArr.Add(0);
            chainL.Clear(); chainR.Clear(); cut.Clear(); cut.AddRange(cutPts);
            int nc = cutPts.Count;
            var dupOf = new Dictionary<int, int>();
            if (nc > 1)
            {
                for (int k = 1; k < nc - 1; k++)
                {
                    int ci = cutStart + k; int ri = pts.Count; pts.Add(pts[ci]); pin.Add(false); sideArr.Add(-1); sideArr[ci] = 1; dupOf[ci] = ri;
                }
                for (int t = 0; t < T.Length; t += 3)
                {
                    Vector2 g = (pts[T[t]] + pts[T[t + 1]] + pts[T[t + 2]]) / 3f;
                    for (int e = 0; e < 3; e++)
                    {
                        int vi = T[t + e]; int dup;
                        if (!dupOf.TryGetValue(vi, out dup)) continue;
                        int k = vi - cutStart;
                        Vector2 tan = (cutPts[Mathf.Min(nc - 1, k + 1)] - cutPts[Mathf.Max(0, k - 1)]).normalized;
                        if (U.Cross2(tan, g - cutPts[k]) < 0) T[t + e] = dup;
                    }
                }
                for (int k = 0; k < nc; k++)
                {
                    int ci = cutStart + k; int d;
                    chainL.Add(ci); chainR.Add(dupOf.TryGetValue(ci, out d) ? d : ci);
                }
            }
            // ---- depth per cut point (carry over from previous cut by arc length)
            depth.Clear();
            float[] oArc = ArcParams(oCut);
            float acc = 0;
            for (int k = 0; k < nc; k++)
            {
                if (k > 0) acc += Vector2.Distance(cutPts[k], cutPts[k - 1]);
                float dv = depth0.HasValue ? depth0.Value : 0.35f;
                if (oCut.Count > 1)
                {
                    float best = 1e9f; float bd = dv;
                    for (int j = 1; j < oCut.Count - 1; j++) { float dd = Vector2.Distance(oCut[j], cutPts[k]); if (dd < best) { best = dd; bd = oDepth[j]; } }
                    if (best < spacing) dv = Mathf.Max(bd, 0);
                }
                if (k == 0 || k == nc - 1) dv = 0;
                depth.Add(dv);
            }

            // ---- particle arrays
            uv.Clear(); rest.Clear(); restN.Clear(); w.Clear(); thick.Clear(); side.Clear(); pinned.Clear();
            x = new Vector3[pts.Count]; vel = new Vector3[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                var s = sample(pts[i]);
                uv.Add(pts[i]); rest.Add(s.pos); restN.Add(s.normal); thick.Add(s.thickness);
                x[i] = s.pos;
                w.Add(pin[i] ? 0f : 1f); pinned.Add(pin[i]); side.Add(sideArr[i]);
            }
            tris = T;
            // ---- transfer old state
            if (oTris.Length > 0)
            {
                var loc = new TriLocator(oUV, oTris, spacing * 4);
                for (int i = 0; i < uv.Count; i++)
                {
                    Vector3 bc; int tri = loc.Find(uv[i], side[i], oSide, out bc);
                    if (tri < 0) continue;
                    int a = oTris[tri], b = oTris[tri + 1], c = oTris[tri + 2];
                    x[i] = oX[a] * bc.x + oX[b] * bc.y + oX[c] * bc.z;
                    vel[i] = (oV[a] * bc.x + oV[b] * bc.y + oV[c] * bc.z) * 0.5f;
                    if (w[i] == 0) x[i] = rest[i];
                }
            }
            // ---- re-map attachments to nearest particle on the same side
            attachments.Clear();
            for (int k = 0; k < oAttach.Count; k++)
            {
                int best = NearestParticle(attachUV[k], attachSide[k]);
                if (best >= 0) { oAttach[k].i = best; attachments.Add(oAttach[k]); }
            }
            sutures.Clear();
            BuildConstraints();
            LoadSolver();
            Version++;
        }

        static float[] ArcParams(List<Vector2> p)
        {
            var a = new float[p.Count]; for (int i = 1; i < p.Count; i++) a[i] = a[i - 1] + Vector2.Distance(p[i], p[i - 1]); return a;
        }

        public int NearestParticle(Vector2 q, int preferSide)
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < uv.Count; i++)
            {
                if (preferSide != 0 && side[i] == -preferSide) continue;
                float d = (uv[i] - q).sqrMagnitude; if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        void BuildConstraints()
        {
            var A = new List<int>(); var B = new List<int>(); var Rl = new List<float>(); var Cm = new List<float>();
            var edgeTris = new Dictionary<long, int>();
            var opp = new Dictionary<long, int>();
            for (int t = 0; t < tris.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = tris[t + e], b = tris[t + (e + 1) % 3], c = tris[t + (e + 2) % 3];
                    long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    int o;
                    if (opp.TryGetValue(k, out o))
                    {
                        // interior edge: bending constraint between the two opposite vertices
                        AddC(A, B, Rl, Cm, o, c, bendCompliance);
                    }
                    else
                    {
                        opp[k] = c;
                        AddC(A, B, Rl, Cm, a, b, stretchCompliance);
                    }
                }
            ca = A.ToArray(); cb = B.ToArray(); crest = Rl.ToArray(); ccomp = Cm.ToArray();
        }

        void AddC(List<int> A, List<int> B, List<float> R, List<float> C, int a, int b, float comp)
        {
            float L0 = Vector3.Distance(rest[a], rest[b]);
            Vector2 d = uv[b] - uv[a]; float c2 = 0;
            if (d.sqrMagnitude > 1e-12f) { c2 = Vector2.Dot(d.normalized, fibreDir.normalized); c2 *= c2; }
            float s = strainPerp + (strainPar - strainPerp) * c2;
            A.Add(a); B.Add(b); R.Add(L0 * (1 - s)); C.Add(comp);
        }

        // ---------------------------------------------------------------- interaction helpers
        public Attachment Attach(int i, Vector3 target, float compliance)
        {
            var a = new Attachment { i = i, target = target, compliance = compliance }; attachments.Add(a); return a;
        }
        public void Detach(Attachment a) { attachments.Remove(a); }

        // Suture across the wound at cut point k (closes the gap physically).
        public bool AddSuture(int k, float compliance = 1e-7f)
        {
            if (k <= 0 || k >= chainL.Count - 1) return false;
            sutures.Add(new Suture { a = chainL[k], b = chainR[k], rest = 0.0006f, compliance = compliance });
            // a suture bite holds a few millimetres of tissue on either side of the knot
            for (int j = k - 1; j <= k + 1; j += 2)
                if (j > 0 && j < chainL.Count - 1) sutures.Add(new Suture { a = chainL[j], b = chainR[j], rest = 0.0008f, compliance = compliance * 6f });
            return true;
        }

        public int NearestCutIndex(Vector2 q, out float dist)
        {
            int best = -1; dist = float.MaxValue;
            for (int k = 0; k < cut.Count; k++) { float d = Vector2.Distance(cut[k], q); if (d < dist) { dist = d; best = k; } }
            return best;
        }

        public float Gap(int k) { if (k < 0 || k >= chainL.Count) return 0; return Vector3.Distance(x[chainL[k]], x[chainR[k]]); }

        public void DeepenAt(int k, float amount, float radius)
        {
            if (cut.Count < 2) return;
            for (int j = 1; j < cut.Count - 1; j++)
            {
                float d = Vector2.Distance(cut[j], cut[k]);
                if (d < radius) depth[j] = Mathf.Min(1f, depth[j] + amount * (1f - d / radius));
            }
        }

        public float MeanDepth()
        {
            if (cut.Count < 3) return 0; float s = 0; for (int j = 1; j < cut.Count - 1; j++) s += depth[j]; return s / (cut.Count - 2);
        }
        public float MinInteriorDepth()
        {
            if (cut.Count < 3) return 0; float s = 1; for (int j = 1; j < cut.Count - 1; j++) s = Mathf.Min(s, depth[j]); return s;
        }

        // ---------------------------------------------------------------- simulation
        void LoadSolver()
        {
            int n = uv.Count;
            px = new float[n]; py = new float[n]; pz = new float[n]; qx = new float[n]; qy = new float[n]; qz = new float[n];
            vx = new float[n]; vy = new float[n]; vz = new float[n]; iw = new float[n];
            rx = new float[n]; ry = new float[n]; rz = new float[n]; nx_ = new float[n]; ny_ = new float[n]; nz_ = new float[n]; tk = new float[n];
            for (int i = 0; i < n; i++)
            {
                px[i] = x[i].x; py[i] = x[i].y; pz[i] = x[i].z; vx[i] = vel[i].x; vy[i] = vel[i].y; vz[i] = vel[i].z; iw[i] = w[i];
                rx[i] = rest[i].x; ry[i] = rest[i].y; rz[i] = rest[i].z; nx_[i] = restN[i].x; ny_[i] = restN[i].y; nz_[i] = restN[i].z;
                tk[i] = side[i] != 0 ? 0.3f : 1f;
            }
        }

        public void Step(float dt)
        {
            int n = uv.Count; if (n == 0) return;
            int ns = substeps; float h = dt / ns; float ih2 = 1f / (h * h);
            float damp = Mathf.Pow(damping, 1f / ns);
            int nc = ca.Length;
            for (int s = 0; s < ns; s++)
            {
                for (int i = 0; i < n; i++)
                {
                    qx[i] = px[i]; qy[i] = py[i]; qz[i] = pz[i];
                    if (iw[i] == 0) continue;
                    px[i] += vx[i] * h; py[i] += vy[i] * h; pz[i] += vz[i] * h;
                }
                for (int c = 0; c < nc; c++) DistF(ca[c], cb[c], crest[c], ccomp[c] * ih2);
                for (int k = 1; k < chainL.Count - 1; k++)
                {
                    float d = depth[k];
                    if (d >= scoreDepth) continue;
                    DistF(chainL[k], chainR[k], 0f, 1e-6f * (1f + 30f * d / scoreDepth) * ih2);
                }
                for (int k = 0; k < sutures.Count; k++) DistF(sutures[k].a, sutures[k].b, sutures[k].rest, sutures[k].compliance * ih2);
                for (int k = 0; k < attachments.Count; k++)
                {
                    var a = attachments[k];
                    if (!a.active || a.i >= n || iw[a.i] == 0) continue;
                    float dx = px[a.i] - a.target.x, dy = py[a.i] - a.target.y, dz = pz[a.i] - a.target.z;
                    float f = 1f / (1f + a.compliance * ih2);
                    px[a.i] -= dx * f; py[a.i] -= dy * f; pz[a.i] -= dz * f;
                }
                float tt = tetherCompliance * ih2;
                for (int i = 0; i < n; i++)
                {
                    if (iw[i] == 0) continue;
                    float f = 1f / (1f + tt / tk[i]);
                    float dx = px[i] - rx[i], dy = py[i] - ry[i], dz = pz[i] - rz[i];
                    px[i] -= dx * f; py[i] -= dy * f; pz[i] -= dz * f;
                    float sd = (px[i] - rx[i]) * nx_[i] + (py[i] - ry[i]) * ny_[i] + (pz[i] - rz[i]) * nz_[i];
                    if (sd < 0) { px[i] -= nx_[i] * sd; py[i] -= ny_[i] * sd; pz[i] -= nz_[i] * sd; }
                }
                float ih = damp / h;
                for (int i = 0; i < n; i++)
                {
                    if (iw[i] == 0) { vx[i] = vy[i] = vz[i] = 0; continue; }
                    vx[i] = (px[i] - qx[i]) * ih; vy[i] = (py[i] - qy[i]) * ih; vz[i] = (pz[i] - qz[i]) * ih;
                }
            }
            for (int i = 0; i < n; i++) { x[i].x = px[i]; x[i].y = py[i]; x[i].z = pz[i]; vel[i].x = vx[i]; vel[i].y = vy[i]; vel[i].z = vz[i]; }
        }

        void DistF(int a, int b, float rest0, float at)
        {
            float wa = iw[a], wb = iw[b]; float ws = wa + wb; if (ws == 0) return;
            float dx = px[a] - px[b], dy = py[a] - py[b], dz = pz[a] - pz[b];
            float L = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz); if (L < 1e-9f) return;
            float dl = -(L - rest0) / (ws + at) / L;
            px[a] += dx * wa * dl; py[a] += dy * wa * dl; pz[a] += dz * wa * dl;
            px[b] -= dx * wb * dl; py[b] -= dy * wb * dl; pz[b] -= dz * wb * dl;
        }

        // Set a particle position directly (e.g. after external constraint) and keep solver in sync.
        public void SetPos(int i, Vector3 p) { x[i] = p; px[i] = p.x; py[i] = p.y; pz[i] = p.z; }
        public void ReloadWeights() { for (int i = 0; i < x.Length; i++) vel[i] = Vector3.zero; LoadSolver(); }

        // ---------------------------------------------------------------- point location for state transfer
        class TriLocator
        {
            readonly List<Vector2> P; readonly int[] T; readonly float cell; readonly Dictionary<long, List<int>> buckets = new Dictionary<long, List<int>>();
            public TriLocator(List<Vector2> p, int[] t, float cellSize)
            {
                P = p; T = t; cell = cellSize;
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector2 a = p[t[i]], b = p[t[i + 1]], c = p[t[i + 2]];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / cell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / cell);
                    int y0 = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) / cell), y1 = Mathf.FloorToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) / cell);
                    for (int yy = y0; yy <= y1; yy++) for (int xx = x0; xx <= x1; xx++)
                        {
                            long k = ((long)xx << 32) ^ (uint)yy; List<int> l;
                            if (!buckets.TryGetValue(k, out l)) { l = new List<int>(); buckets[k] = l; }
                            l.Add(i);
                        }
                }
            }
            public int Find(Vector2 q, int wantSide, List<int> oldSide, out Vector3 bc)
            {
                bc = Vector3.zero;
                long k = ((long)Mathf.FloorToInt(q.x / cell) << 32) ^ (uint)Mathf.FloorToInt(q.y / cell);
                List<int> l; if (!buckets.TryGetValue(k, out l)) return -1;
                int best = -1; float bestMin = -1e9f; Vector3 bestBc = Vector3.zero;
                foreach (int t in l)
                {
                    if (wantSide != 0)
                    {
                        bool bad = false;
                        for (int e = 0; e < 3; e++) { int s = oldSide[T[t + e]]; if (s == -wantSide) bad = true; }
                        if (bad) continue;
                    }
                    Vector3 b = Bary(q, P[T[t]], P[T[t + 1]], P[T[t + 2]]);
                    float mn = Mathf.Min(b.x, Mathf.Min(b.y, b.z));
                    if (mn > bestMin) { bestMin = mn; best = t; bestBc = b; }
                }
                if (best < 0 || bestMin < -0.25f) return -1;
                bestBc.x = Mathf.Max(0, bestBc.x); bestBc.y = Mathf.Max(0, bestBc.y); bestBc.z = Mathf.Max(0, bestBc.z);
                float s2 = bestBc.x + bestBc.y + bestBc.z; bc = bestBc / Mathf.Max(1e-6f, s2);
                return best;
            }
            static Vector3 Bary(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
            {
                Vector2 v0 = b - a, v1 = c - a, v2 = p - a;
                float d00 = Vector2.Dot(v0, v0), d01 = Vector2.Dot(v0, v1), d11 = Vector2.Dot(v1, v1), d20 = Vector2.Dot(v2, v0), d21 = Vector2.Dot(v2, v1);
                float den = d00 * d11 - d01 * d01; if (Mathf.Abs(den) < 1e-20f) return new Vector3(1, 0, 0);
                float v = (d11 * d20 - d01 * d21) / den, w2 = (d00 * d21 - d01 * d20) / den;
                return new Vector3(1 - v - w2, v, w2);
            }
        }
    }
}
