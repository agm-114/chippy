using System.Numerics;

namespace Chippy.Components;

public enum LightType { Point, Directional }

public record struct Light
{
    public LightType Type;
    public Vector3 Color = Vector3.One;
    public float Intensity = 1;
    public float Range; // Point-light cutoff in world units; zero means unlimited.
    public bool Enabled = true;
    public Light() { }
    public readonly bool IsActive => Enabled && Intensity > 0;

    // Basic forward lighting treats each point source as a parallel beam across one mesh.
    public readonly (Vector3 Direction, Vector3 Color) Sample(in Transform transform, Vector3 position)
    {
        var direction = Type == LightType.Directional
            ? Vector3.TransformNormal(-Vector3.UnitZ, transform.LocalToWorldMatrix)
            : position - transform.LocalToWorldMatrix.Translation;
        var distanceSquared = direction.LengthSquared();
        if (distanceSquared < 1e-12f) return (Vector3.UnitY, Vector3.Zero);
        var intensity = Intensity;
        if (Type == LightType.Point)
        {
            intensity /= Math.Max(distanceSquared, 1);
            if (Range > 0) intensity *= MathF.Pow(Math.Clamp(1 - distanceSquared / (Range * Range), 0, 1), 2);
        }
        return (Vector3.Normalize(direction), Color * intensity);
    }
}
