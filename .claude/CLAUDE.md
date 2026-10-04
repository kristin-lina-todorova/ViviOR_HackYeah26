# Virtual OR – project context for Claude Code

## What this is
First-person virtual operating room for surgical training, built for **HackYeah 2026 (Kraków), Sport & Healthcare open task**. Trainees make their first mistakes on a simulation instead of a patient. First module: **open inguinal hernia repair (Lichtenstein), right side**.

Pitch requirements (must stay true): photo-scanned real OR, animated surgical team, instrument tray picked by mouse, tissue that responds to cutting and cautery, every error caught/explained/scored (wrong instrument, wrong incision, skipped safety step, missed anatomy question), three modes (guided, training, exam), operations as modules, next step VR.

## Deadline and rules
- Submission on HackTribe closes **11:00 AM, 4 October 2026** (confirmed AM). Nothing can change after that, including the demo link.
- Required: title, team name, members, description, max 10-slide PDF (≤10 MB). Optional: video, repo, demo link.
- Judging: Idea & Innovation 30%, Relation to category 20%, Usability 20%, Design 20%, Completeness 10%.
- Must disclose pre-existing vs HackYeah work, all third-party assets/licences, and AI use (template at the bottom of README.md in the zip).
- The developer works mostly alone with limited time: keep changes focused, always leave the project in a runnable state.

