using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using Brutal.GltfApi;
using Brutal.Numerics;

namespace Chippy.Models;

public record struct Vertex(Vector3 Position, Vector3 Normal);
public record SubMesh(Vertex[] Vertices, uint[] Indices, Vector4 Color);

public partial class Mesh(List<SubMesh> meshes)
{
    public IReadOnlyList<SubMesh> SubMeshes { get; } = meshes;
    public Bounds Bounds { get; } = CalculateBounds(meshes);
    public int VertexCount { get; } = meshes.Sum(mesh => mesh.Vertices.Length);
    public int TriangleCount { get; } = meshes.Sum(mesh => mesh.Indices.Length / 3);
    public Matrix4x4 Normalization => Bounds.Normalization;

    private static Bounds CalculateBounds(List<SubMesh> meshes)
    {
        var min = new Vector3(float.PositiveInfinity);
        var max = new Vector3(float.NegativeInfinity);
        foreach (var mesh in meshes)
            foreach (var vertex in mesh.Vertices)
            {
                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }
        if (!float.IsFinite(min.LengthSquared()) || !float.IsFinite(max.LengthSquared()))
            throw new ArgumentException("A model must contain finite vertex positions.", nameof(meshes));
        return new Bounds(min, max);
    }

    public static Mesh Load(string path)
    {
        using var loader = new GltfLoader(path);
        var document = loader.GltfJson;
        if (document.ExtensionsRequired is { Length: > 0 })
            throw new NotSupportedException($"Required glTF extensions are unsupported: {string.Join(", ", document.ExtensionsRequired)}");

        List<SubMesh> meshes = [];
        var nodes = document.Nodes ?? [];
        var children = nodes.SelectMany(node => node.Children ?? []).ToHashSet();
        var roots = document.Scenes is { Length: > 0 }
            ? document.Scenes[document.Scene ?? 0].Nodes ?? []
            : Enumerable.Range(0, nodes.Length).Where(index => !children.Contains(index));
        HashSet<int> ancestors = [];
        foreach (var root in roots)
            VisitNode(root, Matrix4x4.Identity);

        if (meshes.Count == 0)
            throw new InvalidDataException("The selected glTF scene contains no triangle meshes.");
        return new Mesh(meshes);

        void VisitNode(int index, Matrix4x4 parent)
        {
            if (!ancestors.Add(index))
                throw new InvalidDataException("The glTF node hierarchy contains a cycle.");
            var node = nodes[index];
            if (node.Skin.HasValue)
                throw new NotSupportedException("Skinned meshes are unsupported by this static model viewer.");

            var transform = LocalTransform(node) * parent;
            if (node.Mesh is int meshIndex)
                foreach (var primitive in document.Meshes[meshIndex].Primitives)
                    meshes.Add(ReadSubMesh(loader, primitive, transform));
            foreach (var child in node.Children ?? [])
                VisitNode(child, transform);
            ancestors.Remove(index);
        }
    }

    private static Matrix4x4 LocalTransform(Node node)
    {
        // Both matrix types use System.Numerics row-vector layout internally.
        var matrix = Unsafe.BitCast<float4x4, Matrix4x4>(node.Matrix);
        var scale = new Vector3(node.Scale.X, node.Scale.Y, node.Scale.Z);
        var rotation = new Quaternion(node.Rotation.X, node.Rotation.Y, node.Rotation.Z, node.Rotation.W);
        var translation = new Vector3(node.Translation.X, node.Translation.Y, node.Translation.Z);
        return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(translation) * matrix;
    }

