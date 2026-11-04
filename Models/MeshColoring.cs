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

    /// <summary>Colors a sphere with broad landmasses and irregular coastlines.</summary>
    public Mesh WithContinents(Vector4 waterColor, Vector4 landColor, float landCoverage = 0.3f, int seed = 42) =>
        WithLandmasses(waterColor, landColor, landCoverage, seed, count: 6, sharpness: 8, coastDetail: 0.16f);

    /// <summary>Colors a sphere with smaller land clusters scattered through the water.</summary>
    public Mesh WithIslands(Vector4 waterColor, Vector4 landColor, float landCoverage = 0.15f, int seed = 42) =>
        WithLandmasses(waterColor, landColor, landCoverage, seed, count: 16, sharpness: 40, coastDetail: 0.045f);

    private Mesh WithLandmasses(Vector4 waterColor, Vector4 landColor, float landCoverage, int seed,
        int count, int sharpness, float coastDetail)
    {
        ValidateCoverage(landCoverage);
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
        var elevations = new List<float>(TriangleCount);
        foreach (var surface in SubMeshes)
            for (var i = 0; i < surface.Indices.Length; i += 3)
            {
                var indices = surface.Indices.AsSpan(i, 3);
                var centroid = (surface.Vertices[indices[0]].Position + surface.Vertices[indices[1]].Position
                    + surface.Vertices[indices[2]].Position) / 3 - origin;
                var direction = centroid == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(centroid);
                var elevation = 0f;
                foreach (var center in centers)
                    elevation += MathF.Pow(MathF.Max(0, Vector3.Dot(direction, center)), sharpness);
                var coast = MathF.Sin(17 * direction.X + 7 * direction.Z) * MathF.Sin(13 * direction.Y - 5 * direction.X);
                elevations.Add(elevation + coastDetail * coast);
            }

        // Rank faces so coverage stays predictable even when elevations tie.
        var land = new bool[elevations.Count];
        var landCount = (int)MathF.Round(landCoverage * elevations.Count);
        foreach (var face in Enumerable.Range(0, elevations.Count).OrderByDescending(i => elevations[i]).Take(landCount))
            land[face] = true;
        return WithFaceColors(waterColor, landColor, face => land[face]);
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
