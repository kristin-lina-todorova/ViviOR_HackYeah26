# ViviOR: virtual operating room

**Your first patient should never be real.**

ViviOR is a first-person virtual operating room where surgical trainees make their first mistakes on a simulation instead of a patient. Every error is caught, explained and scored. Built for **HackYeah 2026 (Kraków), Sport & Healthcare**.

The first module is an **open inguinal hernia repair (Lichtenstein), right side**: 17 steps from skin prep, draping and the WHO time-out to closing the skin.

- **Three modes:** Guided (every step explained, targets highlighted), Training (errors explained as they happen) and Exam (errors logged silently, pass mark 80 and no critical error).
- **Tissue that responds:** the skin is a simulated pre-tensioned membrane that gapes along Langer's lines when cut. It shows the dermis, fat lobules and Scarpa's fascia, bleeds, and is coagulated with diathermy.
- **A working theatre:** live vitals that react to blood loss, an anaesthesia machine, ventilator, diathermy generator and theatre lamp that you can use, and an assistant surgeon you hand instruments to.
- **Web platform:** the ViviOR website opens the simulator, tracks progress step by step and saves each attempt's score.

## Run it

- **In the browser:** open `website/index.html` from any static web server, for example `cd website && python3 -m http.server 8080`, then go to `http://localhost:8080`. Choose **Explore with a demo account**, then **Operations**, then **Start**.
- **In Unity:** use Unity **6000.6.4f1**. Open the project, run **Virtual OR → Create and open the Virtual OR scene**, then press Play. The whole operating room is built from code at runtime.
- **Rebuild the web version:** **Virtual OR → Build WebGL into the website (website/sim)**.
- **10-second scripted demo:** `website/sim/index.html?demo=1`.

## Repository layout

| Path | Contents |
|---|---|
| `Assets/VirtualOR/Scripts` | Simulator code: `Core` (app, bridge to the website), `Tissue` (membrane simulation and rendering), `Anatomy`, `World` (theatre, team, equipment), `Player`, `Procedure` (steps, errors, scoring, assistant), `UI` |
| `Assets/VirtualOR/Resources` | Models (converted to the project's `.bytes` format), base materials, fonts, UI images |
| `website/` | The ViviOR website (`index.html`) and the WebGL build of the simulator (`website/sim/`) |
| `BrandBook/` | Logo, colour palette, fonts |
| `ViviOR_demo_10s.mp4` | Short demo clip |

## Third-party assets and licences

| Asset | Author | Licence |
|---|---|---|
| Charité operating room photogrammetry | ChrisRE | CC BY-NC 4.0 |
| "James" patient model | StormierTunic16 | CC BY 4.0 |
| Operating table | shashkinv1ad | CC BY 4.0 |
| Surgeon PPE scan | Advanced Visualization Lab, Indiana University | CC BY 4.0 |
| Surgical instruments | Wenschel | CC BY 4.0 |
| Fists 2025 first-person arms | 1Matzh | CC BY 4.0 |
| BodyParts3D anatomy | © DBCLS | CC BY-SA 2.1 JP |
| Inter font | The Inter Project Authors | SIL OFL 1.1 |
| Atkinson Hyperlegible font | Braille Institute of America | SIL OFL 1.1 |
