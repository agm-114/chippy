using Arch.Core;

namespace Chippy.Ecs;

public static class EntityExtensions
{
    extension(Entity entity)
    {
        public World World => Arch.Core.World.Worlds[entity.WorldId];
        public bool IsAlive => entity.World.IsAlive(entity);
    }
}
