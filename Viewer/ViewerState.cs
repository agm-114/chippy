using System.Numerics;
using Arch.Core;
using Chippy.Rendering;

namespace Chippy.Viewer;

public class ViewerState(World world, RenderBackend renderer = RenderBackend.Brutal)
{
    public World World { get; } = world;
    public OrbitCamera Camera { get; } = new();
    public WindowPlacement? WindowPlacement { get; private set; }
    public RenderBackend Renderer = renderer;
    public Vector4 Background = new(0.055f, 0.075f, 0.105f, 1);
    public Vector2 PanelPosition = new(20, 20);
    public Vector2 PanelSize = new(360, 620);
    public bool PanelCollapsed;
    public bool Wireframe;
    public bool AutoRotate;
    public Entity? SelectedEntity;

    public void SaveWindowPlacement(int x, int y, int width, int height)
    {
        if (width > 0 && height > 0)
            WindowPlacement = new(x, y, width, height);
    }
}
