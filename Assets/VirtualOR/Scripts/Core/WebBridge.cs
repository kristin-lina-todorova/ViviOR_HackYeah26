// Virtual OR - bridge to the ViviOR website (website/vor-website.html), which opens the WebGL build in an iframe with
// ?op=lichtenstein&mode=train|exam&embed=1(&resume=N) and listens for postMessage:
//   vivior:progress {step}   vivior:result {score, steps, time, fails, mode}   vivior:exit
// The website numbers the operation in 11 steps; our 17 internal steps are grouped onto them (Groups below).
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace VirtualOR
{
    public static class WebBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern string VB_Query();
        [DllImport("__Internal")] static extern void VB_Post(string json);
#else
        static string VB_Query() { return ""; }
        static void VB_Post(string json) { }
#endif
        public static bool Embedded { get; private set; }
        public static string Op { get; private set; }
        public static bool Exam { get; private set; }
        public static int Resume { get; private set; }   // 1-based website step requested (not supported beyond 1, see README)
        public static bool Demo { get; private set; }     // ?demo=1: scripted 10 s promo sequence
        public static bool Diag { get; private set; }     // ?diag=1 (with demo): close-up shot of the wound for checking

        // website step (1..11) -> our internal step ids, in order
        static readonly string[][] Groups = {
            new[] { "prep", "drape", "timeout" }, new[] { "incision" }, new[] { "dissect", "retract" }, new[] { "ring", "open_apo" },
            new[] { "nerve" }, new[] { "cord" }, new[] { "sac_q", "reduce" }, new[] { "mesh" }, new[] { "fix" },
            new[] { "count", "close_apo" }, new[] { "close_skin" },
        };
        public const int WebSteps = 11;
        static int sent = -1; static bool finished;

        public static void Init()
        {
            Op = "lichtenstein"; Resume = 1;
            var q = ParseQuery(VB_Query());
            string v;
            Embedded = q.TryGetValue("embed", out v) && v == "1";
            if (q.TryGetValue("op", out v) && v.Length > 0) Op = v;
            Exam = q.TryGetValue("mode", out v) && v == "exam";
            int r; if (q.TryGetValue("resume", out v) && int.TryParse(v, out r)) Resume = Mathf.Clamp(r, 1, WebSteps);
            Demo = q.TryGetValue("demo", out v) && v == "1";
            Diag = q.TryGetValue("diag", out v) && v == "1";
            sent = -1; finished = false;
        }

        static Dictionary<string, string> ParseQuery(string s)
        {
            var d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(s)) return d;
            foreach (var part in s.TrimStart('?').Split('&'))
            {
                if (part.Length == 0) continue; int i = part.IndexOf('=');
                string k = System.Uri.UnescapeDataString(i < 0 ? part : part.Substring(0, i)), val = i < 0 ? "" : System.Uri.UnescapeDataString(part.Substring(i + 1).Replace('+', ' '));
                d[k] = val;
            }
            return d;
        }

        // website step (1..11) that an internal step belongs to (0 if none)
        public static int WebStepOf(string internalId)
        {
            for (int g = 0; g < Groups.Length; g++) foreach (var id in Groups[g]) if (id == internalId) return g + 1;
            return 0;
        }
        // number of website steps fully completed once the procedure is at internal step index `current`
        public static int Completed(int current)
        {
            int done = 0;
            for (int g = 0; g < Groups.Length; g++)
            {
                bool all = true;
                foreach (var id in Groups[g]) { int idx = IndexOf(id); if (idx < 0 || idx >= current) all = false; }
                if (all) done = g + 1; else break;
            }
            return done;
        }
        static int IndexOf(string id) { for (int i = 0; i < Procedure.Steps.Length; i++) if (Procedure.Steps[i].id == id) return i; return -1; }

        public static void Progress(int currentStepIndex)
        {
            int n = Completed(currentStepIndex);
            if (n == sent) return; sent = n;
            VB_Post("{\"type\":\"vivior:progress\",\"step\":" + n + "}");
        }

        public static void Result(Procedure p)
        {
            if (finished) return; finished = true;
            var fails = new SortedSet<int>();
            foreach (var e in p.errors) { int w = WebStepOf(e.stepId); if (w > 0) fails.Add(w); }
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"type\":\"vivior:result\",\"score\":").Append(Mathf.Clamp(p.score, 0, 100))
              .Append(",\"steps\":").Append(Completed(p.stepIndex))
              .Append(",\"time\":").Append(Mathf.RoundToInt(p.time))
              .Append(",\"fails\":[").Append(string.Join(",", fails)).Append("]")
              .Append(",\"mode\":\"").Append(p.mode == Mode.Exam ? "exam" : "train").Append("\"}");
            VB_Post(sb.ToString());
        }

        public static void Exit() { VB_Post("{\"type\":\"vivior:exit\"}"); }
    }
}
