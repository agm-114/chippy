using System.Diagnostics;
using Brutal.GlfwApi;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using Chippy.Input;
using Chippy.Rendering;
using Chippy.Viewer;
using static Brutal.OpenGlApi.OpenGl;

namespace Chippy.Backends.Brutal;

public unsafe class BrutalViewer(GlfwWindow window) : IInputSystem
{
    public bool IsFocused => window.IsFocused;

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
            Glfw.WindowHint(GlfwWindowHint.Visible, 0);
            var placement = state.WindowPlacement;
            using var nativeWindow = Glfw.CreateWindow(new GlfwWindow.CreateInfo
            {
                Title = $"Chippy · {name} · Arrows: orbit | +/-: zoom | Esc: close",
                Size = new int2(placement?.Width ?? 1280, placement?.Height ?? 720)
            });
            if (placement is { } saved)
                nativeWindow.Position = new(saved.X, saved.Y);
            nativeWindow.MakeContextCurrent();
            if (!Glad.Initialize(Glfw.PInvoke.GetProcAddress))
                throw new InvalidOperationException("Failed to load OpenGL functions.");
            Glfw.SwapInterval(1);
            if (!smokeTest)
                nativeWindow.Visible = true;
            try
            {
                return new BrutalViewer(nativeWindow).RunLoop(createRenderSystem, state, name, smokeTest);
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

    private bool RunLoop(Func<IRenderSystem> createRenderSystem, ViewerState state, string name, bool smokeTest)
    {
        using var renderer = createRenderSystem();
        var viewer = new ModelViewer(renderer, this, state);
        using var ui = new BrutalViewerUi(window, state, name);
        var timer = Stopwatch.StartNew();
        var previousTime = timer.Elapsed.TotalSeconds;
        var frames = 0;

        while (!window.ShouldClose)
        {
            Glfw.PollEvents();
            var now = timer.Elapsed.TotalSeconds;
            var delta = (float)(now - previousTime);
            previousTime = now;

            var size = window.FramebufferSize;
            if (size.X == 0 || size.Y == 0)
            {
                Glfw.WaitEvents();
                continue;
            }
            ui.BeginFrame();
            if (!viewer.Update(delta, ui.WantsKeyboard))
            {
                ui.Render();
                return false;
            }

            var color = state.Background;
            ClearColor(new float4(color.X, color.Y, color.Z, color.W));
            Viewport(0, 0, size.X, size.Y);
            Clear(GlClearBit.Color | GlClearBit.Depth);
            viewer.Draw(size.X / (float)size.Y);
            var checkFrame = smokeTest && ++frames == 3;
            if (checkFrame)
                CheckFrame(size.X, size.Y);
            var uiVertices = ui.Render();
            if (checkFrame)
            {
                if (uiVertices == 0 || GetError() != GL_NO_ERROR)
                    throw new InvalidOperationException("ImGui rendering check failed.");
                Console.WriteLine($"ImGui check passed: {uiVertices} UI vertices; no OpenGL errors.");
                RenderChecks.CheckMeshSharing(renderer, state.World);
                return state.Renderer != RenderBackend.Brutal;
            }
            window.SwapBuffers();
            if (!window.ShouldClose && state.Renderer != RenderBackend.Brutal)
                return true;
        }
        return false;
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
