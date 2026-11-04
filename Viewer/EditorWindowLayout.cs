using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Chippy.Viewer;

internal struct EditorWindowLayout
{
    private float2 position;
    private float2 size;
    private bool collapsed;
    private bool initialized;

    public bool Begin(ReadOnlySpan<byte> title, float2 defaultPosition, float2 defaultSize)
    {
        ImGui.SetNextWindowPos(initialized ? position : defaultPosition, ImGuiCond.Once);
        ImGui.SetNextWindowSize(initialized ? size : defaultSize, ImGuiCond.Once);
        ImGui.SetNextWindowCollapsed(collapsed, ImGuiCond.Once);
        var visible = ImGui.Begin(title);
        position = ImGui.GetWindowPos();
        collapsed = ImGui.IsWindowCollapsed();
        if (!collapsed) size = ImGui.GetWindowSize();
        else if (!initialized) size = defaultSize;
        initialized = true;
        return visible;
    }
}
