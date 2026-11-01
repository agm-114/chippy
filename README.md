# Chippy

A small model API and glTF viewer with injectable BRUTAL and MonoGame render systems.
Requires .NET 10 and an OpenGL-capable graphics driver; the BRUTAL backend requires OpenGL 4.1.

Run the included bakery model with either backend (BRUTAL is the default):

```powershell
dotnet run -- --renderer=brutal
dotnet run -- --renderer=monogame
```

Open another model (relative paths resolve from the working directory):

```powershell
dotnet run -- "C:\Models\building.glb"
```

Use the arrow keys to orbit, `+`/`-` to zoom, and `Esc` to close. The model is
centered and scaled to fit the initial view. Resizing updates the viewport and camera aspect ratio.

The Dear ImGui panel shows model statistics and frame timing, with camera sliders,
a camera reset button, automatic orbit, wireframe mode, and a background color picker.
Camera shortcuts pause while the window is unfocused or the panel captures keyboard input; click the scene to resume them.
Use the panel's **Renderer** selector to switch between BRUTAL and MonoGame while
running. The window is recreated in the same process; mesh references, model edits,
camera, display settings, selected instance, and window position/client size are preserved.
The ImGui panel also keeps its position, expanded size, and collapsed state. GPU buffers are
released with the old backend and uploaded again by the new one. Closing the
window or pressing Esc exits the viewer.

The model controls edit the selected instance's position, scale, tint, and visibility.
To see two independently transformed instances sharing one loaded model:

```powershell
dotnet run -- --model-demo
```

## Mesh API

`Chippy.Models` uses Unity-style vocabulary with C# PascalCase names:

| Type | Purpose |
| --- | --- |
| `Mesh` | Shared CPU geometry, submeshes, bounds, and vertex/triangle counts. Load with `Mesh.Load(path)`. |
| `SubMesh` | One triangle primitive's vertices, indices, and base color. |
| `Bounds` | Geometry extents, center, size, and an optional normalization matrix. |
| `Transform` | `Position`, quaternion `Rotation`, `LocalScale`, and `LocalToWorldMatrix`. `new Transform()` is identity. |
| `MeshInstance` | Plain struct containing a `SharedMesh` reference, transform, tint, and visibility. |
| `IRenderSystem` | Rendering dependency with `BeginFrame`, `DrawMesh`, and `ReleaseMesh`. |
| `BrutalRenderSystem` | BRUTAL/OpenGL implementation. |
| `MonoGameRenderSystem` | MonoGame implementation using a `GraphicsDevice`. |
| `ViewerState` | Shared instances, camera, window placement, display settings, selection, and requested renderer. |
| `IInputSystem` | Window focus, held keys, and digital axes using shared `KeyCode` values. |
| `ModelViewer` | Shared scene and controls, injected with `IRenderSystem`, `IInputSystem`, and `ViewerState`. |

```csharp
using System.Numerics;
using Chippy.Models;
using Chippy.Rendering;
using Chippy.Backends.Brutal;
using Chippy.Backends.MonoGame;

var bakery = Mesh.Load("Assets/Bakery.gltf");
var first = new MeshInstance(bakery)
{
    Transform = new() { Position = new(-2, 0, 0) }
};
var second = new MeshInstance(bakery)
{
    Transform = new()
    {
        Position = new(2, 0, 0),
        Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f)
    },
    Tint = new(0.6f, 0.8f, 1, 1)
};

// With BRUTAL's initialized OpenGL context current:
using IRenderSystem renderer = new BrutalRenderSystem();
// In a MonoGame Game.LoadContent instead:
// using IRenderSystem renderer = new MonoGameRenderSystem(GraphicsDevice);

// Each frame, using your camera's matrices:
renderer.BeginFrame(view, projection);
renderer.DrawMesh(in first);
renderer.DrawMesh(in second);
```

Both instances reference the same `Mesh` object. Their transforms and tints are
independent values. `Mesh.Load` loads a new object on each call; load once and
reuse that reference to share geometry. Treat loaded mesh arrays as immutable
after uploading them.

