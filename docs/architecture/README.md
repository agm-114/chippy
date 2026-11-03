# Simulation and rendering timeline

Open [sim-render-timeline.html](sim-render-timeline.html) directly in a browser.
It is a standalone interactive diagram; no server or package installation is needed.

Edit [sim-render-timeline.source.html](sim-render-timeline.source.html) to change
the controls, scheduling model, or drawing code. The same source fragment can be
shown inline in Codex for architecture discussions. Regenerate the standalone
preview after editing, from the repository root:

```powershell
pwsh -NoProfile -File docs/architecture/update-preview.ps1
```

The update script replaces only the embedded source fragment. The standalone
document carries its styles and preview runtime, so updating it does not require
Codex plugins or machine-specific paths. Keep both HTML files in sync.

Verify the scheduling model without browser dependencies:

```powershell
node docs/architecture/check-scheduling.cjs
```

The dropdown contains Chippy's world loop and Conquer-Space's source-derived loop.
**Publish render data** is an independent option. It is disabled and forced off for
Conquer-Space. Old saved `snapshot` selections migrate to Chippy with publishing on;
other removed architecture selections fall back to Chippy, retaining their publishing preference.
Rows follow the data flow: ImGui edits, world/simulation, published data when enabled,
then rendering. ImGui sits directly above the world it reads and edits.

**Preemptive ticking** is an optional publishing-only setting, off by default.
After a tick is published, the next tick can start as soon as the world is free.
Render data is prepared immediately, but becomes visible only at the later of
its ready time and its scheduled tick target. Late work publishes immediately;
early work is held as **Ready**. The next computation waits for that publication,
limiting the working world to one unpublished tick ahead. With the option off,
tick computation still starts at its target time. Conquer-Space cannot enable it.

ImGui continues to read/edit the working world, which may now be ahead of the
displayed world. Input after early computation changes that result; it does not
rewind or rerun the tick. An edit while a prepared packet is held invalidates that
packet and prepares a replacement for the same deadline. The UI can redraw its
feedback immediately while the world image continues using the previous packet.
This is an illustrative timing model, not a transactional simulation rollback.

With publishing enabled, rendering runs on a separate thread using the last
completed render packet and prepared ImGui draw data. ImGui widget code still
reads and edits the same live world as simulation. Blue arrows show completed
world data being published, and solid arrows show a packet's first render read.
Purple arrows show ImGui writing the live world. Packets include the completed tick
and edits already applied, and can be published after a UI edit as well as a tick.
The data lane shows the latest published version, not CPU work or the lifetime of
every physical buffer. An already submitted frame can finish using an older version.

Target framerate and tick rate control the deadline guides; workload controls
the variable simulation durations. The chart uses synthetic timings, not measured
engine performance. In particular, its 2 ms edit/apply/publish costs are illustrative.
The non-Conquer modes poll pending input before simulation/render work.
Input is processed immediately when world access is safe. An active tick delays
that ImGui pass until it finishes; live render reads also prevent concurrent edits
when publishing is off. ImGui writes directly to the world, without a separate
world-command replay. A responsive window/input pump is assumed. The model counts
only the illustrative 2 ms entity-edit cost; other UI construction/polling costs
are omitted. Prepared UI draw data must be handed to the renderer with an immutable
lifetime, just like a render packet.

With publishing enabled, input waiting for an active simulation tick does not
block rendering: frames reuse the last completed world packet and prepared UI
draw data. Only an actively running UI pass and preparation of its changed render data
gate new frames. After that handoff, a UI-triggered redraw shows the new state.
For example, at 24 FPS an input at 36 ms can wait for a tick ending at 60 ms while
the renderer still draws the old state at 41.7 ms; the updated state is redrawn at
64 ms. An already submitted render completes normally. Without publishing, pending
input still runs before the next live-world render once world access is safe.

