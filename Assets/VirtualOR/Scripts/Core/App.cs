// Virtual OR - application bootstrap. Press Play in any scene: the whole operating room is built from code.
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VirtualOR
{
    public enum AppState { Loading, Menu, Running, Debrief }

    public class App : MonoBehaviour
    {
        public static App Instance;
        public AppState state = AppState.Loading;
        public Mode mode = Mode.Guided;
        public string loadingText = "Preparing the operating room"; public float loadingProgress;
        public ORWorld world; public SurgeonView view; public InstrumentKit kit; public InguinalField field;
        public TissueSheet skin; public SkinSurface surf; public Procedure procedure; public Vitals vitals; public Synth synth;
        public bool audioMuted; public EquipmentRoom equipment;
        GameObject sceneRoot; bool autoStart;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            // Disable the template's own camera and lights so they don't fight ours.
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.GetComponentInChildren<Camera>(true) != null || go.GetComponentInChildren<Light>(true) != null) go.SetActive(false);
            var g = new GameObject("VirtualOR_App");
            Object.DontDestroyOnLoad(g);
            g.AddComponent<InputState>();
            Instance = g.AddComponent<App>();
            g.AddComponent<HUD>().app = Instance;
        }

        void Start()
        {
            Application.targetFrameRate = 60;
            WebBridge.Init();   // opened from the ViviOR website? (?embed=1&mode=train|exam)
            QualitySettings.shadowDistance = 6f;
            synth = gameObject.AddComponent<Synth>(); synth.Init();
            StartCoroutine(Load());
        }

        IEnumerator Load()
        {
            state = AppState.Loading;
            sceneRoot = new GameObject("VirtualOR_Scene");
            vitals = new Vitals();
            // a temporary camera so the loading screen renders
            var tmpCam = new GameObject("loading_camera").AddComponent<Camera>(); tmpCam.clearFlags = CameraClearFlags.SolidColor; tmpCam.backgroundColor = new Color(0.06f, 0.15f, 0.18f);
            Step("Loading the photo-scanned operating room", 0.1f); yield return null;
            world = new ORWorld();
            float wp = 0.1f;
            foreach (var stage in world.Build(sceneRoot.transform)) { wp += 0.07f; Step(stage, wp); yield return null; }
            Step("Placing the surgeon", 0.45f); yield return null;
            view = new SurgeonView();
            Vector3 groin = world.GroinWorld;
            view.Build(sceneRoot.transform, new Vector3(0.44f, 1.72f, groin.z - 0.04f), groin);
            Destroy(tmpCam.gameObject);
            Step("Laying out the instrument tray", 0.55f); yield return null;
            kit = new InstrumentKit(); kit.Build(world.tray, sceneRoot.transform);
            Step("Building the inguinal anatomy (BodyParts3D)", 0.65f); yield return null;
            var fg = U.Child(world.patient, "inguinal_field"); fg.transform.localPosition = new Vector3(ORWorld.Groin.x, ORWorld.Groin.y, 0);
            field = fg.AddComponent<InguinalField>(); field.Build(world.patient, world.skinZAtGroin);
            field.BuildSimulated(ORWorld.Window);
            Step("Simulating skin and subcutaneous tissue", 0.82f); yield return null;
            BuildSkin();
            Step("Briefing the team", 0.95f); yield return null;
            procedure = new Procedure { world = world, view = view, kit = kit, field = field, skin = skin, surf = surf, audio = synth };
            procedure.Setup();
            equipment = new EquipmentRoom(); equipment.Build(world, view.home);
            loadingProgress = 1f;
            state = AppState.Menu;
            if (WebBridge.Embedded) mode = WebBridge.Exam ? Mode.Exam : Mode.Guided;   // website "Training" = labels, targets, feedback
            if (autoStart || WebBridge.Embedded || WebBridge.Demo) { autoStart = false; StartOperation(); }   // embedded: the website already chose op + mode
            if (WebBridge.Demo) StartCoroutine(procedure.DemoRun());
        }

        void Step(string t, float p) { loadingText = t; loadingProgress = p; }

        void BuildSkin()
        {
            var g = U.Child(world.patient, "skin_and_subcutis"); skin = g.AddComponent<TissueSheet>();
            var c = skin.core; var P = world.patient; var col = world.patientCollider;
            c.domain = ORWorld.Window; c.spacing = 0.004f; c.fibreDir = field.LigamentDir;   // Langer's lines run parallel to the inguinal ligament here
            c.sample = uv =>
            {
                RaycastHit h; Vector3 o = P.TransformPoint(new Vector3(uv.x, uv.y, 0.5f));
                Vector3 pos = new Vector3(uv.x, uv.y, world.skinZAtGroin), n = Vector3.forward;
                if (col.Raycast(new Ray(o, P.TransformDirection(Vector3.back)), out h, 1f)) { pos = P.InverseTransformPoint(h.point); n = P.InverseTransformDirection(h.normal).normalized; }
                float apoZ = field.ApoSample(uv).pos.z;
                return new SurfaceSample { pos = pos + n * 0.0004f, normal = n, thickness = Mathf.Clamp(pos.z - apoZ, 0.008f, 0.045f) };
            };
            surf = new SkinSurface();
            surf.Init(c.domain, world.skinTone, uv => P.TransformPoint(c.sample(uv).pos).y);
            var skinMaps = new TissueMaps.Set { albedo = surf.albedo, gloss = surf.gloss, normal = TissueMaps.SkinNormal(c.fibreDir, c.domain.width * 1000f, c.domain.height * 1000f) };
            var sm = Mats.Tissue(skinMaps, 1f, 0.5f);   // pores + creases, wet/dry gloss from the skin simulation
            skin.surfaceMat = sm;
            skin.wallMat = Mats.Tissue(TissueMaps.Wall(), 1f, 0.85f);          // dermis band, fat lobules, Scarpa's fascia, bleeding
            skin.floorMat = Mats.Tissue(TissueMaps.FatFloor(), 1f, 0.85f);
            skin.wallTexScale = TissueMaps.WallDepthM;
            c.Init(); skin.Build(); skin.Refresh();
        }

        public void StartOperation()
        {
            if (state != AppState.Menu) return;
            state = AppState.Running; procedure.Begin(mode);
        }

        public void ToggleSound() { audioMuted = !audioMuted; synth.muted = audioMuted; AudioListener.volume = audioMuted ? 0 : 1; }

        public void Restart(bool directly)
        {
            StopAllCoroutines();
            if (sceneRoot != null) Destroy(sceneRoot);
            InputState.ReleaseAll();
            autoStart = directly;
            StartCoroutine(Load());
        }

        void Update()
        {
            InputState.BeginFrame();
            if (state == AppState.Loading || world == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            vitals.Tick(dt, procedure != null ? procedure.ebl : 0);
            if (vitals.beat && state == AppState.Running) synth.Play("beep", 0.9f + (vitals.spo2 - 90) * 0.012f);
            world.Tick(dt, vitals.breath, view.cam.transform.position);
            if (equipment != null) equipment.Tick(dt, vitals, procedure);
            if (state == AppState.Menu)
            {
                // slow look around the room behind the menu
                view.yaw += dt * 2.5f * Mathf.Sin(Time.time * 0.1f); view.Update(dt, world.GroinWorld.z);
                return;
            }
            view.Update(dt, world.GroinWorld.z);
            procedure.Tick(dt);
            if (state == AppState.Running && procedure.finished) { state = AppState.Debrief; WebBridge.Result(procedure); }
        }
    }
}
