using Arch.Core;
using Arch.Core.Extensions;
using Chippy.Components;
using Chippy.Systems;

namespace Chippy.Rendering;

// Prepare has exclusive source-world access. Publish runs between draws on the window thread.
public class RenderWorld(World source) : IDisposable
{
    private readonly TransformSystem transforms = new(source);
    private readonly QueryDescription query = new QueryDescription().WithAll<Transform>();
    private World prepared = World.Create();
    private long? preparedTick;
    public World World { get; private set; } = World.Create();
    public long Tick { get; private set; }
    public bool HasPrepared => preparedTick is not null;

    public void Prepare(long tick)
    {
        preparedTick = null;
        transforms.Update();
        prepared.Clear();
        source.Query(in query, (Entity entity, ref Transform transform) =>
        {
            var hasMesh = entity.TryGet<MeshRenderer>(out var renderer) && renderer.IsRenderable;
            var hasLine = entity.TryGet<LineRenderer>(out var line) && line.IsRenderable;
            var hasLight = entity.TryGet<Light>(out var light) && light.IsActive;
            if (!hasMesh && !hasLine && !hasLight) return;
            var copy = transform;
            copy.Parent = null; // LocalToWorldMatrix is resolved; rendering never follows simulation parents.
            var target = prepared.Create(copy, new SourceEntity(entity));
            if (hasMesh)
            {
                prepared.Add(target, renderer);
                if (entity.TryGet<DistantMarker>(out var marker)) prepared.Add(target, marker);
            }
            if (hasLine) prepared.Add(target, line);
            if (hasLight) prepared.Add(target, light);
        });
        preparedTick = tick;
    }

    public void Publish()
    {
        if (preparedTick is not { } tick)
            throw new InvalidOperationException("Prepare the render world before publishing it.");
        (World, prepared) = (prepared, World);
        Tick = tick;
        preparedTick = null;
    }

    public void Dispose()
    {
        World.Dispose();
        prepared.Dispose();
    }
}
