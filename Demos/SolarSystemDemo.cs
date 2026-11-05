using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Chippy.Audio;
using Chippy.Components;
using Chippy.Models;
using Chippy.Rendering;
using Chippy.Simulation;
using Chippy.Systems;
using Chippy.Viewer;
using YamlDotNet.Core;

namespace Chippy.Demos;

// One world unit = 1,000 km. Radii/distances are rounded NASA fact-sheet values.
// Circular, coplanar orbits; one simulated second represents one hour.
public static class SolarSystemDemo
{
    public static ViewerState Create(World world, RenderBackend backend, string? shipPath = null, bool keepShipParts = false)
    {
        var sphere = Mesh.CreateIcosphere();
        var earthMesh = sphere.WithBroadClusters(new(0.05f, 0.3f, 0.85f, 1), new(0.12f, 0.65f, 0.2f, 1));
        const float earthDistance = 149_600, earthRadius = 6.378f;
        var sun = world.Create(new Name("Sun"), new Transform { LocalScale = new(695.7f) },
            new MeshRenderer(sphere) { Tint = new(1, 0.85f, 0.55f, 1), Unlit = true },
            new DistantMarker(new(1, 0.85f, 0.55f, 1)),
            new Light { Intensity = earthDistance * earthDistance, Color = new(1, 0.97f, 0.9f) });
        (string Name, float Distance, float Radius, double Days, Vector4 Color)[] planets =
        [
            ("Mercury", 57_900, 2.4395f, 88, new(0.65f, 0.6f, 0.52f, 1)),
            ("Venus", 108_200, 6.052f, 224.7, new(0.95f, 0.7f, 0.35f, 1)),
            ("Earth", earthDistance, earthRadius, 365.2, Vector4.One),
            ("Mars", 228_000, 3.396f, 687, new(0.95f, 0.3f, 0.15f, 1)),
            ("Jupiter", 778_500, 71.492f, 4331, new(0.85f, 0.65f, 0.45f, 1)),
            ("Saturn", 1_432_000, 60.268f, 10747, new(0.9f, 0.8f, 0.5f, 1)),
            ("Uranus", 2_867_000, 25.559f, 30589, new(0.4f, 0.85f, 0.9f, 1)),
            ("Neptune", 4_515_000, 24.764f, 59800, new(0.3f, 0.4f, 1, 1))
        ];
        var earth = default(Entity);
        for (var i = 0; i < planets.Length; i++)
        {
            var planet = planets[i];
            var orbit = OrbitMotion.FromPeriod(planet.Distance, planet.Days * 24, phase: i * 0.85);
            var body = world.Create(new Name(planet.Name), new Transform
                { Parent = sun, IgnoreParentScale = true, LocalPosition = orbit.LocalPosition, LocalScale = new(planet.Radius) },
                orbit,
                new MeshRenderer(planet.Name == "Earth" ? earthMesh : sphere) { Tint = planet.Color },
                new DistantMarker(planet.Name == "Earth" ? new(0.35f, 0.65f, 1, 1) : planet.Color));
            world.Create(new Name($"{planet.Name} path"), new Transform { Parent = body },
                new OrbitPath(sun), LineRenderer.Circle(orbit.Radius, new(0.15f, 0.2f, 0.3f, 1), 2048));
            if (planet.Name == "Earth")
            {
                earth = body;
                var lunarOrbit = OrbitMotion.FromPeriod(384.4f, 27.3 * 24);
                var moon = world.Create(new Name("Moon"), new Transform
                    { Parent = earth, IgnoreParentScale = true, LocalPosition = lunarOrbit.LocalPosition, LocalScale = new(1.7375f) },
                    lunarOrbit,
                    new MeshRenderer(sphere) { Tint = new(0.75f, 0.75f, 0.8f, 1) }, new DistantMarker(new(0.75f, 0.75f, 0.8f, 1)));
                world.Create(new Name("Moon path"), new Transform { Parent = moon },
                    new OrbitPath(earth), LineRenderer.Circle(lunarOrbit.Radius, new(0.3f, 0.3f, 0.4f, 1), 256));
            }
            if (planet.Name == "Saturn")
                world.Create(new Name("Saturn rings"), new Transform
                    { Parent = body, IgnoreParentScale = true, LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.47f) },
                    LineRenderer.Circle(140.22f, new(0.85f, 0.7f, 0.45f, 1)));
        }
        if (File.Exists(shipPath))
        {
            try
            {
                // Use a display scale that keeps the test ship visible beside Earth.
                MeshBuilder.LoadAssembly(world, File.ReadAllText(shipPath), new()
                {
                    Parent = earth, IgnoreParentScale = true, LocalPosition = new(0, 9, 0)
                }, scale: 0.45f, keepParts: keepShipParts);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or YamlException or InvalidDataException)
            {
                Console.Error.WriteLine($"Could not load the working ship. {error.Message}");
            }
        }
        var simulation = new SimulationScheduler(ticksPerSecond: 30);
        simulation.Add(new OrbitSystem(world));
        return new(world, backend)
        {
            SelectedEntity = earth,
            FollowSelected = true,
            Simulation = simulation,
            UniverseTimePerTick = TimeSpan.FromHours(1) / simulation.TicksPerSecond,
            Camera = new(distance: earthRadius * 4.5f, pitch: 0.35f,
                target: earth.Get<Transform>().LocalPosition, yaw: -MathF.PI / 2 - 1.7f)
            { MinDistance = earthRadius * 1.1f, MaxDistance = 10_000_000, NearClip = 0.1f, FarClip = 10_000_000 },
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
            var color = renderer.SharedMesh!.SubMeshes[0].Color * renderer.Tint;
            draw.DrawCircle(center + new Vector2(position.X, position.Z) * (80f / 4_515_000), 3, color, filled: true, segments: 12);
        });
        draw.DrawTriangle(center + new Vector2(-4, -91), center + new Vector2(4, -91), center + new Vector2(0, -98), Vector4.One);
    }
}
