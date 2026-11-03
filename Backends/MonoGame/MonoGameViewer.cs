using System.Runtime.InteropServices;
using Chippy.Input;
using Chippy.Rendering;
using Chippy.Viewer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Chippy.Backends.MonoGame;

public class MonoGameViewer : Game, IInputSystem
{
    private readonly Func<GraphicsDevice, IRenderSystem> createRenderSystem;
    private readonly ViewerState state;
    private readonly string name;
    private readonly bool smokeTest;
    private IRenderSystem? renderer;
    private ModelViewer viewer = null!;
    private MonoGameViewerUi? ui;
    private int frames;
    private KeyboardState keyboard;
    public bool SwitchRequested { get; private set; }
    public bool IsFocused => IsActive;

    public bool GetKey(KeyCode key) => key != KeyCode.None && keyboard.IsKeyDown(key.ToMonoGame());

    public MonoGameViewer(Func<GraphicsDevice, IRenderSystem> createRenderSystem,
        ViewerState state, string name, bool smokeTest = false)
    {
        this.createRenderSystem = createRenderSystem;
        this.state = state;
        this.name = name;
        this.smokeTest = smokeTest;
        _ = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = state.WindowPlacement?.Width ?? 1280,
            PreferredBackBufferHeight = state.WindowPlacement?.Height ?? 720,
            GraphicsProfile = GraphicsProfile.HiDef,
            SynchronizeWithVerticalRetrace = false
        };
        Window.Title = $"Chippy · {name} · MonoGame · Arrows: orbit | +/-: zoom | Esc: close";
        Window.AllowUserResizing = true;
        IsMouseVisible = true;
        IsFixedTimeStep = false;
        InactiveSleepTime = TimeSpan.Zero;
    }

    protected override void LoadContent()
    {
        // MonoGame creates its final SDL window during graphics initialization.
        if (state.WindowPlacement is { } saved)
            Window.Position = new(saved.X, saved.Y);
        renderer = createRenderSystem(GraphicsDevice);
        viewer = new ModelViewer(renderer, this, state);
        ui = new MonoGameViewerUi(this, viewer, name);
    }

    protected override void Update(GameTime gameTime)
    {
        keyboard = Keyboard.GetState();
        ui!.PollInput();
        if (!viewer.Update(ui.WantsKeyboard, ui.HasPendingInput, CanDraw ? ui.BuildFrame : null))
        {
            Exit();
            return;
        }
        if (!CanDraw || !viewer.ShouldRender)
        {
            SuppressDraw();
            Thread.Sleep(viewer.GetUpdateDelay(rendering: CanDraw));
        }
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        var viewport = GraphicsDevice.Viewport;
        if (!CanDraw || !viewer.ShouldRender)
            return;
        var background = state.Background;
        GraphicsDevice.Clear(new Color(background.X, background.Y, background.Z, background.W));
        viewer.Draw(viewport.AspectRatio);
        var checkFrame = smokeTest && ++frames == 3;
        Color[]? modelPixels = null;
        if (checkFrame)
        {
            modelPixels = ReadFrame();
            RenderChecks.CheckModelPixels(MemoryMarshal.AsBytes(modelPixels.AsSpan()));
        }
        var uiVertices = ui!.Render();
        if (checkFrame)
        {
            var pixels = ReadFrame();
            var changed = pixels.Where((color, index) => color != modelPixels![index]).Count();
            if (uiVertices == 0 || changed < 100)
                throw new InvalidOperationException("MonoGame ImGui rendering check failed.");
            Console.WriteLine($"ImGui check passed: {uiVertices} UI vertices; {changed} UI pixels.");
            RenderChecks.CheckMeshSharing(renderer!, viewer);
        }
        SwitchRequested = state.Renderer != RenderBackend.MonoGame;
        if (checkFrame || SwitchRequested)
            Exit();
        base.Draw(gameTime);
    }

    private Color[] ReadFrame()
    {
        var size = GraphicsDevice.PresentationParameters;
        var pixels = new Color[size.BackBufferWidth * size.BackBufferHeight];
        GraphicsDevice.GetBackBufferData(pixels);
        return pixels;
    }

    private bool CanDraw => GraphicsDevice.Viewport.Width > 0 && GraphicsDevice.Viewport.Height > 0
        && Window.ClientBounds.Width > 0 && Window.ClientBounds.Height > 0;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (renderer is not null)
            {
                var bounds = Window.ClientBounds;
                state.SaveWindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
            try { viewer?.Dispose(); }
            finally
            {
                ui?.Dispose();
                ui = null;
                renderer?.Dispose();
                renderer = null;
                base.Dispose(disposing);
            }
            return;
        }
        base.Dispose(disposing);
    }
}
