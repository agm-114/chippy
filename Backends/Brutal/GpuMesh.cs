using System.Numerics;
using System.Runtime.InteropServices;
using Brutal.OpenGlApi;
using Chippy.Models;
using static Brutal.OpenGlApi.OpenGl;

namespace Chippy.Backends.Brutal;

internal class GpuMesh : IDisposable
{
    private readonly GlBuffer[] buffers = new GlBuffer[2];
    private readonly GlVertexArray[] arrays = new GlVertexArray[1];
    private readonly int indexCount;
    public Vector4 Color { get; }

    public GpuMesh(SubMesh mesh)
    {
        Color = mesh.Color;
        indexCount = mesh.Indices.Length;
        GenVertexArrays(arrays);
        GenBuffers(buffers);
        arrays[0].Bind();
        Upload(buffers[0], GlBindTarget.ArrayBuffer, mesh.Vertices.AsSpan());
        Upload(buffers[1], GlBindTarget.ElementArrayBuffer, mesh.Indices.AsSpan());
        var stride = Marshal.SizeOf<Vertex>();
        VertexAttribute(new GlAttribute(0, 3, GL_FLOAT, GlScaling.Float, stride, 0));
        VertexAttribute(new GlAttribute(1, 3, GL_FLOAT, GlScaling.Float, stride, 12));
        EnableVertexAttribArray(0);
        EnableVertexAttribArray(1);
    }

    public unsafe void Draw()
    {
        arrays[0].Bind();
        Native.glDrawElements(GL_TRIANGLES, indexCount, GL_UNSIGNED_INT, null);
    }

    private static unsafe void Upload<T>(GlBuffer buffer, GlBindTarget target, Span<T> data) where T : unmanaged
    {
        buffer.Bind(target);
        fixed (T* pointer = data)
            Native.glBufferData((GlEnum)(uint)target, data.Length * sizeof(T), pointer, GL_STATIC_DRAW);
    }

    public void Dispose()
    {
        DeleteBuffers(buffers);
        DeleteVertexArrays(arrays);
    }
}
