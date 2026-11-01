using System.Numerics;

namespace Chippy.Models;

public readonly record struct Bounds(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) / 2;
    public Vector3 Size => Max - Min;
    public Matrix4x4 Normalization => Matrix4x4.CreateTranslation(-Center)
        * Matrix4x4.CreateScale(2 / Math.Max(Math.Max(Size.X, Math.Max(Size.Y, Size.Z)), 1e-6f));
}
