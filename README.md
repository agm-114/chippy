# Chippy

Chippy is a C# simulation engine built on the [Arch ECS](https://github.com/genaray/Arch). It supports two rendering backends, [BRUTAL](https://github.com/ahwootech/BRUTAL) and [MonoGame](https://monogame.net/), which you can switch between at runtime.






## Architecture

- **World and systems.** Arch stores entities and components such as transforms, meshes, and lights. The simulation scheduler runs systems on fixed ticks.
- **Rendering.** A shared scene renderer queries Arch and sends draw calls to the active backend. Switching backends keeps the simulation world and viewer state.
- **Published mode (default).** A dedicated simulation thread publishes a smaller Arch world containing the data needed for drawing. The renderer can keep drawing from the latest published world during a tick. ImGui edits the simulation world between ticks.
- **Live mode.** Simulation, ImGui, and rendering share the window thread. Start with `--no-publish-render-data` to use this mode.

See the [timeline and profiling guide](docs/architecture/README.md) for more on frame and tick timing.
