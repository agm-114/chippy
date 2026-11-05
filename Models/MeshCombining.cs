using System.Numerics;

namespace Chippy.Models;

public partial class Mesh
{
    internal static Mesh Combine(IEnumerable<(Mesh Mesh, Matrix4x4 Transform, Vector4 Tint)> parts)
    {
        var batches = new Dictionary<Vector4, (List<Vertex> Vertices, List<uint> Indices)>();
        foreach (var (mesh, transform, tint) in parts)
        {
            if (!Matrix4x4.Invert(transform, out var inverse))
                throw new InvalidDataException("A mesh part has a non-invertible transform.");

            var normals = Matrix4x4.Transpose(inverse);
            var mirrored = transform.GetDeterminant() < 0;
            foreach (var subMesh in mesh.SubMeshes)
            {
                var color = subMesh.Color * tint;
                if (!batches.TryGetValue(color, out var batch))
                    batches.Add(color, batch = ([], []));

                var start = (uint)batch.Vertices.Count;
                foreach (var vertex in subMesh.Vertices)
                    batch.Vertices.Add(new(Vector3.Transform(vertex.Position, transform),
                        Normalize(Vector3.TransformNormal(vertex.Normal, normals))));
                for (var i = 0; i < subMesh.Indices.Length; i += 3)
                {
                    batch.Indices.Add(start + subMesh.Indices[i]);
                    batch.Indices.Add(start + subMesh.Indices[i + (mirrored ? 2 : 1)]);
                    batch.Indices.Add(start + subMesh.Indices[i + (mirrored ? 1 : 2)]);
                }
            }
        }
        return new(batches.Select(batch => new SubMesh([.. batch.Value.Vertices], [.. batch.Value.Indices], batch.Key)).ToList());
    }
}
