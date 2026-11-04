using System.Diagnostics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Chippy.Audio;

namespace Chippy.Viewer;

public class ViewerPanel : IDisposable
{
    private readonly ImGuiContextPtr context;
    private readonly string name;
    private readonly ViewerState state;
    private readonly ModelViewer viewer;
    private readonly Stopwatch timer = Stopwatch.StartNew();
    private readonly EntityHierarchy hierarchy;
    private readonly EntityInspector inspector;
    private bool disposed;

    public bool WantsKeyboard => ImGui.GetIO().WantCaptureKeyboard;
    public bool WantsMouse => ImGui.GetIO().WantCaptureMouse;
    public bool HasPendingInput => context.InputEventsQueue.Count > 0;
    public bool HasDrawData { get; private set; }

    public unsafe ViewerPanel(ModelViewer viewer, string name)
    {
        state = viewer.State;
        this.name = name;
        this.viewer = viewer;
        hierarchy = new(state);
        inspector = new(state, hierarchy);
        context = ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        io.IniFilename = null;
        ImGui.StyleColorsDark();
        var style = ImGui.GetStyle();
        style.FontSizeBase = 16;
        style.WindowRounding = 6;
        style.FrameRounding = 3;
        style.WindowPadding = new float2(16, 16);
    }

    // ImGui can trickle a queued press/release across frames. Drain it while the world
    // is safe, so the next simulation tick cannot delay the release/click action again.
    public bool BuildFrame()
    {
        ImGui.GetIO().DeltaTime = Math.Max((float)timer.Elapsed.TotalSeconds, 0.001f);
        timer.Restart();
        var changed = false;
        do
        {
            ImGui.NewFrame();
            var io = ImGui.GetIO();
            if (!io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                viewer.SelectAt(new(io.MousePos.X, io.MousePos.Y), new(io.DisplaySize.X, io.DisplaySize.Y));
            DrawViewerControls();
            hierarchy.Draw();
            changed |= inspector.Draw();
            ImGui.Render();
            HasDrawData = true;
            if (!HasPendingInput) return changed;
            ImGui.GetIO().DeltaTime = 0.001f;
        } while (true);
    }

    private void DrawViewerControls()
    {
        ImGui.SetNextWindowPos(new float2(state.PanelPosition.X, state.PanelPosition.Y), ImGuiCond.Once);
        ImGui.SetNextWindowSize(new float2(state.PanelSize.X, state.PanelSize.Y), ImGuiCond.Once);
        ImGui.SetNextWindowCollapsed(state.PanelCollapsed, ImGuiCond.Once);
        var contentsVisible = ImGui.Begin("Chippy viewer"u8);
        var position = ImGui.GetWindowPos();
        state.PanelPosition = new(position.X, position.Y);
        state.PanelCollapsed = ImGui.IsWindowCollapsed();
        if (!state.PanelCollapsed)
        {
            var size = ImGui.GetWindowSize();
            state.PanelSize = new(size.X, size.Y);
        }
        if (contentsVisible)
        {
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X * 0.55f);
            ImGui.TextWrapped(name);
            var fps = ImGui.GetIO().Framerate;
            ImGui.Text($"{fps:F0} UI updates/s");

            DrawRendererControls();
            DrawSimulationControls();
            DrawAudioControls();

            if (ImGui.CollapsingHeader("Camera"u8)) DrawCameraControls();
            if (ImGui.CollapsingHeader("Display"u8))
            {
                ImGui.Checkbox("Wireframe"u8, ref state.Wireframe);
                EntityInspector.EditColor("Background"u8, ref state.Background);
            }
            if (ImGui.CollapsingHeader("Controls"u8))
            {
                ImGui.TextWrapped("Middle drag: orbit · Shift + middle drag: pan · Wheel / +/-: zoom"u8);
                ImGui.TextWrapped("WASD: move · Space/Ctrl: up/down · Shift: faster · F: follow selected"u8);
                ImGui.TextWrapped("Arrows: orbit · Esc: close"u8);
                ImGui.TextWrapped("Left click a mesh to select and center it."u8);
            }
            ImGui.PopItemWidth();
        }
        ImGui.End();
    }

