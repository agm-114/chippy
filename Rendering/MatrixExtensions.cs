using System.Numerics;

namespace Chippy.Rendering;

public static class MatrixExtensions
{
    public static Matrix4x4 GetNormalMatrix(this in Matrix4x4 matrix)
    {
        if (!float.IsFinite(matrix.GetDeterminant()) || !Matrix4x4.Invert(matrix, out var inverse)
            || !float.IsFinite(matrix.Translation.LengthSquared()))
            throw new ArgumentException("Mesh transforms must be finite and invertible.", nameof(matrix));
        return Matrix4x4.Transpose(inverse);
    }
}