UI changes request an immediate render, including another draw in the same target
interval when useful; these bars are marked **R**. Requests coalesce while work is
pending. Ordinary missed frame targets are skipped, and simulation ticks are
retained. A same-interval redraw requires a newer completed UI version. The three
illustrative inputs all change entity state; input that changes neither UI nor
world state need not request a redraw.

The diagram places Chippy's first tick request at zero; the engine accumulates one
interval before its first ordinary automatic tick. Preemptive mode computes that
tick immediately and holds its packet until the first interval expires.

The C# runtime implements publishing and preemptive ticking through
`ViewerState.PublishRenderData` (default on) and `ViewerState.PreemptiveTicking`
(default off). Both backends use `ModelViewer` for orchestration. `RenderWorld` owns
two Arch worlds: a published world and a preparation world. Preparation copies only
renderable transforms/mesh renderers and a `SourceEntity` reference to the original
entity. Publication swaps worlds on the window thread between draws, after the worker
finishes. The renderer queries the supplied world in both published and live mode.
GPU/window and ImGui calls stay on the native window
thread; a worker task computes each simulation tick and prepares render data with
exclusive access to the same Arch world. The diagram's separate rendering lane
represents this concurrency; its world/ImGui lane represents exclusive world access,
rather than a requirement that ImGui and systems use the same OS thread. While the
worker is active, the window thread polls native input and draws completed world/UI
data, then builds ImGui at the next safe boundary before starting another tick.
Live mode performs all work on the window thread.

The runtime permits one held early packet; UI edits prepare a replacement for its
existing deadline. Turning publishing off drains a held tick before live rendering.
Pause, speed zero, manual step, and backend disposal commit already-computed work;
they cannot roll back a tick. Ordinary scheduling retains tick debt and skips missed
render targets. Diagram workloads/copy costs remain illustrative, not measurements.

Conquer-Space has its own scheduling model, verified against `main` at commit
`74b678fbee29b3d4482405368655857ba64699c2`:

- `UniverseScene::ManageTick` checks `now - last_tick > tick_length` once per
  frame and sets `last_tick = now` before ticking. Normal mode runs at most one
  simulation tick per frame and does not preserve a fixed-clock tick backlog.
- Scene update/tick, ImGui widget construction, scene rendering, UI draw submission,
  swap, and input polling run sequentially. Events arriving during a tick wait for
  the end-of-frame poll before a later ImGui frame can handle them.
- `GLWindow::InitWindow` sets `glfwSwapInterval(1)`. Each loop draws and then swaps;
  there is no Chippy-style frame-deadline queue or coalescing logic. The graph
  assumes swaps complete on the next available refresh, with no GPU/driver queue
  effects. Gray segments after red work show that VSync wait. The FPS control
  becomes **Assumed display refresh**, and red guides mark those refreshes.
- Blue guides mark eligibility based on each actual tick start plus its interval,
  rather than an accumulating fixed wall-clock grid. The source's float-seconds
  conversion is preserved; equality with that converted interval waits for a later
  frame because the comparison is strict. The tick interval selector uses
  its actual positive entries in `ctx::tick_speeds`, defaulting to 100 ms.

Conquer-Space's displayed zero is the first tick's eligibility threshold, matching
the other entries' first tick request at zero. Scheduling still runs on its original
clock, starting with `last_tick = 0`; the view subtracts that first interval afterward.
This preserves the first tick's wait, the display refresh phase, and any render/swap
already in flight at zero. Earlier work is cropped. UI actions remain 36, 112, and
172 ms after the displayed origin in every architecture, so their input timing is
comparable when switching entries.

The Conquer-Space illustration assumes running normal mode, synthetic tick/render/edit
costs, and otherwise negligible frame overhead. The source initially starts paused.
Its negative speed entries run multiple ticks per frame and are outside this explicitly
labeled normal-mode entry. Links under the chart point to the frame loop, tick logic,
window integration, and speed table.

The diagram checks above validate the illustrative model separately.
