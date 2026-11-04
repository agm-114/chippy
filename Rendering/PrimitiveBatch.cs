using System.Numerics;
using System.Runtime.InteropServices;

namespace Chippy.Rendering;

public enum PrimitiveTopology { Lines, Triangles }
public record struct PrimitiveVertex(Vector3 Position, Vector4 Color);

// World coordinates for Vector3 overloads; framebuffer pixels, top-left origin, for Vector2 overloads.
public class PrimitiveBatch(IRenderSystem renderer)
{
    private readonly List<PrimitiveVertex> lines = [], triangles = [], overlay = [];

    public void DrawLine(Vector3 start, Vector3 end, Vector4 color)
    {
        lines.Add(new(start, color));
        lines.Add(new(end, color));
    }

    public void DrawTriangle(Vector3 a, Vector3 b, Vector3 c, Vector4 color) => AddTriangle(triangles, a, b, c, color);

    public void DrawPolyline(ReadOnlySpan<Vector3> positions, in Matrix4x4 transform, Vector4 color, bool loop = false)
    {
        for (var i = 1; i < positions.Length; i++)
            DrawLine(Vector3.Transform(positions[i - 1], transform), Vector3.Transform(positions[i], transform), color);
        if (loop && positions.Length > 2)
            DrawLine(Vector3.Transform(positions[^1], transform), Vector3.Transform(positions[0], transform), color);
    }

    public void DrawCircle(Vector3 center, Vector3 normal, float radius, Vector4 color, int segments = 64)
    {
        ValidateCircle(radius, segments);
        if (!float.IsFinite(normal.LengthSquared()) || normal.LengthSquared() < 1e-12f)
            throw new ArgumentException("A circle needs a finite, nonzero normal.", nameof(normal));
        normal = Vector3.Normalize(normal);
        var right = Vector3.Normalize(Vector3.Cross(normal, Math.Abs(normal.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX));
        var up = Vector3.Cross(right, normal);
        var previous = center + right * radius;
        for (var i = 1; i <= segments; i++)
        {
            var angle = i * MathF.Tau / segments;
            var next = center + radius * (right * MathF.Cos(angle) + up * MathF.Sin(angle));
            DrawLine(previous, next, color);
            previous = next;
        }
    }

    public void DrawWireSphere(Vector3 center, float radius, Vector4 color, int segments = 64)
    {
        DrawCircle(center, Vector3.UnitX, radius, color, segments);
        DrawCircle(center, Vector3.UnitY, radius, color, segments);
        DrawCircle(center, Vector3.UnitZ, radius, color, segments);
    }

    public void DrawWireBox(Vector3 center, Vector3 size, Vector4 color)
    {
        Span<Vector3> corners = stackalloc Vector3[8];
        for (var i = 0; i < corners.Length; i++)
            corners[i] = center + size * new Vector3((i & 1) == 0 ? -0.5f : 0.5f,
                (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
        for (var i = 0; i < corners.Length; i++)
            for (var axis = 1; axis <= 4; axis <<= 1)
                if ((i & axis) == 0) DrawLine(corners[i], corners[i | axis], color);
    }

    public void DrawLine(Vector2 start, Vector2 end, Vector4 color, float thickness = 1)
    {
        if (!float.IsFinite(thickness) || thickness <= 0) throw new ArgumentOutOfRangeException(nameof(thickness));
        var delta = end - start;
        if (delta.LengthSquared() < 1e-12f) return;
        var offset = Vector2.Normalize(new(-delta.Y, delta.X)) * (thickness * 0.5f);
        DrawTriangle(start + offset, start - offset, end - offset, color);
        DrawTriangle(start + offset, end - offset, end + offset, color);
    }

    public void DrawTriangle(Vector2 a, Vector2 b, Vector2 c, Vector4 color) =>
        AddTriangle(overlay, new(a, 0), new(b, 0), new(c, 0), color);

    public void DrawRectangle(Vector2 position, Vector2 size, Vector4 color, bool filled = false, float thickness = 1)
    {
        var a = position;
        var b = position + new Vector2(size.X, 0);
        var c = position + size;
        var d = position + new Vector2(0, size.Y);
        if (filled)
        {
            DrawTriangle(a, b, c, color);
            DrawTriangle(a, c, d, color);
        }
        else
        {
            DrawLine(a, b, color, thickness);
            DrawLine(b, c, color, thickness);
            DrawLine(c, d, color, thickness);
            DrawLine(d, a, color, thickness);
        }
    }

    public void DrawCircle(Vector2 center, float radius, Vector4 color, bool filled = false, float thickness = 1, int segments = 64)
    {
        ValidateCircle(radius, segments);
        var previous = center + new Vector2(radius, 0);
        for (var i = 1; i <= segments; i++)
        {
            var angle = i * MathF.Tau / segments;
            var next = center + radius * new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            if (filled) DrawTriangle(center, previous, next, color);
            else DrawLine(previous, next, color, thickness);
            previous = next;
        }
    }

    public void Flush(Vector2 viewportSize, Vector3 origin = default)
    {
        if (viewportSize.X <= 0 || viewportSize.Y <= 0 || !float.IsFinite(viewportSize.LengthSquared()))
            throw new ArgumentOutOfRangeException(nameof(viewportSize));
        foreach (ref var vertex in CollectionsMarshal.AsSpan(lines)) vertex.Position -= origin;
        foreach (ref var vertex in CollectionsMarshal.AsSpan(triangles)) vertex.Position -= origin;
        if (lines.Count > 0) renderer.DrawPrimitives(CollectionsMarshal.AsSpan(lines), PrimitiveTopology.Lines);
        if (triangles.Count > 0) renderer.DrawPrimitives(CollectionsMarshal.AsSpan(triangles), PrimitiveTopology.Triangles);
        var screen = CollectionsMarshal.AsSpan(overlay);
        foreach (ref var vertex in screen)
            vertex.Position = new(2 * vertex.Position.X / viewportSize.X - 1, 1 - 2 * vertex.Position.Y / viewportSize.Y, 0);
        if (screen.Length > 0) renderer.DrawPrimitives(screen, PrimitiveTopology.Triangles, screenSpace: true);
        lines.Clear();
        triangles.Clear();
        overlay.Clear();
    }

    private static void AddTriangle(List<PrimitiveVertex> target, Vector3 a, Vector3 b, Vector3 c, Vector4 color)
    {
        target.Add(new(a, color));
        target.Add(new(b, color));
        target.Add(new(c, color));
    }

    private static void ValidateCircle(float radius, int segments)
    {
        if (!float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (segments < 3) throw new ArgumentOutOfRangeException(nameof(segments));
    }
}
