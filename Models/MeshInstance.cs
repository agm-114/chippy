using System.Numerics;

namespace Chippy.Models;

// Per-instance data only: a future ECS can store these values as separate components.
public record struct MeshInstance(Mesh SharedMesh)
{
    public Transform Transform = new();
    public Vector4 Tint = Vector4.One;
    public bool Visible = true;
}
