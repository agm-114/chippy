using System.Diagnostics;
using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Chippy.Components;
using Chippy.Audio;
using Chippy.Models;

namespace Chippy.Viewer;

public class ViewerPanel : IDisposable
{
    private readonly ImGuiContextPtr context;
    private readonly string name;
    private readonly ViewerState state;
    private readonly ModelViewer viewer;
    private readonly Stopwatch timer = Stopwatch.StartNew();
    private readonly QueryDescription transformQuery = new QueryDescription().WithAll<Transform>();
    private readonly List<Entity> entities = [];
    private readonly HashSet<Mesh> meshes = new(ReferenceEqualityComparer.Instance);
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
            changed |= Draw();
            ImGui.Render();
            HasDrawData = true;
            if (!HasPendingInput) return changed;
            ImGui.GetIO().DeltaTime = 0.001f;
        } while (true);
    }

    private bool Draw()
    {
        var changed = false;
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
            ImGui.TextWrapped(name);
            var fps = ImGui.GetIO().Framerate;
            ImGui.Text($"{fps:F0} UI updates/s");

            DrawRendererControls();
            DrawSimulationControls();
            DrawAudioControls();
            // A manual tick can create or destroy entities.
            RefreshEntities();
            ImGui.Text($"{entities.Count:N0} entities · {meshes.Count:N0} shared meshes");
            changed = DrawEntityControls();

            ImGui.SeparatorText("Camera"u8);
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

            ImGui.SeparatorText("Display"u8);
            ImGui.Checkbox("Wireframe"u8, ref state.Wireframe);
            EditColor("Background"u8, ref state.Background);
            ImGui.Separator();
            ImGui.TextWrapped("Middle drag: orbit · Shift + middle drag: pan · Wheel / +/-: zoom"u8);
            ImGui.TextWrapped("WASD: move · Space/Ctrl: up/down · Shift: faster · F: follow selected"u8);
            ImGui.TextWrapped("Arrows: orbit · Esc: close"u8);
            ImGui.TextWrapped("Click the scene to use camera shortcuts."u8);
        }
        ImGui.End();
        return changed;
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

    private void RefreshEntities()
    {
        entities.Clear();
        meshes.Clear();
        state.World.Query(in transformQuery, (Entity entity, ref Transform _) =>
        {
            entities.Add(entity);
            if (entity.TryGet<MeshRenderer>(out var renderer) && renderer.SharedMesh is { } mesh)
                meshes.Add(mesh);
        });
    }

    private void DrawSimulationControls()
    {
        var simulation = state.Simulation;
        ImGui.SeparatorText("Simulation"u8);
        ImGui.Text($"Tick {simulation.Tick:N0} · {simulation.ElapsedTime:F1} simulated seconds");
        ImGui.Checkbox("Publish render data"u8, ref state.PublishRenderData);
        ImGui.BeginDisabled(!state.PublishRenderData);
        ImGui.Checkbox("Preemptive ticking"u8, ref state.PreemptiveTicking);
        ImGui.EndDisabled();
        if (viewer.UsesPublishedData)
            ImGui.Text($"Displayed tick {viewer.DisplayedTick:N0} · working tick {simulation.Tick:N0}");
        var paused = simulation.Paused;
        if (ImGui.Checkbox("Paused"u8, ref paused))
            simulation.Paused = paused;
        ImGui.SameLine();
        ImGui.BeginDisabled(!simulation.Paused);
        if (ImGui.Button("Step"u8))
            viewer.StepSimulation();
        ImGui.EndDisabled();
        var speed = (float)simulation.Speed;
        if (ImGui.SliderFloat("Simulation speed"u8, ref speed, 0, 10, "%.1fx"u8, ImGuiSliderFlags.AlwaysClamp))
            simulation.Speed = speed;
    }

    private bool DrawEntityControls()
    {
        if (entities.Count == 0)
        {
            state.SelectedEntity = null;
            return false;
        }
        if (state.SelectedEntity is not { } selected || !entities.Contains(selected))
            state.SelectedEntity = entities[0];

        ImGui.SeparatorText("Scene entities"u8);
        if (ImGui.BeginCombo("Selected"u8, EntityLabel(state.SelectedEntity.Value)))
        {
            foreach (var candidate in entities)
                if (ImGui.Selectable(EntityLabel(candidate), candidate == state.SelectedEntity))
                    state.SelectedEntity = candidate;
            ImGui.EndCombo();
        }

        var entity = state.SelectedEntity.Value;
        ref var transform = ref entity.Get<Transform>();
        var changed = DrawParentControls(entity, transform.Parent);
        changed |= DragVector3("Local position"u8, ref transform.LocalPosition, 0.01f);
        var angles = transform.LocalEulerAngles;
        if (DragVector3("Local rotation (deg)"u8, ref angles, 0.5f))
        {
            transform.LocalEulerAngles = angles;
            changed = true;
        }
        changed |= DragVector3("Local scale"u8, ref transform.LocalScale, 0.01f, 0.01f, Math.Max(100, state.Camera.MaxDistance));

        ref var renderer = ref entity.TryGetRef<MeshRenderer>(out var hasRenderer);
        if (hasRenderer)
        {
            if (renderer.SharedMesh is { } mesh)
                ImGui.Text($"{mesh.SubMeshes.Count:N0} submeshes · {mesh.TriangleCount:N0} triangles");
            changed |= ImGui.Checkbox("Mesh enabled"u8, ref renderer.Enabled);
            changed |= ImGui.Checkbox("Unlit"u8, ref renderer.Unlit);
            changed |= EditColor("Tint"u8, ref renderer.Tint);
        }
        ref var line = ref entity.TryGetRef<LineRenderer>(out var hasLine);
        if (hasLine)
        {
            ImGui.Text($"{(line.Positions.IsDefault ? 0 : line.Positions.Length):N0} line points");
            changed |= ImGui.Checkbox("Line enabled"u8, ref line.Enabled);
            changed |= ImGui.Checkbox("Loop"u8, ref line.Loop);
            changed |= EditColor("Line color"u8, ref line.Color);
        }
        ref var light = ref entity.TryGetRef<Light>(out var hasLight);
        if (hasLight)
        {
            ImGui.SeparatorText("Light"u8);
            changed |= ImGui.Checkbox("Light enabled"u8, ref light.Enabled);
            var color = new Vector4(light.Color, 1);
            if (EditColor("Light color"u8, ref color)) { light.Color = new(color.X, color.Y, color.Z); changed = true; }
            changed |= ImGui.DragFloat("Intensity"u8, ref light.Intensity, Math.Max(0.01f, light.Intensity * 0.01f), 0, float.MaxValue, "%.3g"u8);
            if (light.Type == LightType.Point)
                changed |= ImGui.DragFloat("Range (0 = unlimited)"u8, ref light.Range, 1, 0, float.MaxValue, "%.3g"u8);
        }
        return changed;
    }

    private bool DrawParentControls(Entity entity, Entity? parent)
    {
        if (!ImGui.BeginCombo("Parent"u8, parent is { } current ? EntityLabel(current) : "None"))
            return false;
        var changed = false;
        if (ImGui.Selectable("None"u8, parent is null))
            changed |= entity.TrySetParent(null);
        foreach (var candidate in entities)
            if (entity.CanSetParent(candidate)
                && ImGui.Selectable(EntityLabel(candidate), candidate == parent))
                changed |= entity.TrySetParent(candidate);
        ImGui.EndCombo();
        return changed;
    }

    private static string EntityLabel(Entity entity) => entity.TryGet<Name>(out var name)
        ? $"{name.Value}##{entity.Id}" : $"Entity {entity.Id}";

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
        ImGui.TextWrapped("Synthesized in C#; no audio files. Playback continues across renderer switches."u8);
    }

    private static bool DragVector3(ReadOnlySpan<byte> label, ref Vector3 value, float speed, float min = 0, float max = 0)
    {
        var edit = new float3(value.X, value.Y, value.Z);
        if (!ImGui.DragFloat3(label, ref edit, speed, min, max, min < max ? "%.2f"u8 : "%.3f"u8,
            min < max ? ImGuiSliderFlags.AlwaysClamp : ImGuiSliderFlags.None)) return false;
        value = new(edit.X, edit.Y, edit.Z);
        return true;
    }

    private static bool EditColor(ReadOnlySpan<byte> label, ref Vector4 value)
    {
        var edit = new float4(value.X, value.Y, value.Z, value.W);
        if (!ImGui.ColorEdit4(label, ref edit, ImGuiColorEditFlags.NoAlpha)) return false;
        value = new(edit.X, edit.Y, edit.Z, edit.W);
        return true;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        ImGui.DestroyContext(context);
        disposed = true;
    }
}
