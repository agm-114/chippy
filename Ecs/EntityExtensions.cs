using System.Diagnostics.CodeAnalysis;
using Arch.Core;

namespace Chippy.Ecs;

public static class EntityExtensions
{
    extension(Entity entity)
    {
        public World World => entity.TryGetWorld(out var world)
            ? world
            : throw new InvalidOperationException("The entity does not belong to a live world.");
    }

    public static bool TryGetWorld(this Entity entity, [NotNullWhen(true)] out World? world)
    {
        var worlds = Arch.Core.World.Worlds;
        world = (uint)entity.WorldId < (uint)worlds.Length ? worlds[entity.WorldId] : null;
        if (world is not null && world.IsAlive(entity))
            return true;
        world = null;
        return false;
    }
}