Meshes retain their glTF units. The viewer explicitly applies `bakery.Normalization`
to fit its initial camera; this is optional. Transforms currently have no parent hierarchy.
These names provide familiar vocabulary; this is a small API, without Unity's component system.

`IRenderSystem` implementations upload each shared mesh once and reuse its GPU buffers across
instances. Each submesh still issues a draw call per instance; hardware instancing
is not implemented. Create, draw, release, and dispose on the thread with the
backend's graphics context/device available. BRUTAL shader files must be copied to
`Backends/Brutal/Shaders` beside the executable, or supplied through its constructor's
`shaderDirectory` argument. MonoGame uses its built-in `BasicEffect`; the two
backends share geometry and controls but have different lighting/color output.

An ECS can supply its own world transform and tint directly:

```csharp
renderer.DrawMesh(bakery, worldTransformMatrix, tint);
```

`MeshInstance` is a viewer convenience. An ECS can keep these values in separate
components. The shared mesh is a managed C# reference; no ECS library is required.

`renderer.ReleaseMesh(bakery)` releases its cached GPU buffers. Existing mesh
references remain valid, and drawing the mesh again uploads it again. Disposing
the renderer releases all its GPU resources. CPU mesh data is garbage collected
once all references, including any renderer cache entries, are gone.

## Injecting a render system

The shared viewer receives rendering and input dependencies, with backend-independent scene state:

```csharp
using Chippy.Viewer;

var state = new ViewerState([first, second]);
var viewer = new ModelViewer(renderSystem, inputSystem, state);
if (viewer.Update(deltaTime, keyboardCaptured))
    viewer.Draw(width / (float)height);
else
    CloseWindow(); // The host finishes the UI frame and closes its own window.
```

The caller owns `renderSystem` and its graphics context/device. Window hosts create
the renderer through an injected factory after graphics initialization, and dispose
it before shutting graphics down. The executable selects the host and factory in
`Program.cs`:

```csharp
using Chippy.Backends.Brutal;
using Chippy.Backends.MonoGame;

var switchRequested = BrutalViewer.Run(() => new BrutalRenderSystem(), state, name);

// Or:
using var game = new MonoGameViewer(
    device => new MonoGameRenderSystem(device), state, name);
game.Run();
// game.SwitchRequested indicates whether to open state.Renderer next.
```

`Program.cs` repeats host creation when a switch is requested, reusing the same
`ViewerState`. To request a switch programmatically, set `state.Renderer` to
`RenderBackend.Brutal` or `RenderBackend.MonoGame` on the viewer thread. Each host
finishes the frame, signals the switch, and releases UI/renderer resources before
its window/device is destroyed. The caller then initializes the requested host.
Each host captures its client-area position and size before destroying the window.
The replacement uses those bounds; MonoGame restores position in `LoadContent`,
after it creates its final SDL window. BRUTAL restores position before showing
its window. Coordinates can be negative for monitors left of the primary display;
framebuffer pixel dimensions are kept separate from window coordinates.
A normal window close or Escape ends the application without reopening a window.

Both `Chippy.Backends.Brutal.BrutalViewer` and `Chippy.Backends.MonoGame.MonoGameViewer`
implement `Chippy.Input.IInputSystem`.
They translate native keys and supply window focus; MonoGame snapshots its keyboard
in `Update`, and BRUTAL polls GLFW events before updating the viewer. The shared
`ModelViewer.Update` owns arrow-key orbit, main/keypad zoom, Escape-to-close,
focus/UI capture handling, delta clamping, and automatic orbit. It returns `false`
when the host should finish its UI frame and close. Automatic orbit continues
while keyboard controls are blocked. Input behavior stays consistent across
renderer switches, and a test input provider can be injected without opening a window.

