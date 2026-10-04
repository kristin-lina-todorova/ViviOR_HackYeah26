// Virtual OR - procedure engine and the open inguinal hernia (Lichtenstein) module.
// Every error is caught, explained and scored: wrong instrument, wrong incision, skipped safety step,
// tissue injury, uncontrolled bleeding and missed anatomy questions.
using System.Collections.Generic;
using UnityEngine;

namespace VirtualOR
{
    public enum Mode { Guided, Training, Exam }

    public class ErrorRec { public string step, stepId, title, why; public int penalty; public bool critical; public float time; }
    public class Toast { public string title, body; public bool good; public float t; }

    public class StepDef
    {
        public string id, title, guided, brief; public Tool[] tools;
        public StepDef(string i, string t, string g, string b, params Tool[] tl) { id = i; title = t; guided = g; brief = b; tools = tl; }
    }

    public enum PickKind { None, Tray, Skin, Apo, Cord, Sac, Nerve, MeshImplant, Structure, Equipment }
    public struct Pick { public PickKind kind; public Vector3 point; public Vector3 normal; public float dist; public TissueHit th; public Tool tray; public int idx; public string structure; public Equipment equip; }

    public class Procedure
    {
        public static readonly StepDef[] Steps = {
            new StepDef("prep", "Prepare the skin", "Take the prep sponge from the tray. Scrub the exposed groin with back-and-forth strokes, from the planned incision outwards. Once most of the window is covered, the rest is completed for you.", "Skin antisepsis of the operative field.", Tool.PrepSponge),
            new StepDef("drape", "Drape the patient", "Place the sterile drapes: press \"Apply drapes\". Alcohol-based prep keeps drying underneath.", "Sterile draping.", Tool.None),
            new StepDef("timeout", "Surgical safety time-out", "Before any incision the whole team confirms patient, procedure, side and antibiotic prophylaxis. Answer every item of the WHO checklist.", "WHO time-out.", Tool.None),
            new StepDef("incision", "Skin incision", "Take the scalpel. Cut a 5–6 cm incision parallel to the inguinal ligament, about 1.5–2 cm above it, starting just above the pubic tubercle and running laterally. Follow the purple marking. Make sure the prep is dry first.", "Inguinal skin incision.", Tool.Scalpel),
            new StepDef("dissect", "Through fat and Scarpa's fascia", "Take the diathermy. Drag along the floor of the wound to divide subcutaneous fat down to the shiny external oblique aponeurosis along the whole length. Coagulate every bleeding vessel (touch it with the diathermy). Use a swab if blood hides the field.", "Subcutaneous dissection and haemostasis.", Tool.Cautery, Tool.Scalpel, Tool.Swab, Tool.Forceps),
            new StepDef("retract", "Place the retractor", "Take the self-retaining retractor and click inside the wound to spread the edges.", "Exposure.", Tool.Retractor),
            new StepDef("ring", "Find the superficial ring", "Click the superficial inguinal ring: the opening in the external oblique aponeurosis just above and lateral to the pubic tubercle, where the cord emerges.", "Identify the superficial ring.", Tool.None, Tool.Forceps, Tool.Swab),
            new StepDef("open_apo", "Open the external oblique", "Take the Metzenbaum scissors. Starting at the superficial ring, cut laterally IN LINE WITH THE FIBRES of the aponeurosis for at least 3 cm, towards the deep ring. Follow the dotted guide.", "Divide the external oblique aponeurosis.", Tool.Scissors),
            new StepDef("nerve", "Protect the ilioinguinal nerve", "Click the ilioinguinal nerve: the thin pale-yellow nerve lying on the front of the spermatic cord. It must be identified and preserved.", "Identify the ilioinguinal nerve.", Tool.None, Tool.Forceps, Tool.Swab),
            new StepDef("cord", "Mobilise the spermatic cord", "Take the Penrose drain and click the spermatic cord to encircle and lift it. Do not clamp the cord.", "Mobilise the cord.", Tool.Penrose),
            new StepDef("sac_q", "What kind of hernia is it?", "Look at the sac: it lies within the cord, antero-medially, and emerges from the deep ring lateral to the inferior epigastric vessels.", "Classify the hernia.", Tool.None, Tool.Forceps, Tool.Swab),
            new StepDef("reduce", "Reduce the hernia sac", "Take the toothed forceps. Grab the sac and push it back through the deep ring into the abdomen.", "Reduce the indirect sac.", Tool.Forceps),
            new StepDef("mesh", "Place the mesh", "Take the polypropylene mesh and click on the posterior wall of the canal, centred between the pubic tubercle and the deep ring. It must overlap the pubic tubercle by 1.5–2 cm.", "Mesh placement.", Tool.Mesh),
            new StepDef("fix", "Fix the mesh to the inguinal ligament", "Take the needle holder. Place at least 4 sutures along the shelving edge of the inguinal ligament (the white band below the mesh), starting at the pubic tubercle and continuing laterally past the deep ring. Do not go deep below the ligament: the femoral vessels lie there.", "Mesh fixation.", Tool.NeedleHolder),
            new StepDef("count", "Instrument and swab count", "Before closing, count the swabs, needles and instruments. Press \"Request count\".", "Count before closure.", Tool.None),
            new StepDef("close_apo", "Close the external oblique", "Take the needle holder and close the aponeurosis over the cord: click along the cut edge, sutures no more than 1 cm apart, until the whole length is closed.", "Close the aponeurosis.", Tool.NeedleHolder),
            new StepDef("close_skin", "Close the skin", "Click along the skin incision with the needle holder: interrupted sutures no more than 1 cm apart until the wound is closed.", "Skin closure.", Tool.NeedleHolder),
            new StepDef("done", "Operation complete", "Well done. Review your debrief.", "", Tool.None),
        };

        // ---------------- refs
        public ORWorld world; public SurgeonView view; public InstrumentKit kit; public InguinalField field;
        public TissueSheet skin; public SkinSurface surf; public Synth audio;
        public Mode mode = Mode.Guided;
        public int stepIndex; public StepDef Step { get { return Steps[Mathf.Clamp(stepIndex, 0, Steps.Length - 1)]; } }
        public float time; public bool running, finished;
        public readonly List<ErrorRec> errors = new List<ErrorRec>();
        public readonly List<Toast> toasts = new List<Toast>();
        public string caption; public float captionT;
        public int score = 100; public int hintsUsed; public float hintT;
        public bool timeoutDone, countDone, prepDone; public float dryLeft = -1;  // seconds of simulated drying left
        public int swabsUsed;
        public Pick hover;
        // time-out checklist state
        public readonly string[] checklist = { "Patient identity confirmed: James Harlow, 46", "Procedure: open repair of RIGHT inguinal hernia, consent signed", "Operative site marked: RIGHT groin (arrow visible)", "Known allergies: none", "Antibiotic prophylaxis given within 60 minutes" };
        public readonly bool[] checks = new bool[5]; public bool antibioticAsked;
        public bool showChecklist, showQuestion, showAssistant; public int questionAnswer = -1;
        // interaction state
        readonly List<Vector2> stroke = new List<Vector2>(); bool stroking; TissueSheet strokeSheet;
        TissueCore.Attachment grabAttach; Rope.Grab ropeGrab; Rope grabRope;
        float lastCutRebuild;
        Vector3 lastPrepPoint; Vector2 lastPrepUV; bool lastPrepValid;
        const float PrepRadius = 0.035f;   // big sponge: 7 cm wide stroke
        public Vector2 incisionA, incisionB, apoA, apoB;     // guide lines (patient-local uv)
        GameObject retractorGO; readonly List<TissueCore.Attachment> retract = new List<TissueCore.Attachment>(), leaves = new List<TissueCore.Attachment>();
        readonly List<GameObject> leafClamps = new List<GameObject>();
        public readonly List<int> skinSutures = new List<int>(), apoSutures = new List<int>();
        public readonly List<Vector2> meshSutures = new List<Vector2>();
        bool vesselSE, vesselSCI; float burnCooldown, bleedPenaltyT; int vesselChecks;
        public float ebl { get { return surf != null ? surf.bloodLoss : 0; } }
        ParticleSystem smoke; float cauteryOn;
        public int coagW = 35; Equipment hoverEquip;   // diathermy coag power (set on the generator)
        GameObject beacon; LineRenderer guideLine;
        float handoverT = -1; Tool handoverTool; Vector3 handoverFrom;

        static readonly Vector2 Fo = ORWorld.Groin;   // field origin (patient-local xy)

