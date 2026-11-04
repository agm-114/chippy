using Arch.Core;
using Chippy.Components;
using Chippy.Simulation;

namespace Chippy.Systems;

public class OrbitSystem(World world) : ISimulationSystem
{
    private readonly QueryDescription query = new QueryDescription().WithAll<Transform, OrbitMotion>();
    public void Update(double deltaTime) => world.Query(in query, (ref Transform transform, ref OrbitMotion orbit) =>
    {
        orbit.Phase = (orbit.Phase + orbit.AngularSpeed * deltaTime) % Math.Tau;
        transform.LocalPosition = orbit.LocalPosition;
    });
}