    private static SubMesh ReadSubMesh(GltfLoader loader, MeshPrimitive primitive, Matrix4x4 transform)
    {
        if (primitive.Mode != MeshPrimitive.ModeEnum.Triangles)
            throw new NotSupportedException("Only triangle primitives are supported.");
        if (primitive.Targets is { Length: > 0 })
            throw new NotSupportedException("Morph targets are unsupported by this static model viewer.");
        if (!primitive.Attributes.TryGetValue("POSITION", out var positionIndex))
            throw new InvalidDataException("A mesh primitive has no POSITION attribute.");

        var positions = ReadVectors(loader, positionIndex);
        var indices = primitive.Indices is int indexAccessor
            ? ReadIndices(loader, indexAccessor)
            : Enumerable.Range(0, positions.Length).Select(index => (uint)index).ToArray();
        if (indices.Length == 0 || indices.Length % 3 != 0 || indices.Any(index => index >= positions.Length))
            throw new InvalidDataException("Triangle indices are empty, incomplete, or outside the vertex array.");

        var normals = primitive.Attributes.TryGetValue("NORMAL", out var normalIndex)
            ? ReadVectors(loader, normalIndex)
            : GenerateNormals(positions, indices);
        if (normals.Length != positions.Length)
            throw new InvalidDataException("POSITION and NORMAL accessor counts must match.");
        if (!Matrix4x4.Invert(transform, out var inverse))
            throw new InvalidDataException("A mesh node has a non-invertible transform.");

        // Mirrored nodes reverse winding; keep front faces aligned with transformed normals.
        if (transform.GetDeterminant() < 0)
            for (var i = 0; i < indices.Length; i += 3)
                (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);

        var normalTransform = Matrix4x4.Transpose(inverse);
        var vertices = new Vertex[positions.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var position = Vector3.Transform(positions[i], transform);
            var normal = Normalize(Vector3.TransformNormal(normals[i], normalTransform));
            if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
                throw new InvalidDataException("Mesh positions must be finite.");
            vertices[i] = new Vertex(position, normal);
        }

        var factor = primitive.Material is int material
            ? loader.GltfJson.Materials[material].PbrMetallicRoughness?.BaseColorFactor ?? new float4(1)
            : new float4(1);
        return new SubMesh(vertices, indices, new Vector4(factor.X, factor.Y, factor.Z, factor.W));
    }

    private static Vector3[] ReadVectors(GltfLoader loader, int index)
    {
        var accessor = loader.GltfJson.Accessors[index];
        if (accessor.Type != Accessor.TypeEnum.Vec3 || accessor.ComponentType != Accessor.ComponentTypeEnum.Float)
            throw new NotSupportedException("Positions and normals must use float32 VEC3 accessors.");
        var view = new AccessorView(loader, accessor, 12);
        var values = new Vector3[accessor.Count];
        for (var i = 0; i < values.Length; i++)
        {
            var bytes = view.Element(i);
            values[i] = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(bytes),
                BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]));
        }
        return values;
    }

    private static uint[] ReadIndices(GltfLoader loader, int index)
    {
        var accessor = loader.GltfJson.Accessors[index];
        var size = accessor.ComponentType switch
        {
            Accessor.ComponentTypeEnum.UnsignedByte => 1,
            Accessor.ComponentTypeEnum.UnsignedShort => 2,
            Accessor.ComponentTypeEnum.UnsignedInt => 4,
            _ => throw new NotSupportedException("Indices must be unsigned byte, ushort, or uint values.")
        };
        if (accessor.Type != Accessor.TypeEnum.Scalar || accessor.Normalized)
            throw new InvalidDataException("Indices must use non-normalized SCALAR accessors.");
        var view = new AccessorView(loader, accessor, size);
        var indices = new uint[accessor.Count];
        for (var i = 0; i < indices.Length; i++)
        {
            var bytes = view.Element(i);
            indices[i] = size switch
            {
                1 => bytes[0],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
                _ => BinaryPrimitives.ReadUInt32LittleEndian(bytes)
            };
        }
        return indices;
    }

    private static Vector3[] GenerateNormals(Vector3[] positions, uint[] indices)
    {
        var normals = new Vector3[positions.Length];
        for (var i = 0; i < indices.Length; i += 3)
        {
            var (a, b, c) = (indices[i], indices[i + 1], indices[i + 2]);
            var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
        }
        return normals;
    }

    private static Vector3 Normalize(Vector3 vector) =>
        float.IsFinite(vector.LengthSquared()) && vector.LengthSquared() > 1e-12f
            ? Vector3.Normalize(vector) : Vector3.UnitY;

    private readonly ref struct AccessorView
    {
        private readonly ReadOnlySpan<byte> data;
        private readonly int stride;
        private readonly int elementSize;

        public AccessorView(GltfLoader loader, Accessor accessor, int elementSize)
        {
            if (accessor.Sparse is not null || accessor.BufferView is not int bufferView)
                throw new NotSupportedException("Sparse accessors and accessors without buffer views are unsupported.");
            stride = loader.GltfJson.BufferViews[bufferView].ByteStride ?? elementSize;
            this.elementSize = elementSize;
            if (accessor.Count < 0 || stride < elementSize || accessor.ByteOffset < 0)
                throw new InvalidDataException("Invalid accessor count, stride, or offset.");
            var length = accessor.Count == 0 ? 0 : checked((accessor.Count - 1) * stride + elementSize);
            data = loader.GetBufferView(bufferView).Slice(accessor.ByteOffset, length);
        }

        public ReadOnlySpan<byte> Element(int index) => data.Slice(index * stride, elementSize);
    }
}
