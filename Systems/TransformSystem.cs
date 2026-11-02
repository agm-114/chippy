using System.Numerics;
using Arch.Core;
using Chippy.Components;

namespace Chippy.Systems;

public class TransformSystem(World world)
{
    private readonly QueryDescription query = new QueryDescription().WithAll<Transform>();
    private readonly Dictionary<Entity, Matrix4x4> matrices = [];
    private readonly HashSet<Entity> ancestors = [];
    private readonly List<Entity> path = [];

    // Rebuild once per frame so direct component edits and reparenting are immediately reflected.
    public void Update()
    {
        matrices.Clear();
        world.Query(in query, (Entity entity, ref Transform transform) =>
            transform.LocalToWorldMatrix = Resolve(entity));
    }

    private Matrix4x4 Resolve(Entity entity)
    {
        var current = entity;
        var matrix = Matrix4x4.Identity;
        path.Clear();
        ancestors.Clear();
        while (true)
        {
            if (matrices.TryGetValue(current, out matrix))
                break;
            if (!ancestors.Add(current))
                throw new InvalidOperationException("The transform hierarchy contains a parenting cycle.");
            path.Add(current);
            ref var transform = ref world.Get<Transform>(current);
            if (transform.Parent is not { } parent)
            {
                matrix = Matrix4x4.Identity;
                break;
            }
            if (parent.WorldId != world.Id)
                throw new InvalidOperationException("A transform's parent must belong to the same world.");
            if (!world.IsAlive(parent))
            {
                // Keep local values when a parent is deleted; never follow a recycled entity ID.
                transform.Parent = null;
                matrix = Matrix4x4.Identity;
                break;
            }
            if (!world.Has<Transform>(parent))
                throw new InvalidOperationException("A transform's parent must have a Transform component.");
            current = parent;
        }

        // Iterative traversal supports deep hierarchies without consuming the call stack.
        for (var index = path.Count - 1; index >= 0; index--)
        {
            ref var transform = ref world.Get<Transform>(path[index]);
            matrix = transform.LocalMatrix * matrix;
            transform.LocalToWorldMatrix = matrix;
            matrices[path[index]] = matrix;
        }
        return matrix;
    }
}
