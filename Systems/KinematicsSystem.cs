using Arch.Core;
using Chippy.Components;
using Chippy.Simulation;

namespace Chippy.Systems;

public class KinematicsSystem(World world) : ISimulationSystem
{
    private readonly QueryDescription query = new QueryDescription().WithAll<Transform, Kinematics>();

    public void Update(double deltaTime)
    {
        if (!double.IsFinite(deltaTime) || deltaTime < 0 || deltaTime > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "The step must be finite and nonnegative.");
        var seconds = (float)deltaTime;
        if (seconds == 0) return;

        world.Query(in query, (ref Transform transform, ref Kinematics motion) =>
        {
            var acceleration = motion.Acceleration + motion.ConsumeForce() / motion.Mass;
            transform.LocalPosition += (motion.Velocity + acceleration * (0.5f * seconds)) * seconds;
            motion.Velocity += acceleration * seconds;
        });
    }
}
