using System.Numerics;

namespace Chippy.Rendering;

public class OrbitCamera(float distance = 5f, float pitch = 0.3f)
{
    private readonly (float Distance, float Pitch) home = (distance, pitch);
    public float Yaw = 0.65f;
    public float Pitch = pitch;
    public float Distance = distance;
    public Vector3 Target;

    public Vector3 Eye => Target + new Vector3(MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch),
        MathF.Cos(Yaw) * MathF.Cos(Pitch)) * Distance;

    public void Move(float yaw, float pitch, float zoom)
    {
        Yaw = MathF.IEEERemainder(Yaw + yaw, 2 * MathF.PI);
        Pitch = Math.Clamp(Pitch + pitch, -1.4f, 1.4f);
        Distance = Math.Clamp(Distance * MathF.Exp(zoom), 0.5f, 200f);
    }

    public void Pan(Vector3 movement)
    {
        var right = new Vector3(MathF.Cos(Yaw), 0, -MathF.Sin(Yaw));
        var forward = new Vector3(-MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
        Target += right * movement.X + Vector3.UnitY * movement.Y + forward * movement.Z;
    }

    public void Follow(Vector3 position, float deltaTime) =>
        Target = Vector3.Lerp(Target, position, 1 - MathF.Exp(-10 * deltaTime));

    public void Reset() => (Yaw, Pitch, Distance, Target) = (0.65f, home.Pitch, home.Distance, Vector3.Zero);
}