        // ------------------------------------------------------------ setup
        public void Setup()
        {
            Vector2 d = field.LigamentDir, c = new Vector2(d.y, -d.x); if (c.y < 0) c = -c;   // c points cranially
            Vector2 pt = InguinalField.PubicTubercle + Fo;
            incisionA = pt + c * 0.017f + d * 0.004f; incisionB = incisionA - d * 0.058f;
            apoA = InguinalField.SupRing + Fo + c * 0.002f; apoB = apoA - d * 0.045f;
            // pre-operative site marking (arrow + planned incision) drawn by the surgeon on the ward
            surf.DrawLine(new List<Vector2> { incisionA + c * 0.045f - d * 0.03f, incisionA + c * 0.025f - d * 0.03f }, 0.0012f, false, 1f);
            surf.DrawLine(new List<Vector2> { incisionA + c * 0.025f - d * 0.03f + new Vector2(0.006f, 0.006f), incisionA + c * 0.025f - d * 0.03f, incisionA + c * 0.025f - d * 0.03f + new Vector2(-0.006f, 0.006f) }, 0.0012f, false, 1f);
            // smoke
            var sg = U.Child(world.root, "diathermy_smoke"); smoke = sg.AddComponent<ParticleSystem>();
            var main = smoke.main; main.startLifetime = 1.6f; main.startSpeed = 0.03f; main.startSize = 0.012f; main.maxParticles = 300; main.startColor = new Color(0.85f, 0.85f, 0.85f, 0.35f); main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = -0.02f;
            var em = smoke.emission; em.rateOverTime = 0;
            var sh = smoke.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.002f;
            var sz = smoke.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.4f, 1, 2.2f));
            var col = smoke.colorOverLifetime; col.enabled = true; var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.gray, 1) }, new[] { new GradientAlphaKey(0.5f, 0), new GradientAlphaKey(0, 1) }); col.color = gr;
            sg.GetComponent<ParticleSystemRenderer>().sharedMaterial = Mats.Particle(SoftDot());
            // guided-mode helpers
            beacon = new GameObject("guide_beacon"); beacon.transform.SetParent(world.root, false);
            var ring = new List<Vector3>(); for (int i = 0; i <= 32; i++) { float a = i / 32f * Mathf.PI * 2; ring.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.01f); }
            var bm = Mats.Std(new Color(0.42f, 0.81f, 1f), 0.4f); Mats.SetEmission(bm, new Color(0.18f, 0.64f, 0.86f) * 1.5f);   // brand Sky Blue
            U.AddMesh(beacon, U.Tube(ring, i => 0.0007f, 6), bm); beacon.SetActive(false);
            var lg = new GameObject("guide_line"); lg.transform.SetParent(world.root, false); guideLine = lg.AddComponent<LineRenderer>();
            guideLine.material = Mats.Particle(null); guideLine.widthMultiplier = 0.0012f; guideLine.startColor = guideLine.endColor = new Color(0.55f, 0.25f, 0.75f, 0.9f); guideLine.enabled = false;
        }

        static Texture2D SoftDot() { return U.MakeTex(32, 32, (u, v) => { float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2; return new Color(1, 1, 1, Mathf.Clamp01(1 - d) * Mathf.Clamp01(1 - d)); }, false, TextureWrapMode.Clamp); }

        public void Begin(Mode m)
        {
            mode = m; running = true; stepIndex = 0; time = 0; StartStep(); WebBridge.Progress(0);
            Say("Assistant", m == Mode.Exam ? "Ready when you are." : "Prep sponge is on the tray when you want it.");
        }

        void StartStep()
        {
            var id = Step.id;
            if (id == "drape" || id == "timeout" || id == "count" || id == "sac_q") { if (view.heldTool != Tool.None) ReturnTool(); }
            if (id == "timeout") showChecklist = true;
            if (id == "sac_q") { showQuestion = true; questionAnswer = -1; }
            if (id == "close_apo")
            {
                field.LiftCord(false);
                foreach (var a in leaves) field.apo.core.Detach(a); leaves.Clear(); foreach (var g in leafClamps) Object.Destroy(g); leafClamps.Clear();
            }
            if (id == "close_skin")
            {
                foreach (var a in retract) skin.core.Detach(a); retract.Clear(); if (retractorGO != null) Object.Destroy(retractorGO);
            }
            if (id == "done") { finished = true; running = false; Say("Assistant", "Count was correct. Nice work."); }
        }

        void Next()
        {
            if (mode != Mode.Exam) Good("Step complete", Step.title);
            stepIndex++; StartStep(); audio.Play("ok");
            WebBridge.Progress(stepIndex);   // website progress bar (11 website steps)
        }

        // ------------------------------------------------------------ errors & feedback
        readonly HashSet<string> once = new HashSet<string>();
        public void Error(string key, string title, string why, int penalty, bool critical = false)
        {
            if (key != null) { if (once.Contains(key)) return; once.Add(key); }
            errors.Add(new ErrorRec { step = Step.title, stepId = Step.id, title = title, why = why, penalty = penalty, critical = critical, time = time });
            score = Mathf.Max(0, score - penalty);
            if (mode != Mode.Exam) { toasts.Add(new Toast { title = title, body = why, good = false, t = 9f }); audio.Play("error"); }
            else audio.Play("tick");
        }
        void Good(string t, string b) { toasts.Add(new Toast { title = t, body = b, good = true, t = 3.5f }); }
        void Info(string t, string b) { if (mode != Mode.Exam) toasts.Add(new Toast { title = t, body = b, good = true, t = 5f }); }
        public void Say(string who, string line) { caption = who + ": " + line; captionT = 4.5f; }

        public bool Passed { get { foreach (var e in errors) if (e.critical) return false; return score >= 80; } }

        // ------------------------------------------------------------ main tick
        public void Tick(float dt)
        {
            if (!running) { UpdateEffects(dt); return; }
            time += dt;
            for (int i = toasts.Count - 1; i >= 0; i--) { toasts[i].t -= dt; if (toasts[i].t <= 0) toasts.RemoveAt(i); }
            captionT -= dt; hintT -= dt; burnCooldown -= dt;
            // drying of alcohol-based prep (3 minutes, simulated faster)
            if (dryLeft > 0) { dryLeft = Mathf.Max(0, dryLeft - dt * 6f); surf.Dry(dt, 1f / 30f); }
            // bleeding
            surf.MarkWound(skin.core);
            surf.StepBlood(dt, skin.core);
            UpdateBleeders(dt);
            if (surf.pool > surf.WoundCapacityMl * 1.4f) { bleedPenaltyT += dt; if (bleedPenaltyT > 20f) { bleedPenaltyT = 0; Error(null, "Uncontrolled bleeding", "Blood is overflowing the wound. Find the bleeding vessel and coagulate it; mop with a swab to see.", 3); } }
            if (ebl > 150 && !once.Contains("ebl")) Error("ebl", "Excessive blood loss", "More than 150 ml lost in an elective hernia repair. Control each bleeding point as soon as you see it.", 8);
            surf.Update(dt);
            field.Tick(dt);
            Handover(dt);
            TickAssistant(dt);
            if (demo) view.PoseTool(dt); else Interact(dt);
            UpdateGuides();
            UpdateEffects(dt);
        }

        void UpdateEffects(float dt)
        {
            if (smoke == null) return;
            var em = smoke.emission; em.rateOverTime = cauteryOn > 0 ? 70 : 0; cauteryOn -= dt;
            audio.SetLoop("cautery", cauteryOn > 0);
        }

        // ------------------------------------------------------------ picking
        public Pick PickAt(Ray ray)
        {
            var best = new Pick { kind = PickKind.None, dist = 5f };
            RaycastHit h;
            if (Physics.Raycast(ray, out h, 5f))
            {
                var ti = h.collider.GetComponentInParent<TrayItem>();
                if (ti != null && ti.gameObject.activeSelf) best = new Pick { kind = PickKind.Tray, point = h.point, normal = h.normal, dist = h.distance, tray = ti.tool };
                else if (h.collider.GetComponent<Equipment>() != null) best = new Pick { kind = PickKind.Equipment, point = h.point, dist = h.distance, equip = h.collider.GetComponent<Equipment>() };
                else best = new Pick { kind = PickKind.Structure, point = h.point, normal = h.normal, dist = h.distance, structure = h.collider.gameObject.name };
            }
            var sh = skin.Raycast(ray, best.dist);
            if (sh.hit && sh.distance < best.dist) best = new Pick { kind = PickKind.Skin, point = sh.point, normal = sh.normal, dist = sh.distance, th = sh };
            var ah = field.apo.Raycast(ray, best.dist);
            if (ah.hit && ah.distance < best.dist) best = new Pick { kind = PickKind.Apo, point = ah.point, normal = ah.normal, dist = ah.distance, th = ah };
            if (field.meshSheet != null) { var mh = field.meshSheet.Raycast(ray, best.dist); if (mh.hit && mh.distance < best.dist) best = new Pick { kind = PickKind.MeshImplant, point = mh.point, normal = mh.normal, dist = mh.distance, th = mh }; }
            RopePick(ray, field.cord.x, 0.0062f, PickKind.Cord, ref best);
            if (!field.sacReduced && field.sacGO.activeSelf) RopePick(ray, field.sac.x, 0.009f, PickKind.Sac, ref best);
            RopePick(ray, field.nervePts.ToArray(), 0.0022f, PickKind.Nerve, ref best);
            return best;
        }

        void RopePick(Ray ray, Vector3[] localPts, float r, PickKind kind, ref Pick best)
        {
            var P = world.patient;
            for (int i = 0; i < localPts.Length; i++)
            {
                Vector3 w = P.TransformPoint(localPts[i]);
                float t = Vector3.Dot(w - ray.origin, ray.direction); if (t <= 0 || t >= best.dist) continue;
                float d = Vector3.Distance(ray.origin + ray.direction * t, w);
                if (d < r) { float tt = t - Mathf.Sqrt(Mathf.Max(0, r * r - d * d)); if (tt < best.dist) best = new Pick { kind = kind, point = ray.origin + ray.direction * tt, normal = (ray.origin + ray.direction * tt - w).normalized, dist = tt, idx = i }; }
            }
        }

        Vector2 PL(Vector3 world) { var p = this.world.patient.InverseTransformPoint(world); return new Vector2(p.x, p.y); }
        Vector2 FieldXY(Vector3 world) { return PL(world) - Fo; }

        // ------------------------------------------------------------ interaction
        void Interact(float dt)
        {
            if (!showChecklist && !showQuestion && InputState.KeyDown(KeyCode.E)) showAssistant = !showAssistant;
            if (showAssistant && InputState.KeyDown(KeyCode.Escape)) { showAssistant = false; return; }
            if (showChecklist || showQuestion || showAssistant) { view.aimPoint = null; return; }
            var ray = view.MouseRay();
            hover = InputState.OverUI ? new Pick() : PickAt(ray);
            var eq = hover.kind == PickKind.Equipment ? hover.equip : null;
            if (eq != hoverEquip) { if (hoverEquip != null) hoverEquip.SetHover(false); if (eq != null) eq.SetHover(true); hoverEquip = eq; }
            bool tissueAim = hover.kind != PickKind.None && hover.kind != PickKind.Tray && hover.kind != PickKind.Equipment;
            view.aimPoint = tissueAim ? (Vector3?)hover.point : null;
            view.pressing = InputState.LMB && tissueAim;
            view.PoseTool(dt);
            if (InputState.KeyDown(KeyCode.Q) || InputState.KeyDown(KeyCode.Escape)) { if (view.heldTool != Tool.None) ReturnTool(); }
            if (InputState.KeyDown(KeyCode.F)) view.Home();
            if (InputState.KeyDown(KeyCode.H) && mode == Mode.Training) { hintT = 6f; hintsUsed++; score = Mathf.Max(0, score - 2); Info("Hint", Step.guided); }
            if (InputState.OverUI) return;

            if (InputState.LMBDown)
            {
                if (hover.kind == PickKind.Tray) { RequestTool(hover.tray); return; }
                if (hover.kind == PickKind.Equipment) { if (hover.equip.onClick != null) hover.equip.onClick(); return; }
                OnPress(hover);
            }
            else if (InputState.LMB) OnHold(hover, dt);
            if (InputState.LMBUp) OnRelease(hover);
        }

        // ------------------------------------------------------------ instruments
        public void RequestTool(Tool t)
        {
            if (handoverT >= 0) return;
            var s = Step;
            bool allowed = false; foreach (var a in s.tools) if (a == t) allowed = true;
            if (t == Tool.Swab) allowed = true;
            if (!allowed)
            {
                if ((t == Tool.Scalpel || t == Tool.Cautery) && stepIndex <= 2 && !timeoutDone)
                    Error("early_" + t, "Skipped safety step: no time-out yet", "You picked up a " + Tools.Get(t).name.ToLower() + " before the WHO time-out. Nothing sharp touches the patient until the team has confirmed patient, procedure, side and antibiotics.", 15, true);
                else
                {
                    string want = s.tools.Length > 0 && s.tools[0] != Tool.None ? Tools.Get(s.tools[0]).name : "no instrument";
                    Error("wrong_" + s.id + t, "Wrong instrument: " + Tools.Get(t).name, "This step needs " + want.ToLower() + ". The " + Tools.Get(t).name.ToLower() + " is used for " + Tools.Get(t).use + ".", 3);
                }
            }
            if (view.heldTool != Tool.None) ReturnTool(true);
            handoverTool = t; handoverT = 0; handoverFrom = kit.trayItems[t].transform.position; kit.ShowOnTray(t, false);
            if (t == Tool.Swab) { swabsUsed++; }
        }
        static string ShortName(Tool t)
        {
            switch (t) { case Tool.Scalpel: return "Scalpel, ten blade"; case Tool.Cautery: return "Diathermy"; case Tool.NeedleHolder: return "Prolene on a needle holder"; case Tool.PrepSponge: return "Prep"; default: return Tools.Get(t).name.Split('(')[0].Trim(); }
        }

        void Handover(float dt)
        {
            if (handoverT < 0) return;
            handoverT += dt;
            var g = kit.held[handoverTool]; g.SetActive(true);
            Vector3 lift = handoverFrom + Vector3.up * 0.2f;   // instrument rises off the tray, then comes to the surgeon's hand
            Vector3 surgeonHand = view.cam.transform.TransformPoint(new Vector3(0.08f, -0.16f, 0.38f));
            if (handoverT < 0.55f) g.transform.position = Vector3.Lerp(handoverFrom, lift, Smooth(handoverT / 0.55f));
            else g.transform.position = Vector3.Lerp(lift, surgeonHand, Smooth((handoverT - 0.55f) / 0.45f));
            g.transform.rotation = Quaternion.Slerp(g.transform.rotation, Quaternion.LookRotation(view.cam.transform.forward, Vector3.up), dt * 6);
            if (handoverT >= 1f)
            {
                handoverT = -1; view.Take(handoverTool, g);
            }
        }
        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }

        void ReturnTool(bool silent = false)
        {
            var t = view.heldTool; if (t == Tool.None) return;
            kit.held[t].SetActive(false); kit.ShowOnTray(t, true); view.Take(Tool.None, null);
            EndGrab();
        }

        // ------------------------------------------------------------ actions
        void OnPress(Pick p)
        {
            var t = view.heldTool; var s = Step.id;
            if (s == "ring" && p.kind != PickKind.None) { AnswerRing(p); return; }
            if (s == "nerve" && p.kind != PickKind.None) { AnswerNerve(p); return; }
            if (t == Tool.None) return;
            switch (t)
            {
                case Tool.PrepSponge: lastPrepValid = false; break;
                case Tool.Scalpel:
                    if (p.kind == PickKind.Skin && p.th.region == TissueRegion.Surface)
                    {
                        if (!SafetyBeforeCut("scalpel")) return;
                        stroking = true; strokeSheet = skin; stroke.Clear();
                        if (skin.core.HasCut)
                        {
                            var c = skin.core.cut; float d0 = Vector2.Distance(c[0], p.th.uv), d1 = Vector2.Distance(c[c.Count - 1], p.th.uv);
                            if (Mathf.Min(d0, d1) < 0.008f) { if (d0 < d1) { for (int i = c.Count - 1; i >= 0; i--) stroke.Add(c[i]); } else stroke.AddRange(c); }
                            else { Error("second_cut", "Second skin incision", "Extend the existing incision instead of making a new one: start the stroke at one end of the wound.", 4); stroking = false; return; }
                        }
                        stroke.Add(p.th.uv);
                        audio.Play("scalpel");
                    }
                    else if (p.kind == PickKind.Apo) { stroking = true; strokeSheet = field.apo; stroke.Clear(); stroke.Add(p.th.uv); }
                    break;
                case Tool.Scissors:
                    if (p.kind == PickKind.Apo && p.th.region == TissueRegion.Surface && s == "open_apo") { stroking = true; strokeSheet = field.apo; stroke.Clear(); stroke.Add(p.th.uv); }
                    else if (p.kind == PickKind.Cord || p.kind == PickKind.Nerve) Error("scissor_" + p.kind, "Injury: scissors on the " + (p.kind == PickKind.Cord ? "spermatic cord" : "ilioinguinal nerve"), "Cutting here would divide the vas deferens, testicular vessels or the nerve. Scissors only open the aponeurosis along its fibres.", 15, true);
                    break;
                case Tool.Retractor:
                    if (s == "retract" && (p.kind == PickKind.Skin || p.kind == PickKind.Apo) && skin.core.HasCut) PlaceRetractor();
                    break;
                case Tool.Penrose:
                    if (s == "cord" && p.kind == PickKind.Cord) { field.LiftCord(true); Next(); }
                    else if (p.kind == PickKind.Nerve) Info("That is the nerve", "Encircle the cord itself, keeping the nerve out of the drain.");
                    break;
                case Tool.Hemostat:
                    if (p.kind == PickKind.Cord) Error("clamp_cord", "Injury: clamp on the spermatic cord", "Artery forceps crush the vas deferens and testicular vessels. Encircle the cord gently with a Penrose drain.", 15, true);
                    else if (p.kind == PickKind.Nerve) Error("clamp_nerve", "Injury: ilioinguinal nerve crushed", "Crushing the nerve causes chronic groin pain and numbness. Identify it and keep it out of instruments.", 10, true);
                    break;
                case Tool.Forceps:
                    StartGrab(p);
                    break;
                case Tool.Mesh:
                    if (s == "mesh" && (p.kind == PickKind.Structure || p.kind == PickKind.Cord)) PlaceMesh(p);
                    break;
                case Tool.NeedleHolder:
                    Suture(p);
                    break;
                case Tool.Cautery:
                    if (!SafetyBeforeCut("diathermy")) return;
                    break;
            }
        }

        void OnHold(Pick p, float dt)
        {
            var t = view.heldTool;
            if (t == Tool.PrepSponge && p.kind == PickKind.Skin && p.th.region == TissueRegion.Surface)
            {
                if (Step.id != "prep" && !once.Contains("lateprep")) { if (skin.core.HasCut) { Error("lateprep", "Prep on an open wound", "Antiseptic is applied to intact skin before draping, not into the wound.", 4); } }
                float moved = lastPrepValid ? Vector3.Distance(lastPrepPoint, p.point) : 0;
                // paint along the whole path since last frame, so fast strokes leave no gaps
                int n = lastPrepValid ? Mathf.Clamp(Mathf.CeilToInt(Vector2.Distance(lastPrepUV, p.th.uv) / (PrepRadius * 0.4f)), 1, 24) : 1;
                for (int k = 1; k <= n; k++) surf.PaintPrep(lastPrepValid ? Vector2.Lerp(lastPrepUV, p.th.uv, k / (float)n) : p.th.uv, PrepRadius, moved / n, dt);
                lastPrepPoint = p.point; lastPrepUV = p.th.uv; lastPrepValid = true;
                if (Step.id == "prep")
                {
                    var area = ShrinkWindow(0.008f);
                    if (surf.PrepCoverage(area) >= 0.80f && surf.scrubSeconds >= 6f)   // most of it done: finish the rest automatically
                    {
                        surf.CompletePrep(); prepDone = true; dryLeft = 180f;
                        Info("Prep complete", "Coverage complete. Alcohol-based prep must dry for 3 minutes before incision or diathermy.");
                        ReturnTool(true); Next();
                    }
                }
            }
            if (stroking && (p.kind == PickKind.Skin || p.kind == PickKind.Apo) && strokeSheet != null)
            {
                var sh = strokeSheet.Raycast(view.MouseRay());
                if (sh.hit && sh.region == TissueRegion.Surface)
                {
                    if (Vector2.Distance(stroke[stroke.Count - 1], sh.uv) > 0.0015f)
                    {
                        stroke.Add(sh.uv);
                        if (strokeSheet == skin)
                        {
                            surf.AddBloodAt(sh.uv, 0.003f);   // thin bleeding line follows the blade at once
                            // re-cutting the membrane re-triangulates it (~0.1-0.2 s in WebGL): at most twice a second while dragging
                            if (stroke.Count > 3 && Time.unscaledTime - lastCutRebuild > 0.5f) { lastCutRebuild = Time.unscaledTime; skin.core.SetCut(stroke, 0.38f); }
                            BleedAlongCut();
                        }
                    }
                }
            }
            if (t == Tool.Cautery && (p.kind == PickKind.Skin || p.kind == PickKind.Apo || p.kind == PickKind.Cord || p.kind == PickKind.Nerve || p.kind == PickKind.Sac || p.kind == PickKind.Structure))
            {
                cauteryOn = 0.12f; smoke.transform.position = p.point; 
                if (p.kind == PickKind.Skin && p.th.region == TissueRegion.Surface)
                {
                    surf.Burn(p.th.uv, 0.002f * coagW / 35f);
                    if (burnCooldown <= 0) { burnCooldown = 3; Error("burn", "Skin burn", "Diathermy on the skin surface causes a full-thickness burn and poor scar. Use it only inside the wound, on fat and bleeding points.", 5); }
                }
                else if (p.kind == PickKind.Skin) { CauterizeWound(p, dt * coagW / 35f); }
                else if (p.kind == PickKind.Nerve) Error("burn_nerve", "Thermal injury to the ilioinguinal nerve", "Diathermy near the nerve causes neuropathic groin pain. Keep energy devices away from it.", 15, true);
                else if (p.kind == PickKind.Cord) Error("burn_cord", "Thermal injury to the spermatic cord", "Diathermy on the cord can thrombose the testicular vessels (ischaemic orchitis) or injure the vas.", 15, true);
                else if (p.kind == PickKind.Sac && Step.id != "reduce") Error("burn_sac", "Diathermy on the hernia sac", "The sac may contain bowel. Dissect it bluntly and reduce it; do not burn it.", 8);
            }
            if (t == Tool.Swab && (p.kind == PickKind.Skin))
            {
                float got = surf.Swab(p.th.uv, 0.012f, dt);
                if (p.th.region != TissueRegion.Surface) { float take = Mathf.Min(surf.pool, dt * 4f); surf.pool -= take; got += take; }
            }
            if (t == Tool.Scalpel && p.kind == PickKind.Skin && p.th.region != TissueRegion.Surface && Step.id == "dissect")
            {
                skin.core.DeepenAt(p.th.cutIndex, dt * 1.2f, 0.008f); CheckVessels(); if (Random.value < dt * 2) AddOoze(p.th.cutIndex, 0.03f);
            }
            if (grabAttach != null) grabAttach.target = world.patient.InverseTransformPoint(DragPoint());
            if (ropeGrab != null)
            {
                ropeGrab.target = world.patient.InverseTransformPoint(DragPoint());
                if (grabRope == field.sac && Step.id == "reduce" && field.SacFundusToRing() < 0.010f) { EndGrab(); field.StartReduceSac(); Info("Sac reduced", "The sac is back in the peritoneal cavity through the deep ring."); Next(); }
            }
        }

        Vector3 dragPlanePoint; Vector3 dragPlaneNormal;
        Vector3 DragPoint()
        {
            var ray = view.MouseRay(); var pl = new Plane(dragPlaneNormal, dragPlanePoint); float e;
            if (pl.Raycast(ray, out e)) return ray.GetPoint(e); return dragPlanePoint;
        }

        void OnRelease(Pick p)
        {
            if (stroking)
            {
                stroking = false;
                if (strokeSheet == skin) FinishIncision();
                else if (strokeSheet == field.apo) FinishAponeurotomy();
            }
            lastPrepValid = false;
            if (grabAttach != null || ropeGrab != null) EndGrab();
        }

        bool SafetyBeforeCut(string what)
        {
            if (!timeoutDone)
            {
                Error("cut_before_timeout", "Skipped safety step: incision before time-out", "The WHO surgical safety checklist requires a time-out before the skin incision. Wrong-site and wrong-patient surgery are prevented here.", 20, true);
                return false;
            }
            if (dryLeft > 0)
            {
                Error("wet_prep_" + what, "Fire risk: alcohol prep still wet", "Alcohol-based antiseptic must dry for 3 minutes. Using " + what + " on wet prep, especially diathermy, can ignite the vapour under the drapes.", 15, true);
                return what != "diathermy";
            }
            return true;
        }

        void StartGrab(Pick p)
        {
            dragPlanePoint = p.point; dragPlaneNormal = (view.cam.transform.position - p.point).normalized;
            if (p.kind == PickKind.Sac) { grabRope = field.sac; ropeGrab = new Rope.Grab { i = Mathf.Max(1, p.idx), target = world.patient.InverseTransformPoint(p.point), k = 0.35f }; field.sac.grabs.Add(ropeGrab); }
            else if (p.kind == PickKind.Cord) { grabRope = field.cord; ropeGrab = new Rope.Grab { i = Mathf.Max(1, p.idx), target = world.patient.InverseTransformPoint(p.point), k = 0.2f }; field.cord.grabs.Add(ropeGrab); }
            else if (p.kind == PickKind.Skin) grabAttach = skin.core.Attach(p.th.particle, skin.core.x[p.th.particle], 2e-6f);
            else if (p.kind == PickKind.Apo) grabAttach = field.apo.core.Attach(p.th.particle, field.apo.core.x[p.th.particle], 2e-6f);
            else if (p.kind == PickKind.Nerve) Error("grab_nerve", "Nerve handled with toothed forceps", "Do not grasp the ilioinguinal nerve; handle it only with a vessel loop if it must be moved.", 6);
            if (grabAttach != null) grabSheet = p.kind == PickKind.Skin ? skin : field.apo;
        }
        TissueSheet grabSheet;
        void EndGrab()
        {
            if (grabAttach != null && grabSheet != null) grabSheet.core.Detach(grabAttach); grabAttach = null; grabSheet = null;
            if (ropeGrab != null && grabRope != null) grabRope.grabs.Remove(ropeGrab); ropeGrab = null; grabRope = null;
        }

        // ------------------------------------------------------------ incision
        void FinishIncision()
        {
            if (stroke.Count < 2) return;
            skin.core.SetCut(stroke, 0.38f);
            if (Step.id != "incision") { Error("cut_wrong_step", "Incision at the wrong moment", "The skin is incised only in the incision step, after prep, drapes and the time-out.", 10, true); return; }
            var c = skin.core.cut; if (c.Count < 2) return;
            float L = U.PolyLength(c);
            if (L < 0.035f) { Info("Keep going", "The incision is " + (L * 100).ToString("0.0") + " cm. Extend it to 5–6 cm by starting a new stroke at one end."); return; }
            Vector2 d = field.LigamentDir, cr = new Vector2(d.y, -d.x); if (cr.y < 0) cr = -cr;
            Vector2 dir = (c[c.Count - 1] - c[0]).normalized;
            float ang = Mathf.Acos(Mathf.Clamp01(Mathf.Abs(Vector2.Dot(dir, d)))) * Mathf.Rad2Deg;
            // signed offset from the inguinal ligament line (cranial positive), averaged
            float off = 0; foreach (var q in c) { float dd; Vector3 lp = field.NearestLigamentPoint(q - Fo, out dd); off += Vector2.Dot(q - Fo - new Vector2(lp.x, lp.y), cr); }
            off /= c.Count;
            Vector2 pt = InguinalField.PubicTubercle + Fo; float medial = Mathf.Min(Vector2.Distance(c[0], pt), Vector2.Distance(c[c.Count - 1], pt));
            bool ok = true;
            if (ang > 25f) { ok = false; Error("inc_angle", "Wrong incision: orientation", "Your incision is " + ang.ToString("0") + "° off the line of the inguinal ligament. A transverse/oblique cut parallel to the ligament follows Langer's lines (narrow scar) and lies directly over the canal; other directions give poor exposure and a wide scar.", 8); }
            if (off < 0.006f) { ok = false; Error("inc_low", "Wrong incision: too low", "The incision lies on or below the inguinal ligament, over the femoral canal and thigh crease. It should be 1.5–2 cm above and parallel to the ligament.", 8); }
            else if (off > 0.032f) { ok = false; Error("inc_high", "Wrong incision: too high", "The incision is more than 3 cm above the ligament: the canal will lie at the lower edge of the wound and need heavy retraction.", 6); }
            if (L > 0.085f) { ok = false; Error("inc_long", "Incision longer than needed", "A 5–6 cm incision gives full exposure for an open Lichtenstein repair; longer wounds hurt more and heal worse.", 3); }
            if (medial > 0.03f) Error("inc_medial", "Incision does not reach the pubic tubercle", "The medial end should lie just above the pubic tubercle: the mesh must be anchored there.", 4);
            if (ok) Info("Good incision", (L * 100).ToString("0.0") + " cm, " + ang.ToString("0") + "° from the ligament line, " + (off * 100).ToString("0.0") + " cm above it.");
            // dermal ooze along the cut
            for (int k = 2; k < c.Count - 2; k += Mathf.Max(3, c.Count / 4)) AddOoze(k, 0.025f);
            Next();
        }

        void BleedAlongCut() { if (Random.value < 0.08f) surf.AddBloodAt(stroke[stroke.Count - 1], 0.02f); }

        void AddOoze(int k, float rate)
        {
            if (k <= 0 || k >= skin.core.cut.Count - 1) return;
            var b = new Bleeder { cutIndex = k, uv = skin.core.cut[k], rate = rate, vessel = "dermal capillaries" };
            surf.bleeders.Add(b);
        }

        // ------------------------------------------------------------ dissection / haemostasis
        void CauterizeWound(Pick p, float dt)
        {
            int k = p.th.cutIndex; if (k < 0) return;
            if (Step.id == "dissect" || Step.id == "incision" || Step.id == "retract")
            {
                skin.core.DeepenAt(k, dt * 0.75f * (counterTractionT > 0 ? 1.5f : 1f), 0.009f);   // faster with the assistant's counter-traction
                CheckVessels();
            }
            // coagulate bleeders near the tip
            foreach (var b in surf.bleeders)
            {
                if (b.sealed_) continue;
                if (Mathf.Abs(b.cutIndex - k) <= 2 || Vector2.Distance(b.uv, p.th.uv) < 0.006f) { b.sealed_ = true; audio.Play("sizzle"); if (b.blob != null) b.blob.transform.localScale *= 0.5f; }
            }
            CheckDissectDone();
        }
        void CheckDissectDone()
        {
            if (Step.id == "dissect" && skin.core.MinInteriorDepth() >= 0.98f && ActiveBleeders() == 0 && !vesselPending) { Info("At the aponeurosis", "The external oblique aponeurosis is exposed along the whole wound."); Next(); }
        }
        bool vesselPending;

        int ActiveBleeders() { int n = 0; foreach (var b in surf.bleeders) if (!b.sealed_ && b.rate > 0.03f) n++; return n; }

        // superficial epigastric and superficial circumflex iliac veins run in the subcutaneous fat across the field
        void CheckVessels()
        {
            var c = skin.core.cut; if (c.Count < 3) return;
            TryVessel(ref vesselSE, new Vector2(0.004f, -0.075f) + Fo, new Vector2(-0.046f, 0.065f) + Fo, "superficial epigastric vein", 0.16f);
            TryVessel(ref vesselSCI, new Vector2(0.016f, -0.07f) + Fo, new Vector2(0.075f, 0.03f) + Fo, "superficial circumflex iliac vein", 0.12f);
            vesselPending = false;
            foreach (var b in surf.bleeders) if (!b.sealed_ && b.rate > 0.1f) vesselPending = true;
            if (Step.id == "dissect" && skin.core.MinInteriorDepth() >= 0.98f && ActiveBleeders() > 0 && !once.Contains("leftbleed")) Info("Still bleeding", "The wound is deep enough, but a vessel is still bleeding. Coagulate it before moving on.");
        }
        void TryVessel(ref bool done, Vector2 a, Vector2 b, string name, float rate)
        {
            if (done) return; var c = skin.core.cut;
            for (int k = 1; k < c.Count - 1; k++)
            {
                float t; float d = U.DistPointSeg(c[k], a, b, out t);
                if (d < 0.003f && skin.core.depth[k] > 0.45f)
                {
                    done = true;
                    var bl = new Bleeder { cutIndex = k, uv = c[k], rate = rate, vessel = name };
                    var blob = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.Destroy(blob.GetComponent<Collider>()); blob.transform.SetParent(world.patient, false);
                    blob.transform.localScale = Vector3.one * 0.004f; blob.GetComponent<Renderer>().sharedMaterial = Mats.Std(new Color(0.40f, 0.03f, 0.04f), 0.7f);
                    bl.blob = blob; surf.bleeders.Add(bl);
                    if (mode == Mode.Guided) Info("Bleeding vessel", "You divided the " + name + ". Coagulate it with the diathermy.");
                    return;
                }
            }
        }

        void UpdateBleeders(float dt)
        {
            foreach (var b in surf.bleeders)
            {
                if (b.vessel == "dermal capillaries" && !b.sealed_) { b.rate = Mathf.Max(0, b.rate - dt * 0.0006f); if (b.rate <= 0.0005f) b.sealed_ = true; }
                if (b.blob == null || b.cutIndex >= skin.core.chainL.Count) continue;
                int L = skin.core.chainL[b.cutIndex];
                Vector3 pos = skin.core.x[L] - skin.core.restN[L] * (skin.core.depth[b.cutIndex] * skin.core.thick[L] * 0.55f);
                b.blob.transform.localPosition = pos;
                float pulse = b.sealed_ ? 0.5f : 0.8f + 0.3f * Mathf.Sin(Time.time * (b.arterial ? 7.5f : 2.5f));
                b.blob.transform.localScale = Vector3.one * 0.005f * pulse;
                if (b.sealed_) b.blob.GetComponent<Renderer>().sharedMaterial.color = new Color(0.22f, 0.10f, 0.06f);
            }
            float fill = Mathf.Clamp01(surf.pool / surf.WoundCapacityMl);
            Mats.SetColor(skin.floorMat, Color.Lerp(Color.white, new Color(0.45f, 0.05f, 0.05f), fill * 0.9f));
            Mats.SetColor(skin.wallMat, Color.Lerp(Color.white, new Color(0.55f, 0.1f, 0.1f), Mathf.Clamp01(fill - 0.3f)));
        }

        // ------------------------------------------------------------ retraction
        void PlaceRetractor()
        {
            var core = skin.core; int n = core.cut.Count; if (n < 5) return;
            int mid = n / 2;
            foreach (int k in new[] { mid - n / 5, mid, mid + n / 5 })
            {
                if (k <= 0 || k >= n - 1) continue;
                int L = core.chainL[k], R = core.chainR[k]; Vector3 across = (core.x[R] - core.x[L]); across = across.sqrMagnitude > 1e-8f ? across.normalized : Vector3.Cross(core.restN[L], Vector3.up).normalized;
                retract.Add(core.Attach(L, core.x[L] - across * 0.013f, 1e-6f)); retract.Add(core.Attach(R, core.x[R] + across * 0.013f, 1e-6f));
            }
            retractorGO = Object.Instantiate(kit.held[Tool.Retractor]); retractorGO.SetActive(true);
            retractorGO.transform.SetParent(world.patient, false);
            int Lm = core.chainL[mid], Rm = core.chainR[mid];
            Vector3 c = (core.x[Lm] + core.x[Rm]) * 0.5f; Vector3 ac = (core.x[Rm] - core.x[Lm]).normalized;
            retractorGO.transform.localPosition = c + core.restN[Lm] * 0.01f;
            retractorGO.transform.localRotation = Quaternion.LookRotation(Vector3.Cross(ac, core.restN[Lm]), core.restN[Lm]) * Quaternion.Euler(0, 90, 0);
            ReturnTool(true); kit.ShowOnTray(Tool.Retractor, false);
            Say("Assistant", "Retractor in.");
            Next();
        }

        // ------------------------------------------------------------ assistant surgeon
        // The trainee decides WHICH instrument to hand the assistant; the assistant then uses it the way a first assistant
        // should (mop, clamp, coagulate, counter-traction, retract, hold the cord, cut suture ends). Operating-surgeon tasks
        // are declined: only the trainee incises, opens, places the mesh and sutures.
        public static readonly Tool[] AssistantTools = { Tool.Swab, Tool.Hemostat, Tool.Cautery, Tool.Forceps, Tool.Retractor, Tool.Penrose, Tool.Scissors, Tool.Scalpel, Tool.NeedleHolder, Tool.Mesh, Tool.PrepSponge };
        float counterTractionT;
        public bool AssistantBusy { get { return task != null; } }

        // One visible job at a time: the instrument leaves the tray, goes to the assistant's hand, to the target in the field,
        // is used there (the effect happens on arrival), then goes back to the tray (or stays, for retractor / Penrose).
        enum Motion { Still, Dab, Touch, Snip }
        class AssistTask
        {
            public Tool tool; public GameObject g; public int phase; public float t, useTime; public Motion motion;
            public System.Func<Vector3> target; public System.Action onArrive; public bool returns, trayWasShown;
            public Vector3 trayPos, from;
        }
        AssistTask task;

        void Assistant(string line) { caption = "Assistant: " + line; captionT = 4.5f; }

        public void AskAssistant(Tool t)
        {
            showAssistant = false;
            if (!running) return;
            if (task != null) { Assistant("One moment, I'm still on it."); return; }
            var core = skin.core; bool wound = core.HasCut && core.cut.Count > 4; var s = Step.id;
            switch (t)
            {
                case Tool.Swab:
                    if (!wound) { Assistant("There's nothing to mop yet."); return; }
                    Begin(t, "Swab.", () => WoundPoint(0.5f), 2.4f, Motion.Dab, true, () =>
                    {
                        for (int k = 0; k < core.cut.Count; k++) surf.Swab(core.cut[k], 0.012f, 0.6f);
                        surf.pool = 0; swabsUsed++; Assistant("Swabbing. Field's clear.");
                    });
                    return;
                case Tool.Hemostat:
                case Tool.Cautery:
                {
                    Bleeder worst = null; foreach (var b in surf.bleeders) if (!b.sealed_ && b.rate > 0.01f && (worst == null || b.rate > worst.rate)) worst = b;
                    if (worst == null) { Assistant(wound ? "Nothing's bleeding at the moment." : "There's no wound yet."); return; }
                    var bl = worst;
                    System.Func<Vector3> at = () => bl.blob != null ? bl.blob.transform.localPosition : WoundPoint(bl.cutIndex / (float)Mathf.Max(1, core.cut.Count - 1));
                    if (t == Tool.Hemostat)
                        Begin(t, "Clip.", at, 8f, Motion.Still, true, () => { bl.sealed_ = true; if (bl.blob != null) bl.blob.transform.localScale *= 0.5f; audio.Play("ok"); Assistant("Clamped the " + bl.vessel + "."); CheckVessels(); CheckDissectDone(); });
                    else
                        Begin(t, "Diathermy.", at, 1.4f, Motion.Touch, true, () => { bl.sealed_ = true; if (bl.blob != null) bl.blob.transform.localScale *= 0.5f; cauteryOn = 1.0f; smoke.transform.position = world.patient.TransformPoint(at()); audio.Play("sizzle"); Assistant("Coagulated the " + bl.vessel + "."); CheckVessels(); CheckDissectDone(); });
                    return;
                }
                case Tool.Forceps:
                    if (!wound) { Assistant("Nothing to hold yet."); return; }
                    Begin(t, "Forceps.", () => WoundPoint(0.5f) + core.restN[core.chainL[core.cut.Count / 2]] * 0.004f, 20f, Motion.Still, true, () =>
                    { counterTractionT = 20f; Assistant(Step.id == "reduce" ? "Holding the cord steady for you." : "Holding the edge up, counter-traction for you."); });
                    return;
                case Tool.Retractor:
                    if (s == "retract") { Begin(t, "Retractor.", () => WoundPoint(0.5f), 0f, Motion.Still, false, () => { Assistant("Retractor in, edges spread."); PlaceRetractor(); }); return; }
                    if (retractorGO != null) { Assistant("The retractor's already in."); return; }
                    Assistant("Too early for the retractor: get down to the aponeurosis first."); return;
                case Tool.Penrose:
                    if (s == "cord") { Begin(t, "Penrose.", () => field.cord.x[field.cord.x.Length / 3], 0f, Motion.Still, false, () => { field.LiftCord(true); Assistant("Penrose round the cord, I'm lifting it."); Next(); }); return; }
                    if (stepIndex > IndexOf("cord")) { Assistant("I'm already holding the cord up."); return; }
                    Assistant("The cord isn't exposed yet."); return;
                case Tool.Scissors:
                    if (s == "fix" || s == "close_apo" || s == "close_skin")
                    {
                        System.Func<Vector3> end = s == "close_skin" ? (System.Func<Vector3>)(() => WoundPoint(0.8f) + core.restN[core.chainL[0]] * 0.004f) : () => WoundPoint(0.5f);
                        Begin(t, "Scissors.", end, 1.6f, Motion.Snip, true, () => Assistant("Cutting the suture ends."));
                        return;
                    }
                    if (s == "open_apo") { Delegated(t, "Opening the aponeurosis is the operating surgeon's cut: you need to see the nerve beneath. The assistant holds the leaves."); return; }
                    Assistant("Nothing for me to cut right now."); return;
                default:
                    Delegated(t, t == Tool.Scalpel ? "The incision is made by the operating surgeon." :
                                 t == Tool.NeedleHolder ? "Suturing the mesh and closing are the operating surgeon's job; the assistant cuts the ends." :
                                 t == Tool.Mesh ? "The operating surgeon positions and fixes the mesh." :
                                 "Skin prep is part of your procedure here; the assistant helps with draping.");
                    return;
            }
        }

        void Delegated(Tool t, string why)
        {
            Assistant("That's yours, I'm afraid. I'll assist.");
            if (mode == Mode.Guided) Info("Operating surgeon's task", why);
            else Error("delegate_" + t, "Delegated an operating surgeon's task", why, 2);
        }

        static int IndexOf(string id) { for (int i = 0; i < Steps.Length; i++) if (Steps[i].id == id) return i; return -1; }

        // a point along the wound (t = 0..1), patient-local, a little inside the incision
        Vector3 WoundPoint(float t)
        {
            var core = skin.core; int k = Mathf.Clamp(Mathf.RoundToInt(t * (core.cut.Count - 1)), 0, core.cut.Count - 1);
            int L = core.chainL[k], R = core.chainR[k];
            return (core.x[L] + core.x[R]) * 0.5f - core.restN[L] * (core.depth[k] * core.thick[L] * 0.3f);
        }

        void Begin(Tool t, string ask, System.Func<Vector3> target, float useTime, Motion motion, bool returns, System.Action onArrive)
        {
            GameObject src; if (!kit.held.TryGetValue(t, out src)) return;
            var g = Object.Instantiate(src); g.SetActive(true); g.transform.SetParent(world.root, true);
            task = new AssistTask { tool = t, g = g, target = target, useTime = useTime, motion = motion, returns = returns, onArrive = onArrive, trayWasShown = kit.OnTray(t) };
            task.trayPos = kit.OnTray(t) ? kit.TrayPos(t) : world.tray.position + Vector3.up * 0.03f;
            g.transform.position = task.trayPos; g.transform.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);
            kit.ShowOnTray(t, false);
            world.assistantLean = 1f;
            caption = "You: " + ask + " Assistant, please."; captionT = 3f;
        }

        // assistant's working hand: over the table edge in front of him
        Vector3 AssistHand { get { var a = world.assistantT; return a.position + a.forward * 0.30f + Vector3.up * 1.30f; } }

        void TickAssistant(float dt)
        {
            counterTractionT -= dt;
            if (task == null) return;
            var k = task; k.t += dt; var tr = k.g.transform;
            Vector3 hand = AssistHand, target = world.patient.TransformPoint(k.target()) + Vector3.up * 0.002f;
            // work from above, like a real assistant: the instrument comes down steeply from his side, so it stays above the drapes
            Vector3 toHim = hand - target; toHim.y = 0; toHim = toHim.sqrMagnitude > 1e-6f ? toHim.normalized : Vector3.left;
            Vector3 holdDir = (Vector3.up * 0.85f + toHim * 0.5f).normalized;           // ~60 degrees from the horizontal
            Quaternion holdRot = Quaternion.LookRotation(-holdDir, toHim);               // +Z = tip, pointing into the field
            Vector3 approach = target + holdDir * 0.09f;
            switch (k.phase)
            {
                case 0:   // tray -> assistant's hand (arc), passed across the table
                {
                    float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01(k.t / 1.0f));
                    tr.position = Vector3.Lerp(k.trayPos, hand, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.18f;
                    tr.rotation = Quaternion.Slerp(tr.rotation, holdRot, dt * 4f);
                    if (k.t >= 1.0f) { k.phase = 1; k.t = 0; }
                    break;
                }
                case 1:   // hand -> just above the target -> onto it
                {
                    float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01(k.t / 0.9f));
                    tr.position = u < 0.7f ? Vector3.Lerp(hand, approach, u / 0.7f) : Vector3.Lerp(approach, target, (u - 0.7f) / 0.3f);
                    tr.rotation = Quaternion.Slerp(tr.rotation, holdRot, dt * 10f);
                    if (k.t >= 0.9f) { k.phase = 2; k.t = 0; if (k.onArrive != null) k.onArrive(); if (!k.returns) { Object.Destroy(k.g); task = null; world.assistantLean = 0; } }
                    break;
                }
                case 2:   // use it
                {
                    tr.rotation = holdRot;
                    Vector3 side = Vector3.Cross(toHim, Vector3.up).normalized;
                    Vector3 p = target;
                    if (k.motion == Motion.Dab) p += Vector3.up * (0.003f + 0.006f * Mathf.Abs(Mathf.Sin(k.t * 8f))) + side * Mathf.Sin(k.t * 2.6f) * 0.018f;
                    else if (k.motion == Motion.Touch) p += Vector3.up * 0.001f * Mathf.Sin(k.t * 40f);
                    else if (k.motion == Motion.Snip) p += side * 0.004f * Mathf.Sin(k.t * 12f) + Vector3.up * 0.003f;
                    tr.position = p;
                    if (k.motion == Motion.Touch) cauteryOn = Mathf.Max(cauteryOn, 0.12f);
                    if (k.t >= k.useTime) { k.phase = 3; k.t = 0; k.from = tr.position; }
                    break;
                }
                case 3:   // back to the tray
                {
                    float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01(k.t / 1.1f));
                    tr.position = Vector3.Lerp(k.from, k.trayPos, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 0.2f;
                    if (u > 0.5f) world.assistantLean = 0;
                    if (k.t >= 1.1f) { Object.Destroy(k.g); if (k.trayWasShown) kit.ShowOnTray(k.tool, true); task = null; world.assistantLean = 0; }
                    break;
                }
            }
        }

        // ------------------------------------------------------------ scripted 10 s demo (?demo=1), for promo video capture
        // drapes fall (wide shot) -> camera to the surgeon's view -> scalpel incision along the guide -> diathermy through
        // the fat in close-up. Uses the real simulation (cut, bleeding, wound walls), only the input is scripted.
        public bool demo;
        public System.Collections.IEnumerator DemoRun()
        {
            demo = true;
            Vector3 g = world.GroinWorld, home = view.home;
            Vector3 mid = world.patient.TransformPoint(skin.core.sample((incisionA + incisionB) * 0.5f).pos);   // centre of the planned incision
            Vector3 wide = new Vector3(1.05f, 2.05f, g.z + 1.05f), close = mid + new Vector3(0.15f, 0.30f, -0.01f);
            view.ShowHands(false);   // keep the wound unobstructed in the clip
            // prep done, start at the drapes, wide shot
            surf.CompletePrep(); prepDone = true; dryLeft = 0;
            stepIndex = IndexOf("drape"); StartStep();
            view.pos = wide; view.LookAt(g); view.fov = 50f; view.cam.fieldOfView = 50f;
            Debug.Log("VIVIOR_DEMO_START");
            float t = 0;
            while (t < 0.4f) { t += Time.deltaTime; yield return null; }
            world.ApplyDrapes();
            for (int i = 0; i < checks.Length; i++) checks[i] = true; antibioticAsked = true; timeoutDone = true; showChecklist = false;
            stepIndex = IndexOf("incision"); StartStep();
            // hold on the drapes falling, then glide to the surgeon's view
            t = 0; while (t < 1.6f) { t += Time.deltaTime; yield return null; }
            t = 0; const float glide = 1.6f;
            Vector3 over = mid + new Vector3(0.24f, 0.44f, -0.02f);
            while (t < glide) { t += Time.deltaTime; float u = Mathf.SmoothStep(0, 1, t / glide); view.pos = Vector3.Lerp(wide, over, u); view.fov = Mathf.Lerp(50f, 38f, u); view.LookAt(Vector3.Lerp(g, mid, u)); yield return null; }
            // scalpel incision along the guide (medial -> lateral)
            view.Take(Tool.Scalpel, kit.held[Tool.Scalpel]); kit.ShowOnTray(Tool.Scalpel, false);
            stroke.Clear(); t = 0; const float cutT = 2.2f;
            while (t < cutT)
            {
                t += Time.deltaTime; float u = Mathf.Clamp01(t / cutT);
                Vector2 uv = Vector2.Lerp(incisionA, incisionB, u);
                if (stroke.Count == 0 || Vector2.Distance(stroke[stroke.Count - 1], uv) > 0.0015f) { stroke.Add(uv); if (stroke.Count > 3 && stroke.Count % 3 == 0) skin.core.SetCut(stroke, 0.38f); if (Random.value < 0.25f) surf.AddBloodAt(uv, 0.02f); }
                view.aimPoint = world.patient.TransformPoint(skin.core.sample(uv).pos); view.pressing = true;
                view.pos = Vector3.Lerp(over, Vector3.Lerp(over, close, 0.4f), u); view.LookAt(mid);
                yield return null;
            }
            FinishIncision();
            // diathermy down through Scarpa's fascia and the fat, camera moving into close-up
            view.Take(Tool.Cautery, kit.held[Tool.Cautery]); kit.ShowOnTray(Tool.Cautery, false); kit.ShowOnTray(Tool.Scalpel, true);
            t = 0; const float burn = 2.0f; Vector3 from = view.pos;
            while (t < burn)
            {
                t += Time.deltaTime; float u = Mathf.Clamp01(t / burn);
                var c = skin.core.cut; int n = c.Count; if (n < 3) break;
                float sweep = Mathf.PingPong(t * 1.3f, 1f);
                int k = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1, n - 2, sweep)), 1, n - 2);
                for (int j = 1; j < n - 1; j++) skin.core.DeepenAt(j, Time.deltaTime * 0.45f, 0.009f);
                int L = skin.core.chainL[k], R = skin.core.chainR[k];
                Vector3 tip = (skin.core.x[L] + skin.core.x[R]) * 0.5f - skin.core.restN[L] * (skin.core.depth[k] * skin.core.thick[L] * 0.8f);
                view.aimPoint = world.patient.TransformPoint(tip); view.pressing = true;
                cauteryOn = 0.12f; smoke.transform.position = view.aimPoint.Value;
                view.pos = Vector3.Lerp(from, close, Mathf.SmoothStep(0, 1, u)); view.fov = Mathf.Lerp(38f, 30f, u); view.LookAt(mid);
                yield return null;
            }
            foreach (var b in surf.bleeders) b.sealed_ = true;
            view.pressing = false;
            {   // diagnostics for the WebGL build
                var c2 = skin.core; int nn = c2.cut.Count, km = nn / 2;
                var mf = skin.GetComponent<MeshFilter>(); var mesh = mf != null ? mf.sharedMesh : null;
                Debug.Log("VIVIOR_DIAG cut=" + nn + " chainL=" + c2.chainL.Count + " tris=" + c2.tris.Length / 3 + " depth=" + (nn > 0 ? c2.depth[km] : -1)
                    + " gap=" + (nn > 0 ? Vector3.Distance(c2.x[c2.chainL[km]], c2.x[c2.chainR[km]]) : -1)
                    + " L=" + (nn > 0 ? c2.x[c2.chainL[km]].ToString("F4") : "") + " uvA=" + incisionA.ToString("F4") + " uvB=" + incisionB.ToString("F4")
                    + " dom=" + c2.domain + " meshV=" + (mesh != null ? mesh.vertexCount : -1) + " sub=" + (mesh != null ? mesh.subMeshCount : -1)
                    + " wallIdx=" + (mesh != null && mesh.subMeshCount > 1 ? mesh.GetIndexCount(1) : 0) + " bounds=" + (mesh != null ? mesh.bounds.ToString("F3") : ""));
                if (mesh != null && nn > 2)
                {
                    int Lp = c2.chainL[km], Rp = c2.chainR[km]; var t0 = mesh.GetTriangles(0); int uL = 0, uR = 0;
                    for (int i = 0; i < t0.Length; i++) { if (t0[i] == Lp) uL++; if (t0[i] == Rp) uR++; }
                    var mv = mesh.vertices; var r = skin.GetComponent<MeshRenderer>();
                    Debug.Log("VIVIOR_DIAG2 meshL=" + mv[Lp].ToString("F4") + " meshR=" + mv[Rp].ToString("F4") + " useL=" + uL + " useR=" + uR + " top=" + t0.Length / 3
                        + " rend=" + (r != null && r.enabled) + " mats=" + (r != null ? r.sharedMaterials.Length : 0) + " wallMat=" + (r != null && r.sharedMaterials.Length > 1 && r.sharedMaterials[1] != null ? r.sharedMaterials[1].shader.name + "/" + r.sharedMaterials[1].shader.isSupported : "null")
                        + " skinPos=" + skin.transform.position.ToString("F3") + " camDist=" + Vector3.Distance(view.cam.transform.position, world.patient.TransformPoint(mv[Lp])).ToString("F3"));
                }
            }
            // the retractor spreads the wound: fat walls and the floor come into view
            PlaceRetractor();
            t = 0; while (t < 1.6f) { t += Time.deltaTime; view.pos = Vector3.Lerp(close, close + new Vector3(0.015f, -0.04f, 0), t / 1.6f); view.LookAt(mid); yield return null; }
            Debug.Log("VIVIOR_DEMO_END");
            if (WebBridge.Diag)
            {
                view.ShowHands(false); view.Take(Tool.None, null);
                Vector3 cm = world.patient.TransformPoint((skin.core.x[skin.core.chainL[skin.core.cut.Count / 2]] + skin.core.x[skin.core.chainR[skin.core.cut.Count / 2]]) * 0.5f);
                view.pos = cm + new Vector3(0.02f, 0.14f, 0.0f); view.LookAt(cm); view.fov = 40f;
                t = 0; while (t < 1f) { t += Time.deltaTime; yield return null; }
                Debug.Log("VIVIOR_SHOT");
                t = 0; while (t < 3f) { t += Time.deltaTime; yield return null; }
            }
            view.ShowHands(true);
        }

        // ------------------------------------------------------------ anatomy questions
        string Describe(Pick p)
        {
            switch (p.kind)
            {
                case PickKind.Cord: return "the spermatic cord";
                case PickKind.Sac: return "the hernia sac";
                case PickKind.Nerve: return "the ilioinguinal nerve";
                case PickKind.Skin: return p.th.region == TissueRegion.Surface ? "skin" : "subcutaneous fat";
                case PickKind.Apo: return "the external oblique aponeurosis";
                case PickKind.Structure:
                    if (p.structure.Contains("int_oblique")) return "the internal oblique muscle";
                    if (p.structure.Contains("ligament")) return "the inguinal ligament";
                    if (p.structure.Contains("transversus")) return "the transversus abdominis / posterior wall";
                    if (p.structure.Contains("epigastric")) return "the inferior epigastric vessels";
                    if (p.structure.Contains("rectus")) return "the rectus sheath";
                    return p.structure;
            }
            return "that";
        }

        void AnswerRing(Pick p)
        {
            Vector2 f = FieldXY(p.point);
            if (Vector2.Distance(f, InguinalField.SupRing) < 0.012f && (p.kind == PickKind.Apo || p.kind == PickKind.Cord)) { Info("Correct", "The superficial ring: a triangular opening in the aponeurosis, just above and lateral to the pubic tubercle."); Next(); }
            else Error("ring_" + p.kind, "Anatomy: that is not the superficial ring", "You clicked " + Describe(p) + ". The superficial ring lies at the medial end of the canal, 1–2 cm above and lateral to the pubic tubercle, where the cord leaves the aponeurosis.", 5);
        }

        void AnswerNerve(Pick p)
        {
            if (p.kind == PickKind.Nerve) { Info("Correct", "The ilioinguinal nerve. It is preserved and kept away from the mesh and sutures."); Next(); }
            else Error("nerve_" + p.kind, "Anatomy: that is not the ilioinguinal nerve", "You clicked " + Describe(p) + ". The ilioinguinal nerve is a thin pale-yellow strand running on the anterior surface of the spermatic cord.", 5);
        }

        public void AnswerQuestion(int a)
        {
            questionAnswer = a; showQuestion = false;
            if (a == 0) { Info("Correct: indirect inguinal hernia", "The sac follows the cord through the deep ring, lateral to the inferior epigastric vessels."); }
            else Error("sac_q", "Anatomy: wrong classification", "A sac that emerges lateral to the inferior epigastric vessels, inside the cord, is an INDIRECT inguinal hernia. Direct hernias bulge through the posterior wall medial to the vessels (Hesselbach's triangle); femoral hernias lie below the inguinal ligament.", 5);
            Next();
        }

        // ------------------------------------------------------------ aponeurotomy
        void FinishAponeurotomy()
        {
            if (stroke.Count < 2) return;
            if (Step.id != "open_apo" || view.heldTool != Tool.Scissors)
            {
                if (view.heldTool == Tool.Scalpel) Error("apo_scalpel", "Aponeurosis opened with the scalpel", "Nick the aponeurosis at most; open it with scissors under vision so the nerve beneath is not cut.", 4);
                if (Step.id != "open_apo") return;
            }
            float L = U.PolyLength(stroke);
            Vector2 dir = (stroke[stroke.Count - 1] - stroke[0]).normalized;
            float ang = Mathf.Acos(Mathf.Clamp01(Mathf.Abs(Vector2.Dot(dir, field.LigamentDir)))) * Mathf.Rad2Deg;
            Vector2 sr = InguinalField.SupRing + Fo;
            float startD = Mathf.Min(Vector2.Distance(stroke[0], sr), Vector2.Distance(stroke[stroke.Count - 1], sr));
            if (L < 0.012f) return;
            field.apo.core.SetCut(stroke, 1f);
            if (ang > 30f) Error("apo_across", "Aponeurosis cut across its fibres", "The external oblique must be split IN LINE with its fibres (parallel to the ligament). Cutting across them weakens the anterior wall and makes closure under tension.", 8);
            if (startD > 0.016f) Error("apo_ring", "Did not open from the superficial ring", "Start at the superficial ring and cut laterally: this opens the ring and keeps the cut directly over the canal.", 4);
            if (L < 0.03f) { Info("Extend the opening", "Open at least 3 cm to reach the deep ring."); }
            // leaves held apart with artery forceps
            var core = field.apo.core; int n = core.cut.Count;
            for (int k = n / 4; k < n - 1; k += Mathf.Max(1, n / 3))
            {
                int Lp = core.chainL[k], Rp = core.chainR[k];
                Vector3 ac = core.x[Rp] - core.x[Lp]; ac = ac.sqrMagnitude > 1e-9f ? ac.normalized : Vector3.Cross(core.restN[Lp], Vector3.up).normalized;
                leaves.Add(core.Attach(Lp, core.x[Lp] - ac * 0.011f + core.restN[Lp] * 0.005f, 1e-6f));
                leaves.Add(core.Attach(Rp, core.x[Rp] + ac * 0.011f + core.restN[Rp] * 0.005f, 1e-6f));
                for (int s = 0; s < 2; s++)
                {
                    var cl = Object.Instantiate(kit.held[Tool.Hemostat]); cl.SetActive(true); cl.transform.SetParent(world.patient, false);
                    Vector3 at = s == 0 ? core.x[Lp] - ac * 0.011f : core.x[Rp] + ac * 0.011f;
                    cl.transform.localPosition = at + core.restN[Lp] * 0.004f; cl.transform.localRotation = Quaternion.LookRotation(s == 0 ? ac : -ac, core.restN[Lp]);
                    leafClamps.Add(cl);
                }
            }
            field.OpenAponeurosis();
            Say("Assistant", "Clips on the leaves.");
            if (L >= 0.03f) Next();
        }

        // ------------------------------------------------------------ mesh
        void PlaceMesh(Pick p)
        {
            Vector2 d = field.LigamentDir, cr = new Vector2(d.y, -d.x); if (cr.y < 0) cr = -cr;
            Vector2 ideal = (InguinalField.PubicTubercle + InguinalField.DeepRing) * 0.5f + cr * 0.010f + d * 0.006f;
            Vector2 f = FieldXY(p.point); float err = Vector2.Distance(f, ideal);
            if (err > 0.025f) Error("mesh_pos", "Mesh malpositioned", "The mesh must lie on the posterior wall from 1.5–2 cm medial to the pubic tubercle to beyond the deep ring. Recentre it over the canal.", 6);
            Vector2 c = err > 0.012f ? ideal + (f - ideal).normalized * 0.012f : f;
            field.PlaceMesh(new Vector3(c.x + Fo.x, c.y + Fo.y, 0));
            ReturnTool(true); kit.ShowOnTray(Tool.Mesh, false);
            Next();
        }

        // ------------------------------------------------------------ suturing
        void Suture(Pick p)
        {
            var s = Step.id;
            if (s == "count") { Error("close_before_count", "Skipped safety step: closing before the count", "Swabs, needles and instruments must be counted before the wound is closed to avoid a retained foreign body.", 15, true); return; }
            if (s == "fix")
            {
                Vector2 f = FieldXY(p.point); float dist; Vector3 lp = field.NearestLigamentPoint(f, out dist);
                Vector2 d = field.LigamentDir, cr = new Vector2(d.y, -d.x); if (cr.y < 0) cr = -cr;
                float side = Vector2.Dot(f - new Vector2(lp.x, lp.y), cr);
                if (side < -0.006f) { Error("femoral", "Danger: suture below the inguinal ligament", "Below the shelving edge lie the femoral vein and artery. Sutures go INTO the ligament edge, never deep to it.", 15, true); return; }
                if (dist > 0.008f) { Info("Not on the ligament", "Place this suture through the shelving edge of the inguinal ligament (the white band at the lower edge of the mesh)."); return; }
                var core = field.meshCloth; if (core == null) return;
                Vector3 ligPL = field.F2P(lp) + new Vector3(0, 0, 0.0015f);
                int best = 0; float bd = float.MaxValue; for (int i = 0; i < core.x.Length; i++) { float dd = (core.x[i] - ligPL).sqrMagnitude; if (dd < bd) { bd = dd; best = i; } }
                core.Attach(best, ligPL, 1e-7f);
                int bi = best; var meshRef = core; var fieldRef = field;
                field.AddStitch(() => world.patient.TransformPoint(meshRef.x[bi]), () => world.patient.TransformPoint(ligPL - new Vector3(0, 0, 0.002f)), new Color(0.1f, 0.2f, 0.65f));
                meshSutures.Add(f); audio.Play("stitch");
                if (meshSutures.Count >= 2) { float gap = Vector2.Distance(meshSutures[meshSutures.Count - 1], meshSutures[meshSutures.Count - 2]); if (gap > 0.022f) Info("Wide spacing", "Sutures more than 2 cm apart can let the mesh fold or a hernia recur between them."); }
                float medial = float.MaxValue, lateral = float.MinValue;
                foreach (var q in meshSutures) { medial = Mathf.Min(medial, Vector2.Distance(q, InguinalField.PubicTubercle)); lateral = Mathf.Max(lateral, Vector2.Dot(q - InguinalField.PubicTubercle, -d)); }
                if (meshSutures.Count >= 4 && medial < 0.018f && lateral > 0.06f) { field.meshFixed = true; Info("Mesh fixed", "Continuous fixation from the pubic tubercle to beyond the deep ring."); Next(); }
                else if (meshSutures.Count >= 4 && medial >= 0.018f && !once.Contains("mesh_medial")) Error("mesh_medial", "Mesh not anchored at the pubic tubercle", "The first suture anchors the mesh to the tissue over the pubic tubercle: most recurrences start medially.", 5);
                return;
            }
            if (s == "close_apo" && (p.kind == PickKind.Apo || p.kind == PickKind.Cord))
            {
                float dist; int k = field.apo.core.NearestCutIndex(p.kind == PickKind.Apo ? p.th.uv : PL(p.point), out dist);
                if (k < 0 || dist > 0.008f) return;
                if (field.apo.core.AddSuture(k, 2e-7f)) { apoSutures.Add(k); AddSutureVisual(field.apo, k); audio.Play("stitch"); }
                if (Covered(field.apo.core, apoSutures, 0.0075f) && apoSutures.Count >= 4) { Info("Aponeurosis closed", "The cord is back in the canal under a closed external oblique."); Next(); }
                return;
            }
            if (s == "close_skin" && p.kind == PickKind.Skin)
            {
                int k = p.th.cutIndex; float dist;
                if (p.th.region == TissueRegion.Surface) { k = skin.core.NearestCutIndex(p.th.uv, out dist); if (dist > 0.008f) return; }
                if (skin.core.AddSuture(k, 1.5e-7f)) { skinSutures.Add(k); AddSutureVisual(skin, k); audio.Play("stitch"); }
                if (Covered(skin.core, skinSutures, 0.006f) && skinSutures.Count >= 5) { Info("Wound closed", "Interrupted skin sutures, evenly spaced."); Next(); }
                return;
            }
            if (s != "fix" && s != "close_apo" && s != "close_skin" && (p.kind == PickKind.Skin || p.kind == PickKind.Apo)) Info("Not yet", "Sutures come later in the operation.");
        }

        bool Covered(TissueCore core, List<int> sut, float maxGap)
        {
            for (int k = 1; k < core.cut.Count - 1; k++)
            {
                float best = float.MaxValue; foreach (var j in sut) best = Mathf.Min(best, Vector2.Distance(core.cut[k], core.cut[Mathf.Clamp(j, 0, core.cut.Count - 1)]));
                if (best > maxGap) return false;
            }
            return true;
        }

        void AddSutureVisual(TissueSheet sheet, int k)
        {
            var core = sheet.core; int L = core.chainL[k], R = core.chainR[k];
            var P = world.patient; var c = core;
            field.AddStitch(() => P.TransformPoint(c.x[L] - (c.x[R] - c.x[L]).normalized * 0.003f), () => P.TransformPoint(c.x[R] + (c.x[R] - c.x[L]).normalized * 0.003f), sheet == skin ? new Color(0.08f, 0.08f, 0.12f) : new Color(0.1f, 0.2f, 0.65f));
        }

        // ------------------------------------------------------------ buttons (HUD)
        public void ApplyDrapes()
        {
            if (Step.id != "drape") return;
            if (surf.PrepCoverage(ShrinkWindow(0.008f)) < 0.95f) Error("prep_incomplete", "Skipped safety step: incomplete prep", "Part of the operative field was not prepped before draping. Every area that could enter the wound must be cleaned.", 10, true);
            world.ApplyDrapes(); Say("Assistant", "Drapes on."); Next();
        }
        public void ConfirmCheck(int i, bool fix)
        {
            if (i == 4 && !fix) { Error("antibiotic", "Skipped safety step: no antibiotic prophylaxis", "Mesh repairs need antibiotic prophylaxis (e.g. cefazolin 2 g IV) within 60 minutes before incision. Ask the anaesthetist to give it now.", 15, true); }
            if (i == 4 && fix) { antibioticAsked = true; Say("Anaesthetist", "Cefazolin 2 g going in now."); }
            checks[i] = true;
            bool all = true; foreach (var c in checks) all &= c;
            if (all) { timeoutDone = true; showChecklist = false; Say("Assistant", "Time-out complete."); Next(); }
        }
        public void RequestCount()
        {
            if (Step.id != "count") return;
            countDone = true;
            Say("Assistant", "Count correct: " + (10 + swabsUsed) + " swabs, 2 needles, 11 instruments.");
            Next();
        }

        Rect ShrinkWindow(float m) { var w = ORWorld.Window; return new Rect(w.xMin + m, w.yMin + m, w.width - 2 * m, w.height - 2 * m); }

        // ------------------------------------------------------------ guidance (guided mode, or a training hint)
        void UpdateGuides()
        {
            bool show = mode == Mode.Guided || (mode == Mode.Training && hintT > 0);
            Vector3? target = null; List<Vector3> line = null;
            var P = world.patient; string id = Step.id;
            if (show)
            {
                if (id == "incision") { line = new List<Vector3> { Surf(incisionA), Surf(incisionB) }; }
                if (id == "open_apo") { line = new List<Vector3> { ApoPt(apoA), ApoPt(apoB) }; }
                if (id == "ring") target = ApoPt(InguinalField.SupRing + Fo);
                if (id == "nerve" && field.nervePts.Count > 4) target = P.TransformPoint(field.nervePts[field.nervePts.Count / 2]);
                if (id == "cord" || id == "reduce") target = P.TransformPoint(id == "cord" ? field.cord.x[field.cord.x.Length / 3] : field.sac.x[field.sac.x.Length - 1]);
                if (id == "mesh" || id == "fix") { float dd; target = P.TransformPoint(field.F2P(field.NearestLigamentPoint((InguinalField.PubicTubercle + InguinalField.DeepRing) * 0.5f, out dd))); }
                // highlight the right instrument on the tray
                foreach (var kv in kit.trayItems)
                {
                    bool want = false; foreach (var t in Step.tools) if (t == kv.Key) want = true;
                    want &= view.heldTool != kv.Key;
                    kv.Value.transform.localScale = Vector3.one * (want && view.heldTool == Tool.None ? 1f + 0.08f * Mathf.Sin(Time.time * 6f) : 1f);
                }
            }
            beacon.SetActive(target.HasValue);
            if (target.HasValue) { beacon.transform.position = target.Value + Vector3.up * 0.002f; beacon.transform.localScale = Vector3.one * (1f + 0.25f * Mathf.Sin(Time.time * 5f)); }
            guideLine.enabled = line != null;
            if (line != null)
            {
                var pts = new List<Vector3>();
                for (int i = 0; i <= 24; i++) pts.Add(Vector3.Lerp(line[0], line[1], i / 24f) + Vector3.up * 0.001f);
                guideLine.positionCount = pts.Count; guideLine.SetPositions(pts.ToArray());
            }
        }
        Vector3 Surf(Vector2 uv) { var s = skin.core.sample(uv); return world.patient.TransformPoint(s.pos + s.normal * 0.0008f); }
        Vector3 ApoPt(Vector2 uv) { var s = field.ApoSample(uv); return world.patient.TransformPoint(s.pos + s.normal * 0.0008f); }
    }

    // ------------------------------------------------------------------ procedural audio
    public class Synth : MonoBehaviour
    {
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        AudioSource one, loop; public bool muted;
        public void Init()
        {
            one = gameObject.AddComponent<AudioSource>(); loop = gameObject.AddComponent<AudioSource>(); loop.loop = true;
            clips["beep"] = Tone(0.09f, f => 0.35f * Mathf.Sin(2 * Mathf.PI * 880 * f) * Env(f, 0.09f));
            clips["error"] = Tone(0.32f, f => 0.3f * Mathf.Sin(2 * Mathf.PI * (f < 0.15f ? 440 : 330) * f) * Env(f % 0.16f, 0.16f));
            clips["ok"] = Tone(0.22f, f => 0.22f * Mathf.Sin(2 * Mathf.PI * (f < 0.1f ? 660 : 990) * f) * Env(f % 0.11f, 0.11f));
            clips["tick"] = Tone(0.03f, f => 0.15f * Mathf.Sin(2 * Mathf.PI * 1500 * f));
            clips["stitch"] = Tone(0.12f, f => 0.12f * (Random.value * 2 - 1) * Env(f, 0.12f));
            clips["scalpel"] = Tone(0.25f, f => 0.06f * (Random.value * 2 - 1) * Env(f, 0.25f));
            clips["sizzle"] = Tone(0.4f, f => 0.12f * (Random.value * 2 - 1) * Env(f, 0.4f));
            clips["cautery"] = Tone(1f, f => 0.10f * (Mathf.Sin(2 * Mathf.PI * 100 * f) * 0.6f + (Random.value * 2 - 1) * 0.5f + Mathf.Sign(Mathf.Sin(2 * Mathf.PI * 200 * f)) * 0.15f));
        }
        static float Env(float t, float len) { return Mathf.Clamp01(t / 0.005f) * Mathf.Clamp01((len - t) / 0.03f); }
        static AudioClip Tone(float len, System.Func<float, float> f)
        {
            int sr = 22050, n = (int)(len * sr); var d = new float[n]; for (int i = 0; i < n; i++) d[i] = f(i / (float)sr);
            var c = AudioClip.Create("synth", n, 1, sr, false); c.SetData(d, 0); return c;
        }
        public void Play(string k, float pitch = 1f) { AudioClip c; if (muted || !clips.TryGetValue(k, out c)) return; one.pitch = pitch; one.PlayOneShot(c); }
        public void SetLoop(string k, bool on)
        {
            AudioClip c; if (!clips.TryGetValue(k, out c)) return;
            if (on && !muted) { if (!loop.isPlaying || loop.clip != c) { loop.clip = c; loop.Play(); } } else if (loop.isPlaying && loop.clip == c) loop.Stop();
        }
    }

    // ------------------------------------------------------------------ patient monitor
    public class Vitals
    {
        public float hr = 72, sys = 124, dia = 78, spo2 = 99, etco2 = 36, rr = 12;
        public float fluid;        // ml of IV fluid given (from the anaesthesia cart), offsets blood loss
        public float breath;       // -1..1 ventilator phase
        public float[] ecg = new float[256]; int head; float phase, beatT; public bool beat;
        float t;
        public void Tick(float dt, float ebl)
        {
            t += dt;
            ebl = Mathf.Max(0, ebl - fluid * 0.6f);
            float targetHr = 72 + Mathf.Clamp(ebl - 60, 0, 400) * 0.08f + Mathf.Sin(t * 0.13f) * 2f;
            hr = Mathf.Lerp(hr, targetHr, dt * 0.3f);
            sys = Mathf.Lerp(sys, 124 - Mathf.Clamp(ebl - 100, 0, 400) * 0.06f + Mathf.Sin(t * 0.07f) * 2, dt * 0.2f); dia = sys * 0.63f;
            breath = Mathf.Sin(t * 2 * Mathf.PI * rr / 60f);
            etco2 = 35 + Mathf.Sin(t * 0.05f);
            beat = false; beatT += dt;
            if (beatT > 60f / hr) { beatT = 0; beat = true; phase = 0; }
            phase += dt;
            // PQRST
            float p = phase, v = 0;
            v += 0.12f * Mathf.Exp(-Mathf.Pow((p - 0.08f) / 0.025f, 2));
            v -= 0.15f * Mathf.Exp(-Mathf.Pow((p - 0.17f) / 0.008f, 2));
            v += 1.0f * Mathf.Exp(-Mathf.Pow((p - 0.19f) / 0.010f, 2));
            v -= 0.25f * Mathf.Exp(-Mathf.Pow((p - 0.215f) / 0.010f, 2));
            v += 0.25f * Mathf.Exp(-Mathf.Pow((p - 0.40f) / 0.05f, 2));
            int steps = Mathf.Max(1, Mathf.RoundToInt(dt * 120));
            for (int i = 0; i < steps; i++) { ecg[head] = v; head = (head + 1) % ecg.Length; }
        }
        public float EcgAt(int i) { return ecg[(head + i) % ecg.Length]; }
    }
}
