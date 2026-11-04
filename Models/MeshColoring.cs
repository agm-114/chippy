using System.Numerics;

namespace Chippy.Models;

public partial class Mesh
{
    /// <summary>Chooses a color independently for each triangle using a seeded random sequence.</summary>
    public Mesh WithFaceNoise(Vector4 baseColor, Vector4 noiseColor, float coverage = 0.3f, int seed = 42)
    {
        ValidateCoverage(coverage);
        var random = new Random(seed);
        return WithFaceColors(baseColor, noiseColor, _ => random.NextDouble() < coverage);
    }

    /// <summary>Colors faces on a spherical mesh in large, irregular patches.</summary>
    public Mesh WithBroadClusters(Vector4 baseColor, Vector4 accentColor, float coverage = 0.3f, int seed = 42) =>
        WithClusters(baseColor, accentColor, coverage, seed, count: 6, sharpness: 8, edgeDetail: 0.16f);

    /// <summary>Colors faces on a spherical mesh in smaller, scattered patches.</summary>
    public Mesh WithFineClusters(Vector4 baseColor, Vector4 accentColor, float coverage = 0.15f, int seed = 42) =>
        WithClusters(baseColor, accentColor, coverage, seed, count: 16, sharpness: 40, edgeDetail: 0.045f);

    private Mesh WithClusters(Vector4 baseColor, Vector4 accentColor, float coverage, int seed,
        int count, int sharpness, float edgeDetail)
    {
        ValidateCoverage(coverage);
        var random = new Random(seed);
        var centers = new Vector3[count];
        for (var i = 0; i < centers.Length; i++)
        {
            var y = random.NextSingle() * 2 - 1;
            var angle = random.NextSingle() * MathF.Tau;
            var ringRadius = MathF.Sqrt(1 - y * y);
            centers[i] = new(ringRadius * MathF.Cos(angle), y, ringRadius * MathF.Sin(angle));
        }

        var origin = Bounds.Center;
        var scores = new List<float>(TriangleCount);
        foreach (var surface in SubMeshes)
            for (var i = 0; i < surface.Indices.Length; i += 3)
            {
                var indices = surface.Indices.AsSpan(i, 3);
                var centroid = (surface.Vertices[indices[0]].Position + surface.Vertices[indices[1]].Position
                    + surface.Vertices[indices[2]].Position) / 3 - origin;
                var direction = centroid == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(centroid);
                var score = 0f;
                foreach (var center in centers)
                    score += MathF.Pow(MathF.Max(0, Vector3.Dot(direction, center)), sharpness);
                var detail = MathF.Sin(17 * direction.X + 7 * direction.Z) * MathF.Sin(13 * direction.Y - 5 * direction.X);
                scores.Add(score + edgeDetail * detail);
            }

        // Rank faces so coverage stays predictable even when scores tie.
        var accentFaces = new bool[scores.Count];
        var accentCount = (int)MathF.Round(coverage * scores.Count);
        foreach (var face in Enumerable.Range(0, scores.Count).OrderByDescending(i => scores[i]).Take(accentCount))
            accentFaces[face] = true;
        return WithFaceColors(baseColor, accentColor, face => accentFaces[face]);
    }

    private Mesh WithFaceColors(Vector4 baseColor, Vector4 accentColor, Func<int, bool> isAccent)
    {
        var colored = new List<SubMesh>();
        var faceIndex = 0;
        foreach (var surface in SubMeshes)
        {
            List<uint> baseFaces = [], accentFaces = [];
            for (var i = 0; i < surface.Indices.Length; i += 3)
                (isAccent(faceIndex++) ? accentFaces : baseFaces).AddRange(surface.Indices.AsSpan(i, 3));

            if (baseFaces.Count > 0 || accentFaces.Count == 0) colored.Add(new(surface.Vertices, [.. baseFaces], baseColor));
            if (accentFaces.Count > 0) colored.Add(new(surface.Vertices, [.. accentFaces], accentColor));
        }
        return new(colored);
    }

    private static void ValidateCoverage(float coverage)
    {
        if (!float.IsFinite(coverage) || coverage is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(coverage));
    }
}
