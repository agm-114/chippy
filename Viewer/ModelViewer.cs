using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Chippy.Components;
using Chippy.Input;
using Chippy.Rendering;
using Chippy.Systems;

namespace Chippy.Viewer;

public class ModelViewer : IDisposable
{
    private readonly IRenderSystem renderSystem;
    private readonly IInputSystem input;
    private readonly TransformSystem transforms;
    private readonly SceneRenderSystem scene;
    private readonly TimeProvider clock;
    private readonly PacingWait pacingWait;
    private readonly long started;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private PublishedSimulation? published;
    private double previousTime, previousSimulationTime, nextRenderTime, previousRenderTime;
    private Entity? followedEntity;
    private Vector3 previousFollowPosition;
    internal readonly record struct FrameCamera(Matrix4x4 View, Vector2 ProjectionScale, Vector3 Eye, float Near, float Far);
    private FrameCamera? drawnCamera;
    private readonly Action<World, PrimitiveBatch, Vector2>? drawOverlay;
    private bool redraw, disposed;
    private bool uiRefreshPending = true;
    private bool uiAvailable;
    private bool inputAwaitingUi;
    private long? uiRequestStart, uiRequestRevision, uiRevision;
    private string? uiRequestReason;
    private (RenderBackend Renderer, bool Published, bool Early, bool VSync, bool Paused, double Rate, int Fps, double MinimumMs)? traceSettings;
    private bool middleHeld, dragging, focusHeld;
    private readonly QueryDescription focusQuery = new QueryDescription().WithAll<Transform>();
    public ViewerState State { get; }
    public bool UsesPublishedData => published is not null;
    public bool UsesVSync => UsesPublishedData && State.WantsVSync;
    public bool SimulationInProgress => published?.IsStepping == true;
    public long? PublishedTick => published?.Render.Tick;
    public long DisplayedTick { get; private set; }
    public World RenderWorld => published?.Render.World ?? State.World;
    internal int DrawnMeshAssetCount => scene.DrawnMeshAssetCount;
    public bool HasPreparedTick => published?.HasPreparedTick == true;
    public bool ShouldRender => UsesVSync || redraw || Now >= nextRenderTime;
    private double Now => clock.GetElapsedTime(started).TotalSeconds;

    public ModelViewer(IRenderSystem renderSystem, IInputSystem input, ViewerState state, TimeProvider? clock = null)
    {
        this.renderSystem = renderSystem;
        this.input = input;
        State = state;
        this.clock = clock ?? TimeProvider.System;
        transforms = new(state.World);
        scene = new(renderSystem);
        drawOverlay = state.MeshBuilder is { } builder
            ? (_, batch, size) => builder.DrawSockets(scene, batch, size, drawnCamera!.Value)
            : state.DrawOverlay;
        started = this.clock.GetTimestamp();
        RecordSettings();
        if (state.PublishRenderData) published = new(state, this.clock);
        pacingWait = new();
    }

    public TimeSpan GetUpdateDelay(bool rendering = true)
    {
        var now = Now;
        var updateDelay = published is { } background
            ? background.IsWaitingForUi ? 0.001 : double.PositiveInfinity
            : State.Simulation.SecondsUntilNextTick - (now - previousSimulationTime);
        var renderDelay = !rendering || UsesVSync ? double.PositiveInfinity : redraw ? 0 : nextRenderTime - now;
        return TimeSpan.FromSeconds(Math.Clamp(Math.Min(updateDelay, renderDelay), 0, 0.01));
    }

    internal void WaitForUpdate(bool rendering)
    {
        var delay = GetUpdateDelay(rendering);
        var trace = State.Timeline;
        var start = trace is { IsEnabled: true } ? Stopwatch.GetTimestamp() : 0;
        pacingWait.Wait(delay);
        if (start == 0) return;
        var end = Stopwatch.GetTimestamp();
        // Keep sub-millisecond waits when they measurably delayed the window.
        if (delay.TotalMilliseconds >= 1 || Stopwatch.GetElapsedTime(start, end).TotalMilliseconds >= 0.05)
            trace!.Record("sleep", start, end, detail: $"Requested {delay.TotalMilliseconds:0.###} ms");
    }

