# Task: connect the VIVIOR operating room simulator to the VIVIOR website

You are working in the repository of the **VIVIOR operating room simulator** (Three.js, WebXR, runs in the browser). The VIVIOR website (`vor-website.html`, a single self-contained HTML file) already has everything on its side. When a user picks an operation, the website opens the simulator in a full-screen window (an `<iframe>`), shows a live step progress bar and timer, and saves the result when the attempt ends.

Your job is to make the **simulator** speak the website's protocol, then point the website at the simulator. Do not redesign the website. The only website change is setting one link (step 4).

---

## 1. How the website opens the simulator

The website loads the simulator in an iframe with these URL parameters:

```
<simulator-url>?op=lichtenstein&mode=train&embed=1&resume=8
```

| Parameter | Values | Meaning |
|---|---|---|
| `op` | `lichtenstein` | Which operation to load. Only `lichtenstein` exists today. |
| `mode` | `train` or `exam` | **Training:** anatomy labels, target areas and feedback after every step. **Exam:** no labels or guides, mistakes cost points. The pass mark is 80/100. |
| `embed` | `1` | The simulator is running inside the website window. Hide any of the simulator's own menus, sign-in or "back" buttons that duplicate the website. |
| `resume` | `1`–`11`, optional | Start at this step number (1-based). Earlier steps should already be shown as done. If it's missing, start at step 1. |

The iframe is created with `allow="fullscreen; xr-spatial-tracking; autoplay; gamepad"`, so WebXR "Enter VR" and full screen work from inside it.

## 2. What the simulator must send back

Send messages to the website with `window.parent.postMessage`. The website only accepts messages that come from the simulator's own iframe.

```js
// After each step is completed (step = number of steps completed so far, 0–11)
window.parent.postMessage({ type: 'vivior:progress', step: 3 }, '*');

// When the operation ends (finished, or the user stops partway)
window.parent.postMessage({
  type: 'vivior:result',
  score: 86,          // 0–100. Optional; if omitted the website calculates it from `fails`
  steps: 11,          // number of steps completed (11 = whole operation)
  time: 742,          // seconds spent
  fails: [2, 9],      // 1-based step numbers that had errors
  mode: 'exam'        // optional, defaults to the mode the window was opened with
}, '*');

// If the user exits from inside the simulator without a result
window.parent.postMessage({ type: 'vivior:exit' }, '*');
```

After `vivior:result`, the website closes the window, saves the attempt, and opens the attempt review page.

## 3. Step list (must match this order)

The website numbers steps 1–11 like this. Map the simulator's internal steps to these numbers for `progress`, `resume` and `fails`. If the simulator's steps differ, tell me and I'll update the website list instead. Don't silently renumber.

1. Prepare and drape the skin
2. Make the skin incision
3. Divide subcutaneous fat and Scarpa's fascia
4. Open the external oblique aponeurosis
5. Identify and protect the ilioinguinal nerve
6. Mobilise the spermatic cord
7. Dissect and reduce the hernia sac
8. Place the mesh
9. Fix the mesh
10. Close the external oblique aponeurosis
11. Close the skin

The website shows a feedback note per failed step (for example "Incision sat 1.4 cm too low"). The current protocol only sends step numbers. If the simulator already measures specifics (incision length, distance from the inguinal ligament, nerve proximity), propose an optional `notes: { "2": "Incision 1.4 cm too low" }` field and I'll add support on the website.

## 4. Point the website at the simulator

In `vor-website.html`, find the catalogue at the top of the script (`const OPS = [`). The first operation has an empty link:

```js
{ id:'lichtenstein', simUrl:'', name:'Open inguinal hernia repair', ...
```

Set `simUrl` to the simulator's address, for example `simUrl:'/sim/'` (same site) or `simUrl:'https://vivior-sim.example.com/'`. While it's empty, the website shows a "Run a demo attempt" button instead of the simulator.

## 5. Suggested implementation

Create a small module, `vivior-bridge.js`, and call it from the existing step and scoring logic rather than spreading `postMessage` calls around:

```js
// vivior-bridge.js
const params = new URLSearchParams(location.search);
export const embedded = params.get('embed') === '1' && window.parent !== window;
export const op = params.get('op') || 'lichtenstein';
export const mode = params.get('mode') === 'exam' ? 'exam' : 'train';
export const resumeStep = Math.max(1, parseInt(params.get('resume') || '1', 10) || 1);

const startedAt = performance.now();
let completed = resumeStep - 1;
const fails = new Set();
let finished = false;

function send(msg) { if (embedded) window.parent.postMessage(msg, '*'); }

// Call when a step is finished. stepNumber is 1–11 in the website's order.
export function stepCompleted(stepNumber, { hadError = false } = {}) {
  if (hadError) fails.add(stepNumber);
  completed = Math.max(completed, stepNumber);
  send({ type: 'vivior:progress', step: completed });
}

// Call when the operation ends, or when the user quits partway.
export function finish({ score } = {}) {
  if (finished) return; finished = true;
  send({
    type: 'vivior:result', mode,
    steps: completed,
    time: Math.round((performance.now() - startedAt) / 1000),
    fails: [...fails].sort((a, b) => a - b),
    ...(Number.isFinite(score) ? { score: Math.round(score) } : {})
  });
}

export function exit() { send({ type: 'vivior:exit' }); }
```

Then:

- On load, read `mode` and turn labels and guides on or off. Read `resumeStep` and fast-forward the scene to that step, with earlier steps shown as done.
- When `embedded` is true, hide duplicate UI (the simulator's own home or menu screen) and go straight into the theatre.
- Call `stepCompleted(n, { hadError })` wherever a step is validated.
- Call `finish({ score })` when the last step is validated, and from any "End attempt" button.
- Keep everything working when the simulator is opened on its own (not embedded). In that case `send` does nothing.

## 6. Hosting requirements

- Serve the simulator over **https** (WebXR requires it).
- The simulator must be allowed inside an iframe. Don't send `X-Frame-Options: DENY`, and if there is a `Content-Security-Policy`, its `frame-ancestors` must include the website's address. GitHub Pages, Netlify and Vercel allow framing by default.
- The easiest setup for the hackathon is hosting both on the same site, for example the website at `/index.html` and the simulator at `/sim/`, with `simUrl:'/sim/'`.
- Note that the claude.ai preview of the website may block other sites inside it. Test the connection with the website file hosted yourself.

## 7. How to test

1. Run the simulator locally (for example `npx vite` or `npx serve`) and open `vor-website.html` from a local server too. Set `simUrl` to the simulator's local address.
2. On the website, choose **Explore with a demo account**, go to **Operations**, and click **Start** on the hernia repair.
3. Pick Training, then **Enter the operating room**. The simulator should appear in the window.
4. Complete a few steps. The stitch progress bar in the window's top bar should advance with each step.
5. Finish the operation. The window should close and the attempt review should show your score and the failed steps.
6. Repeat in Exam mode, and test **resume**. Stop partway, return to the dashboard, click **Resume training**, and check that the simulator starts at the right step.

When you're done, summarise what you changed in the simulator, the `simUrl` you set, and any differences you found between the simulator's steps and the list in section 3.
