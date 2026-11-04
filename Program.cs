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
    var backend = args.FirstOrDefault(arg => arg.StartsWith("--renderer="))?["--renderer=".Length..] ?? "brutal";
    if (backend is not ("brutal" or "monogame"))
        throw new ArgumentException($"Unknown renderer '{backend}'. Use --renderer=brutal or --renderer=monogame.");
    var renderer = backend == "monogame" ? RenderBackend.MonoGame : RenderBackend.Brutal;
    var smokeTest = args.Contains("--smoke-test");
    var publish = !args.Contains("--no-publish-render-data");
    var vsync = args.Contains("--vsync");
    var preemptive = args.Contains("--preemptive-ticking");
    var timelinePath = args.FirstOrDefault(arg => arg.StartsWith("--timeline="))?["--timeline=".Length..];
    var timelineDuration = args.FirstOrDefault(arg => arg.StartsWith("--timeline-seconds="))?["--timeline-seconds=".Length..];
    var speedArgument = args.FirstOrDefault(arg => arg.StartsWith("--speed="))?["--speed=".Length..];
    var tickDurationArgument = args.FirstOrDefault(arg => arg.StartsWith("--tick-ms="))?["--tick-ms=".Length..];
    var frameRateArgument = args.FirstOrDefault(arg => arg.StartsWith("--fps="))?["--fps=".Length..];
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
        name = "Solar system";
    }
    using var audio = new AudioSystem();
    state.Audio = audio;
    state.PublishRenderData = publish;
    state.VSync = vsync;
    state.PreemptiveTicking = preemptive;
    state.TargetFrameRate = frameRate;
    if (initialSpeed is { } multiplier) state.Simulation.Speed = multiplier;
    state.Simulation.MinimumTickMilliseconds = tickMilliseconds;
    state.ShowProfiler = args.Contains("--profiler");
    using var timeline = new TimelineCapture(timelinePath, timelineSeconds);
    timeline.SetLive(state.ShowProfiler);
    state.Timeline = timeline;
    state.Simulation.Timeline = timeline;
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
