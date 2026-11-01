using System.Numerics;
using Chippy.Models;

namespace Chippy.Rendering;

public interface IRenderSystem : IDisposable
{
    int UploadedMeshCount { get; }

    // Matrices use System.Numerics conventions, including a [0, 1] projection depth range.
    void BeginFrame(in Matrix4x4 view, in Matrix4x4 projection, bool wireframe = false);
    void DrawMesh(Mesh mesh, in Matrix4x4 transform, Vector4? tint = null);
    bool ReleaseMesh(Mesh mesh);

    void DrawMesh(in MeshInstance instance)
    {
        if (instance.Visible)
            DrawMesh(instance.SharedMesh, instance.Transform.LocalToWorldMatrix, instance.Tint);
    }
}
