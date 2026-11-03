using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Chippy.Backends.Brutal;
using Chippy.Backends.MonoGame;
using Chippy.Components;
using Chippy.Input;
using Chippy.Models;
using Chippy.Rendering;
using Chippy.Simulation;
using Chippy.Viewer;

if (args.Contains("--native"))
{
    NativeBackends();
    return;
}

Scheduler();
PublishedWorlds();
WorkerIsolation();
EarlyTicks();
LiveModeAndPacing();
WorkerFailure();
ShutdownWaits();
QueuedUiInput();
Console.WriteLine("All scheduling, publication, ownership, and pacing checks passed.");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}

static void PumpUntil(ModelViewer viewer, Func<bool> complete)
{
    var timeout = Stopwatch.StartNew();
    while (!complete())
    {
        if (timeout.Elapsed.TotalSeconds > 5) throw new TimeoutException("Simulation did not complete.");
        viewer.Update();
        Thread.Sleep(1);
    }
}

static Mesh Triangle() => new([new SubMesh(
    [new(new(-1, 0, 0), Vector3.UnitY), new(new(1, 0, 0), Vector3.UnitY), new(new(0, 1, 0), Vector3.UnitY)],
    [0, 1, 2], Vector4.One)]);

static void Scheduler()
{
    var scheduler = new SimulationScheduler(10, 2);
    List<string> calls = [];
    scheduler.Add(new Callback(dt => calls.Add($"a:{dt:F1}")));
    scheduler.Add(new Callback(dt => calls.Add($"b:{dt:F1}")), every: 2);
    Check(scheduler.Advance(0.35) == 2 && scheduler.Tick == 2, "Catch-up limit failed.");
    Check(scheduler.Advance(0) == 1 && scheduler.Tick == 3, "Tick debt was lost.");
    Check(calls.SequenceEqual(["a:0.1", "a:0.1", "b:0.2", "a:0.1"]), "System order/cadence failed.");
    scheduler.Paused = true;
    Check(scheduler.Advance(10) == 0, "Paused wall time advanced the simulation.");
    scheduler.Step();
    scheduler.Paused = false;
    scheduler.Speed = 0;
    Check(scheduler.Advance(10) == 0, "Zero speed advanced the simulation.");
    scheduler.Speed = 2;
    Check(scheduler.Advance(0.025) == 1, "Fractional debt/speed was not retained.");
    Throws<ArgumentOutOfRangeException>(() => scheduler.Speed = double.NaN);
    Throws<ArgumentOutOfRangeException>(() => scheduler.Advance(-1));
    var recursive = new SimulationScheduler();
    recursive.Add(new Callback(_ => recursive.Step()));
    Throws<InvalidOperationException>(() => recursive.Step());
}

