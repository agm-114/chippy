using System.Numerics;

namespace Chippy.Rendering;

public class OrbitCamera(float distance = 5f, float pitch = 0.3f, Vector3 target = default, float yaw = 0.65f)
{
    private readonly (float Distance, float Pitch, Vector3 Target, float Yaw) home = (distance, pitch, target, yaw);
    public float Yaw = yaw;
    public float Pitch = pitch;
    public float Distance = distance;
    public Vector3 Target = target;
    public float MinDistance = 0.5f;
    public float MaxDistance = 200;
    public float NearClip = 0.01f;
    public float FarClip = 2000;

    public Vector3 Offset => new Vector3(MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch), MathF.Cos(Yaw) * MathF.Cos(Pitch)) * Distance;
    public Vector3 Eye => Target + Offset;

    public void Move(float yaw, float pitch, float zoom)
    {
        Yaw = MathF.IEEERemainder(Yaw + yaw, 2 * MathF.PI);
        Pitch = Math.Clamp(Pitch + pitch, -1.4f, 1.4f);
        Distance = Math.Clamp(Distance * MathF.Exp(zoom), MinDistance, MaxDistance);
    }

    public void Pan(Vector3 movement)
    {
        var right = new Vector3(MathF.Cos(Yaw), 0, -MathF.Sin(Yaw));
        var forward = new Vector3(-MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
        Target += right * movement.X + Vector3.UnitY * movement.Y + forward * movement.Z;
    }

    public void Follow(Vector3 position, float deltaTime) =>
        Target = Vector3.Lerp(Target, position, 1 - MathF.Exp(-10 * deltaTime));

    public void Reset() => (Yaw, Pitch, Distance, Target) = (home.Yaw, home.Pitch, home.Distance, home.Target);
}
