using System.Globalization;
using System.Numerics;
using Arch.Core;
using Chippy.Audio;
using Chippy.Backends.Brutal;
using Chippy.Backends.MonoGame;
using Chippy.Components;
using Chippy.Demos;
using Chippy.Diagnostics;
using Chippy.Models;
using Chippy.Viewer;

try
{
    string? Option(string prefix) => args.FirstOrDefault(arg => arg.StartsWith(prefix))?[prefix.Length..];

    var backend = Option("--renderer=") ?? "brutal";
    if (backend is not ("brutal" or "monogame"))
        throw new ArgumentException($"Unknown renderer '{backend}'. Use --renderer=brutal or --renderer=monogame.");
    var renderer = backend == "monogame" ? RenderBackend.MonoGame : RenderBackend.Brutal;
    var smokeTest = args.Contains("--smoke-test");
    var publish = !args.Contains("--no-publish-render-data");
    var vsync = args.Contains("--vsync");
    var preemptive = args.Contains("--preemptive-ticking");
    var timelinePath = Option("--timeline=");
    var timelineDuration = Option("--timeline-seconds=");
    var speedArgument = Option("--speed=");
    var tickDurationArgument = Option("--tick-ms=");
    var frameRateArgument = Option("--fps=");
    var frameRate = 60;
    if (frameRateArgument is not null && (!int.TryParse(frameRateArgument, CultureInfo.InvariantCulture, out frameRate)
        || frameRate is < 15 or > ViewerState.MaxFrameRate))
        throw new ArgumentException($"Use --fps with a frame target from 15 through {ViewerState.MaxFrameRate}.");
    var tickMilliseconds = 0.0;
    if (tickDurationArgument is not null && (!double.TryParse(tickDurationArgument, CultureInfo.InvariantCulture, out tickMilliseconds)
        || !double.IsFinite(tickMilliseconds) || tickMilliseconds is < 0 or > 100))
        throw new ArgumentException("Use --tick-ms with a minimum duration from 0 through 100 ms.");
    double? initialSpeed = null;
    if (speedArgument is not null)
    {
        if (!double.TryParse(speedArgument, CultureInfo.InvariantCulture, out var speed)
            || !double.IsFinite(speed) || speed < 0 || speed > ViewerState.MaxSpeedMultiplier)
            throw new ArgumentException($"Use --speed with a multiplier from 0 through {ViewerState.MaxSpeedMultiplier}.");
        initialSpeed = speed;
    }
    double? timelineSeconds = null;
    if (timelineDuration is not null)
    {
        if (string.IsNullOrWhiteSpace(timelinePath)
            || !double.TryParse(timelineDuration, CultureInfo.InvariantCulture, out var seconds)
            || !double.IsFinite(seconds) || seconds <= 0)
            throw new ArgumentException("Use --timeline-seconds with --timeline=<file> and a positive duration.");
        timelineSeconds = seconds;
    }
    if (timelinePath is not null && string.IsNullOrWhiteSpace(timelinePath))
        throw new ArgumentException("Use --timeline=<file> to record a timeline.");
    if (preemptive && !publish)
        throw new ArgumentException("Preemptive ticking requires render data publishing.");
    if (vsync && !publish)
        throw new ArgumentException("VSync requires render data publishing.");
    const int solarScene = 0, meshScene = 1, modelScene = 2;
    var startInMeshBuilder = args.Contains("--mesh-builder") || args.Contains("--ship-builder");
    var modelDemo = args.Contains("--model-demo");
    var modelPath = args.FirstOrDefault(arg => !arg.StartsWith("--"));
    if (modelDemo)
        modelPath ??= Path.Combine(AppContext.BaseDirectory, "Assets", "Bakery.gltf");
    var workingShipPath = MeshBuilder.FindWorkingShipPath();
    var solarShipPath = workingShipPath ?? Path.Combine(AppContext.BaseDirectory, MeshBuilder.WorkingShipRelativePath);
    var keepShipParts = args.Contains("--ship-parts");
    var startInSolarSystem = args.Contains("--solar-system") || keepShipParts;
    var resumeWorkingShip = !smokeTest && !startInSolarSystem && modelPath is null && File.Exists(workingShipPath);
    string[] sceneNames = modelPath is null
        ? ["Solar System", "Mesh Builder"]
        : ["Solar System", "Mesh Builder", Path.GetFileName(modelPath)];
    using var solarWorld = World.Create();
    using var meshWorld = World.Create();
    using var modelWorld = modelPath is null ? null : World.Create();
    using var audio = new AudioSystem();
    using var timeline = new TimelineCapture(timelinePath, timelineSeconds);
    var showProfiler = args.Contains("--profiler");
    timeline.SetLive(showProfiler);
    if (!audio.IsAvailable) Console.WriteLine($"Audio unavailable: {audio.Error}");

    var scenes = new ViewerState?[sceneNames.Length];
    var initialScene = startInMeshBuilder || resumeWorkingShip
        ? meshScene
        : startInSolarSystem || modelPath is null ? solarScene : modelScene;
    var state = GetScene(initialScene);
    while (true)
    {
        var name = sceneNames[state.SceneIndex];
        bool switchRequested;
        if (state.Renderer == RenderBackend.MonoGame)
        {
            using var game = new MonoGameViewer(device => new MonoGameRenderSystem(device), state, name, smokeTest);
            game.Run();
            switchRequested = game.SwitchRequested;
        }
        else
            switchRequested = BrutalViewer.Run(() => new BrutalRenderSystem(), state, name, smokeTest);
        state.MeshBuilder?.SaveWorkingShip(force: true);
        if (!switchRequested) break;
        if (state.RequestedScene is not { } requested) continue;

        var previous = state;
        previous.RequestedScene = null;
        state = GetScene(requested);
        state.WindowPlacement = previous.WindowPlacement;
        state.Renderer = previous.Renderer;
        state.PublishRenderData = previous.PublishRenderData;
        state.VSync = previous.VSync;
        state.PreemptiveTicking = previous.PreemptiveTicking;
        state.TargetFrameRate = previous.TargetFrameRate;
        state.ShowProfiler = previous.ShowProfiler;
    }
    return 0;

    ViewerState GetScene(int index)
    {
        if (scenes[index] is { } existing) return existing;
        var scene = index switch
        {
            solarScene => SolarSystemDemo.Create(solarWorld, renderer, solarShipPath, keepShipParts),
            meshScene => new MeshBuilder(meshWorld, renderer).State,
            modelScene => LoadModelScene(modelWorld!, renderer, modelPath!, modelDemo),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };
        if (!smokeTest) scene.MeshBuilder?.OpenWorkingShip(workingShipPath);
        scene.SceneIndex = index;
        scene.SceneNames = sceneNames;
        scene.Audio = audio;
        scene.PublishRenderData = publish;
        scene.VSync = vsync;
        scene.PreemptiveTicking = preemptive;
        scene.TargetFrameRate = frameRate;
        if (initialSpeed is { } multiplier) scene.Simulation.Speed = multiplier;
        scene.Simulation.MinimumTickMilliseconds = tickMilliseconds;
        scene.ShowProfiler = showProfiler;
        scene.Timeline = timeline;
        scene.Simulation.Timeline = timeline;
        return scenes[index] = scene;
    }
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static ViewerState LoadModelScene(World world, RenderBackend renderer, string path, bool demo)
{
    var model = Mesh.Load(path);
    var name = Path.GetFileName(path);
    Console.WriteLine($"Loaded {name}: {model.SubMeshes.Count} primitives, {model.TriangleCount} triangles.");
    Entity selected;
    if (demo)
    {
        var parent = world.Create(new Name("Models"), new Transform());
        selected = world.Create(new Name("Bakery A"), (model.Normalization * Matrix4x4.CreateScale(0.6f)
            * Matrix4x4.CreateTranslation(-0.85f, 0, 0)).ToTransform(parent), new MeshRenderer(model));
        world.Create(new Name("Bakery B"), (model.Normalization * Matrix4x4.CreateScale(0.6f)
            * Matrix4x4.CreateRotationY(0.4f) * Matrix4x4.CreateTranslation(0.85f, 0, 0)).ToTransform(parent),
            new MeshRenderer(model) { Tint = new(0.6f, 0.8f, 1, 1) });
    }
    else
        selected = world.Create(new Name(name), model.Normalization.ToTransform(), new MeshRenderer(model));
    world.Create(new Name("Key light"), new Transform
    {
        LocalRotation = Quaternion.CreateFromYawPitchRoll(0.7f, -0.8f, 0)
    }, new Light { Type = LightType.Directional, Intensity = 0.85f });
    return new(world, renderer) { SelectedEntity = selected };
}
