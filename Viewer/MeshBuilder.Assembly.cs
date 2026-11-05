using System.Numerics;
using Arch.Core;
using Chippy.Components;
using Chippy.Models;
using Chippy.Systems;

namespace Chippy.Viewer;

internal partial class MeshBuilder
{
    internal static void LoadAssembly(World world, string yaml, Transform placement, float scale, bool keepParts)
    {
        var (rotation, parts) = ReadAssembly(yaml);
        if (parts.Count == 0) return;
        var name = new Name("Working Ship");
        var rootTransform = new Transform { LocalEulerAngles = rotation };
        if (keepParts)
        {
            placement.LocalRotation = Quaternion.Concatenate(rootTransform.LocalRotation, placement.LocalRotation);
            CreateAssemblyParts(world, world.Create(name, placement), parts, scale);
            return;
        }

        using var bakeWorld = World.Create();
        var nodes = CreateAssemblyParts(bakeWorld, bakeWorld.Create(rootTransform), parts, 1);
        new TransformSystem(bakeWorld).Update();
        var mesh = Mesh.Combine(parts.Select(part =>
            (part.Part.Mesh, bakeWorld.Get<Transform>(nodes[part.Id].Entity).LocalToWorldMatrix, part.Color)));
        placement.LocalScale = new(scale);
        world.Create(name, placement, new MeshRenderer(mesh));
    }

    private static Dictionary<int, (Entity Entity, Socket[] Sockets, Vector3 Scale)> CreateAssemblyParts(
        World world, Entity root, List<AssemblyPart> parts, float scale)
    {
        var nodes = new Dictionary<int, (Entity Entity, Socket[] Sockets, Vector3 Scale)> { [0] = (root, RootSockets, Vector3.One) };
        foreach (var part in parts)
        {
            var parent = nodes[part.Parent];
            var (position, rotation) = GetAttachmentPose(part.Part.Sockets[part.Mount], parent.Sockets[part.Target],
                part.Scale, parent.Scale, part.Roll, part.Offset);
            var transform = new Transform
            {
                Parent = parent.Entity, IgnoreParentScale = true,
                LocalPosition = position * scale, LocalRotation = rotation, LocalScale = part.Scale * scale
            };
            var entity = world.Create(new Name(part.Part.Name), transform, new MeshRenderer(part.Part.Mesh) { Tint = part.Color });
            nodes.Add(part.Id, (entity, part.Part.Sockets, part.Scale));
        }
        return nodes;
    }
}