static void PublishedWorlds()
{
    using var world = World.Create();
    var mesh = Triangle();
    var parent = world.Create(new Transform { LocalPosition = new(4, 0, 0) });
    var child = world.Create(new Transform { Parent = parent, LocalPosition = new(2, 0, 0) },
        new MeshRenderer(mesh), new SimulationOnly());
    world.Create(new Transform(), new MeshRenderer(mesh) { Enabled = false });
    world.Create(new Transform(), new MeshRenderer(null));
    using var publication = new RenderWorld(world);
    Check(!publication.HasPrepared, "An empty render world claimed to be prepared.");
    Throws<InvalidOperationException>(publication.Publish);
    publication.Prepare(0);
    Check(publication.HasPrepared, "Preparation did not make the world ready for publication.");
    publication.Publish();
    Check(!publication.HasPrepared, "Publication did not consume the prepared world.");
    var held = publication.World;
    var query = new QueryDescription().WithAll<Transform, MeshRenderer, SourceEntity>();
    held.Query(in query, (Entity entity, ref Transform transform, ref MeshRenderer renderer, ref SourceEntity source) =>
    {
        Check(transform.LocalToWorldMatrix.M41 == 6 && transform.Parent is null, "Published hierarchy was not resolved/detached.");
        Check(source.Entity == child && entity.WorldId != child.WorldId, "Source entity was not preserved in the new world.");
        Check(!entity.Has<SimulationOnly>(), "Simulation-only data was copied into the render world.");
        Check(ReferenceEquals(renderer.SharedMesh, mesh), "Mesh geometry was copied.");
    });
    Check(held.Size == 1 && held.CountEntities(in query) == 1, "The world was not trimmed to renderable entities.");
    var renderer = new TestRenderer();
    var meshes = new MeshRenderSystem(renderer);
    meshes.Draw(held);
    Check(renderer.Positions.SequenceEqual([6f]), "The ECS renderer could not draw the copied world.");
    // Preparing on a worker never modifies the world currently being drawn.
    Task.Run(() =>
    {
        for (var i = 1; i <= 500; i++)
        {
            child.Get<Transform>().LocalPosition.X = i;
            publication.Prepare(i);
        }
        world.Destroy(child);
        publication.Prepare(501);
    }).GetAwaiter().GetResult();
    renderer.Positions.Clear();
    meshes.Draw(held);
    Check(publication.Tick == 0 && renderer.Positions.SequenceEqual([6f]), "Preparation modified the published world.");
    publication.Publish();
    Check(publication.Tick == 501 && publication.World.Size == 0, "Publication/deletion was lost.");
    publication.Prepare(502);
    publication.Publish();
    Check(ReferenceEquals(publication.World, held), "The pair of render worlds was not reused.");
    var replacement = world.Create(new Transform { LocalPosition = new(9, 0, 0) }, new MeshRenderer(mesh));
    publication.Prepare(503);
    publication.Publish();
    renderer.Positions.Clear();
    meshes.Draw(publication.World);
    Check(renderer.Positions.SequenceEqual([9f]), "A reused render world's ECS query returned stale data.");
    publication.World.Query(in query, (ref Transform _, ref MeshRenderer _, ref SourceEntity source) =>
        Check(source.Entity == replacement && source.Entity != child, "Source entity generations were lost after deletion/recreation."));
}

static void WorkerIsolation()
{
    using var world = World.Create();
    var entity = world.Create(new Transform(), new MeshRenderer(Triangle()));
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var windowThread = Environment.CurrentManagedThreadId;
    var workerThread = windowThread;
    var state = new ViewerState(world);
    state.Simulation.Add(new Callback(_ =>
    {
        workerThread = Environment.CurrentManagedThreadId;
        entity.Get<Transform>().LocalPosition.X = 10;
        world.Create(new Transform()); // Structural changes must not race renderer queries.
        entered.Set();
        if (!release.Wait(5000)) throw new TimeoutException("Test tick was not released.");
    }));
    var clock = new TestClock();
    var renderer = new TestRenderer();
    using var viewer = new ModelViewer(renderer, new TestInput(), state, clock);
    Check(!ReferenceEquals(viewer.RenderWorld, world), "Publishing did not use a separate ECS world.");
    var uiCalls = 0;
    bool Ui() { uiCalls++; return false; }
    viewer.Update(updateUi: Ui);
    viewer.Draw(1);
    clock.Advance(0.1);
    viewer.Update(updateUi: Ui);
    Check(entered.Wait(5000), "Worker did not start.");
    try
    {
        var before = uiCalls;
        Check(!viewer.TryEditWorld(_ => throw new Exception("Unsafe edit ran.")), "World editing was permitted during a tick.");
        viewer.Update(uiInputPending: true, updateUi: Ui);
        viewer.Draw(1);
        Check(renderer.Positions.Single() == 0 && uiCalls == before, "Rendering or ImGui read a world during mutation.");
        Check(workerThread != windowThread, "Simulation blocked the graphics thread.");
    }
    finally { release.Set(); }
    Check(SpinWait.SpinUntil(() => !viewer.SimulationInProgress, 5000), "Released worker did not finish.");
    Check(viewer.GetUpdateDelay(rendering: false) == TimeSpan.Zero, "A completed tick was left sleeping before publication.");
    PumpUntil(viewer, () => viewer.PublishedTick == 1);
    state.Simulation.Paused = true;
    viewer.Update(uiInputPending: true, updateUi: () =>
    {
        entity.Get<Transform>().LocalPosition.X = 12;
        return true;
    });
    viewer.Draw(1);
    Check(renderer.Positions.Single() == 12, "A safe ImGui edit did not reach the published data.");
    Check(viewer.TryEditWorld(_ => entity.Get<Transform>().LocalPosition.X = 14), "Idle world editing failed.");
    viewer.Update();
    viewer.Draw(1);
    Check(renderer.Positions.Single() == 14, "External world edits were not published.");
    Check(renderer.Threads.All(id => id == windowThread), "GPU calls left the window thread.");
}

