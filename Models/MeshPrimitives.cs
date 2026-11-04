using System.Numerics;

namespace Chippy.Models;

// Centered meshes with outward counterclockwise triangles. Round shapes use the Y axis.
public partial class Mesh
{
    public static Mesh CreateCube(float size = 1)
    {
        Positive(size, nameof(size));
        List<Vertex> vertices = [];
        List<uint> indices = [];
        var half = size / 2;
        foreach (var normal in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
        {
            var right = MathF.Abs(normal.Y) < 0.5f ? Vector3.Cross(Vector3.UnitY, normal) : Vector3.UnitX;
            var up = Vector3.Cross(normal, right);
            var center = normal * half;
            AddFace(vertices, indices, center - (right + up) * half, center + (right - up) * half,
                center + (right + up) * half, center + (up - right) * half);
        }
        return new([new([.. vertices], [.. indices], Vector4.One)]);
    }

    // Quad lies in XY, facing +Z. Plane lies in XZ, facing +Y.
    public static Mesh CreateQuad(float width = 1, float height = 1) => CreateFlat(width, height, Vector3.UnitX, Vector3.UnitY);
    public static Mesh CreatePlane(float width = 10, float length = 10) => CreateFlat(width, length, Vector3.UnitX, -Vector3.UnitZ);

    public static Mesh CreateSphere(int segments = 32, int rings = 16, float radius = 1)
    {
        Positive(radius, nameof(radius));
        if (rings < 2) throw new ArgumentOutOfRangeException(nameof(rings));
        var profile = new Vector2[rings + 1];
        var normals = new Vector2[rings + 1];
        for (var i = 0; i <= rings; i++)
        {
            var angle = i * MathF.PI / rings;
            normals[i] = new(MathF.Sin(angle), MathF.Cos(angle));
            profile[i] = normals[i] * radius;
        }
        profile[0].X = profile[^1].X = 0;
        return Revolve(profile, normals, segments);
    }

    /// <summary>Creates a sphere with more evenly sized triangles by subdividing an icosahedron.</summary>
    public static Mesh CreateIcosphere(int subdivisions = 3, float radius = 1)
    {
        Positive(radius, nameof(radius));
        if (subdivisions is < 0 or > 8) throw new ArgumentOutOfRangeException(nameof(subdivisions));

        var t = (1 + MathF.Sqrt(5)) / 2;
        List<Vector3> directions =
        [
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1)
        ];
        for (var i = 0; i < directions.Count; i++) directions[i] = Vector3.Normalize(directions[i]);

        uint[] indices =
        [
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        ];

        // Adjacent triangles share one projected midpoint for each undirected edge.
        Dictionary<(uint, uint), uint> midpoints = [];
        for (var level = 0; level < subdivisions; level++)
        {
            midpoints.Clear();
            var refined = new uint[indices.Length * 4];
            for (var i = 0; i < indices.Length; i += 3)
            {
                var (a, b, c) = (indices[i], indices[i + 1], indices[i + 2]);
                var ab = Midpoint(a, b);
                var bc = Midpoint(b, c);
                var ca = Midpoint(c, a);
                var offset = i * 4;
                WriteFace(refined, offset, a, ab, ca);
                WriteFace(refined, offset + 3, b, bc, ab);
                WriteFace(refined, offset + 6, c, ca, bc);
                WriteFace(refined, offset + 9, ab, bc, ca);
            }
            indices = refined;
        }

        var vertices = directions.Select(normal => new Vertex(normal * radius, normal)).ToArray();
        return new([new(vertices, indices, Vector4.One)]);

        uint Midpoint(uint a, uint b)
        {
            var edge = a < b ? (a, b) : (b, a);
            if (midpoints.TryGetValue(edge, out var index)) return index;
            index = (uint)directions.Count;
            directions.Add(Vector3.Normalize(directions[(int)a] + directions[(int)b]));
            midpoints.Add(edge, index);
            return index;
        }

        static void WriteFace(uint[] buffer, int offset, uint a, uint b, uint c)
        {
            buffer[offset] = a;
            buffer[offset + 1] = b;
            buffer[offset + 2] = c;
        }
    }

