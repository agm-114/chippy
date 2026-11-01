using System.Numerics;
using Xna = Microsoft.Xna.Framework;

namespace Chippy.Backends.MonoGame;

internal static class MonoGameConversions
{
    public static Xna.Vector3 ToXna(this Vector3 value) => new(value.X, value.Y, value.Z);
    public static Xna.Matrix ToXna(this Matrix4x4 value) => new(
        value.M11, value.M12, value.M13, value.M14,
        value.M21, value.M22, value.M23, value.M24,
        value.M31, value.M32, value.M33, value.M34,
        value.M41, value.M42, value.M43, value.M44);
}