static void EarlyTicks()
{
    using var world = World.Create();
    var entity = world.Create(new Transform(), new MeshRenderer(Triangle()));
    var state = new ViewerState(world) { PreemptiveTicking = true };
    state.Simulation.Add(new Callback(_ => entity.Get<Transform>().LocalPosition.X++));
    var clock = new TestClock();
    var renderer = new TestRenderer();
    using var viewer = new ModelViewer(renderer, new TestInput(), state, clock);
    viewer.Update();
    PumpUntil(viewer, () => viewer.HasPreparedTick);
    Check(state.Simulation.Tick == 1 && viewer.PublishedTick == 0, "Early work published before its target.");
    viewer.Update(uiInputPending: true, updateUi: () =>
    {
        entity.Get<Transform>().LocalPosition.X = 7;
        return true;
    });
    viewer.Draw(1);
    Check(renderer.Positions.Single() == 0 && viewer.PublishedTick == 0, "An edit leaked a held tick early.");
    for (var i = 0; i < 5; i++) viewer.Update();
    Check(state.Simulation.Tick == 1, "Simulation ran more than one unpublished tick ahead.");
    // Turning off publishing drains the held tick at its target, then returns to live drawing.
    var publishedWorldId = viewer.RenderWorld.Id;
    state.PublishRenderData = false;
    viewer.Update();
    Check(viewer.UsesPublishedData, "Switching mode exposed early state before its target.");
    clock.Advance(0.1);
    viewer.Update();
    viewer.Draw(1);
    Check(!viewer.UsesPublishedData && viewer.PublishedTick is null && renderer.Positions.Single() == 7,
        "Held edit or publication deadline was lost when changing mode.");
    Check(World.Worlds[publishedWorldId] is null, "Disabling publication retained a snapshot world.");
    state.PublishRenderData = true;
    viewer.Update();
    PumpUntil(viewer, () => viewer.HasPreparedTick);
    Check(state.Simulation.Tick == 2 && viewer.PublishedTick == 1, "Re-enabling publishing did not resume early computation.");
    // Pause commits the already-computed tick. Step must not execute that tick again.
    viewer.Update(uiInputPending: true, updateUi: () =>
    {
        state.Simulation.Paused = true;
        viewer.StepSimulation();
        return false;
    });
    Check(state.Simulation.Tick == 2 && viewer.PublishedTick == 2, "Manual step duplicated/lost a prepared tick.");
    viewer.StepSimulation();
    viewer.Update();
    Check(state.Simulation.Tick == 3 && viewer.PublishedTick == 3, "Paused stepping failed.");
}