    public static Mesh CreateCylinder(float radius = 0.5f, float height = 2, int segments = 32) =>
        CreateFrustum(radius, radius, height, segments);

    public static Mesh CreateCone(float radius = 0.5f, float height = 1, int segments = 32) =>
        CreateFrustum(radius, 0, height, segments);

    public static Mesh CreateFrustum(float bottomRadius, float topRadius, float height = 1, int segments = 32)
    {
        if (!float.IsFinite(bottomRadius) || bottomRadius < 0) throw new ArgumentOutOfRangeException(nameof(bottomRadius));
        if (!float.IsFinite(topRadius) || topRadius < 0) throw new ArgumentOutOfRangeException(nameof(topRadius));
        Positive(MathF.Max(bottomRadius, topRadius), nameof(bottomRadius));
        Positive(height, nameof(height));
        var slope = Vector2.Normalize(new Vector2(height, bottomRadius - topRadius));
        return Revolve(
            [new(0, height / 2), new(topRadius, height / 2), new(topRadius, height / 2),
             new(bottomRadius, -height / 2), new(bottomRadius, -height / 2), new(0, -height / 2)],
            [Vector2.UnitY, Vector2.UnitY, slope, slope, -Vector2.UnitY, -Vector2.UnitY], segments);
    }

    // Regular polygon cross section with flat side normals: 3 = triangular prism, 6 = hexagonal.
    public static Mesh CreatePrism(int sides = 3, float radius = 0.5f, float height = 1)
    {
        if (sides < 3) throw new ArgumentOutOfRangeException(nameof(sides));
        Positive(radius, nameof(radius));
        Positive(height, nameof(height));
        List<Vertex> vertices = [];
        List<uint> indices = [];
        for (var i = 0; i < sides; i++)
        {
            var a = i * MathF.Tau / sides;
            var b = (i + 1) * MathF.Tau / sides;
            var topA = new Vector3(MathF.Cos(a) * radius, height / 2, MathF.Sin(a) * radius);
            var topB = new Vector3(MathF.Cos(b) * radius, height / 2, MathF.Sin(b) * radius);
            var bottomA = topA - Vector3.UnitY * height;
            var bottomB = topB - Vector3.UnitY * height;
            AddFace(vertices, indices, topA, topB, bottomB, bottomA);
            AddTriangle(vertices, indices, Vector3.UnitY * height / 2, topB, topA);
            AddTriangle(vertices, indices, -Vector3.UnitY * height / 2, bottomA, bottomB);
        }
        return new([new([.. vertices], [.. indices], Vector4.One)]);
    }

    // Height includes the hemispherical ends and must be at least twice the radius.
    public static Mesh CreateCapsule(float radius = 0.5f, float height = 2, int segments = 32, int hemisphereRings = 8)
    {
        Positive(radius, nameof(radius));
        if (!float.IsFinite(height) || height < 2 * radius) throw new ArgumentOutOfRangeException(nameof(height));
        if (hemisphereRings < 2) throw new ArgumentOutOfRangeException(nameof(hemisphereRings));
        var profile = new Vector2[2 * (hemisphereRings + 1)];
        var normals = new Vector2[profile.Length];
        var offset = height / 2 - radius;
        for (var i = 0; i <= hemisphereRings; i++)
        {
            var angle = i * MathF.PI / (2 * hemisphereRings);
            normals[i] = new(MathF.Sin(angle), MathF.Cos(angle));
            profile[i] = normals[i] * radius + new Vector2(0, offset);
            var lower = i + hemisphereRings + 1;
            normals[lower] = new(MathF.Cos(angle), -MathF.Sin(angle));
            profile[lower] = normals[lower] * radius - new Vector2(0, offset);
        }
        profile[0].X = profile[^1].X = 0;
        return Revolve(profile, normals, segments);
    }

