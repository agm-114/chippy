using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Chippy.Components;

namespace Chippy.Systems;

public class TransformSystem(World world)
{
    private readonly QueryDescription query = new QueryDescription().WithAll<Transform>();
    private readonly QueryDescription lineQuery = new QueryDescription().WithAll<Transform, LineRenderer>();
    private readonly Dictionary<Entity, (Matrix4x4 Matrix, Quaternion Rotation)> frames = [];
    private readonly HashSet<Entity> ancestors = [];
    private readonly List<Entity> path = [];

    // Rebuild once per frame so direct component edits and reparenting are immediately reflected.
    public void Update()
    {
        frames.Clear();
        world.Query(in query, (Entity entity, ref Transform transform) =>
            transform.LocalToWorldMatrix = Resolve(entity));
        world.Query(in lineQuery, (Entity entity, ref Transform transform, ref LineRenderer line) =>
        {
            line.PointSpaceMatrix = entity.TryGet<OrbitPath>(out var path) && frames.TryGetValue(path.Center, out var center)
                ? transform.LocalMatrix * WithoutScale(center) : null;
        });
    }

    private Matrix4x4 Resolve(Entity entity)
    {
        var current = entity;
        (Matrix4x4 Matrix, Quaternion Rotation) frame;
        path.Clear();
        ancestors.Clear();
        while (!frames.TryGetValue(current, out frame))
        {
            if (!ancestors.Add(current))
                throw new InvalidOperationException("The transform hierarchy contains a parenting cycle.");
            path.Add(current);
            ref var transform = ref world.Get<Transform>(current);
            if (transform.Parent is not { } parent)
            {
                frame = (Matrix4x4.Identity, Quaternion.Identity);
                break;
            }
            if (parent.WorldId != world.Id)
                throw new InvalidOperationException("A transform's parent must belong to the same world.");
            if (!world.IsAlive(parent))
            {
                // Keep local values when a parent is deleted; never follow a recycled entity ID.
                transform.Parent = null;
                frame = (Matrix4x4.Identity, Quaternion.Identity);
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
            var parentMatrix = transform.IgnoreParentScale ? WithoutScale(frame) : frame.Matrix;
            frame = (transform.LocalMatrix * parentMatrix, Quaternion.Concatenate(transform.LocalRotation, frame.Rotation));
            transform.LocalToWorldMatrix = frame.Matrix;
            frames[path[index]] = frame;
        }
        return frame.Matrix;
    }

    private static Matrix4x4 WithoutScale((Matrix4x4 Matrix, Quaternion Rotation) frame)
    {
        var matrix = Matrix4x4.CreateFromQuaternion(frame.Rotation);
        matrix.Translation = frame.Matrix.Translation;
        return matrix;
    }
}
