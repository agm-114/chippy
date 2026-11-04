using System.Numerics;
using System.Runtime.InteropServices;
using Brutal.OpenGlApi;
using Chippy.Rendering;
using static Brutal.OpenGlApi.OpenGl;

namespace Chippy.Backends.Brutal;

internal unsafe class BrutalPrimitives : IDisposable
{
    private readonly GlBuffer[] buffers = new GlBuffer[1];
    private readonly GlVertexArray[] arrays = new GlVertexArray[1];
    private readonly GlProgram program;
    private readonly int matrixLocation;

    public BrutalPrimitives()
    {
        using var vertex = CreateShader(GlShaderType.Vertex);
        using var fragment = CreateShader(GlShaderType.Fragment);
        vertex.SetSource("""
            #version 330 core
            layout(location=0) in vec3 aPosition;
            layout(location=1) in vec4 aColor;
            uniform mat4 uViewProjection;
            out vec4 color;
            void main() { gl_Position = uViewProjection * vec4(aPosition, 1.0); color = aColor; }
            """u8).Compile();
        fragment.SetSource("""
            #version 330 core
            in vec4 color;
            out vec4 fragmentColor;
            void main() { fragmentColor = color; }
            """u8).Compile();
        program = CreateProgram();
        try
        {
            program.Attach(vertex).Attach(fragment).Link();
            matrixLocation = program.GetUniformLocation("uViewProjection\0"u8);
            GenVertexArrays(arrays);
            GenBuffers(buffers);
            arrays[0].Bind();
            buffers[0].Bind(GlBindTarget.ArrayBuffer);
            var stride = Marshal.SizeOf<PrimitiveVertex>();
            VertexAttribute(new GlAttribute(0, 3, GL_FLOAT, GlScaling.Float, stride, 0));
            VertexAttribute(new GlAttribute(1, 4, GL_FLOAT, GlScaling.Float, stride, 12));
            EnableVertexAttribArray(0);
            EnableVertexAttribArray(1);
        }
        catch { Dispose(); throw; }
    }

    public void Draw(ReadOnlySpan<PrimitiveVertex> vertices, PrimitiveTopology topology, in Matrix4x4 matrix, bool screenSpace, bool writeDepth)
    {
        if (vertices.IsEmpty) return;
        program.Use();
        if (screenSpace)
        {
            Disable(GlCapability.DepthTest);
            Disable(GlCapability.StencilTest);
        }
        else SetWorldDepth(topology != PrimitiveTopology.Triangles, writeDepth);
        Disable(GlCapability.CullFace);
        Enable(GlCapability.Blend);
        Native.glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
        PolygonMode(GlFace.FrontAndBack, GL_FILL);
        fixed (Matrix4x4* pointer = &matrix)
            Native.glUniformMatrix4fv(matrixLocation, 1, false, (float*)pointer);
        arrays[0].Bind();
        buffers[0].Bind(GlBindTarget.ArrayBuffer);
        fixed (PrimitiveVertex* pointer = vertices)
            Native.glBufferData((GlEnum)(uint)GlBindTarget.ArrayBuffer, vertices.Length * sizeof(PrimitiveVertex), pointer, GL_DYNAMIC_DRAW);
        var mode = topology switch
        {
            PrimitiveTopology.Lines => GL_LINES,
            PrimitiveTopology.LineStrip => GL_LINE_STRIP,
            _ => GL_TRIANGLES
        };
        Native.glDrawArrays(mode, 0, vertices.Length);
        Disable(GlCapability.StencilTest);
        Native.glDepthMask(true);
    }

    internal static void SetWorldDepth(bool lines, bool writeDepth = true)
    {
        Enable(GlCapability.DepthTest);
        Enable(GlCapability.StencilTest);
        Native.glDepthMask(!lines && writeDepth);
        Native.glStencilMask(1);
        Native.glStencilFunc((GlFunc)(lines ? GL_EQUAL : GL_ALWAYS), lines ? 0 : 1, 1);
        Native.glStencilOp((GlStencilOp)GL_KEEP, (GlStencilOp)GL_KEEP, (GlStencilOp)(lines ? GL_KEEP : GL_REPLACE));
    }

    public void Dispose()
    {
        DeleteBuffers(buffers);
        DeleteVertexArrays(arrays);
        program.Dispose();
    }
}
