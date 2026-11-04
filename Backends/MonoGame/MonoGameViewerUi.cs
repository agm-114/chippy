using System.Runtime.InteropServices;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Chippy.Input;
using Chippy.Viewer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Color = Microsoft.Xna.Framework.Color;

namespace Chippy.Backends.MonoGame;

public class MonoGameViewerUi : IDisposable
{
    private static readonly (Keys Key, ImGuiKey ImGuiKey)[] KeyMap = Enum.GetValues<KeyCode>()
        .Where(key => key != KeyCode.None)
        .Select(key => (key.ToMonoGame(), key.ToImGui())).ToArray();
    private readonly Game game;
    private readonly GraphicsDevice device;
    private readonly ViewerPanel panel;
    private readonly BasicEffect effect;
    private readonly RasterizerState rasterizer = new() { CullMode = CullMode.None, ScissorTestEnable = true };
    private readonly Dictionary<ImTextureID, Texture2D> textures = [];
    private VertexPositionColorTexture[] vertices = [];
    private short[] indices = [];
    private MouseState previousMouse;
    private int nextTextureId;
    private bool disposed;
    public bool WantsKeyboard => panel.WantsKeyboard;
    public bool WantsMouse => panel.WantsMouse;
    public bool HasPendingInput => panel.HasPendingInput;

    public MonoGameViewerUi(Game game, ModelViewer viewer)
    {
        this.game = game;
        device = game.GraphicsDevice;
        panel = new ViewerPanel(viewer);
        try
        {
            effect = new BasicEffect(device) { VertexColorEnabled = true, TextureEnabled = true };
            ImGui.GetIO().BackendFlags |= ImGuiBackendFlags.RendererHasTextures | ImGuiBackendFlags.RendererHasVtxOffset;
            game.Window.TextInput += OnTextInput;
        }
        catch
        {
            rasterizer.Dispose();
            panel.Dispose();
            throw;
        }
    }

