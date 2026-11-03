using Arch.Core;
using Arch.Core.Extensions;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Chippy.Components;
using Chippy.Models;

namespace Chippy.Viewer;

public class ViewerPanel : IDisposable
{
    private readonly ImGuiContextPtr context;
    private readonly string name;
    private readonly ViewerState state;
    private readonly QueryDescription transformQuery = new QueryDescription().WithAll<Transform>();
    private readonly List<Entity> entities = [];
    private readonly HashSet<Mesh> meshes = new(ReferenceEqualityComparer.Instance);
    private bool disposed;

    public bool WantsKeyboard => ImGui.GetIO().WantCaptureKeyboard;

    public unsafe ViewerPanel(ViewerState state, string name)
    {
        this.state = state;
        this.name = name;
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

    public void Draw()
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
            RefreshEntities();
            ImGui.TextWrapped(name);
            ImGui.Text($"{entities.Count:N0} entities · {meshes.Count:N0} shared meshes");
            var fps = ImGui.GetIO().Framerate;
            ImGui.Text($"{fps:F0} FPS · {(fps > 0 ? 1000 / fps : 0):F2} ms/frame");

            DrawRendererControls();
            DrawEntityControls();

            ImGui.SeparatorText("Camera"u8);
            ImGui.SliderAngle("Yaw"u8, ref state.Camera.Yaw, -180, 180, "%.0f deg"u8, ImGuiSliderFlags.AlwaysClamp);
            ImGui.SliderAngle("Pitch"u8, ref state.Camera.Pitch, -80, 80, "%.0f deg"u8, ImGuiSliderFlags.AlwaysClamp);
            ImGui.SliderFloat("Distance"u8, ref state.Camera.Distance, 1.5f, 20f, "%.2f"u8, ImGuiSliderFlags.AlwaysClamp);
            if (ImGui.Button("Reset camera"u8))
            {
                state.Camera.Reset();
                state.AutoRotate = false;
            }
            ImGui.Checkbox("Auto rotate"u8, ref state.AutoRotate);

            ImGui.SeparatorText("Display"u8);
            ImGui.Checkbox("Wireframe"u8, ref state.Wireframe);
            var color = state.Background;
            var background = new float4(color.X, color.Y, color.Z, color.W);
            if (ImGui.ColorEdit4("Background"u8, ref background, ImGuiColorEditFlags.NoAlpha))
                state.Background = new(background.X, background.Y, background.Z, background.W);
            ImGui.Separator();
            ImGui.TextWrapped("Arrows: orbit · +/-: zoom · Esc: close"u8);
            ImGui.TextWrapped("Click the scene to use camera shortcuts."u8);
        }
        ImGui.End();
    }

    private void DrawRendererControls()
    {
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

    private void DrawEntityControls()
    {
        if (entities.Count == 0)
        {
            state.SelectedEntity = null;
            return;
        }
        if (state.SelectedEntity is not { } selected || !entities.Contains(selected))
            state.SelectedEntity = entities[0];

        ImGui.SeparatorText("Scene entities"u8);
        foreach (var candidate in entities)
            if (ImGui.Selectable($"Entity {candidate.Id}", candidate == state.SelectedEntity))
                state.SelectedEntity = candidate;

        var entity = state.SelectedEntity.Value;
        ref var transform = ref entity.Get<Transform>();
        DrawParentControls(entity, transform.Parent);
        var position = transform.LocalPosition;
        var positionEdit = new float3(position.X, position.Y, position.Z);
        if (ImGui.DragFloat3("Local position"u8, ref positionEdit, 0.01f))
            transform.LocalPosition = new(positionEdit.X, positionEdit.Y, positionEdit.Z);
        var angles = transform.LocalEulerAngles;
        var rotationEdit = new float3(angles.X, angles.Y, angles.Z);
        if (ImGui.DragFloat3("Local rotation (deg)"u8, ref rotationEdit, 0.5f))
            transform.LocalEulerAngles = new(rotationEdit.X, rotationEdit.Y, rotationEdit.Z);
        var scale = transform.LocalScale;
        var scaleEdit = new float3(scale.X, scale.Y, scale.Z);
        if (ImGui.DragFloat3("Local scale"u8, ref scaleEdit, 0.01f, 0.01f, 100f, "%.2f"u8, ImGuiSliderFlags.AlwaysClamp))
            transform.LocalScale = new(scaleEdit.X, scaleEdit.Y, scaleEdit.Z);

        ref var renderer = ref entity.TryGetRef<MeshRenderer>(out var hasRenderer);
        if (!hasRenderer)
            return;
        if (renderer.SharedMesh is { } mesh)
            ImGui.Text($"{mesh.SubMeshes.Count:N0} submeshes · {mesh.TriangleCount:N0} triangles");
        ImGui.Checkbox("Enabled"u8, ref renderer.Enabled);
        var tint = renderer.Tint;
        var tintEdit = new float4(tint.X, tint.Y, tint.Z, tint.W);
        if (ImGui.ColorEdit4("Tint"u8, ref tintEdit, ImGuiColorEditFlags.NoAlpha))
            renderer.Tint = new(tintEdit.X, tintEdit.Y, tintEdit.Z, tintEdit.W);
    }

    private void DrawParentControls(Entity entity, Entity? parent)
    {
        if (!ImGui.BeginCombo("Parent"u8, parent is { } current ? $"Entity {current.Id}" : "None"))
            return;
        if (ImGui.Selectable("None"u8, parent is null))
            entity.TrySetParent(null);
        foreach (var candidate in entities)
            if (entity.CanSetParent(candidate)
                && ImGui.Selectable($"Entity {candidate.Id}", candidate == parent))
                entity.TrySetParent(candidate);
        ImGui.EndCombo();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        ImGui.DestroyContext(context);
        disposed = true;
    }
}
