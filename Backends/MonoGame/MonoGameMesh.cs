using System.Numerics;
using Chippy.Models;
using Microsoft.Xna.Framework.Graphics;
using Xna = Microsoft.Xna.Framework;

namespace Chippy.Backends.MonoGame;

internal class MonoGameMesh : IDisposable
{
    private readonly VertexBuffer vertices;
    private readonly IndexBuffer indices;
    private readonly int triangleCount;
    public Vector4 Color { get; }

    public MonoGameMesh(GraphicsDevice device, SubMesh mesh)
    {
        Color = mesh.Color;
        triangleCount = mesh.Indices.Length / 3;
        var data = mesh.Vertices.Select(vertex => new VertexPositionNormalTexture(
            vertex.Position.ToXna(), vertex.Normal.ToXna(), Xna.Vector2.Zero)).ToArray();
        vertices = new VertexBuffer(device, VertexPositionNormalTexture.VertexDeclaration, data.Length, BufferUsage.WriteOnly);
        try
        {
            vertices.SetData(data);
            indices = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, mesh.Indices.Length, BufferUsage.WriteOnly);
            try { indices.SetData(mesh.Indices); }
            catch { indices.Dispose(); throw; }
        }
        catch
        {
            vertices.Dispose();
            throw;
        }
    }

    public void Draw(GraphicsDevice device)
    {
        device.SetVertexBuffer(vertices);
        device.Indices = indices;
        device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, triangleCount);
    }

    public void Dispose()
    {
        indices.Dispose();
        vertices.Dispose();
    }
}
