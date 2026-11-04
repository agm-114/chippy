using System.Collections.Immutable;
using System.Numerics;

namespace Chippy.Components;

public record struct LineRenderer(ImmutableArray<Vector3> Positions)
{
    public Vector4 Color = Vector4.One;
    public bool Loop;
    public bool UseWorldSpace;
    public bool Enabled = true;
    public readonly bool IsRenderable => Enabled && !Positions.IsDefaultOrEmpty && Positions.Length >= 2;

    public static LineRenderer Circle(float radius, Vector4 color, int segments = 128)
    {
        if (!float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (segments < 3) throw new ArgumentOutOfRangeException(nameof(segments));
        return new([.. Enumerable.Range(0, segments).Select(i =>
            new Vector3(MathF.Cos(i * MathF.Tau / segments), 0, MathF.Sin(i * MathF.Tau / segments)) * radius)])
        { Color = color, Loop = true };
    }
}
