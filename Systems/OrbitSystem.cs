using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Chippy.Components;
using Chippy.Simulation;

namespace Chippy.Systems;

public class OrbitSystem(World world) : ISimulationSystem
{
    private readonly QueryDescription query = new QueryDescription().WithAll<Transform, OrbitMotion>();
    private readonly QueryDescription paths = new QueryDescription().WithAll<Transform, OrbitPath>();

    public void Update(double deltaTime)
    {
        world.Query(in query, (ref Transform transform, ref OrbitMotion orbit) =>
        {
            orbit.Phase = (orbit.Phase + orbit.AngularSpeed * deltaTime) % Math.Tau;
            transform.LocalPosition = orbit.LocalPosition;
        });
        world.Query(in paths, (ref Transform path) =>
        {
            if (path.Parent is not { } body || body.WorldId != world.Id || !world.IsAlive(body)
                || !world.Has<OrbitMotion>(body) || !body.TryGet<Transform>(out var transform)) return;

            // Cancel the body's local pose so the path stays centered on its orbit origin.
            path.LocalRotation = Quaternion.Inverse(transform.LocalRotation);
            path.LocalPosition = Vector3.Transform(-transform.LocalPosition, path.LocalRotation);
        });
    }
}
