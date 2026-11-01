using System.Numerics;

namespace Chippy.Models;

public record struct Transform(Vector3 Position, Quaternion Rotation, Vector3 LocalScale)
{
    public Transform() : this(Vector3.Zero, Quaternion.Identity, Vector3.One) { }

    public readonly Matrix4x4 LocalToWorldMatrix => Matrix4x4.CreateScale(LocalScale)
        * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    public static Transform FromMatrix(Matrix4x4 matrix) => Matrix4x4.Decompose(matrix, out var scale,
        out var rotation, out var position)
        ? new Transform(position, rotation, scale)
        : throw new ArgumentException("The matrix cannot be represented as a position, rotation, and scale.", nameof(matrix));

    public static Matrix4x4 GetNormalMatrix(in Matrix4x4 matrix)
    {
        if (!float.IsFinite(matrix.GetDeterminant()) || !Matrix4x4.Invert(matrix, out var inverse)
            || !float.IsFinite(matrix.Translation.LengthSquared()))
            throw new ArgumentException("Mesh transforms must be finite and invertible.", nameof(matrix));
        return Matrix4x4.Transpose(inverse);
    }
}
