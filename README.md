# Chippy

A small C# engine using [Arch](https://github.com/genaray/Arch) for scene entities
and injectable BRUTAL/OpenGL and MonoGame rendering backends. Requires .NET 10
and an OpenGL-capable graphics driver; BRUTAL requires OpenGL 4.1.

```powershell
dotnet run -- --renderer=brutal
dotnet run -- --renderer=monogame
dotnet run -- "C:\Models\building.glb"
dotnet run -- --model-demo
```

The default model is `Assets/Bakery.gltf`. The demo creates two mesh entities
under a shared parent, reusing one loaded mesh. Select the parent in the inspector
to move, rotate, or scale both models together.

Use the arrow keys to orbit, `+`/`-` to zoom, and `Esc` to close. Camera shortcuts
pause while the window is unfocused or ImGui captures keyboard input. The panel
edits entity parenting, local transforms, renderer tint/enabled state, camera,
wireframe mode, and background color.

## Components and world

The Arch world owns the scene. Components are ordinary C# structs without marker
interfaces. CPU mesh assets remain shared C# objects.

| Type | Purpose |
| --- | --- |
| `Transform` | Parent entity, local position, quaternion rotation, scale, and the resolved world matrix. |
| `MeshRenderer` | A shared CPU mesh reference, tint, and `Enabled` state. |
| `TransformSystem` | Resolves parent hierarchies into world matrices. |
| `MeshRenderSystem` | Queries renderable entities and submits them to an injected `IRenderSystem`. |

```csharp
using System.Numerics;
using Arch.Core;
using Chippy.Components;
using Chippy.Models;
using Chippy.Rendering;
using Chippy.Systems;

using var world = World.Create();
var bakery = Mesh.Load("Assets/Bakery.gltf");
var parent = world.Create(new Transform { LocalPosition = new(2, 0, 0) });
var entity = world.Create(
    bakery.Normalization.ToTransform(parent),
    new MeshRenderer(bakery) { Tint = new(0.6f, 0.8f, 1, 1) });

// Systems receive the actual Arch world; there is no generic ECS adapter.
var transforms = new TransformSystem(world);
var meshes = new MeshRenderSystem(world, renderer); // An initialized IRenderSystem.

// Mutate components directly; do not retain refs across structural world changes.
ref var transform = ref world.Get<Transform>(entity);
transform.LocalPosition += Vector3.UnitX;
transform.LocalEulerAngles = new(0, 45, 0);

// Per frame, after gameplay/simulation and inspector edits:
transforms.Update();
renderer.BeginFrame(view, projection);
meshes.Draw();
```

The caller owns and disposes the world and renderer. Use the world on the scene
thread; finish simulation jobs before editing or rendering it. GPU creation,
drawing, release, and disposal happen on the graphics thread with its context or
device available.

## Transform hierarchy

`Transform` uses Unity-style local names: `Parent`, `LocalPosition`, `LocalRotation`,
and `LocalScale`. `LocalEulerAngles` edits rotation in degrees using X=pitch,
Y=yaw, Z=roll. `LocalRotation` is the authoritative quaternion. Initialize identity
transforms with `new Transform()`; `default(Transform)` has zero scale/rotation.

`LocalMatrix` is scale × rotation × translation. With System.Numerics row vectors,
`LocalToWorldMatrix` is local × parent world. `TransformSystem.Update()` refreshes
that derived matrix once before rendering; direct component changes become visible
on its next update. Each transform is resolved once per update using reusable
buffers and an iterative traversal, including parents without mesh components.

```csharp
world.SetParent(entity, parent);
world.SetParent(entity, null); // Detach.
if (world.TrySetParent(entity, parent))
    Console.WriteLine("Parent changed.");
```

Parenting keeps local values, so the world position can change. Parents must be
live entities in the same world with a `Transform`. `CanSetParent` validates a
request without changing it; `TrySetParent` returns false on invalid requests and
leaves the child unchanged. `SetParent` throws for invalid requests. These are World
extension methods in `Chippy.Components`, alongside `matrix.ToTransform()`.
Direct writes to `Parent` are also validated by the next update.
When a parent is deleted, its children detach on the next update and retain local
values. Generation-aware Arch entity references prevent recycled IDs from becoming
replacement parents. Removing a live parent's `Transform` requires reparenting its
children first. Parenting affects transforms; renderer enabled state is per entity.

## Rendering and backend switching

`MeshRenderSystem` queries `Transform` and `MeshRenderer`. It draws
entities with an enabled renderer and a non-null mesh using their world matrix and
tint. Adding/removing components or entities changes the next draw automatically.
The system filters disabled renderers and missing meshes, then passes both components
directly to `IRenderSystem.DrawMesh(in Transform, in MeshRenderer)`. Backend methods
receive the full components, including the shared mesh, world matrix, and tint.

`IRenderSystem` supplies `BeginFrame`, `DrawMesh`, and `ReleaseMesh`. The concrete
backends own shaders/effects and GPU buffers; neither stores the scene. Each backend
uploads a shared mesh once and reuses its buffers across entities. Each submesh still
issues a draw call per entity; hardware instancing is not implemented.

Use the panel's **Renderer** selector to switch backends at runtime. The Arch world,
entities, components, selection, camera, and panel/window placement survive the switch.
The old backend releases its GPU resources before its window/device closes; the new
backend uploads the same CPU meshes again. `Program.cs` owns the world outside this
loop. Closing the window or pressing Escape exits the viewer.

`ReleaseMesh(mesh)` drops only that backend's GPU cache entry; drawing the mesh again
uploads it again. CPU assets are collected when no references remain. Treat loaded
mesh arrays as immutable after upload. Meshes retain glTF units; normalization is an
explicit viewer choice. Drawable transforms must be finite and invertible.

Matrices use the System.Numerics row-vector convention and a `[0, 1]` projection
depth range. BRUTAL converts depth for OpenGL internally. The backends share geometry
and controls but currently have different lighting/color output.

## Input and project structure

Both window hosts implement `IInputSystem`. Shared `ModelViewer.Update` owns camera
controls, focus/UI capture handling, delta clamping, and automatic orbit. The host
owns native events, clearing, viewport changes, presentation, and ImGui rendering.

- `Components`: Arch scene data: transforms and mesh renderers.
- `Systems`: transform hierarchy resolution and parenting validation.
- `Models`: shared CPU mesh assets, glTF import, and bounds.
- `Rendering`: renderer interface, world mesh query, matrix helpers, and orbit camera.
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
dotnet run -- --renderer=brutal --model-demo --smoke-test
dotnet run -- --renderer=monogame --model-demo --smoke-test
```

Built with [Arch](https://github.com/genaray/Arch),
[MonoGame](https://monogame.net/), and [BRUTAL](https://github.com/ahwootech/BRUTAL).
BRUTAL is Copyright (c) 2024 Ahwoo Limited.
