using System.Numerics;
using Arch.Core;
using Chippy.Audio;
using Chippy.Backends.Brutal;
using Chippy.Backends.MonoGame;
using Chippy.Components;
using Chippy.Demos;
using Chippy.Models;
using Chippy.Viewer;

try
{
    var backend = args.FirstOrDefault(arg => arg.StartsWith("--renderer="))?["--renderer=".Length..] ?? "brutal";
    if (backend is not ("brutal" or "monogame"))
        throw new ArgumentException($"Unknown renderer '{backend}'. Use --renderer=brutal or --renderer=monogame.");
    var renderer = backend == "monogame" ? RenderBackend.MonoGame : RenderBackend.Brutal;
    var smokeTest = args.Contains("--smoke-test");
    var publish = !args.Contains("--no-publish-render-data");
    var preemptive = args.Contains("--preemptive-ticking");
    if (preemptive && !publish)
        throw new ArgumentException("Preemptive ticking requires render data publishing.");
    using var world = World.Create();
    var modelPath = args.FirstOrDefault(arg => !arg.StartsWith("--"));
    ViewerState state;
    string name;
    if (modelPath is not null || args.Contains("--model-demo"))
    {
        modelPath ??= Path.Combine(AppContext.BaseDirectory, "Assets", "Bakery.gltf");
        var model = Mesh.Load(modelPath);
        Console.WriteLine($"Loaded {Path.GetFileName(modelPath)}: {model.SubMeshes.Count} primitives, {model.TriangleCount} triangles.");
        Entity selected;
        if (args.Contains("--model-demo"))
        {
            var parent = world.Create(new Name("Models"), new Transform());
            selected = world.Create(new Name("Bakery A"), (model.Normalization * Matrix4x4.CreateScale(0.6f)
                * Matrix4x4.CreateTranslation(-0.85f, 0, 0)).ToTransform(parent), new MeshRenderer(model));
            world.Create(new Name("Bakery B"), (model.Normalization * Matrix4x4.CreateScale(0.6f)
                * Matrix4x4.CreateRotationY(0.4f) * Matrix4x4.CreateTranslation(0.85f, 0, 0)).ToTransform(parent),
                new MeshRenderer(model) { Tint = new(0.6f, 0.8f, 1, 1) });
        }
        else
            selected = world.Create(new Name(Path.GetFileName(modelPath)), model.Normalization.ToTransform(), new MeshRenderer(model));
        state = new(world, renderer) { SelectedEntity = selected };
        world.Create(new Name("Key light"), new Transform
        {
            LocalRotation = Quaternion.CreateFromYawPitchRoll(0.7f, -0.8f, 0)
        }, new Light { Type = LightType.Directional, Intensity = 0.85f });
        name = Path.GetFileName(modelPath);
    }
    else
    {
        state = SolarSystemDemo.Create(world, renderer);
        name = "Solar system · 1 unit = 1,000 km · 1 second = 1 hour";
    }
    using var audio = new AudioSystem();
    state.Audio = audio;
    state.PublishRenderData = publish;
    state.PreemptiveTicking = preemptive;
    if (!audio.IsAvailable) Console.WriteLine($"Audio unavailable: {audio.Error}");
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
        if (!switchRequested) break;
    }
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