static void LiveModeAndPacing()
{
    using var world = World.Create();
    world.Create(new Transform(), new MeshRenderer(Triangle()));
    var windowThread = Environment.CurrentManagedThreadId;
    var state = new ViewerState(world) { PublishRenderData = false, PreemptiveTicking = true };
    var tickThread = 0;
    state.Simulation.Add(new Callback(_ => tickThread = Environment.CurrentManagedThreadId));
    var clock = new TestClock();
    var worldCount = World.Worlds.Count(world => world is not null);
    using var viewer = new ModelViewer(new TestRenderer(), new TestInput(), state, clock);
    Check(ReferenceEquals(viewer.RenderWorld, world), "Live rendering did not use the simulation world directly.");
    Check(World.Worlds.Count(world => world is not null) == worldCount, "Live mode allocated unused snapshot worlds.");
    viewer.Update();
    Check(state.Simulation.Tick == 0, "Preemptive ticking ran without publishing.");
    viewer.Draw(1);
    clock.Advance(0.099);
    viewer.Update();
    Check(state.Simulation.Tick == 0, "Fixed tick ran before its target.");
    clock.Advance(0.001);
    viewer.Update();
    Check(state.Simulation.Tick == 1 && tickThread == windowThread, "Live mode must run world work on the graphics thread.");
    viewer.Draw(1);
    Check(!viewer.ShouldRender, "Missed frames caused catch-up renders.");
    viewer.Update(uiInputPending: true, updateUi: () => false);
    Check(viewer.ShouldRender, "Processed UI input did not request an immediate redraw.");
    viewer.Draw(1);
    viewer.Update();
    Check(!viewer.ShouldRender, "A completed UI redraw was repeated.");
    state.Simulation.Paused = true;
    clock.Advance(2);
    viewer.Update();
    state.Simulation.Paused = false;
    viewer.Update();
    Check(state.Simulation.Tick == 1, "Paused wall time became tick debt.");
}

static void WorkerFailure()
{
    using var world = World.Create();
    var state = new ViewerState(world) { PreemptiveTicking = true };
    state.Simulation.Add(new Callback(_ => throw new ArithmeticException("Expected test failure.")));
    var viewer = new ModelViewer(new TestRenderer(), new TestInput(), state, new TestClock());
    viewer.Update();
    Check(SpinWait.SpinUntil(() => !viewer.SimulationInProgress, 5000), "Faulting worker did not finish.");
    Throws<ArithmeticException>(() => viewer.Update());
    viewer.Dispose();
}

static void NativeBackends()
{
    using var world = World.Create();
    var mesh = Mesh.Load(Path.Combine(AppContext.BaseDirectory, "Assets", "Bakery.gltf"));
    world.Create(mesh.Normalization.ToTransform(), new MeshRenderer(mesh));
    var state = new ViewerState(world) { PreemptiveTicking = true };
    var windowThread = Environment.CurrentManagedThreadId;
    var tickThread = windowThread;
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    state.Simulation.Add(new Callback(_ =>
    {
        tickThread = Environment.CurrentManagedThreadId;
        entered.Set();
        if (!release.Wait(5000)) throw new TimeoutException("Rendering did not release the native test tick.");
        world.Create(new Transform());
    }));
    foreach (var backend in new[] { RenderBackend.Brutal, RenderBackend.MonoGame })
    {
        entered.Reset();
        release.Reset();
        var framesDuringTick = 0;
        var frames = 0;
        IRenderSystem Observe(IRenderSystem renderer) => new ObservedRenderer(renderer, () =>
        {
            Check(entered.Wait(5000), "Native tick did not start.");
            if (!release.IsSet) framesDuringTick++;
            if (++frames == 3) release.Set();
        });
        state.Renderer = backend;
        try
        {
            if (backend == RenderBackend.Brutal)
                BrutalViewer.Run(() => Observe(new BrutalRenderSystem()), state, "Blocked tick publication check", smokeTest: true);
            else
            {
                using var game = new MonoGameViewer(device => Observe(new MonoGameRenderSystem(device)), state,
                    "Blocked tick publication check", smokeTest: true);
                game.Run();
            }
        }
        finally { release.Set(); }
        Check(tickThread != windowThread && state.Simulation.Tick >= 1, "Native backend did not run a simulation worker.");
        Check(framesDuringTick == 3, "Native presentation did not continue through a blocked tick.");
    }
    Console.WriteLine("Both native backends rendered completed world/UI data during slow ticks and joined their workers on exit.");
}