    // UI remains on the window thread, with exclusive live-world access between ticks.
    // The callback returns true for world edits. Native backends queue input without touching the world.
    public bool Update(bool keyboardCaptured = false, bool uiInputPending = false, Func<bool>? updateUi = null, bool mouseCaptured = false)
    {
        EnsureOwner();
        if (State.Timeline?.IsComplete == true) return false;
        State.Audio?.Update();
        var now = Now;
        var delta = now - previousTime;
        previousTime = now;
        if (!UpdateCamera(delta, keyboardCaptured, mouseCaptured)) return false;
        uiAvailable = updateUi is not null;
        inputAwaitingUi = uiAvailable && uiInputPending;
        if (!uiAvailable) uiRequestStart = null;
        else if (uiInputPending || uiRefreshPending) RecordUiRequest(uiInputPending ? "Input" : "Frame refresh");
        if (published is { } background)
        {
            background.CheckForFailure();
            var uiPending = uiAvailable && (uiInputPending || uiRefreshPending);
            background.SetUiPending(uiPending);
            if ((uiPending || !State.PublishRenderData)
                && background.TryUseWorld(() => uiPending && BuildUi(updateUi!, uiInputPending)))
            {
                background.SetUiPending(false);
                if (!State.PublishRenderData && !background.HasPreparedTick)
                {
                    published = null;
                    background.Dispose();
                    previousSimulationTime = Now;
                    return true;
                }
            }
            background.Render.AcquireLatest();
            return true;
        }

        UpdateClock();
        var simulation = State.Simulation;
        if (updateUi is not null && (uiRefreshPending || uiInputPending || ShouldRender || simulation.CanTakeTick()))
        {
            BuildUi(updateUi, uiInputPending);
            UpdateClock();
        }
        if (State.PublishRenderData) published = new(State, clock);
        else if (simulation.TryTakeTick()) simulation.Step();
        return true;
    }

    private bool BuildUi(Func<bool> updateUi, bool inputPending)
    {
        var trace = State.Timeline;
        var tick = State.Simulation.Tick;
        if (trace is { IsEnabled: true })
        {
            uiRevision = uiRequestStart is null ? trace.NextUi() : uiRequestRevision;
            if (uiRequestStart is { } requested)
                trace.Record("ui-wait", requested, Stopwatch.GetTimestamp(), tick, ui: uiRevision, detail: uiRequestReason);
            uiRequestStart = null;
        }
        using var span = trace?.Measure("ui", tick, ui: uiRevision, detail: inputPending ? "Input" : "Frame refresh");
        var changed = updateUi();
        RecordSettings();
        uiRefreshPending = false;
        inputAwaitingUi = false;
        redraw |= inputPending || changed;
        return changed;
    }

    private void RecordUiRequest(string reason)
    {
        if (State.Timeline is not { IsEnabled: true } trace || uiRequestStart is not null) return;
        uiRequestStart = Stopwatch.GetTimestamp();
        uiRequestRevision = trace.NextUi();
        uiRequestReason = reason;
        trace.Record("ui-request", uiRequestStart.Value, null, ui: uiRequestRevision, detail: reason);
    }

    private void RecordSettings()
    {
        if (State.Timeline is not { IsEnabled: true } trace) return;
        var simulation = State.Simulation;
        var settings = (State.Renderer, State.PublishRenderData, State.PreemptiveTicking, State.WantsVSync, !simulation.IsRunning,
            simulation.TargetTickRate, State.TargetFrameRate, simulation.MinimumTickMilliseconds);
        if (traceSettings == settings) return;
        traceSettings = settings;
        var mode = !State.PublishRenderData ? "Live" : State.PreemptiveTicking ? "Published · early ticks" : "Published";
        var playback = simulation.IsRunning ? "" : " · paused";
        var workload = simulation.MinimumTickMilliseconds > 0 ? $" · {simulation.MinimumTickMilliseconds:0.##} ms minimum tick" : "";
        var framePacing = State.WantsVSync ? "VSync" : $"{State.TargetFrameRate} FPS target";
        trace.Mark("settings", simulation.Tick, detail: $"{State.Renderer} · {mode} · {framePacing} · {simulation.TargetTickRate:0.##} ticks/s target{playback}{workload}");
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
        if (published is { } background)
            return background.TryUseWorld(() => { edit(State.World); return redraw = true; });
        edit(State.World);
        return redraw = true;
    }

