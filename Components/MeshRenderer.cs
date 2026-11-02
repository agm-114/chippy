using System.Numerics;
using Chippy.Models;

namespace Chippy.Components;

public record struct MeshRenderer(Mesh? SharedMesh)
{
    public Vector4 Tint = Vector4.One;
    public bool Enabled = true;

    public MeshRenderer() : this(null) { }
}
