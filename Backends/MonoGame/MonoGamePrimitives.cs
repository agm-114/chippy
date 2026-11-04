using System.Numerics;
using Chippy.Rendering;
using Microsoft.Xna.Framework.Graphics;
using Xna = Microsoft.Xna.Framework;

namespace Chippy.Backends.MonoGame;

internal class MonoGamePrimitives(GraphicsDevice device) : IDisposable
{
    private readonly BasicEffect effect = new(device) { VertexColorEnabled = true };
    private readonly RasterizerState rasterizer = new() { CullMode = CullMode.None };
    public DepthStencilState SolidDepth { get; } = CreateSolidDepth(writeDepth: true);
    private readonly DepthStencilState markerDepth = CreateSolidDepth(writeDepth: false);
    private readonly DepthStencilState lineDepth = new()
    {
        DepthBufferWriteEnable = false,
        StencilEnable = true,
        ReferenceStencil = 0,
        StencilMask = 1,
        StencilFunction = CompareFunction.Equal
    };
    private VertexPositionColor[] vertices = [];

    private static DepthStencilState CreateSolidDepth(bool writeDepth) => new()
    {
        DepthBufferWriteEnable = writeDepth,
        StencilEnable = true,
        ReferenceStencil = 1,
        StencilMask = 1,
        StencilWriteMask = 1,
        StencilPass = StencilOperation.Replace
    };

    public void Draw(ReadOnlySpan<PrimitiveVertex> data, PrimitiveTopology topology, in Matrix4x4 matrix, bool screenSpace, bool writeDepth)
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
        device.DepthStencilState = screenSpace ? DepthStencilState.None
            : topology != PrimitiveTopology.Triangles ? lineDepth
            : writeDepth ? SolidDepth : markerDepth;
        device.RasterizerState = rasterizer;
        var (kind, count) = topology switch
        {
            PrimitiveTopology.Lines => (PrimitiveType.LineList, data.Length / 2),
            PrimitiveTopology.LineStrip => (PrimitiveType.LineStrip, data.Length - 1),
            _ => (PrimitiveType.TriangleList, data.Length / 3)
        };
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(kind, vertices, 0, count);
        }
    }

    public void Dispose()
    {
        rasterizer.Dispose();
        SolidDepth.Dispose();
        markerDepth.Dispose();
        lineDepth.Dispose();
        effect.Dispose();
    }
}