    public void PollInput()
    {
        var io = ImGui.GetIO();
        io.AddFocusEvent(game.IsActive);
        var mouse = Mouse.GetState();
        io.AddMousePosEvent(game.IsActive ? mouse.X : -float.MaxValue, game.IsActive ? mouse.Y : -float.MaxValue);
        io.AddMouseButtonEvent(0, game.IsActive && mouse.LeftButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(1, game.IsActive && mouse.RightButton == ButtonState.Pressed);
        io.AddMouseButtonEvent(2, game.IsActive && mouse.MiddleButton == ButtonState.Pressed);
        if (game.IsActive)
            io.AddMouseWheelEvent((mouse.HorizontalScrollWheelValue - previousMouse.HorizontalScrollWheelValue) / 120f,
                (mouse.ScrollWheelValue - previousMouse.ScrollWheelValue) / 120f);
        previousMouse = mouse;
        var keyboard = Keyboard.GetState();
        foreach (var (key, imGuiKey) in KeyMap)
            io.AddKeyEvent(imGuiKey, game.IsActive && keyboard.IsKeyDown(key));
        io.AddKeyEvent(ImGuiKey.Mod_Ctrl, Down(KeyCode.LeftControl) || Down(KeyCode.RightControl));
        io.AddKeyEvent(ImGuiKey.Mod_Shift, Down(KeyCode.LeftShift) || Down(KeyCode.RightShift));
        io.AddKeyEvent(ImGuiKey.Mod_Alt, Down(KeyCode.LeftAlt) || Down(KeyCode.RightAlt));
        io.AddKeyEvent(ImGuiKey.Mod_Super, Down(KeyCode.LeftWindows) || Down(KeyCode.RightWindows));
        bool Down(KeyCode key) => game.IsActive && keyboard.IsKeyDown(key.ToMonoGame());
    }

    public bool BuildFrame()
    {
        var io = ImGui.GetIO();
        var viewport = device.Viewport;
        io.DisplaySize = new float2(game.Window.ClientBounds.Width, game.Window.ClientBounds.Height);
        io.DisplayFramebufferScale = new float2(viewport.Width / io.DisplaySize.X, viewport.Height / io.DisplaySize.Y);
        return panel.BuildFrame();
    }

    public unsafe int Render()
    {
        if (!panel.HasDrawData) return 0;
        UpdateTextures();
        var data = ImGui.GetDrawData();
        if (data.TotalVtxCount == 0)
            return 0;
        effect.Projection = Matrix.CreateOrthographicOffCenter(data.DisplayPos.X, data.DisplayPos.X + data.DisplaySize.X,
            data.DisplayPos.Y + data.DisplaySize.Y, data.DisplayPos.Y, 0, 1);
        ApplyRenderState();
        foreach (var list in data.CmdLists)
        {
            if (vertices.Length < list.VtxBuffer.Count)
                Array.Resize(ref vertices, Math.Max(list.VtxBuffer.Count, vertices.Length * 2));
            if (indices.Length < list.IdxBuffer.Count)
                Array.Resize(ref indices, Math.Max(list.IdxBuffer.Count, indices.Length * 2));
            for (var i = 0; i < list.VtxBuffer.Count; i++)
            {
                var vertex = list.VtxBuffer[i];
                vertices[i] = new(new Vector3(vertex.pos.X, vertex.pos.Y, 0),
                    new Color { PackedValue = vertex.col }, new Vector2(vertex.uv.X, vertex.uv.Y));
            }
            MemoryMarshal.Cast<ushort, short>(list.IdxBuffer.Span).CopyTo(indices);
            for (var i = 0; i < list.CmdBuffer.Count; i++)
            {
                var command = new ImDrawCmdPtr(list.CmdBuffer.DataRaw + i);
                if (command.UserCallback != 0)
                {
                    // Dear ImGui's standard reset-state callback. Custom callbacks are outside this panel's API.
                    if (command.UserCallback != -8)
                        throw new NotSupportedException("Custom ImGui draw callbacks are unsupported.");
                    ApplyRenderState();
                    continue;
                }
                var clip = command.ClipRect;
                var scale = data.FramebufferScale;
                var x = Math.Clamp((int)((clip.X - data.DisplayPos.X) * scale.X), 0, device.Viewport.Width);
                var y = Math.Clamp((int)((clip.Y - data.DisplayPos.Y) * scale.Y), 0, device.Viewport.Height);
                var right = Math.Clamp((int)Math.Ceiling((clip.Z - data.DisplayPos.X) * scale.X), 0, device.Viewport.Width);
                var bottom = Math.Clamp((int)Math.Ceiling((clip.W - data.DisplayPos.Y) * scale.Y), 0, device.Viewport.Height);
                if (right <= x || bottom <= y || command.ElemCount == 0)
                    continue;
                device.ScissorRectangle = new Rectangle(x, y, right - x, bottom - y);
                effect.Texture = textures[command.GetTexID()];
                foreach (var pass in effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList,
                        vertices, (int)command.VtxOffset, list.VtxBuffer.Count - (int)command.VtxOffset,
                        indices, (int)command.IdxOffset, (int)command.ElemCount / 3);
                }
            }
        }
        return data.TotalVtxCount;
    }

    private void ApplyRenderState()
    {
        device.BlendState = BlendState.NonPremultiplied;
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = rasterizer;
        device.SamplerStates[0] = SamplerState.LinearClamp;
    }

    private unsafe void UpdateTextures()
    {
        foreach (var data in ImGui.GetPlatformIO().Textures)
        {
            if (data.Status is ImTextureStatus.WantCreate or ImTextureStatus.WantUpdates)
            {
                if (data.Format != ImTextureFormat.RGBA32)
                    throw new NotSupportedException("The ImGui renderer requires RGBA32 textures.");
                if (data.Status == ImTextureStatus.WantCreate)
                {
                    var texture = new Texture2D(device, data.Width, data.Height, false, SurfaceFormat.Color);
                    var id = new ImTextureID(++nextTextureId);
                    textures.Add(id, texture);
                    data.SetTexID(id);
                }
                var pixels = new ReadOnlySpan<byte>(data.Pixels, data.GetSizeInBytes());
                textures[data.TexID].SetData(pixels.ToArray());
                data.SetStatus(ImTextureStatus.OK);
            }
            else if (data.Status == ImTextureStatus.WantDestroy)
            {
                if (textures.Remove(data.TexID, out var texture))
                    texture.Dispose();
                data.SetTexID(ImTextureID.Invalid);
                data.SetStatus(ImTextureStatus.Destroyed);
            }
        }
    }

    private void OnTextInput(object? sender, TextInputEventArgs args)
    {
        if (game.IsActive && !char.IsControl(args.Character))
            ImGui.GetIO().AddInputCharacterUTF16(args.Character);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        game.Window.TextInput -= OnTextInput;
        foreach (var texture in textures.Values)
            texture.Dispose();
        textures.Clear();
        effect.Dispose();
        rasterizer.Dispose();
        panel.Dispose();
        disposed = true;
    }
}
