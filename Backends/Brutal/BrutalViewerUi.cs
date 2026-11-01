using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using Brutal.ImGuiApi.Backends;
using Chippy.Viewer;

namespace Chippy.Backends.Brutal;

public class BrutalViewerUi : IDisposable
{
    private readonly ViewerPanel panel;
    private readonly GlfwBackend platform;
    private readonly OpenGlBackend renderer;
    private bool disposed;
    public bool WantsKeyboard => panel.WantsKeyboard;

    public BrutalViewerUi(GlfwWindow window, ViewerState state, string name)
    {
        panel = new ViewerPanel(state, name);
        GlfwBackend? initializedPlatform = null;
        try
        {
            initializedPlatform = new GlfwBackend(window, GlfwBackend.ClientApi.OpenGL);
            platform = initializedPlatform;
            renderer = new OpenGlBackend();
        }
        catch
        {
            initializedPlatform?.Dispose();
            panel.Dispose();
            throw;
        }
    }

    public void BeginFrame()
    {
        renderer.NewFrame();
        platform.NewFrame();
        ImGui.NewFrame();
        panel.Draw();
    }

    public int Render()
    {
        ImGui.Render();
        var data = ImGui.GetDrawData();
        renderer.RenderDrawData(data);
        return data.TotalVtxCount;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        renderer.Dispose();
        platform.Dispose();
        panel.Dispose();
        disposed = true;
    }
}
