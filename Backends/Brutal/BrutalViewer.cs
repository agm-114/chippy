using Brutal.GlfwApi;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using Chippy.Input;
using Chippy.Rendering;
using Chippy.Viewer;
using static Brutal.OpenGlApi.OpenGl;
using Vector2 = System.Numerics.Vector2;

namespace Chippy.Backends.Brutal;

public unsafe class BrutalViewer(GlfwWindow window) : IInputSystem
{
    public bool IsFocused => window.IsFocused;
    public Vector2 MouseDelta { get; private set; }
    public float ScrollDelta { get; private set; }
    private Vector2 previousMouse;
    private float scroll;
    private bool mouseInitialized, wasFocused;

    public bool GetMouseButton(MouseButton button) => window.GetMouseButton((GlfwMouseButton)(int)button) == GlfwButtonAction.Press;

    public bool GetKey(KeyCode key) => key != KeyCode.None && (Pressed(key.ToGlfw())
        || key == KeyCode.Return && Pressed(GlfwKey.KpEnter));

    private bool Pressed(GlfwKey key) => window.GetKey(key) == GlfwKeyAction.Press;

    public static bool Run(Func<IRenderSystem> createRenderSystem, ViewerState state, string name, bool smokeTest = false)
    {
        if (!Glfw.Init())
            throw new InvalidOperationException("Failed to initialize GLFW.");
        try
        {
            Glfw.WindowHint((GlfwWindowHint)GlfwContextAttribute.VersionMajor, 4);
            Glfw.WindowHint((GlfwWindowHint)GlfwContextAttribute.VersionMinor, 1);
            Glfw.WindowHint((GlfwWindowHint)GlfwContextAttribute.OpenGlProfile, Glfw.Constants.GLFW_OPENGL_CORE_PROFILE);
            Glfw.WindowHint(GlfwWindowHint.DepthBits, 24);
            Glfw.WindowHint(GlfwWindowHint.StencilBits, 8);
            Glfw.WindowHint(GlfwWindowHint.Visible, 0);
            var placement = state.WindowPlacement;
            using var nativeWindow = Glfw.CreateWindow(new GlfwWindow.CreateInfo
            {
                Title = $"Chippy · {name} · Middle drag: orbit | WASD: move | Wheel: zoom",
                Size = new int2(placement?.Width ?? 1280, placement?.Height ?? 720)
            });
            if (placement is { } saved)
                nativeWindow.Position = new(saved.X, saved.Y);
            nativeWindow.MakeContextCurrent();
            if (!Glad.Initialize(Glfw.PInvoke.GetProcAddress))
                throw new InvalidOperationException("Failed to load OpenGL functions.");
            Glfw.SwapInterval(state.WantsVSync ? 1 : 0);
            if (!smokeTest)
                nativeWindow.Visible = true;
            try
            {
                return new BrutalViewer(nativeWindow).RunLoop(createRenderSystem, state, smokeTest);
            }
            finally
            {
                if (!nativeWindow.IsIconified)
                {
                    var position = nativeWindow.Position;
                    var size = nativeWindow.Size;
                    state.SaveWindowPlacement(position.X, position.Y, size.X, size.Y);
                }
            }
        }
        finally
        {
            Glfw.Terminate();
        }
    }

    private bool RunLoop(Func<IRenderSystem> createRenderSystem, ViewerState state, bool smokeTest)
    {
        using var renderer = createRenderSystem();
        using var viewer = new ModelViewer(renderer, this, state);
        using var ui = new BrutalViewerUi(window, viewer);
        var frames = 0;
        var vsync = viewer.UsesVSync;
        window.OnScroll += OnScroll;

        try
        {
            while (!window.ShouldClose)
            {
                Glfw.PollEvents();
                PollInput();
                ui.PollInput();
                var size = window.FramebufferSize;
                var canDraw = size.X > 0 && size.Y > 0;
                if (!viewer.Update(ui.WantsKeyboard, ui.HasPendingInput, canDraw ? ui.BuildFrame : null, ui.WantsMouse))
                    return false;
                if (vsync != viewer.UsesVSync)
                {
                    vsync = viewer.UsesVSync;
                    Glfw.SwapInterval(vsync ? 1 : 0);
                }
                if (!canDraw || !viewer.ShouldRender)
                {
                    viewer.WaitForUpdate(canDraw);
                    continue;
                }

                var color = state.Background;
                ClearColor(new float4(color.X, color.Y, color.Z, color.W));
                Viewport(0, 0, size.X, size.Y);
                Clear(GlClearBit.Color | GlClearBit.Depth | GlClearBit.Stencil);
                viewer.Draw(new(size.X, size.Y));
                var checkFrame = smokeTest && ++frames == 3;
                if (checkFrame)
                    CheckFrame(size.X, size.Y);
                int uiVertices;
                using (state.Timeline?.Measure("ui-submit", viewer.DisplayedTick))
                    uiVertices = ui.Render();
                if (checkFrame)
                {
                    if (uiVertices == 0 || GetError() != GL_NO_ERROR)
                        throw new InvalidOperationException("ImGui rendering check failed.");
                    Console.WriteLine($"ImGui check passed: {uiVertices} UI vertices; no OpenGL errors.");
                    RenderChecks.CheckMeshSharing(renderer, viewer);
                    return state.Renderer != RenderBackend.Brutal;
                }
                using (state.Timeline?.Measure("present", viewer.DisplayedTick))
                    window.SwapBuffers();
                if (!window.ShouldClose && state.Renderer != RenderBackend.Brutal)
                    return true;
            }
            return false;
        }
        finally { window.OnScroll -= OnScroll; }
    }

    private void OnScroll(GlfwWindow _, double2 offset) => scroll += (float)offset.Y;

    private void PollInput()
    {
        var cursor = window.CursorPos;
        var mouse = new Vector2((float)cursor.X, (float)cursor.Y);
        MouseDelta = mouseInitialized && wasFocused && IsFocused ? mouse - previousMouse : Vector2.Zero;
        ScrollDelta = IsFocused ? scroll : 0;
        scroll = 0;
        previousMouse = mouse;
        mouseInitialized = true;
        wasFocused = IsFocused;
    }

    private static void CheckFrame(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        fixed (byte* pointer = pixels)
            ReadPixels(0, 0, width, height, GL_RGBA, GL_UNSIGNED_BYTE, pointer);
        var error = GetError();
        if (error != GL_NO_ERROR)
            throw new InvalidOperationException($"Render check failed: GL error {error}.");
        RenderChecks.CheckModelPixels(pixels);
    }
}
