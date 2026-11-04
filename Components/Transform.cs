using System.Numerics;
using Arch.Core;

namespace Chippy.Components;

public record struct Transform
{
    public Entity? Parent;
    // Follow the parent's world position and rotation without inheriting its scale.
    public bool IgnoreParentScale;
    public Vector3 LocalPosition;
    public Quaternion LocalRotation = Quaternion.Identity;
    public Vector3 LocalScale = Vector3.One;
    public Matrix4x4 LocalToWorldMatrix { get; internal set; } = Matrix4x4.Identity;

    public Transform() { }

    public readonly Matrix4x4 LocalMatrix => Matrix4x4.CreateScale(LocalScale)
        * Matrix4x4.CreateFromQuaternion(LocalRotation) * Matrix4x4.CreateTranslation(LocalPosition);

    // Inspector angles in degrees: X = pitch, Y = yaw, Z = roll.
    public Vector3 LocalEulerAngles
    {
        readonly get
        {
            var matrix = Matrix4x4.CreateFromQuaternion(LocalRotation);
            var pitch = MathF.Asin(Math.Clamp(-matrix.M32, -1, 1));
            var regular = MathF.Abs(matrix.M32) < 0.999999f;
            var yaw = regular ? MathF.Atan2(matrix.M31, matrix.M33) : MathF.Atan2(-matrix.M13, matrix.M11);
            var roll = regular ? MathF.Atan2(matrix.M12, matrix.M22) : 0;
            return new Vector3(pitch, yaw, roll) * (180 / MathF.PI);
        }
        set => LocalRotation = Quaternion.CreateFromYawPitchRoll(
            value.Y * (MathF.PI / 180), value.X * (MathF.PI / 180), value.Z * (MathF.PI / 180));
    }
}
