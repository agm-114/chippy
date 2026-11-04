using System.Numerics;
using System.Runtime.InteropServices;
using Arch.Core;
using Arch.Core.Extensions;
using Chippy.Components;
using Chippy.Models;

namespace Chippy.Rendering;

// World matrices must be resolved before drawing. GPU resources belong to the injected backend.
public class SceneRenderSystem(IRenderSystem renderer)
{
    private readonly QueryDescription meshQuery = new QueryDescription().WithAll<Transform, MeshRenderer>();
    private readonly QueryDescription lineQuery = new QueryDescription().WithAll<Transform, LineRenderer>();
    private readonly QueryDescription lightQuery = new QueryDescription().WithAll<Transform, Light>();
    private readonly List<(Transform, Light)> lights = [];
    private readonly List<(Entity Source, Mesh Mesh, Matrix4x4 Matrix, Vector3 Position)> drawnMeshes = [];
    private readonly List<(Entity Source, Vector3 Position, Marker Dot, Vector4 Color)> drawnDots = [];
    private readonly PrimitiveBatch primitives = new(renderer);
    private readonly record struct Marker(Vector3 Center, float Radius, float Depth, float SizeSquared);

    internal int DrawnMeshAssetCount => drawnMeshes.Select(entry => entry.Mesh).Distinct(ReferenceEqualityComparer.Instance).Count();

    internal Matrix4x4? GetDrawnMeshMatrix(Entity source)
    {
        foreach (var mesh in drawnMeshes)
            if (mesh.Source == source) return mesh.Matrix;
        return null;
    }

    public void Draw(World world, Vector2 viewportSize, Action<World, PrimitiveBatch, Vector2>? drawOverlay = null,
        Vector3 origin = default, (Matrix4x4 View, float ProjectionScale)? camera = null)
    {
        lights.Clear();
        world.Query(in lightQuery, (ref Transform transform, ref Light light) =>
        {
            if (!light.IsActive) return;
            var copy = RelativeTo(transform, origin);
            lights.Add((copy, light));
        });
        renderer.SetLights(CollectionsMarshal.AsSpan(lights));
        drawnMeshes.Clear();
        drawnDots.Clear();
        world.Query(in meshQuery, (Entity entity, ref Transform transform, ref MeshRenderer meshRenderer) =>
        {
            if (!meshRenderer.Enabled || meshRenderer.SharedMesh is not { } mesh) return;
            var copy = RelativeTo(transform, origin);
            var source = entity.TryGet<SourceEntity>(out var link) ? link.Entity : entity;
            var position = transform.LocalToWorldMatrix.Translation;
            if (camera is { } frame && entity.TryGet<DistantMarker>(out var marker)
                && GetMarker(mesh.Bounds, copy.LocalToWorldMatrix, frame.View, frame.ProjectionScale * viewportSize.Y / 2) is { } dot)
            {
                drawnDots.Add((source, position, dot, marker.Color));
                return;
            }
            renderer.DrawMesh(in copy, in meshRenderer);
            drawnMeshes.Add((source, mesh, copy.LocalToWorldMatrix, position));
        });
        if (camera is { } markerCamera && drawnDots.Count > 0)
        {
            drawnDots.Sort(static (a, b) =>
            {
                var bySize = a.Dot.SizeSquared.CompareTo(b.Dot.SizeSquared);
                return bySize != 0 ? bySize : b.Dot.Depth.CompareTo(a.Dot.Depth);
            });
            var view = markerCamera.View;
            var normal = new Vector3(view.M13, view.M23, view.M33);
            foreach (var (_, _, dot, color) in drawnDots)
                primitives.DrawMarker(dot.Center + origin, normal, dot.Radius, color);
        }
        world.Query(in lineQuery, (ref Transform transform, ref LineRenderer line) =>
        {
            if (!line.IsRenderable) return;
            var matrix = line.UseWorldSpace ? Matrix4x4.Identity : line.PointSpaceMatrix ?? transform.LocalToWorldMatrix;
            primitives.DrawPolyline(line.Positions.AsSpan(), matrix, line.Color, line.Loop);
        });
        drawOverlay?.Invoke(world, primitives, viewportSize);
        primitives.Flush(viewportSize, origin);
    }

    private static Marker? GetMarker(Bounds bounds, Matrix4x4 matrix, Matrix4x4 view, float focalLengthPixels)
    {
        const float dotRadiusPixels = 3;
        var center = Vector3.Transform(bounds.Center, matrix);
        var depth = -Vector3.Transform(center, view).Z;
        if (depth <= 0 || focalLengthPixels <= 0) return null;
        var radius = dotRadiusPixels * depth / focalLengthPixels;
        var size = bounds.Size;
        var x = Vector3.TransformNormal(new(size.X, 0, 0), matrix).LengthSquared();
        var y = Vector3.TransformNormal(new(0, size.Y, 0), matrix).LengthSquared();
        var z = Vector3.TransformNormal(new(0, 0, size.Z), matrix).LengthSquared();
        var sizeSquared = MathF.Max(x, MathF.Max(y, z));
        return sizeSquared < 4 * radius * radius ? new(center, radius, depth, sizeSquared) : null;
    }

    internal (Entity Entity, Vector3 Position)? Pick(Vector3 eye, Vector3 direction, float near, float far)
    {
        (Entity, Vector3)? hit = null;
        foreach (var (entity, mesh, matrix, position) in drawnMeshes)
        {
            if (!Matrix4x4.Invert(matrix, out var inverse)) continue;
            var localEye = Vector3.Transform(eye, inverse);
            var localDirection = Vector3.TransformNormal(direction, inverse);
            // Keep the ray parameter in view-depth units after transforming into mesh space.
            foreach (var part in mesh.SubMeshes)
                for (var i = 0; i < part.Indices.Length; i += 3)
                {
                    var a = part.Vertices[part.Indices[i]].Position;
                    var b = part.Vertices[part.Indices[i + 1]].Position;
                    var c = part.Vertices[part.Indices[i + 2]].Position;
                    var depth = TriangleDepth(localEye, localDirection, a, b, c);
                    if (depth >= near && depth < far)
                    {
                        far = depth;
                        hit = (entity, position);
                    }
                }
        }
        // Dots draw from smallest to largest, with depth testing against solid geometry.
        for (var i = drawnDots.Count - 1; i >= 0; i--)
        {
            var (entity, position, marker, _) = drawnDots[i];
            if (marker.Depth >= near && marker.Depth <= far
                && Vector3.DistanceSquared(eye + direction * marker.Depth, marker.Center) <= marker.Radius * marker.Radius)
                return (entity, position);
        }
        return hit;
    }

    internal static float TriangleDepth(Vector3 eye, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var p = Vector3.Cross(direction, ac);
        var determinant = Vector3.Dot(ab, p);
        if (determinant == 0) return float.NaN;

        var offset = eye - a;
        var u = Vector3.Dot(offset, p) / determinant;
        if (u < 0 || u > 1) return float.NaN;
        var q = Vector3.Cross(offset, ab);
        var v = Vector3.Dot(direction, q) / determinant;
        return v < 0 || u + v > 1 ? float.NaN : Vector3.Dot(ac, q) / determinant;
    }

    private static Transform RelativeTo(Transform transform, Vector3 origin)
    {
        var matrix = transform.LocalToWorldMatrix;
        matrix.Translation -= origin;
        transform.LocalToWorldMatrix = matrix;
        return transform;
    }
}
