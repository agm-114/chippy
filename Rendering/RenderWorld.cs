using Arch.Core;
using Arch.Core.Extensions;
using Chippy.Components;
using Chippy.Diagnostics;
using Chippy.Systems;

namespace Chippy.Rendering;

// The producer builds one world while the window thread draws another. The third holds the latest result.
public class RenderWorld(World source) : IDisposable
{
    private readonly TransformSystem transforms = new(source);
    private readonly QueryDescription query = new QueryDescription().WithAll<Transform>();
    private readonly Lock exchange = new();
    private World prepared = World.Create();
    private World latest = World.Create();
    private long? preparedTick;
    private long? preparedRevision;
    private long? latestTick;
    private long? latestRevision;
    internal TimelineCapture? Timeline { get; init; }
    internal long? Revision { get; private set; }
    public World World { get; private set; } = World.Create();
    public long Tick { get; private set; }
    public bool HasPrepared => preparedTick is not null;

    public void Prepare(long tick)
    {
        preparedRevision = Timeline is { IsEnabled: true } timeline ? timeline.NextSnapshot() : null;
        using var span = Timeline?.Measure("prepare", tick, preparedRevision);
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
        PublishLatest();
        AcquireLatest();
    }

    internal void PublishLatest()
    {
        if (preparedTick is not { } tick)
            throw new InvalidOperationException("Prepare the render world before publishing it.");
        lock (exchange)
        {
            (latest, prepared) = (prepared, latest);
            latestTick = tick;
            latestRevision = preparedRevision;
            Timeline?.Mark("publish", tick, latestRevision);
        }
        preparedTick = null;
    }

    internal void AcquireLatest()
    {
        lock (exchange)
        {
            if (latestTick is not { } tick) return;
            (World, latest) = (latest, World);
            Tick = tick;
            Revision = latestRevision;
            Timeline?.Mark("acquire", tick, Revision);
            latestTick = null;
        }
    }

    public void Dispose()
    {
        World.Dispose();
        latest.Dispose();
        prepared.Dispose();
    }
}
