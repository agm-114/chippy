using Arch.Core;
using Chippy.Components;

namespace Chippy.Rendering;

// TransformSystem.Update must run before drawing. GPU resources belong to the injected backend.
public class MeshRenderSystem(World world, IRenderSystem renderer)
{
    private readonly QueryDescription query = new QueryDescription().WithAll<Transform, MeshRenderer>();

    public void Draw() => world.Query(in query, (ref Transform transform, ref MeshRenderer meshRenderer) =>
    {
        if (meshRenderer.Enabled && meshRenderer.SharedMesh is not null)
            renderer.DrawMesh(in transform, in meshRenderer);
    });
}
