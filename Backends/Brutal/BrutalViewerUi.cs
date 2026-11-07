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
    public bool WantsMouse => panel.WantsMouse;

    public BrutalViewerUi(GlfwWindow window, ModelViewer viewer)
    {
        panel = new ViewerPanel(viewer);
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

    public void PollInput() => platform.NewFrame();

    public bool BuildFrame()
    {
        renderer.NewFrame();
        return panel.BuildFrame();
    }

    public int Render()
    {
        if (!panel.HasDrawData) return 0;
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
