using System.Numerics;
using Chippy.Components;
using Chippy.Models;

namespace Chippy.Rendering;

public interface IRenderSystem : IDisposable
{
    int UploadedMeshCount { get; }

    // Matrices use System.Numerics conventions, including a [0, 1] projection depth range.
    void BeginFrame(in Matrix4x4 view, in Matrix4x4 projection, bool wireframe = false);
    void DrawMesh(in Transform transform, in MeshRenderer meshRenderer);
    bool ReleaseMesh(Mesh mesh);
}
