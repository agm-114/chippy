using System.Numerics;

namespace Chippy.Components;

/// <summary>Linear motion in the same coordinate space as Transform.LocalPosition.</summary>
public record struct Kinematics
{
    public Vector3 Velocity;
    public Vector3 Acceleration;
    public Vector3 PendingForce { get; private set; }

    public float Mass
    {
        readonly get => field == 0 ? 1 : field;
        set
        {
            if (!float.IsFinite(value) || value <= 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Mass must be finite and positive.");
            field = value;
        }
    }

    public void AddForce(Vector3 force) => PendingForce += force;

    internal Vector3 ConsumeForce()
    {
        var force = PendingForce;
        PendingForce = Vector3.Zero;
        return force;
    }
}
