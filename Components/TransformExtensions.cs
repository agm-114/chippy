using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;

namespace Chippy.Components;

// Prefer our IsAlive property over Arch's extension method with the same name.
using Chippy.Ecs;

public static class TransformExtensions
{
    public static Transform ToTransform(this in Matrix4x4 matrix, Entity? parent = null) =>
        Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var position)
            ? new() { Parent = parent, LocalPosition = position, LocalRotation = rotation, LocalScale = scale }
            : throw new ArgumentException("The matrix cannot be represented as a position, rotation, and scale.", nameof(matrix));

    public static bool CanSetParent(this Entity child, Entity? parent)
    {
        if (!child.IsAlive || !child.Has<Transform>())
            return false;
        HashSet<Entity> seen = [child];
        while (parent is { } ancestor)
        {
            if (ancestor.WorldId != child.WorldId || !ancestor.IsAlive
                || !ancestor.Has<Transform>() || !seen.Add(ancestor))
                return false;
            parent = ancestor.Get<Transform>().Parent;
        }
        return true;
    }

    // Parenting retains local values; TransformSystem.Update refreshes world matrices.
    public static bool TrySetParent(this Entity child, Entity? parent)
    {
        if (!child.CanSetParent(parent))
            return false;
        child.Get<Transform>().Parent = parent;
        return true;
    }

    public static void SetParent(this Entity child, Entity? parent)
    {
        if (!child.TrySetParent(parent))
            throw new ArgumentException("Parenting requires live transforms in the same world and cannot create a cycle.", nameof(parent));
    }
}
