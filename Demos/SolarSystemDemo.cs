using System.Numerics;
using Arch.Core;
using Chippy.Audio;
using Chippy.Components;
using Chippy.Models;
using Chippy.Rendering;
using Chippy.Simulation;
using Chippy.Viewer;

namespace Chippy.Demos;

// A schematic animation: sizes, distances and periods are deliberately exaggerated.
public static class SolarSystemDemo
{
    public static ViewerState Create(World world, RenderBackend backend)
    {
        var sphere = Mesh.CreateSphere();
        var sun = world.Create(new Name("Sun"), new Transform { LocalScale = new(1.1f) },
            new MeshRenderer(sphere) { Tint = new(1, 0.65f, 0.12f, 1), Unlit = true });
        (string Name, float Distance, float Radius, double Period, Vector4 Color)[] planets =
        [
            ("Mercury", 2.2f, 0.16f, 8, new(0.65f, 0.6f, 0.52f, 1)),
            ("Venus", 3.2f, 0.26f, 13, new(0.95f, 0.7f, 0.35f, 1)),
            ("Earth", 4.4f, 0.3f, 20, new(0.2f, 0.55f, 1, 1)),
            ("Mars", 5.7f, 0.22f, 28, new(0.95f, 0.3f, 0.15f, 1)),
            ("Jupiter", 8, 0.75f, 47, new(0.85f, 0.65f, 0.45f, 1)),
            ("Saturn", 10.7f, 0.63f, 68, new(0.9f, 0.8f, 0.5f, 1)),
            ("Uranus", 13.4f, 0.46f, 92, new(0.4f, 0.85f, 0.9f, 1)),
            ("Neptune", 16, 0.44f, 120, new(0.3f, 0.4f, 1, 1))
        ];
        for (var i = 0; i < planets.Length; i++)
        {
            var planet = planets[i];
            var phase = i * 0.85;
            var pivot = world.Create(new Name($"{planet.Name} orbit"), new Transform(),
                new OrbitMotion(planet.Distance, phase, Math.Tau / planet.Period));
            world.Create(new Name(planet.Name), new Transform { Parent = pivot, LocalScale = new(planet.Radius) },
                new MeshRenderer(sphere) { Tint = planet.Color });
            world.Create(new Name($"{planet.Name} path"), new Transform(),
                LineRenderer.Circle(planet.Distance, new(planet.Color.X * 0.45f, planet.Color.Y * 0.45f, planet.Color.Z * 0.45f, 1)));
            if (planet.Name == "Earth")
            {
                var moon = world.Create(new Name("Moon orbit"), new Transform { Parent = pivot }, new OrbitMotion(0.65f, 0, Math.Tau / 4));
                world.Create(new Name("Moon"), new Transform { Parent = moon, LocalScale = new(0.09f) },
                    new MeshRenderer(sphere) { Tint = new(0.75f, 0.75f, 0.8f, 1) });
                world.Create(new Name("Moon path"), new Transform { Parent = pivot }, LineRenderer.Circle(0.65f, new(0.3f, 0.3f, 0.4f, 1), 64));
            }
            if (planet.Name == "Saturn")
                world.Create(new Name("Saturn rings"), new Transform { Parent = pivot, LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.35f) },
                    LineRenderer.Circle(0.95f, new(0.85f, 0.7f, 0.45f, 1)));
        }
        var motion = new OrbitSystem(world);
        motion.Update(0);
        var simulation = new SimulationScheduler(ticksPerSecond: 30);
        simulation.Add(motion);
        return new(world, backend)
        {
            SelectedEntity = sun,
            Simulation = simulation,
            Camera = new(distance: 46, pitch: 0.9f),
            Background = new(0.012f, 0.018f, 0.035f, 1),
            DrawOverlay = DrawOverlay,
            MusicClip = AudioClip.Generate(4, t =>
                (float)((Math.Sin(Math.Tau * 110 * t) + 0.5 * Math.Sin(Math.Tau * 165 * t) + 0.3 * Math.Sin(Math.Tau * 220 * t))
                    * 0.25 * Math.Pow(Math.Sin(Math.PI * t / 4), 2))),
            EffectClip = AudioClip.Generate(0.45, t =>
                (float)((Math.Sin(Math.Tau * 660 * t) + 0.4 * Math.Sin(Math.Tau * 990 * t))
                    * Math.Exp(-8 * t) * Math.Min(t / 0.01, 1) * Math.Min((0.45 - t) / 0.02, 1) * 0.5))
        };
    }

    private static void DrawOverlay(World world, PrimitiveBatch draw, Vector2 size)
    {
        // A small live map proves the overlay uses the same completed world as the 3D scene.
        var center = size - new Vector2(112, 112);
        draw.DrawRectangle(center - new Vector2(98), new(196), new(0.03f, 0.05f, 0.09f, 0.85f), filled: true);
        draw.DrawCircle(center, 86, new(0.25f, 0.35f, 0.5f, 1));
        draw.DrawLine(center - new Vector2(90, 0), center + new Vector2(90, 0), new(0.15f, 0.2f, 0.3f, 1));
        draw.DrawLine(center - new Vector2(0, 90), center + new Vector2(0, 90), new(0.15f, 0.2f, 0.3f, 1));
        var query = new QueryDescription().WithAll<Transform, MeshRenderer>();
        world.Query(in query, (ref Transform transform, ref MeshRenderer renderer) =>
        {
            if (!renderer.IsRenderable) return;
            var position = transform.LocalToWorldMatrix.Translation;
            draw.DrawCircle(center + new Vector2(position.X, position.Z) * 5, 3, renderer.Tint, filled: true, segments: 12);
        });
        draw.DrawTriangle(center + new Vector2(-4, -91), center + new Vector2(4, -91), center + new Vector2(0, -98), Vector4.One);
    }
}

public record struct OrbitMotion(float Radius, double Phase, double AngularSpeed);

public class OrbitSystem(World world) : ISimulationSystem
{
    private readonly QueryDescription query = new QueryDescription().WithAll<Transform, OrbitMotion>();
    public void Update(double deltaTime) => world.Query(in query, (ref Transform transform, ref OrbitMotion orbit) =>
    {
        orbit.Phase = (orbit.Phase + orbit.AngularSpeed * deltaTime) % Math.Tau;
        transform.LocalPosition = orbit.Radius * new Vector3((float)Math.Cos(orbit.Phase), 0, (float)Math.Sin(orbit.Phase));
    });
}
