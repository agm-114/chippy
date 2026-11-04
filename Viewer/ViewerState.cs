using System.Numerics;
using Arch.Core;
using Chippy.Audio;
using Chippy.Diagnostics;
using Chippy.Rendering;
using Chippy.Simulation;

namespace Chippy.Viewer;

public class ViewerState(World world, RenderBackend renderer = RenderBackend.Brutal)
{
    internal const int MaxSpeedMultiplier = 10_000;
    internal const int MaxFrameRate = 500;
    public World World { get; } = world;
    public SimulationScheduler Simulation { get; init; } = new();
    internal TimelineCapture? Timeline { get; set; }
    public TimeSpan? UniverseTimePerTick { get; init; }
    public bool PublishRenderData = true;
    public bool VSync;
    internal bool WantsVSync => PublishRenderData && VSync;
    public bool PreemptiveTicking;
    public bool ShowProfiler;
    public OrbitCamera Camera { get; init; } = new();
    public Action<World, PrimitiveBatch, Vector2>? DrawOverlay { get; init; }
    public AudioSystem? Audio { get; set; }
    public AudioClip? MusicClip { get; init; }
    public AudioClip? EffectClip { get; init; }
    public WindowPlacement? WindowPlacement { get; private set; }
    public RenderBackend Renderer = renderer;
    public Vector4 Background = new(0.055f, 0.075f, 0.105f, 1);
    public Vector2 PanelPosition = new(12, 355);
    public Vector2 PanelSize = new(280, 350);
    public bool PanelCollapsed;
    internal EditorWindowLayout HierarchyWindow;
    internal EditorWindowLayout InspectorWindow;
    internal EditorWindowLayout TimeControlsWindow;
    internal EditorWindowLayout ProfilerWindow;
    public bool Wireframe;
    public bool AutoRotate;
    public bool FollowSelected;
    public Entity? SelectedEntity;
    public int TargetFrameRate
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 60;

    public void SaveWindowPlacement(int x, int y, int width, int height)
    {
        if (width > 0 && height > 0)
            WindowPlacement = new(x, y, width, height);
    }
}
