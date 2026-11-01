using System.Numerics;

namespace Chippy.Rendering;

public class OrbitCamera
{
    public float Yaw = 0.65f;
    public float Pitch = 0.3f;
    public float Distance = 5f;

    public Vector3 Eye => new Vector3(MathF.Sin(Yaw) * MathF.Cos(Pitch), MathF.Sin(Pitch),
        MathF.Cos(Yaw) * MathF.Cos(Pitch)) * Distance;

    public void Move(float yaw, float pitch, float zoom)
    {
        Yaw = MathF.IEEERemainder(Yaw + yaw, 2 * MathF.PI);
        Pitch = Math.Clamp(Pitch + pitch, -1.4f, 1.4f);
        Distance = Math.Clamp(Distance * MathF.Exp(zoom), 1.5f, 20f);
    }

    public void Reset() => (Yaw, Pitch, Distance) = (0.65f, 0.3f, 5f);
}
