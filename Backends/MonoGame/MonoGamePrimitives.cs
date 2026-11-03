using System.Numerics;
using Chippy.Rendering;
using Microsoft.Xna.Framework.Graphics;
using Xna = Microsoft.Xna.Framework;

namespace Chippy.Backends.MonoGame;

internal class MonoGamePrimitives(GraphicsDevice device) : IDisposable
{
    private readonly BasicEffect effect = new(device) { VertexColorEnabled = true };
    private readonly RasterizerState rasterizer = new() { CullMode = CullMode.None };
    private VertexPositionColor[] vertices = [];

    public void Draw(ReadOnlySpan<PrimitiveVertex> data, PrimitiveTopology topology, in Matrix4x4 matrix, bool screenSpace)
    {
        if (data.IsEmpty) return;
        if (vertices.Length < data.Length) Array.Resize(ref vertices, Math.Max(data.Length, vertices.Length * 2));
        for (var i = 0; i < data.Length; i++)
        {
            var color = data[i].Color;
            vertices[i] = new(data[i].Position.ToXna(), new Xna.Color(color.X, color.Y, color.Z, color.W));
        }
        effect.World = effect.View = Xna.Matrix.Identity;
        effect.Projection = matrix.ToXna();
        device.BlendState = BlendState.NonPremultiplied;
        device.DepthStencilState = screenSpace ? DepthStencilState.None : DepthStencilState.Default;
        device.RasterizerState = rasterizer;
        var kind = topology == PrimitiveTopology.Lines ? PrimitiveType.LineList : PrimitiveType.TriangleList;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(kind, vertices, 0, data.Length / (topology == PrimitiveTopology.Lines ? 2 : 3));
        }
    }

    public void Dispose()
    {
        rasterizer.Dispose();
        effect.Dispose();
    }
}