    public static Mesh CreateTorus(float majorRadius = 1, float tubeRadius = 0.25f, int segments = 48, int tubeSegments = 16)
    {
        Positive(majorRadius, nameof(majorRadius));
        Positive(tubeRadius, nameof(tubeRadius));
        if (tubeRadius >= majorRadius) throw new ArgumentOutOfRangeException(nameof(tubeRadius));
        if (segments < 3) throw new ArgumentOutOfRangeException(nameof(segments));
        if (tubeSegments < 3) throw new ArgumentOutOfRangeException(nameof(tubeSegments));
        var vertices = new Vertex[(segments + 1) * (tubeSegments + 1)];
        List<uint> indices = [];
        for (var i = 0; i <= segments; i++)
            for (var j = 0; j <= tubeSegments; j++)
            {
                var angle = i * MathF.Tau / segments;
                var tubeAngle = j * MathF.Tau / tubeSegments;
                var radial = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
                var normal = radial * MathF.Cos(tubeAngle) + Vector3.UnitY * MathF.Sin(tubeAngle);
                vertices[i * (tubeSegments + 1) + j] = new(radial * majorRadius + normal * tubeRadius, normal);
                if (i == segments || j == tubeSegments) continue;
                var a = (uint)(i * (tubeSegments + 1) + j);
                var b = a + (uint)tubeSegments + 1;
                indices.AddRange([a, a + 1, b, a + 1, b + 1, b]);
            }
        return new([new(vertices, [.. indices], Vector4.One)]);
    }

    private static Mesh CreateFlat(float width, float height, Vector3 right, Vector3 up)
    {
        Positive(width, nameof(width));
        Positive(height, nameof(height));
        right *= width / 2;
        up *= height / 2;
        List<Vertex> vertices = [];
        List<uint> indices = [];
        AddFace(vertices, indices, -right - up, right - up, right + up, -right + up);
        return new([new([.. vertices], [.. indices], Vector4.One)]);
    }

    // Profiles run top to bottom. Repeated positions split normals at cap/side boundaries.
    private static Mesh Revolve(ReadOnlySpan<Vector2> profile, ReadOnlySpan<Vector2> normals, int segments)
    {
        if (segments < 3) throw new ArgumentOutOfRangeException(nameof(segments));
        var vertices = new Vertex[profile.Length * (segments + 1)];
        List<uint> indices = [];
        for (var y = 0; y < profile.Length; y++)
            for (var x = 0; x <= segments; x++)
            {
                var angle = x * MathF.Tau / segments;
                var radial = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
                vertices[y * (segments + 1) + x] = new(radial * profile[y].X + Vector3.UnitY * profile[y].Y,
                    radial * normals[y].X + Vector3.UnitY * normals[y].Y);
            }
        for (var y = 0; y < profile.Length - 1; y++)
        {
            if (Vector2.DistanceSquared(profile[y], profile[y + 1]) < 1e-12f) continue;
            for (var x = 0; x < segments; x++)
            {
                var a = (uint)(y * (segments + 1) + x);
                var b = a + (uint)segments + 1;
                if (profile[y].X > 0) indices.AddRange([a, a + 1, b]);
                if (profile[y + 1].X > 0) indices.AddRange([a + 1, b + 1, b]);
            }
        }
        return new([new(vertices, [.. indices], Vector4.One)]);
    }

    private static void AddFace(List<Vertex> vertices, List<uint> indices, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        var start = (uint)vertices.Count;
        var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        vertices.AddRange([new(a, normal), new(b, normal), new(c, normal), new(d, normal)]);
        indices.AddRange([start, start + 1, start + 2, start, start + 2, start + 3]);
    }

    private static void AddTriangle(List<Vertex> vertices, List<uint> indices, Vector3 a, Vector3 b, Vector3 c)
    {
        var start = (uint)vertices.Count;
        var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        vertices.AddRange([new(a, normal), new(b, normal), new(c, normal)]);
        indices.AddRange([start, start + 1, start + 2]);
    }

    private static void Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }
}
