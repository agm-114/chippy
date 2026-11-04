using System.Diagnostics;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Chippy.Audio;
using Chippy.Simulation;

namespace Chippy.Viewer;

public class ViewerPanel : IDisposable
{
    private static readonly double[] SpeedMultipliers =
        [0.25, 0.5, 1, 2, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, ViewerState.MaxSpeedMultiplier];
    private readonly ImGuiContextPtr context;
    private readonly ViewerState state;
    private readonly ModelViewer viewer;
    private readonly Stopwatch timer = Stopwatch.StartNew();
    private readonly EntityHierarchy hierarchy;
    private readonly EntityInspector inspector;
    private readonly ProfilerPanel profiler;
    private long tickSampleStart = Stopwatch.GetTimestamp();
    private long tickSampleCount;
    private double sampledTargetRate, achievedTickRate;
    private bool disposed;

    public bool WantsKeyboard => ImGui.GetIO().WantCaptureKeyboard;
    public bool WantsMouse => ImGui.GetIO().WantCaptureMouse;
    public bool HasPendingInput => context.InputEventsQueue.Count > 0;
    public bool HasDrawData { get; private set; }

    public unsafe ViewerPanel(ModelViewer viewer)
    {
        state = viewer.State;
        this.viewer = viewer;
        hierarchy = new(state);
        inspector = new(state, hierarchy);
        profiler = new(state);
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
            DrawTimeControls();
            profiler.Draw();
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
            var fps = ImGui.GetIO().Framerate;
            ImGui.Text($"{fps:F0} UI updates/s");

            DrawRendererControls();
            DrawSimulationSettings();
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
        if (ImGui.SliderInt("Target FPS"u8, ref targetFrameRate, 15, ViewerState.MaxFrameRate))
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

    private void DrawSimulationSettings()
    {
        ImGui.SeparatorText("Simulation"u8);
        ImGui.Checkbox("Publish render data"u8, ref state.PublishRenderData);
        ImGui.BeginDisabled(!state.PublishRenderData);
        ImGui.Checkbox("Preemptive ticking"u8, ref state.PreemptiveTicking);
        ImGui.EndDisabled();
        if (state.Timeline is not null) ImGui.Checkbox("Profiler"u8, ref state.ShowProfiler);
        if (viewer.UsesPublishedData)
            ImGui.TextWrapped($"Displayed {viewer.DisplayedTick:N0} · working {state.Simulation.Tick:N0}");
    }

    private void DrawTimeControls()
    {
        var display = ImGui.GetIO().DisplaySize;
        const float width = 300;
        var simulation = state.Simulation;
        if (state.TimeControlsWindow.Begin("Time Controls"u8, new(Math.Max(12, (display.X - width) / 2), 12), new(width, 255)))
        {
            ImGui.BeginDisabled(simulation.IsRunning);
            if (ImGui.Button("Play"u8))
            {
                if (simulation.Speed == 0) simulation.Speed = 1;
                simulation.Paused = false;
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!simulation.IsRunning);
            if (ImGui.Button("Pause"u8)) simulation.Paused = true;
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(simulation.IsRunning);
            if (ImGui.Button("Step"u8)) viewer.StepSimulation();
            ImGui.EndDisabled();

            DrawSpeedButtons(simulation);
            SampleTickRate(simulation);
            ImGui.Text($"Tick rate  {achievedTickRate:0.#} / {simulation.TargetTickRate:0.#} Hz");
            if (state.UniverseTimePerTick is { } timePerTick)
            {
                var minutes = Math.Floor(simulation.Tick * timePerTick.TotalMinutes);
                var hours = Math.Floor(minutes / 60);
                ImGui.Text($"Universe time  {Math.Floor(hours / 24):N0}d {hours % 24:0}h {minutes % 60:0}m");
                ImGui.Text($"Tick {simulation.Tick:N0}");
            }
            else ImGui.Text($"Tick {simulation.Tick:N0} · {simulation.ElapsedTime:F1} sim seconds");
        }
        ImGui.End();
    }

    private static void DrawSpeedButtons(SimulationScheduler simulation)
    {
        const float buttonWidth = 58;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + spacing) / (buttonWidth + spacing)));
        var selectedColor = ImGui.GetStyleColorVec4(ImGuiCol.ButtonActive);
        for (var index = 0; index < SpeedMultipliers.Length; index++)
        {
            if (index % columns != 0) ImGui.SameLine();
            var multiplier = SpeedMultipliers[index];
            var selected = simulation.Speed == multiplier;
            if (selected) ImGui.PushStyleColor(ImGuiCol.Button, selectedColor);
            if (ImGui.Button($"{multiplier:0.##}×", new float2(buttonWidth, 0))) simulation.Speed = multiplier;
            if (selected) ImGui.PopStyleColor();
        }
    }

    private void SampleTickRate(SimulationScheduler simulation)
    {
        var now = Stopwatch.GetTimestamp();
        var target = simulation.IsRunning ? simulation.TargetTickRate : 0;
        var elapsed = Stopwatch.GetElapsedTime(tickSampleStart, now).TotalSeconds;
        if (target > 0 && target == sampledTargetRate && elapsed < 1) return;

        achievedTickRate = target > 0 && target == sampledTargetRate ? (simulation.Tick - tickSampleCount) / elapsed : 0;
        sampledTargetRate = target;
        tickSampleCount = simulation.Tick;
        tickSampleStart = now;
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
