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
    private readonly long started;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private PublishedSimulation? published;
    private double previousTime, previousSimulationTime, nextRenderTime, previousRenderTime;
    private double? lastUiUpdate;
    private Entity? followedEntity;
    private Vector3 previousFollowPosition;
    private (Matrix4x4 View, Vector2 ProjectionScale, Vector3 Eye, float Near, float Far)? drawnCamera;
    private bool redraw, disposed;
    private bool middleHeld, dragging, focusHeld;
    private readonly QueryDescription focusQuery = new QueryDescription().WithAll<Transform>();
    public ViewerState State { get; }
    public bool UsesPublishedData => published is not null;
    public bool SimulationInProgress => published?.IsStepping == true;
    public long? PublishedTick => published?.Render.Tick;
    public long DisplayedTick { get; private set; }
    public World RenderWorld => published?.Render.World ?? State.World;
    internal int DrawnMeshAssetCount => scene.DrawnMeshAssetCount;
    public bool HasPreparedTick => published?.HasPreparedTick == true;
    public bool ShouldRender => redraw || Now >= nextRenderTime;
    private double Now => clock.GetElapsedTime(started).TotalSeconds;

    public ModelViewer(IRenderSystem renderSystem, IInputSystem input, ViewerState state, TimeProvider? clock = null)
    {
        this.renderSystem = renderSystem;
        this.input = input;
        State = state;
        this.clock = clock ?? TimeProvider.System;
        transforms = new(state.World);
        scene = new(renderSystem);
        started = this.clock.GetTimestamp();
        if (state.PublishRenderData) published = new(state, this.clock);
    }

    public TimeSpan GetUpdateDelay(bool rendering = true)
    {
        var now = Now;
        var updateDelay = published is { } background
            ? background.IsWaitingForUi ? 0.001 : double.PositiveInfinity
            : State.Simulation.SecondsUntilNextTick - (now - previousSimulationTime);
        var renderDelay = !rendering ? double.PositiveInfinity : redraw ? 0 : nextRenderTime - now;
        return TimeSpan.FromSeconds(Math.Clamp(Math.Min(updateDelay, renderDelay), 0, 0.01));
    }

    // UI remains on the window thread, with exclusive live-world access between ticks.
    // The callback returns true for world edits. Native backends queue input without touching the world.
    public bool Update(bool keyboardCaptured = false, bool uiInputPending = false, Func<bool>? updateUi = null, bool mouseCaptured = false)
    {
        EnsureOwner();
        State.Audio?.Update();
        var now = Now;
        var delta = now - previousTime;
        previousTime = now;
        if (!UpdateCamera(delta, keyboardCaptured, mouseCaptured)) return false;
        if (published is { } background)
        {
            background.CheckForFailure();
            // Keep the tick-rate display updating even if catch-up ticks are running back to back.
            var uiRefreshDue = lastUiUpdate is not { } lastUpdate || now - lastUpdate >= 1;
            var uiPending = updateUi is not null && (uiInputPending || uiRefreshDue);
            var needsUi = updateUi is not null && (uiPending || ShouldRender);
            background.SetUiPending(uiPending);
            if ((needsUi || !State.PublishRenderData)
                && background.TryUseWorld(() => needsUi && BuildUi(updateUi!, uiInputPending)))
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
        if (updateUi is not null && (lastUiUpdate is null || uiInputPending || ShouldRender || simulation.CanTakeTick()))
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
        var changed = updateUi();
        lastUiUpdate = Now;
        redraw |= inputPending || changed;
        return changed;
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
        if (!UsesPublishedData) transforms.Update();
        FollowSelection((float)Math.Clamp(Now - previousRenderTime, 0, 0.1));
        previousRenderTime = Now;
        var interval = 1.0 / State.TargetFrameRate;
        nextRenderTime = (Math.Floor(Now / interval) + 1) * interval;
        redraw = false;
        var camera = State.Camera;
        var eye = camera.Offset;
        var view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitY);
        // Keep the far range beyond the target and preserve depth precision in overview shots.
        var near = MathF.Max(camera.NearClip, camera.Distance * 0.0001f);
        var far = camera.Distance + camera.FarClip;
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, viewportSize.X / viewportSize.Y, near, far);
        renderSystem.BeginFrame(view, projection, State.Wireframe);
        scene.Draw(RenderWorld, viewportSize, State.DrawOverlay, camera.Target, (view, projection.M22));
        drawnCamera = (view, new(projection.M11, projection.M22), eye, near, far);
        DisplayedTick = PublishedTick ?? State.Simulation.Tick;
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

    private bool UpdateCamera(double delta, bool keyboardCaptured, bool mouseCaptured)
    {
        var camera = State.Camera;
        var frameDelta = (float)Math.Clamp(delta, 0, 0.1);
        var shift = input.GetKey(KeyCode.LeftShift) || input.GetKey(KeyCode.RightShift);
        var movement = Vector3.Zero;
        if (input.IsFocused && !keyboardCaptured)
        {
            if (input.GetKey(KeyCode.Escape)) return false;
            movement = new(input.GetAxis(KeyCode.A, KeyCode.D),
                (input.GetKey(KeyCode.Space) ? 1 : 0) - (input.GetKey(KeyCode.LeftControl) || input.GetKey(KeyCode.RightControl) ? 1 : 0),
                input.GetAxis(KeyCode.S, KeyCode.W));
            if (movement != Vector3.Zero)
            {
                State.FollowSelected = false;
                camera.Pan(Vector3.Normalize(movement) * Math.Max(camera.Distance, 1) * frameDelta * (shift ? 2 : 0.5f));
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
            if (shift)
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
        // Track the completed frame's movement exactly; smooth only the offset from its center.
        // Reading published data here keeps focus aligned with the mesh, even during slow ticks.
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
        published?.Dispose();
    }
}
