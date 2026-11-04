using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Chippy.Components;
using Chippy.Diagnostics;
using Chippy.Systems;

namespace Chippy.Rendering;

// The producer builds one world while the window thread draws another. The third holds the latest result.
public class RenderWorld(World source) : IDisposable
{
    private readonly record struct Motion(Vector3 Position, Vector3 Velocity, Vector3 LinePosition, Vector3 LineVelocity);

    private readonly TransformSystem transforms = new(source);
    private readonly QueryDescription query = new QueryDescription().WithAll<Transform>();
    private readonly QueryDescription motionQuery = new QueryDescription().WithAll<Transform, Motion>();
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
        transforms.Update(trackMotion: true);
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

            var velocity = transforms.GetWorldVelocity(entity);
            var lineVelocity = hasLine && !line.UseWorldSpace && line.PointSpaceMatrix is not null
                && entity.TryGet<OrbitPath>(out var orbit) ? transforms.GetWorldVelocity(orbit.Center) : Vector3.Zero;
            if (velocity != Vector3.Zero || lineVelocity != Vector3.Zero)
                prepared.Add(target, new Motion(copy.LocalToWorldMatrix.Translation, velocity,
                    line.PointSpaceMatrix?.Translation ?? Vector3.Zero, lineVelocity));
        });
        preparedTick = tick;
    }

    // The window thread owns this snapshot until it acquires a newer one.
    internal void PredictMotion(float seconds)
    {
        World.Query(in motionQuery, (Entity entity, ref Transform transform, ref Motion motion) =>
        {
            var matrix = transform.LocalToWorldMatrix;
            matrix.Translation = motion.Position + motion.Velocity * seconds;
            transform.LocalToWorldMatrix = matrix;
            if (motion.LineVelocity != Vector3.Zero)
            {
                ref var line = ref World.Get<LineRenderer>(entity);
                if (line.PointSpaceMatrix is not { } lineMatrix) return;
                lineMatrix.Translation = motion.LinePosition + motion.LineVelocity * seconds;
                line.PointSpaceMatrix = lineMatrix;
            }
        });
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