static void ShutdownWaits()
{
    using var world = World.Create();
    using var entered = new ManualResetEventSlim();
    using var release = new ManualResetEventSlim();
    var state = new ViewerState(world) { PreemptiveTicking = true };
    state.Simulation.Add(new Callback(_ =>
    {
        entered.Set();
        if (!release.Wait(5000)) throw new TimeoutException("Shutdown test tick was not released.");
        world.Create(new Transform());
    }));
    var viewer = new ModelViewer(new TestRenderer(), new TestInput(), state, new TestClock());
    viewer.Update();
    Check(entered.Wait(5000), "Shutdown test worker did not start.");
    // Release from another thread, because Dispose must join the active world writer.
    var releaser = Task.Run(() => { Thread.Sleep(20); release.Set(); });
    viewer.Dispose();
    releaser.GetAwaiter().GetResult();
    Check(!viewer.SimulationInProgress && state.Simulation.Tick == 1, "Disposal left a world writer running.");
    viewer.Dispose();
    Throws<ObjectDisposedException>(() => viewer.Update());
}

static void QueuedUiInput()
{
    using var world = World.Create();
    var state = new ViewerState(world);
    state.Simulation.Paused = true;
    using var viewer = new ModelViewer(new TestRenderer(), new TestInput(), state, new TestClock());
    using var panel = new ViewerPanel(viewer, "Queued UI input check");
    var io = ImGui.GetIO();
    io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
    io.DisplaySize = new float2(1280, 720);
    io.DeltaTime = 1f / 60;
    panel.BuildFrame();
    io.AddMousePosEvent(600, 600);
    io.AddMouseButtonEvent(0, true);
    io.AddMouseButtonEvent(0, false);
    Check(panel.HasPendingInput, "Test input was not queued.");
    panel.BuildFrame();
    Check(!panel.HasPendingInput && !io.MouseDown[0], "ImGui did not drain a press/release before the next tick.");
}

class Callback(Action<double> update) : ISimulationSystem
{
    public void Update(double deltaTime) => update(deltaTime);
}

record struct SimulationOnly;

class TestClock : TimeProvider
{
    private long timestamp;
    public override long TimestampFrequency => 1_000_000;
    public override long GetTimestamp() => timestamp;
    public void Advance(double seconds) => timestamp += (long)Math.Round(seconds * TimestampFrequency);
}

class TestInput : IInputSystem
{
    public bool IsFocused => true;
    public bool GetKey(KeyCode key) => false;
}

class TestRenderer : IRenderSystem
{
    public List<float> Positions { get; } = [];
    public List<int> Threads { get; } = [];
    public int UploadedMeshCount => 0;
    public void BeginFrame(in Matrix4x4 view, in Matrix4x4 projection, bool wireframe = false) => Positions.Clear();
    public void DrawMesh(in Transform transform, in MeshRenderer meshRenderer)
    {
        Positions.Add(transform.LocalToWorldMatrix.M41);
        Threads.Add(Environment.CurrentManagedThreadId);
    }
    public bool ReleaseMesh(Mesh mesh) => false;
    public void Dispose() { }
}

class ObservedRenderer(IRenderSystem renderer, Action beforeFrame) : IRenderSystem
{
    public int UploadedMeshCount => renderer.UploadedMeshCount;
    public void BeginFrame(in Matrix4x4 view, in Matrix4x4 projection, bool wireframe = false)
    {
        beforeFrame();
        renderer.BeginFrame(in view, in projection, wireframe);
    }
    public void DrawMesh(in Transform transform, in MeshRenderer meshRenderer) => renderer.DrawMesh(in transform, in meshRenderer);
    public bool ReleaseMesh(Mesh mesh) => renderer.ReleaseMesh(mesh);
    public void Dispose() => renderer.Dispose();
}
