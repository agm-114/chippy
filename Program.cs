using System.Numerics;
using Chippy.Backends.Brutal;
using Chippy.Backends.MonoGame;
using Chippy.Models;
using Chippy.Viewer;

try
{
    var backend = args.FirstOrDefault(arg => arg.StartsWith("--renderer="))?["--renderer=".Length..] ?? "brutal";
    if (backend is not ("brutal" or "monogame"))
        throw new ArgumentException($"Unknown renderer '{backend}'. Use --renderer=brutal or --renderer=monogame.");
    var modelPath = args.FirstOrDefault(arg => !arg.StartsWith("--"))
        ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Bakery.gltf");
    var model = Mesh.Load(modelPath);
    Console.WriteLine($"Loaded {Path.GetFileName(modelPath)}: {model.SubMeshes.Count} primitives, " +
        $"{model.TriangleCount} triangles.");
    MeshInstance[] instances = args.Contains("--model-demo")
        ? [
            new(model) { Transform = Transform.FromMatrix(model.Normalization * Matrix4x4.CreateScale(0.6f)
                * Matrix4x4.CreateTranslation(-0.85f, 0, 0)) },
            new(model) { Transform = Transform.FromMatrix(model.Normalization * Matrix4x4.CreateScale(0.6f)
                * Matrix4x4.CreateRotationY(0.4f) * Matrix4x4.CreateTranslation(0.85f, 0, 0)), Tint = new(0.6f, 0.8f, 1, 1) }
        ]
        : [new(model) { Transform = Transform.FromMatrix(model.Normalization) }];
    var name = Path.GetFileName(modelPath);
    var smokeTest = args.Contains("--smoke-test");
    var state = new ViewerState(instances, backend == "monogame" ? RenderBackend.MonoGame : RenderBackend.Brutal);
    while (true)
    {
        bool switchRequested;
        if (state.Renderer == RenderBackend.MonoGame)
        {
            using var game = new MonoGameViewer(device => new MonoGameRenderSystem(device), state, name, smokeTest);
            game.Run();
            switchRequested = game.SwitchRequested;
        }
        else
            switchRequested = BrutalViewer.Run(() => new BrutalRenderSystem(), state, name, smokeTest);
        if (!switchRequested)
            break;
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