    private void DrawCameraControls()
    {
        ImGui.Checkbox("Follow selected (F)"u8, ref state.FollowSelected);
        ImGui.SliderAngle("Yaw"u8, ref state.Camera.Yaw, -180, 180, "%.0f deg"u8, ImGuiSliderFlags.AlwaysClamp);
        ImGui.SliderAngle("Pitch"u8, ref state.Camera.Pitch, -80, 80, "%.0f deg"u8, ImGuiSliderFlags.AlwaysClamp);
        ImGui.SliderFloat("Distance"u8, ref state.Camera.Distance, state.Camera.MinDistance, state.Camera.MaxDistance,
            "%.2f"u8, ImGuiSliderFlags.AlwaysClamp | ImGuiSliderFlags.Logarithmic);
        if (ImGui.Button("Reset camera"u8))
        {
            var target = state.Camera.Target;
            state.Camera.Reset();
            if (state.FollowSelected) state.Camera.Target = target;
            state.AutoRotate = false;
        }
        ImGui.Checkbox("Auto rotate"u8, ref state.AutoRotate);
    }

    private void DrawRendererControls()
    {
        var targetFrameRate = state.TargetFrameRate;
        if (ImGui.SliderInt("Target FPS"u8, ref targetFrameRate, 15, 144))
            state.TargetFrameRate = targetFrameRate;
        var preview = state.Renderer == RenderBackend.Brutal ? "BRUTAL"u8 : "MonoGame"u8;
        if (!ImGui.BeginCombo("Renderer"u8, preview))
            return;
        foreach (var backend in Enum.GetValues<RenderBackend>())
        {
            var selected = state.Renderer == backend;
            if (ImGui.Selectable(backend == RenderBackend.Brutal ? "BRUTAL" : "MonoGame", selected))
                state.Renderer = backend;
            if (selected)
                ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    private void DrawSimulationControls()
    {
        var simulation = state.Simulation;
        ImGui.SeparatorText("Simulation"u8);
        ImGui.TextWrapped($"Tick {simulation.Tick:N0} · {simulation.ElapsedTime:F1} simulated seconds");
        ImGui.Checkbox("Publish render data"u8, ref state.PublishRenderData);
        ImGui.BeginDisabled(!state.PublishRenderData);
        ImGui.Checkbox("Preemptive ticking"u8, ref state.PreemptiveTicking);
        ImGui.EndDisabled();
        if (viewer.UsesPublishedData)
            ImGui.TextWrapped($"Displayed {viewer.DisplayedTick:N0} · working {simulation.Tick:N0}");
        var paused = simulation.Paused;
        if (ImGui.Checkbox("Paused"u8, ref paused))
            simulation.Paused = paused;
        ImGui.SameLine();
        ImGui.BeginDisabled(!simulation.Paused);
        if (ImGui.Button("Step"u8))
            viewer.StepSimulation();
        ImGui.EndDisabled();
        var speed = (float)simulation.Speed;
        if (ImGui.SliderFloat("Speed"u8, ref speed, 0, 10, "%.1fx"u8, ImGuiSliderFlags.AlwaysClamp))
            simulation.Speed = speed;
    }

    private void DrawAudioControls()
    {
        if (state.Audio is not { } audio || !ImGui.CollapsingHeader("Procedural audio"u8)) return;
        if (!audio.IsAvailable) { ImGui.TextWrapped(audio.Error ?? "Audio unavailable."); return; }
        var master = audio.MasterVolume;
        var music = audio.MusicVolume;
        var effects = audio.EffectsVolume;
        if (ImGui.SliderFloat("Master volume"u8, ref master, 0, 1)) audio.MasterVolume = master;
        if (ImGui.SliderFloat("Music volume"u8, ref music, 0, 1)) audio.MusicVolume = music;
        if (ImGui.SliderFloat("Effects volume"u8, ref effects, 0, 1)) audio.EffectsVolume = effects;
        if (state.MusicClip is { } track)
        {
            if (audio.MusicState == PlaybackState.Playing)
            {
                if (ImGui.Button("Pause ambient"u8)) audio.PauseMusic();
            }
            else if (ImGui.Button("Play ambient"u8))
            {
                if (audio.MusicState == PlaybackState.Paused) audio.ResumeMusic();
                else audio.PlayMusic(track);
            }
            ImGui.SameLine();
            if (ImGui.Button("Stop"u8)) audio.StopMusic();
        }
        if (state.EffectClip is { } chime && ImGui.Button("Play chime"u8)) audio.PlayOneShot(chime);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        ImGui.DestroyContext(context);
        disposed = true;
    }
}
