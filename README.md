# Chippy

A small C# engine using [Arch](https://github.com/genaray/Arch) for scene entities
and injectable BRUTAL/OpenGL and MonoGame rendering backends. Requires .NET 10
and an OpenGL-capable graphics driver; BRUTAL requires OpenGL 4.1.

```powershell
dotnet run -- --renderer=brutal
dotnet run -- --renderer=monogame
dotnet run -- "C:\Models\building.glb"
dotnet run -- --model-demo
dotnet run -- --preemptive-ticking
dotnet run -- --vsync
dotnet run -- --no-publish-render-data
```

The default scene is a solar system at a common scale of 1 world unit = 1,000 km.
Radii and orbital distances use rounded [NASA fact-sheet values](https://nssdc.gsfc.nasa.gov/planetary/factsheet/).
The camera starts following Earth, which uses `Mesh.WithBroadClusters` for its green land
and blue ocean. `Mesh.WithFineClusters` and `Mesh.WithFaceNoise` are available for other meshes.
The Sun is an unlit mesh with a point light. Orbits remain circular
and coplanar, with one simulated second representing one hour; this is not an orbital
physics solver. The 2D map uses fixed-size markers for visibility.
`--model-demo` loads two copies of `Assets/Bakery.gltf` under a shared parent.

Middle-drag or the arrow keys orbit; wheel or `+`/`-` zoom; Shift + middle-drag pans.
WASD moves the pivot along the camera's ground plane, Space/Ctrl raises/lowers it,
and Shift speeds up keyboard movement. Press F or enable **Follow selected** for smooth
tracking of a rendered entity. Moving the pivot cancels following; Esc closes.
Controls take inspiration from [Felicette's camera](https://github.com/agm-114/felicette/blob/main/Assets/Controls/CameraController.cs).
Camera input pauses while unfocused or captured by ImGui. The **Hierarchy** window
shows entities and their parent-child relationships. Select an entity there or in the
scene to see its components in **Inspector**. Transform, mesh, line, light, and marker
components have dedicated editors. Other components show their public values.
**Chippy viewer** holds the renderer, simulation, audio, camera, and display controls.
Publishing defaults to on and preemptive ticking defaults to off.

## Simulation scheduling

Use the [runtime timeline](docs/architecture/runtime-timeline.html) to inspect recorded
ticks, frames, snapshots, and ImGui work. The [capture instructions](docs/architecture/README.md)
show how to record a run for comparison.

`SimulationScheduler` advances a fixed clock independently of drawing. It defaults
to 10 ticks per simulated second and runs systems sequentially in registration
order. Implement `ISimulationSystem.Update(double deltaTime)` and register the
system with `simulation.Add(system, every: 10)` to run it on ticks 10, 20, 30, etc.
The interval is measured against the shared tick counter, including for systems
registered later. Each call receives its scheduled interval in simulated seconds;
at 10 ticks/second, `every: 10` receives `1.0`. The solar demo uses 30 ticks/second
and registers a simple orbit animation system.

```csharp
using Chippy.Simulation;

var simulation = new SimulationScheduler(ticksPerSecond: 10);
simulation.Speed = 4;
simulation.Advance(realDelta); // Unscaled elapsed wall time in seconds.
simulation.Paused = true;
simulation.Step();             // Exactly one tick, even while paused or at speed zero.
```

`Advance` returns the number of ticks executed. It runs at most 8 ticks per call
by default (`maxTicksPerFrame` in the constructor), keeping remaining catch-up
time for later updates rather than skipping ticks. Pausing or setting speed to zero
freezes automatic advancement without accumulating paused wall time. Manual steps
preserve any pending catch-up time. Tick rate and catch-up limit must be positive;
speed and elapsed time must be finite and nonnegative. Register systems before the
viewer starts. The scheduler is sequential: do not change it or access its world
concurrently with a running tick.

`ViewerState.Simulation` lives alongside the world, outside the renderer-switch
loop, so its systems, time, pause state, and speed survive backend recreation.
Both backends use a monotonic clock and update the camera with a separately clamped
frame delta. They sleep for at most 10 ms between idle input polls. In live mode they
also wake for the next tick deadline. `ViewerState.TargetFrameRate` defaults
to 60 and can be changed in the panel. A late frame renders once in the current
target interval; missed render targets are skipped. Simulation ticks keep their
catch-up time. In published mode, enable **VSync** in the viewer or pass `--vsync`
to pace rendering through presentation. The manual FPS target applies when VSync
is off. Live mode uses the manual target so display refresh waits stay out of
the shared simulation and render loop.

BRUTAL calls `Update` after polling events; MonoGame suppresses `Draw` until a
frame is due. Both use the same scheduling code. GPU/window calls and all ImGui calls
stay on the window thread. With publishing enabled, a dedicated simulation loop wakes
for its own tick deadlines, runs systems, and publishes render data. Drawing reads the
latest snapshot while the simulation works on the live world. The render cap controls
frame deadlines independently of tick deadlines.

ImGui edits the actual Arch world between ticks. Backends collect native input while
a tick runs, but defer widget code until it finishes. Meanwhile, presentation reuses
the previous snapshot and completed ImGui draw data. Each drawn frame requests a UI
refresh. Frame requests and pending input share the next safe point before another
tick, and several requests can share one UI pass. UI edits request an immediate
redraw. Camera controls continue during ticks.

`RenderWorld` reuses three Arch worlds for drawing, holding the latest publication,
and preparing the next one. Preparation rebuilds a trimmed world containing only
renderable `Transform`, `MeshRenderer`/`LineRenderer`/`Light`, and `SourceEntity` components.
Mesh assets and immutable line positions stay shared. Transform world matrices are resolved before copying; copied parents are
cleared, and rendering uses those resolved matrices without another transform update.
`SourceEntity.Entity` refers to the original simulation entity, including its version;
render-world IDs are allocated normally. Deleted entities and disabled renderers
disappear from the next publication.

The render-world object owns preparation readiness; publication consumes that ready
world. It exists only while publishing is active. Starting in live mode creates no
snapshot worlds, and disabling publishing releases them after a held tick is drained.
`viewer.UsesPublishedData` reflects the active render-world object; `PublishedTick`
is null in live mode.

The producer and window thread exchange snapshot references under a short lock. The
window thread acquires the latest snapshot between draws, and rendering uses it without
holding that lock. `SceneRenderSystem.Draw(world, viewportSize)`
queries either the published world or the simulation world directly in live mode.
Treat `viewer.RenderWorld` as read-only and obtain it for each draw; do not retain
its entities or component references across publications, because the old world
can be reused for a later publication.
Direct world changes outside registered systems/UI must go through
`viewer.TryEditWorld(world => ...)`, on the window thread. It returns false while the
simulation owns the world. Retain the edit and retry. Do not mutate the world or scheduler from
other threads, or retain component references across structural changes.

With **Preemptive ticking**, the worker starts the next tick early and prepares its
packet, then holds publication until that tick's target. Only one unpublished tick is
allowed. Late results publish as soon as preparation completes. ImGui can
edit the computed world while its packet is held; preparation is repeated for the
same deadline. The UI can therefore show a working tick ahead of the displayed tick.
Pausing, setting speed to zero, or manually stepping commits an already-computed tick;
the first manual step consumes that tick rather than running it twice. These actions
do not roll back completed work. Turning publishing off drains a held tick at its
deadline before live drawing resumes. Preemptive ticking only takes effect with
publishing on; the conflicting CLI flags are rejected.

With publishing off, ticks, UI, transform resolution, and drawing share the window
thread, so a long tick blocks presentation. Simulation continues while minimized.
Disposal/backend switches join an outstanding worker before destroying its world or
GPU resources. The new viewer captures the completed world; backend recreation time
is excluded from its clock. For headless runs, use the scheduler directly with
`Step()` or `Advance()`; it has no graphics dependency.

## Components and world

The Arch world owns the scene. Components are ordinary C# structs without marker
interfaces. CPU mesh assets remain shared C# objects.

| Type | Purpose |
| --- | --- |
| `Transform` | Parent entity, local position, quaternion rotation, scale, and the resolved world matrix. Can ignore parent scale. |
| `OrbitMotion` | Circular orbit radius, phase, and angular speed. `OrbitSystem` updates the entity's local position. |
| `OrbitPath` | Positions a line relative to its center entity's world position and rotation, independent of its hierarchy parent. |
| `MeshRenderer` | A shared CPU mesh reference, tint, `Enabled` and `Unlit` state. |
| `LineRenderer` | Immutable points, color, loop and enabled state. Set `UseWorldSpace` to interpret points as world coordinates. |
| `Light` | Point/directional light color, intensity, optional range and enabled state. |
| `Name` | A readable entity name for the inspector. |
| `SourceEntity` | On published copies, a reference to the original simulation entity. |
| `TransformSystem` | Resolves parent hierarchies into world matrices. |
| `SceneRenderSystem` | Queries renderable meshes/lines and submits them to an injected `IRenderSystem`. |

```csharp
using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Chippy.Components;
using Chippy.Ecs;
using Chippy.Models;
using Chippy.Rendering;
using Chippy.Systems;

using var world = World.Create();
var bakery = Mesh.Load("Assets/Bakery.gltf");
var parent = world.Create(new Transform { LocalPosition = new(2, 0, 0) });
var entity = world.Create(
    bakery.Normalization.ToTransform(parent),
    new MeshRenderer(bakery) { Tint = new(0.6f, 0.8f, 1, 1) });

var owner = entity.World; // C# 14 extension property, available through Chippy.Ecs.

// Systems receive the actual Arch world; there is no generic ECS adapter.
var transforms = new TransformSystem(world);
var scene = new SceneRenderSystem(renderer); // An initialized IRenderSystem.

// Mutate components directly; do not retain refs across structural world changes.
ref var transform = ref entity.Get<Transform>();
transform.LocalPosition += Vector3.UnitX;
transform.LocalEulerAngles = new(0, 45, 0);

// Per frame, after gameplay/simulation and inspector edits:
transforms.Update();
renderer.BeginFrame(view, projection);
scene.Draw(world, new Vector2(1280, 720));
```

The caller owns and disposes the world and renderer. Give each tick or UI pass
exclusive world access; render only published data while a worker runs. GPU creation,
drawing, release, and disposal happen on the graphics thread with its context or
device available.

`entity.World` indexes Arch's world registry directly. `entity.IsAlive` delegates to
`entity.World.IsAlive(entity)`. Both assume the owning world still exists. A destroyed
entity can still resolve its existing world. Keep entities within their world's
lifetime; Arch can recycle world IDs after disposal. Systems can continue receiving
an explicit world dependency.

Import `Arch.Core.Extensions` for Arch's existing entity methods, including
`entity.Get<T>()`, `entity.Has<T>()`, `entity.TryGet<T>(out var component)`,
`entity.Set(component)`, `entity.Add(component)`, and `entity.Remove<T>()`.
`Get<T>()` returns a reference; `TryGet<T>()` copies the component. Use
`ref var component = ref entity.TryGetRef<T>(out var exists)` when an optional
component needs editing, and check `exists` before accessing the reference.
These methods assume a live entity; structural changes can invalidate component refs.
Arch also defines `IsAlive()`; to use our `IsAlive` property when importing both
namespaces, put `using Chippy.Ecs;` inside your namespace and leave
`using Arch.Core.Extensions;` outside it. The nearer import takes precedence.

## Transform hierarchy

`Transform` uses Unity-style local names: `Parent`, `LocalPosition`, `LocalRotation`,
and `LocalScale`. `LocalEulerAngles` edits rotation in degrees using X=pitch,
Y=yaw, Z=roll. `LocalRotation` is the authoritative quaternion. Initialize identity
transforms with `new Transform()`; `default(Transform)` has zero scale/rotation.

`LocalMatrix` is scale × rotation × translation. With System.Numerics row vectors,
`LocalToWorldMatrix` is local × parent world. Enable `IgnoreParentScale` on a child
to follow its parent's world position and rotation at the child's own scale.
`TransformSystem.Update()` refreshes
that derived matrix before preparing render data (or drawing in live mode); changes become visible
on its next update. Each transform is resolved once per update using reusable
buffers and an iterative traversal, including parents without mesh components.

```csharp
entity.SetParent(parent);
entity.SetParent(null); // Detach.
if (entity.TrySetParent(parent))
    Console.WriteLine("Parent changed.");
```

Parenting keeps local values, so the world position can change. Parents must be
live entities in the same world with a `Transform`. `CanSetParent` validates a
request without changing it; `TrySetParent` returns false on invalid requests and
leaves the child unchanged. `SetParent` throws for invalid requests. These are Entity
extension methods in `Chippy.Components`, alongside `matrix.ToTransform()`.
Direct writes to `Parent` are also validated by the next update.
When a parent is deleted, its children detach on the next update and retain local
values. Generation-aware Arch entity references prevent recycled IDs from becoming
replacement parents. Removing a live parent's `Transform` requires reparenting its
children first. Parenting affects transforms; renderer enabled state is per entity.

## Rendering and backend switching

`SceneRenderSystem` queries `Transform` with `MeshRenderer`, `LineRenderer` or `Light`. It draws
entities with an enabled renderer and a non-null mesh using their world matrix and
tint. Adding/removing components or entities changes the next draw automatically.
The system filters disabled renderers and missing meshes, then passes both components
directly to `IRenderSystem.DrawMesh(in Transform, in MeshRenderer)`. Backend methods
receive the full components, including the shared mesh, world matrix, and tint.

`IRenderSystem` supplies `BeginFrame`, `SetLights`, `DrawMesh`, `DrawPrimitives`, and `ReleaseMesh`. The concrete
backends own shaders/effects and GPU buffers; neither stores the scene. Each backend
uploads a shared mesh once and reuses its buffers across entities. Each submesh still
issues a draw call per entity; hardware instancing is not implemented.

Use the panel's **Renderer** selector to switch backends at runtime. The Arch world,
entities, components, simulation schedule, selection, camera, and panel/window
placement survive the switch. The old backend releases its GPU resources before
its window/device closes; the new backend uploads the same CPU meshes again.
`Program.cs` owns the world outside this loop. Closing the window or pressing
Escape exits the viewer.

`ReleaseMesh(mesh)` drops only that backend's GPU cache entry; drawing the mesh again
uploads it again. CPU assets are collected when no references remain. Treat loaded
mesh arrays as immutable after upload. Meshes retain glTF units; normalization is an
explicit viewer choice. Drawable transforms must be finite and invertible.

Matrices use the System.Numerics row-vector convention and a `[0, 1]` projection
depth range. BRUTAL converts depth for OpenGL internally. The backends share geometry
and controls but currently have different lighting/color output.

`Light` supports point and directional sources, edited through the inspector. Directional
lights shine along local -Z. Point intensity falls with squared distance; a positive
`Range` adds a smooth cutoff, while zero is unlimited. Both backends select the three
strongest contributions per mesh, approximating each point source's direction and
falloff at the mesh center. This keeps MonoGame's built-in `BasicEffect` and avoids
another shader build pipeline; it is suitable for distant sunlight, with approximate
lighting near large meshes. Shadows and physically based materials are not implemented.
Rendering subtracts the camera target from submitted positions to keep GPU transforms
near the origin; simulation coordinates remain unchanged and still use floats.

## Primitives and procedural audio

`Mesh.CreateCube`, `CreateSphere`, `CreateCapsule`, `CreateCylinder`, `CreatePlane`
and `CreateQuad` create the standard mesh shapes. `CreateCone`, `CreateFrustum`,
`CreatePrism(sides: 3)` and `CreateTorus` cover additional common shapes. Round
solids use the Y axis; the quad faces +Z and the plane faces +Y. Meshes contain
positions and normals, without UVs/textures. Reuse each mesh across entities.

```csharp
var cube = Mesh.CreateCube();
world.Create(new Transform(), new MeshRenderer(cube));
world.Create(new Transform(), LineRenderer.Circle(5, Vector4.One));
```

`PrimitiveBatch` draws world-space lines, polylines, circles, wire spheres/boxes
and triangles, plus pixel-space lines, rectangles, circles and triangles. `Vector3`
overloads use world coordinates; `Vector2` overloads use framebuffer pixels from
the top-left. 3D lines are one pixel wide; 2D lines support thickness. Filled 2D
shapes use triangles. Work is batched per topology, with 2D drawn over the scene.
`ViewerState.DrawOverlay` receives the rendered world, batch and viewport size;
use that world for scene reads so publishing remains safe.

`AudioClip.Generate(duration, sample)` creates mono PCM from a function of time;
`AudioClip.Tone(frequency)` supplies a simple sine tone. `AudioSystem` supports
one-shot effects with pitch/pan, looping music, pause/resume/stop and separate
master/music/effect volumes. The demo's **Procedural audio** section plays a
generated ambient chord or chime; playback starts only when requested.

Audio uses an independent OpenAL context from MonoGame's existing native dependency,
so renderer switches preserve playback. No audio files or new packages are needed.
`Program` owns audio outside the backend loop. Call its API on the window thread;
`Update` reclaims finished one-shots. If no output device is available, graphics
still work and the panel reports the audio error.

## Input and project structure

Both window hosts implement `IInputSystem`. Shared `ModelViewer.Update` owns camera
controls, focus/UI capture handling, simulation advancement, camera delta clamping,
and automatic orbit. Input capture uses the most recent UI frame. The host
owns native events, clearing, viewport changes, presentation, and ImGui rendering.

- `Components`: Arch scene data: transforms, meshes, lines and lights.
- `Ecs`: Arch entity convenience extensions, including `entity.World` and `entity.IsAlive`.
- `Systems`: transform hierarchy resolution and parenting validation.
- `Simulation`: fixed-tick scheduling and the simulation system interface.
- `Models`: shared CPU mesh assets, glTF import, and bounds.
- `Audio`: procedural PCM clips and playback, independent of the renderer.
- `Demos`: the real-scale solar-system scene and circular orbit animation.
- `Rendering`: renderer interface, world mesh query, matrix helpers, and orbit camera.
- `Rendering/RenderWorld.cs`: trimmed render-world preparation and publication.
- `Input`: shared key codes, input interface, and ImGui key conversions.
- `Viewer`: shared editor state, entity inspector, camera controls, and smoke checks.
- `Backends/Brutal` and `Backends/MonoGame`: backend rendering, GPU resources,
  window/input/UI integration, and native key conversions.

The shared key set covers letters, digits, F1–F24, navigation/editing keys,
punctuation, locks, modifiers, menu, and keypad digits/operators. Names follow a US
layout and represent key states; use platform text-input events for typed characters.
MonoGame combines keypad Enter with Return, so Return checks both on BRUTAL too.

## Model support and verification

The importer supports static triangle meshes in `.gltf`/`.glb`, glTF node hierarchy
transforms baked into geometry, interleaved float positions/normals, unsigned indices,
and material base colors. Missing normals are generated. The original bakery sample
has an embedded buffer. Textures, transparency, vertex colors, animation, skins,
morph targets, sparse accessors, compressed geometry, and required extensions are
outside the current importer. Runtime entity parenting is separate from the baked
glTF hierarchy.

Build, or run a three-frame rendering/UI/GPU-sharing check that closes itself:

```powershell
dotnet build Chippy.slnx
dotnet run -- --renderer=brutal --smoke-test
dotnet run -- --renderer=monogame --smoke-test
dotnet run -- --renderer=brutal --model-demo --smoke-test
dotnet run -- --renderer=monogame --model-demo --smoke-test
```

The optional smoke mode checks visible geometry, ImGui output and shared GPU mesh uploads,
then closes the window after three frames.

Built with [Arch](https://github.com/genaray/Arch),
[MonoGame](https://monogame.net/), and [BRUTAL](https://github.com/ahwootech/BRUTAL).
BRUTAL is Copyright (c) 2024 Ahwoo Limited.