    public void StepSimulation()
    {
        EnsureOwner();
        if (published is null) State.Simulation.Step();
        else if (!published.TryStep()) throw new InvalidOperationException("Wait for the active tick before stepping.");
        redraw = true;
    }

    public void Draw(Vector2 viewportSize)
    {
        EnsureOwner();
        var displayedTick = PublishedTick ?? State.Simulation.Tick;
        var now = Now;
        if (!UsesVSync && nextRenderTime > 0 && now >= nextRenderTime)
            State.Timeline?.MarkTarget("frame-target", nextRenderTime - now);
        using var span = State.Timeline?.Measure("frame", displayedTick, published?.Render.Revision, uiRevision,
            redraw ? "Redraw requested" : null);
        if (published is { } motionSource) motionSource.Render.PredictMotion(motionSource.GetPredictionSeconds());
        else transforms.Update();
        FollowSelection((float)Math.Clamp(Now - previousRenderTime, 0, 0.1));
        previousRenderTime = Now;
        var interval = 1.0 / State.TargetFrameRate;
        nextRenderTime = UsesVSync ? 0 : (Math.Floor(Now / interval) + 1) * interval;
        redraw = false;
        var camera = State.Camera;
        var eye = camera.Offset;
        var view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY);
        // Keep the far range beyond the target and preserve depth precision in overview shots.
        var near = MathF.Max(camera.NearClip, camera.Distance * 0.0001f);
        var far = camera.Distance + camera.FarClip;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, viewportSize.X / viewportSize.Y, near, far);
        renderSystem.BeginFrame(view, projection, State.Wireframe);
        drawnCamera = new(view, new(projection.M11, projection.M22), eye, near, far);
        scene.Draw(RenderWorld, viewportSize, drawOverlay, camera.Target, (view, projection.M22));
        DisplayedTick = displayedTick;
        if (published is { } background)
        {
            uiRefreshPending = true;
            if (uiAvailable)
            {
                RecordUiRequest("Frame refresh");
                // A frame refresh can wait until the window returns from a synchronized present.
                background.SetUiPending(!UsesVSync || inputAwaitingUi);
            }
        }
    }

    internal void SelectAt(Vector2 pointer, Vector2 displaySize)
    {
        EnsureOwner();
        if (!input.IsFocused || drawnCamera is not { } frame) return;
        if (pointer.X < 0 || pointer.Y < 0 || pointer.X >= displaySize.X || pointer.Y >= displaySize.Y) return;

        // ImGui reports logical window coordinates. Normalize before using the framebuffer projection.
        var normalized = pointer / displaySize;
        var viewRay = new Vector3((2 * normalized.X - 1) / frame.ProjectionScale.X,
            (1 - 2 * normalized.Y) / frame.ProjectionScale.Y, -1);
        var direction = Vector3.TransformNormal(viewRay, Matrix4x4.Transpose(frame.View));
        if (scene.Pick(frame.Eye, direction, frame.Near, frame.Far) is not { } hit || !State.World.IsAlive(hit.Entity)) return;

        State.SelectedEntity = hit.Entity;
        State.FollowSelected = true;
        State.Camera.Target = hit.Position;
        followedEntity = null;
        redraw = true;
    }

    internal bool TryPlaceMeshPart(Vector2 pointer, Vector2 displaySize) => input.IsFocused && drawnCamera is not null
        && State.MeshBuilder?.TryPlace(pointer, displaySize) == true;

    private bool UpdateCamera(double delta, bool keyboardCaptured, bool mouseCaptured)
    {
        var camera = State.Camera;
        var frameDelta = (float)Math.Clamp(delta, 0, 0.1);
        var canPan = State.MeshBuilder is null;
        var shift = input.GetKey(KeyCode.LeftShift) || input.GetKey(KeyCode.RightShift);
        if (input.IsFocused && !keyboardCaptured)
        {
            if (input.GetKey(KeyCode.Escape)) return false;
            if (canPan)
            {
                Vector3 movement = new(input.GetAxis(KeyCode.A, KeyCode.D),
                    (input.GetKey(KeyCode.Space) ? 1 : 0) - (input.GetKey(KeyCode.LeftControl) || input.GetKey(KeyCode.RightControl) ? 1 : 0),
                    input.GetAxis(KeyCode.S, KeyCode.W));
                if (movement != Vector3.Zero)
                {
                    State.FollowSelected = false;
                    camera.Pan(Vector3.Normalize(movement) * Math.Max(camera.Distance, 1) * frameDelta * (shift ? 2 : 0.5f));
                }
            }
            var focus = input.GetKey(KeyCode.F);
            if (focus && !focusHeld) State.FollowSelected = true;
            focusHeld = focus;
            camera.Move(input.GetAxis(KeyCode.LeftArrow, KeyCode.RightArrow) * frameDelta,
                input.GetAxis(KeyCode.DownArrow, KeyCode.UpArrow) * frameDelta,
                Math.Clamp(input.GetAxis(KeyCode.Equals, KeyCode.Minus) + input.GetAxis(KeyCode.KeypadPlus, KeyCode.KeypadMinus), -1, 1) * frameDelta);
        }
        else focusHeld = false;
        var middle = input.IsFocused && input.GetMouseButton(MouseButton.Middle);
        if (!middle) dragging = false;
        else if (!middleHeld && !mouseCaptured) dragging = true;
        middleHeld = middle;
        if (dragging)
        {
            var mouse = input.MouseDelta;
            if (shift && canPan)
            {
                State.FollowSelected = false;
                camera.Pan(new Vector3(-mouse.X, mouse.Y, 0) * camera.Distance * 0.0015f);
            }
            else camera.Move(-mouse.X * 0.005f, mouse.Y * 0.005f, 0);
        }
        if (input.IsFocused && !mouseCaptured) camera.Move(0, 0, -input.ScrollDelta * 0.12f);
        if (State.AutoRotate) camera.Move(0.35f * frameDelta, 0, 0);
        return true;
    }

    private void FollowSelection(float deltaTime)
    {
        // Use the same predicted position as rendering and smooth the offset from its center.
        if (State.FollowSelected && State.SelectedEntity is { } selected)
            RenderWorld.Query(in focusQuery, (Entity entity, ref Transform transform) =>
            {
                var original = entity.TryGet<SourceEntity>(out var source) ? source.Entity : entity;
                if (original != selected) return;
                var position = transform.LocalToWorldMatrix.Translation;
                if (followedEntity != selected && entity.TryGet<MeshRenderer>(out var renderer)
                    && renderer.SharedMesh is { } mesh
                    && Matrix4x4.Decompose(transform.LocalToWorldMatrix, out var scale, out _, out _))
                {
                    var distance = (mesh.Bounds.Size * Vector3.Abs(scale)).Length() * 1.3f;
                    State.Camera.Distance = Math.Clamp(Math.Max(State.Camera.Distance, distance), State.Camera.MinDistance, State.Camera.MaxDistance);
                }
                if (followedEntity == selected) State.Camera.Target += position - previousFollowPosition;
                State.Camera.Follow(position, deltaTime);
                previousFollowPosition = position;
                followedEntity = selected;
            });
        else followedEntity = null;
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
        disposed = true;
        try { published?.Dispose(); }
        finally { pacingWait.Dispose(); }
    }
}