BRUTAL uses GLFW; MonoGame uses its `Game` loop. Both use the same `ModelViewer`
and ImGui panel. Each backend's renderer, window/UI host, GPU resources, and
key conversions live together in `Backends/Brutal` or `Backends/MonoGame`.
Namespaces follow the folder structure.
No DI container is needed. A future ECS can depend on `IRenderSystem` and call
`DrawMesh` directly without using the viewer.

Pass separate view and projection matrices to `BeginFrame`. Use the normal
`System.Numerics` row-vector convention and a `[0, 1]` projection depth range;
BRUTAL converts depth for OpenGL internally. Begin a frame before drawing meshes.
Window clearing, viewport changes, presentation, native input polling, and UI
rendering belong to the host; keyboard control behavior lives in the shared viewer.
Mesh release/disposal only affects that render system's GPU cache.

## Project structure

- `Models`: shared mesh data, bounds, and transforms.
- `Backends/Brutal`: BRUTAL rendering, window/input/UI integration, GPU meshes, key conversions, and shaders.
- `Backends/MonoGame`: MonoGame rendering, window/input/UI integration, GPU meshes, and key conversions.
- `Input`: shared `IInputSystem`, `KeyCode`, and `ImGuiKeyExtensions`.
- `Rendering`: shared `IRenderSystem` and camera.
- `Viewer`: shared scene controls, state, menu contents, renderer selection, and rendering checks.

`KeyCode` covers the shared keyboard set: A–Z, top-row digits, F1–F24,
navigation/editing keys, punctuation, locks, left/right modifiers, menu, and keypad
digits/operators. `None` is the default and never reads a pressed key.
`ToGlfw()`, `ToMonoGame()`, and `ToImGui()` live in `Chippy.Backends.Brutal`,
`Chippy.Backends.MonoGame`, and `Chippy.Input` respectively. They convert explicitly,
with contiguous ranges for letters, numbers, function keys, and keypad digits. Invalid enum values throw.
MonoGame's ImGui keyboard table is generated once from these extensions and the
shared enum, including individual modifier keys. BRUTAL's GLFW ImGui backend
handles its native keyboard events directly.

Key names follow a US layout and represent key states rather than typed text.
`Backslash` maps to MonoGame's `OemPipe`; `NonUsBackslash` maps to
`OemBackslash`/GLFW `World2`. MonoGame combines keypad Enter with Return, so
`Return` checks both Enter keys on BRUTAL too. Distinct keypad Enter/Equals,
GLFW World1/F25, and MonoGame-only media/browser/IME keys are outside the shared
set. Use the UI platform's text-input events for characters. Native definitions:
[GLFW key tokens](https://www.glfw.org/docs/latest/group__keys.html) and
[MonoGame Keys](https://docs.monogame.net/api/Microsoft.Xna.Framework.Input.Keys.html).

## Format support

The viewer supports static triangle meshes in `.gltf` and `.glb`, scene hierarchies,
node transforms, interleaved float positions/normals, unsigned 8/16/32-bit indices,
and material base colors. Missing normals are generated. The included bakery is
an original sample with an embedded buffer, so it needs no external assets.

Textures, transparency, vertex colors, animation, skins, morph targets, sparse
accessors, compressed geometry, and required extensions are outside this first viewer's scope.
Unsupported geometry features fail with a message; materials display their base color without textures.

Run a three-frame rendering check for either backend. Each checks model output,
ImGui rendering, and GPU mesh sharing, then closes its window:

```powershell
dotnet run -- --renderer=brutal --model-demo --smoke-test
dotnet run -- --renderer=monogame --model-demo --smoke-test
dotnet run -- "C:\Models\building.glb" --renderer=monogame --smoke-test
```

`Examples/Ecs` and `Examples/Common` contain reference ECS economic simulations.
They are excluded from Chippy's build and do not require installing the ECS packages.

Built with [MonoGame](https://monogame.net/) and [BRUTAL](https://github.com/ahwootech/BRUTAL).
BRUTAL is Copyright (c) 2024 Ahwoo Limited.
