using System.Numerics;
using Arch.Core;

namespace Chippy.Components;

public static class TransformExtensions
{
    public static Transform ToTransform(this in Matrix4x4 matrix, Entity? parent = null) =>
        Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var position)
            ? new() { Parent = parent, LocalPosition = position, LocalRotation = rotation, LocalScale = scale }
            : throw new ArgumentException("The matrix cannot be represented as a position, rotation, and scale.", nameof(matrix));

    public static bool CanSetParent(this World world, Entity child, Entity? parent)
    {
        if (!world.HasTransform(child))
            return false;
        HashSet<Entity> seen = [child];
        while (parent is { } ancestor)
        {
            if (!world.HasTransform(ancestor) || !seen.Add(ancestor))
                return false;
            parent = world.Get<Transform>(ancestor).Parent;
        }
        return true;
    }

    // Parenting retains local values; TransformSystem.Update refreshes world matrices.
    public static bool TrySetParent(this World world, Entity child, Entity? parent)
    {
        if (!world.CanSetParent(child, parent))
            return false;
        world.Get<Transform>(child).Parent = parent;
        return true;
    }

    public static void SetParent(this World world, Entity child, Entity? parent)
    {
        if (!world.TrySetParent(child, parent))
            throw new ArgumentException("Parenting requires live transforms in the same world and cannot create a cycle.", nameof(parent));
    }

    private static bool HasTransform(this World world, Entity entity) =>
        entity.WorldId == world.Id && world.IsAlive(entity) && world.Has<Transform>(entity);
}