## Environment
- macOS (Apple Silicon), **Unity 6.6 (6000.6.4f1)**, project folder `ViviOR`, created from the **3D (Built-In Render Pipeline)** template. Input System package present (code doesn't depend on it).
- Web Build Support is installed (for a WebGL demo link).
- Status at handoff: code imported, **0 compile errors, 23 warnings (UAC1001, harmless: non-serializable public fields on MonoBehaviours)**. Next step was menu **Virtual OR → Create and open the Virtual OR scene**, then **Play**. It has **not yet been run in Unity**; expect runtime/visual issues on first Play.

## How it works
Everything is built from code at Play (no scene setup, no packages). `App.Boot()` runs via `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`, disables the template camera/light, and creates the app.

Code: `Assets/VirtualOR/Scripts`
- `Core/App.cs`: bootstrap, staged loading coroutine, main loop, restart. States: Loading, Menu, Running, Debrief.
- `Core/Core.cs`: `Mats` (Standard/URP material factory; base materials in `Resources/VirtualOR/*.mat` so shaders survive builds), `InputState` (reads **IMGUI events**, works with either input backend; HUD sets `OverUI`), `U` helpers (noise, tube mesh, polyline), `VorLoader` (loads our binary model format from `Resources/VirtualOR/Models/*.bytes`, incl. skinned arms + sampled animation clips).
- `Tissue/TissueCore.cs`: **XPBD pre-tensioned membrane** with anisotropic residual strain (higher along Langer's lines = `fibreDir`). Cutting = Delaunay re-triangulation of the rest domain with the blade path embedded, then vertex duplication along the cut (chainL/chainR). Per-cut-point `depth` (0..1): <0.25 "scored" (bridged), dermis ~0.38, 1 = through fat. Sutures/attachments are constraints. Tested headless: 6 cm cut gapes ~5.9 mm along fibres, ~7.1 mm across; ~1.6 ms/step.
- `Tissue/TissueSheet.cs`: renders a TissueCore (submesh 0 skin, 1 wound walls dermis-over-fat, 2 uncut fat floor), custom raycast returning region/uv/cutIndex/particle.
- `Tissue/SkinSurface.cs`: CPU-painted skin texture + gloss map: prep (coverage, friction seconds, wetness/drying), marker, burns, blood film flowing downhill on a 96² grid, wound pool, overflow, clotting, bleeders, swabbing.
- `Anatomy/Anatomy.cs`: procedural tissue textures, `Rope` (XPBD chain), `InguinalField` (real BodyParts3D meshes + simulated aponeurosis sheet, spermatic cord rope, indirect sac rope, ilioinguinal nerve, inferior epigastric vessels, polypropylene mesh cloth, stitches).
- `World/World.cs`: OR scan, table, patient (window triangles cut out, ventilation breathing by CPU vertex displacement), animated drapes with window, Mayo tray, lights, team. `AutoRig` rigs the static nurse scan at load (distance-to-bone weights); `TeamMember` drives breathing/look-at/reach. Assistant surgeon is static with procedural sway.
- `Player/Surgeon.cs`: `Tool` enum + `Tools` defs, `InstrumentKit` (tray items with BoxColliders + held copies; real instrument meshes recentred so +Z = tip, origin at tip), `SurgeonView` (camera, look/move/lean, gloved arms playing Idle/Take clips, instrument posed so its tip lands on the aim point).
- `Procedure/Procedure.cs`: the module: 17 steps (prep, drape, WHO time-out, incision, dissection+haemostasis, retractor, superficial ring, open external oblique, ilioinguinal nerve, Penrose on cord, hernia type MCQ, reduce sac, place mesh, fix mesh to ligament, count, close aponeurosis, close skin), picking, error catalogue, scoring (100 minus penalties; pass ≥80 and no critical), guided beacons/lines, nurse handover, `Synth` procedural audio, `Vitals` (ECG, HR/BP respond to blood loss).
- `UI/HUD.cs`: IMGUI screens (loading, menu, step card, vitals + ECG, toasts, captions, checklist, MCQ, debrief). Palette: ink #10262D, drape teal #2F6E7A, paper #F1F5F3, steel #B0C0C5, Betadine amber #D9922E (guidance), arterial red #C8372D (errors), monitor green #3FD18A. Font: Atkinson Hyperlegible (Resources/VirtualOR/Fonts).
- `Editor/VirtualORMenu.cs`: menu "Virtual OR": create scene (+ add to build settings), configure WebGL (compression disabled for static hosting).

## Coordinate frames (important)
- Unity world: table along Z, patient head towards −Z, **patient's right side towards +X** (surgeon stands at x≈+0.58, eye y 1.66).
- Patient root: position (0, 0.975, 0.50), rotation Euler(−90,0,0). **Patient-local**: x = patient right, y = towards head, z = anterior.
- Operative window (patient-local xy): `ORWorld.Window = Rect(-0.035, 0.80, 0.20, 0.1875)`; groin `(0.066, 0.885)`.
- Inguinal field frame = patient-local translated to the groin (same axes). Landmarks (field xy, metres): deep ring (0,0), superficial ring (−0.042,−0.046), pubic tubercle (−0.05,−0.059), ASIS (0.05,0.033), ligament polyline in `InguinalField.Ligament`. Medial = −x.
- Skin, aponeurosis and mesh sheets live in patient-local space (children of the patient root).
- All models were converted glTF→Unity: x→−x, winding flipped, v→1−v.

## Assets and licences
Models in `Resources/VirtualOR/Models/*.bytes` (VOR1 format: "VOR1", uint32 json length, JSON header, binary blob). Converters in `Tools/` (Node: gltf-transform, meshoptimizer, sharp, three).
- Charité OR photogrammetry (ChrisRE): **CC BY-NC 4.0**
- James patient (StormierTunic16), nurse (restore50), operating table (shashkinv1ad), surgeon PPE (Advanced Visualization Lab, Indiana University), surgical instruments (Wenschel), Fists 2025 FP arms (1Matzh): CC BY 4.0
- BodyParts3D anatomy (© DBCLS): **CC BY-SA 2.1 JP**
- Atkinson Hyperlegible: SIL OFL 1.1
- Mixamo Breathing_Idle FBX was supplied but is not used.

## Known issues / likely first fixes
- Never run in the editor yet: check field depth under the skin, hand placement, tray item orientation/scale, nurse rig deformation, drape fit, lamp intensity.
- UAC1001 warnings: could mark the offending fields `[System.NonSerialized]` or make them non-public.
- Assistant surgeon not rigged. Nurse rig deforms simply (no fingers).
- WebGL build not yet attempted; takes 10–30 min, so allow time before 11:00.

## Remaining deliverables before 11:00
1. Get it running and polish visuals.
2. WebGL build + host (GitHub Pages / itch.io) for the demo link, or record a demo video.
3. Max 10-slide PDF.
4. Fill in the README disclosure section (pre-existing vs HackYeah work, AI use, the developer's role).