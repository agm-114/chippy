using System.Numerics;
using Brutal.Numerics;
using Brutal.OpenGlApi;
using Chippy.Components;
using Chippy.Models;
using Chippy.Rendering;
using static Brutal.OpenGlApi.OpenGl;

namespace Chippy.Backends.Brutal;

public class BrutalRenderSystem : IRenderSystem
{
    private readonly Dictionary<Mesh, GpuMesh[]> uploaded = new(ReferenceEqualityComparer.Instance);
    private readonly GlProgram program;
    private readonly int modelLocation;
    private readonly int normalLocation;
    private readonly int viewProjectionLocation;
    private readonly int colorLocation;
    private readonly int unlitLocation;
    private readonly BrutalPrimitives primitives;
    private Matrix4x4 viewProjection;
    private bool disposed;

    public int UploadedMeshCount => uploaded.Count;

    // Construct, draw, and dispose on the thread with the initialized OpenGL context current.
    public BrutalRenderSystem(string? shaderDirectory = null)
    {
        shaderDirectory ??= Path.Combine(AppContext.BaseDirectory, "Backends", "Brutal", "Shaders");
        using var vertexShader = CreateShader(GlShaderType.Vertex);
        using var fragmentShader = CreateShader(GlShaderType.Fragment);
        vertexShader.SetSource(File.ReadAllText(Path.Combine(shaderDirectory, "Model.vert"))).Compile();
        fragmentShader.SetSource(File.ReadAllText(Path.Combine(shaderDirectory, "Model.frag"))).Compile();
        program = CreateProgram();
        try
        {
            program.Attach(vertexShader).Attach(fragmentShader).Link();
            modelLocation = program.GetUniformLocation("uModel\0"u8);
            normalLocation = program.GetUniformLocation("uNormalMatrix\0"u8);
            viewProjectionLocation = program.GetUniformLocation("uViewProjection\0"u8);
            colorLocation = program.GetUniformLocation("uColor\0"u8);
            unlitLocation = program.GetUniformLocation("uUnlit\0"u8);
            primitives = new();
        }
        catch
        {
            program.Dispose();
            throw;
        }
    }

    public void BeginFrame(in Matrix4x4 view, in Matrix4x4 projection, bool wireframe = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        program.Use();
        Disable(GlCapability.Blend);
        Enable(GlCapability.DepthTest);
        PolygonMode(GlFace.FrontAndBack, wireframe ? GL_LINE : GL_FILL);
        // Convert the shared [0, 1] depth convention to OpenGL's [-1, 1].
        var clipDepth = Matrix4x4.Identity;
        clipDepth.M33 = 2;
        clipDepth.M43 = -1;
        viewProjection = view * projection * clipDepth;
        SetMatrix(viewProjectionLocation, viewProjection);
    }

    public void DrawMesh(in Transform transform, in MeshRenderer meshRenderer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var mesh = meshRenderer.SharedMesh;
        ArgumentNullException.ThrowIfNull(mesh);
        var matrix = transform.LocalToWorldMatrix;
        var normalMatrix = matrix.GetNormalMatrix();

        if (!uploaded.TryGetValue(mesh, out var meshes))
        {
            List<GpuMesh> created = [];
            try
            {
                foreach (var subMesh in mesh.SubMeshes)
                    created.Add(new GpuMesh(subMesh));
                meshes = [.. created];
                uploaded.Add(mesh, meshes);
            }
            catch
            {
                created.ForEach(gpuMesh => gpuMesh.Dispose());
                throw;
            }
        }

        SetMatrix(modelLocation, matrix);
        Uniform(unlitLocation, meshRenderer.Unlit ? 1 : 0);
        SetMatrix(normalLocation, normalMatrix);
        SetFrontFace(matrix.GetDeterminant() < 0);
        foreach (var gpuMesh in meshes)
        {
            var color = gpuMesh.Color * meshRenderer.Tint;
            Uniform(colorLocation, new float4(color.X, color.Y, color.Z, color.W));
            gpuMesh.Draw();
        }
    }

    public void DrawPrimitives(ReadOnlySpan<PrimitiveVertex> vertices, PrimitiveTopology topology, bool screenSpace = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        primitives.Draw(vertices, topology, screenSpace ? Matrix4x4.Identity : viewProjection, screenSpace);
    }

    // Release cached GPU buffers; the shared CPU mesh remains valid and can be drawn again.
    public bool ReleaseMesh(Mesh mesh)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(mesh);
        if (!uploaded.Remove(mesh, out var meshes))
            return false;
        Array.ForEach(meshes, gpuMesh => gpuMesh.Dispose());
        return true;
    }

    private static unsafe void SetMatrix(int location, in Matrix4x4 matrix)
    {
        fixed (Matrix4x4* pointer = &matrix)
            Native.glUniformMatrix4fv(location, 1, false, (float*)pointer);
    }

    private static unsafe void SetFrontFace(bool mirrored) => Native.glFrontFace((GlFrontFace)(mirrored ? GL_CW : GL_CCW));

    public void Dispose()
    {
        if (disposed)
            return;
        foreach (var meshes in uploaded.Values)
            Array.ForEach(meshes, mesh => mesh.Dispose());
        uploaded.Clear();
        primitives.Dispose();
        program.Dispose();
        disposed = true;
    }
}
