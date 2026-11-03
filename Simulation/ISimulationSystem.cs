namespace Chippy.Simulation;

public interface ISimulationSystem
{
    // This system's scheduled interval in simulated seconds.
    void Update(double deltaTime);
}
