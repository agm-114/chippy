using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Chippy.Components;
using Chippy.Models;
using Chippy.Systems;

namespace Chippy.Viewer;

internal partial class MeshBuilder
{
    internal static void LoadAssembly(World world, string yaml, Transform placement, float scale, bool keepParts)
    {
        // Use the builder's loader to resolve sockets, links, and authored part transforms.
        using var sourceWorld = World.Create();
        var source = new MeshBuilder(sourceWorld, RenderBackend.Brutal);
        source.ApplyYaml(yaml);
        if (source.history.Count == 0) return;

        var name = new Name("Working Ship");
        if (!keepParts)
        {
            new TransformSystem(sourceWorld).Update();
            var mesh = Mesh.Combine(source.history.Select(entity =>
            {
                var renderer = entity.Get<MeshRenderer>();
                return (renderer.SharedMesh!, entity.Get<Transform>().LocalToWorldMatrix, renderer.Tint);
            }));
            placement.LocalScale = new(scale);
            world.Create(name, placement, new MeshRenderer(mesh));
            return;
        }

        placement.LocalRotation = Quaternion.Concatenate(source.root.Get<Transform>().LocalRotation, placement.LocalRotation);
        var entities = new Dictionary<Entity, Entity> { [source.root] = world.Create(name, placement) };
        foreach (var entity in source.history)
        {
            var transform = entity.Get<Transform>();
            transform.Parent = entities[source.parts[entity].Parent];
            transform.LocalPosition *= scale;
            transform.LocalScale *= scale;
            entities.Add(entity, world.Create(entity.Get<Name>(), transform, entity.Get<MeshRenderer>()));
        }
    }
}
