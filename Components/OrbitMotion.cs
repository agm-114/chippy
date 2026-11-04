using System.Numerics;

namespace Chippy.Components;

/// <summary>Circular motion in the local XZ plane, with angles in radians.</summary>
public record struct OrbitMotion(float Radius, double Phase, double AngularSpeed)
{
    public readonly Vector3 LocalPosition => Radius * new Vector3((float)Math.Cos(Phase), 0, (float)Math.Sin(Phase));

    public static OrbitMotion FromPeriod(float radius, double periodSeconds, double phase = 0)
    {
        if (!float.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        if (!double.IsFinite(periodSeconds) || periodSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(periodSeconds));
        if (!double.IsFinite(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        return new(radius, phase, Math.Tau / periodSeconds);
    }
}
