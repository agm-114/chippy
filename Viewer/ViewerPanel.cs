using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Chippy.Viewer;

public class ViewerPanel : IDisposable
{
    private readonly ImGuiContextPtr context;
    private readonly string name;
    private readonly ViewerState state;
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
            ImGui.TextWrapped(name);
            ImGui.Text($"{state.Instances.Length:N0} instances · {state.Instances.Select(instance => instance.SharedMesh).Distinct().Count():N0} shared meshes");
            var fps = ImGui.GetIO().Framerate;
            ImGui.Text($"{fps:F0} FPS · {(fps > 0 ? 1000 / fps : 0):F2} ms/frame");

            DrawRendererControls();
            DrawModelControls();

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

    private void DrawModelControls()
    {
        if (state.Instances.Length == 0)
            return;
        ImGui.SeparatorText("Model instances"u8);
        if (state.Instances.Length > 1)
            for (var i = 0; i < state.Instances.Length; i++)
                if (ImGui.Selectable($"Instance {i + 1}", i == state.SelectedInstance))
                    state.SelectedInstance = i;

        ref var instance = ref state.Instances[state.SelectedInstance];
        var mesh = instance.SharedMesh;
        if (mesh is null)
        {
            ImGui.TextWrapped("No mesh assigned."u8);
            return;
        }
        ImGui.Text($"{mesh.SubMeshes.Count:N0} submeshes · {mesh.TriangleCount:N0} triangles");
        ImGui.Checkbox("Visible"u8, ref instance.Visible);
        var position = instance.Transform.Position;
        var positionEdit = new float3(position.X, position.Y, position.Z);
        if (ImGui.DragFloat3("Position"u8, ref positionEdit, 0.01f))
            instance.Transform.Position = new(positionEdit.X, positionEdit.Y, positionEdit.Z);
        var scale = instance.Transform.LocalScale;
        var scaleEdit = new float3(scale.X, scale.Y, scale.Z);
        if (ImGui.DragFloat3("Local scale"u8, ref scaleEdit, 0.01f, 0.01f, 100f, "%.2f"u8, ImGuiSliderFlags.AlwaysClamp))
            instance.Transform.LocalScale = new(scaleEdit.X, scaleEdit.Y, scaleEdit.Z);
        var tint = instance.Tint;
        var tintEdit = new float4(tint.X, tint.Y, tint.Z, tint.W);
        if (ImGui.ColorEdit4("Tint"u8, ref tintEdit, ImGuiColorEditFlags.NoAlpha))
            instance.Tint = new(tintEdit.X, tintEdit.Y, tintEdit.Z, tintEdit.W);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        ImGui.DestroyContext(context);
        disposed = true;
    }
}
