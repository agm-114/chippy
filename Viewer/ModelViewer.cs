using System.Numerics;
using Arch.Core;
using Chippy.Input;
using Chippy.Rendering;
using Chippy.Systems;

namespace Chippy.Viewer;

public class ModelViewer : IDisposable
{
    private readonly IRenderSystem renderSystem;
    private readonly IInputSystem input;
    private readonly TransformSystem transforms;
    private readonly MeshRenderSystem meshes;
    private readonly TimeProvider clock;
    private readonly long started;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private RenderWorld? published;
    private Task? simulationWork;
    private double previousTime, previousSimulationTime, nextRenderTime;
    private bool earlyTick, redraw, uiInitialized, worldChanged, disposed;
    public ViewerState State { get; }
    public bool UsesPublishedData => published is not null;
    public bool SimulationInProgress => simulationWork is { IsCompleted: false };
    public long? PublishedTick => published?.Tick;
    public long DisplayedTick { get; private set; }
    public World RenderWorld => published?.World ?? State.World;
    public bool HasPreparedTick => HasPreparedWorld && earlyTick;
    public bool ShouldRender => redraw || Now >= nextRenderTime;
    private bool TickEarly => UsesPublishedData && State.PreemptiveTicking;
    private bool HasPreparedWorld => simulationWork is null && published is { HasPrepared: true };
    private double Now => clock.GetElapsedTime(started).TotalSeconds;

    public ModelViewer(IRenderSystem renderSystem, IInputSystem input, ViewerState state, TimeProvider? clock = null)
    {
        this.renderSystem = renderSystem;
        this.input = input;
        State = state;
        this.clock = clock ?? TimeProvider.System;
        transforms = new(state.World);
        meshes = new(renderSystem);
        UpdatePublication();
        started = this.clock.GetTimestamp();
    }

    public TimeSpan GetUpdateDelay(bool rendering = true)
    {
        var now = Now;
        var tickDelay = simulationWork switch
        {
            { IsCompleted: true } => 0,
            not null => 0.001,
            _ => (HasPreparedTick ? State.Simulation.SecondsUntilPublication : State.Simulation.SecondsUntilNextTick)
                - (now - previousSimulationTime)
        };
        var renderDelay = !rendering ? double.PositiveInfinity : redraw ? 0 : nextRenderTime - now;
        return TimeSpan.FromSeconds(Math.Clamp(Math.Min(tickDelay, renderDelay), 0, 0.01));
    }

    // UI remains on the window thread, with exclusive live-world access between tick tasks.
    // The callback returns true for world edits. Native backends queue input without touching the world.
    public bool Update(bool keyboardCaptured = false, bool uiInputPending = false, Func<bool>? updateUi = null)
    {
        EnsureOwner();
        var now = Now;
        var delta = now - previousTime;
        previousTime = now;
        if (!UpdateCamera(delta, keyboardCaptured)) return false;
        if (SimulationInProgress) return true;
        var completed = simulationWork is not null;
        if (simulationWork is { } work)
        {
            simulationWork = null;
            work.GetAwaiter().GetResult();
        }
        UpdateClock();
        var simulation = State.Simulation;
        var tickRequested = !HasPreparedWorld && simulation.CanTakeTick(TickEarly);
        if (updateUi is not null && (!uiInitialized || uiInputPending || completed || ShouldRender || tickRequested))
        {
            var changed = updateUi();
            worldChanged |= changed;
            uiInitialized = true;
            redraw |= uiInputPending || changed;
            UpdateClock();
        }
        UpdatePublication();
        if (!HasPreparedWorld && simulation.TryTakeTick(TickEarly))
        {
            earlyTick = !simulation.ReservedTickIsDue;
            if (published is { } target)
                simulationWork = Task.Run(() =>
                {
                    simulation.Step();
                    target.Prepare(simulation.Tick);
                });
            else simulation.Step();
        }
        return true;
    }

    private void UpdatePublication()
    {
        var simulation = State.Simulation;
        if (earlyTick && !simulation.IsRunning) simulation.CommitEarlyTick();
        if (published is null && State.PublishRenderData)
        {
            published = new(State.World);
            worldChanged = true;
        }
        if (worldChanged) published?.Prepare(simulation.Tick);
        worldChanged = false;
        if (published is { HasPrepared: true } ready && (!earlyTick || simulation.ReservedTickIsDue))
        {
            ready.Publish();
            earlyTick = false;
        }
        // Finish an already computed tick before returning to direct live-world rendering.
        if (!State.PublishRenderData && !HasPreparedWorld)
        {
            published?.Dispose();
            published = null;
        }
    }

    private void UpdateClock()
    {
        var now = Now;
        State.Simulation.Accumulate(now - previousSimulationTime);
        previousSimulationTime = now;
    }

    public bool TryEditWorld(Action<World> edit)
    {
        EnsureOwner();
        if (simulationWork is not null) return false;
        edit(State.World);
        worldChanged = redraw = true;
        return true;
    }

    public void StepSimulation()
    {
        EnsureOwner();
        if (simulationWork is not null) throw new InvalidOperationException("Wait for the active tick before stepping.");
        if (HasPreparedTick) State.Simulation.CommitEarlyTick();
        else State.Simulation.Step();
        earlyTick = false;
        worldChanged = redraw = true;
    }

    public void Draw(float aspectRatio)
    {
        EnsureOwner();
        var interval = 1.0 / State.TargetFrameRate;
        nextRenderTime = (Math.Floor(Now / interval) + 1) * interval;
        redraw = false;
        var view = Matrix4x4.CreateLookAt(State.Camera.Eye, Vector3.Zero, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, aspectRatio, 0.01f, 100f);
        renderSystem.BeginFrame(view, projection, State.Wireframe);
        if (!UsesPublishedData) transforms.Update();
        meshes.Draw(RenderWorld);
        DisplayedTick = PublishedTick ?? State.Simulation.Tick;
    }

    private bool UpdateCamera(double delta, bool keyboardCaptured)
    {
        var movement = Vector3.Zero;
        if (input.IsFocused && !keyboardCaptured)
        {
            if (input.GetKey(KeyCode.Escape)) return false;
            movement = new(input.GetAxis(KeyCode.LeftArrow, KeyCode.RightArrow),
                input.GetAxis(KeyCode.DownArrow, KeyCode.UpArrow),
                Math.Clamp(input.GetAxis(KeyCode.Equals, KeyCode.Minus)
                    + input.GetAxis(KeyCode.KeypadPlus, KeyCode.KeypadMinus), -1, 1));
        }
        var frameDelta = (float)Math.Clamp(delta, 0, 0.1);
        State.Camera.Move((movement.X + (State.AutoRotate ? 0.35f : 0)) * frameDelta,
            movement.Y * frameDelta, movement.Z * frameDelta);
        return true;
    }

    private void EnsureOwner()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("Drive the viewer and its UI from the window thread.");
    }

    public void Dispose()
    {
        if (disposed) return;
        EnsureOwner();
        // A backend/world cannot be torn down while a tick is still using it.
        try { simulationWork?.GetAwaiter().GetResult(); }
        finally
        {
            simulationWork = null;
            State.Simulation.CommitEarlyTick();
            disposed = true;
            published?.Dispose();
        }
    }
}
