using System.Numerics;
using Arch.Core;
using Chippy.Backends.Brutal;
using Chippy.Backends.MonoGame;
using Chippy.Components;
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
    using var world = World.Create();
    Entity selected;
    if (args.Contains("--model-demo"))
    {
        var parent = world.Create(new Transform());
        selected = world.Create((model.Normalization * Matrix4x4.CreateScale(0.6f)
            * Matrix4x4.CreateTranslation(-0.85f, 0, 0)).ToTransform(parent), new MeshRenderer(model));
        world.Create((model.Normalization * Matrix4x4.CreateScale(0.6f)
            * Matrix4x4.CreateRotationY(0.4f) * Matrix4x4.CreateTranslation(0.85f, 0, 0)).ToTransform(parent),
            new MeshRenderer(model) { Tint = new(0.6f, 0.8f, 1, 1) });
    }
    else
        selected = world.Create(model.Normalization.ToTransform(), new MeshRenderer(model));
    var name = Path.GetFileName(modelPath);
    var smokeTest = args.Contains("--smoke-test");
    var publish = !args.Contains("--no-publish-render-data");
    var preemptive = args.Contains("--preemptive-ticking");
    if (preemptive && !publish)
        throw new ArgumentException("Preemptive ticking requires render data publishing.");
    var state = new ViewerState(world, backend == "monogame" ? RenderBackend.MonoGame : RenderBackend.Brutal)
    {
        SelectedEntity = selected,
        PublishRenderData = publish,
        PreemptiveTicking = preemptive
    };
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
