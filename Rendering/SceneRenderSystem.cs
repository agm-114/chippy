using System.Numerics;
using Arch.Core;
using Chippy.Components;

namespace Chippy.Rendering;

// World matrices must be resolved before drawing. GPU resources belong to the injected backend.
public class SceneRenderSystem(IRenderSystem renderer)
{
    private readonly QueryDescription meshQuery = new QueryDescription().WithAll<Transform, MeshRenderer>();
    private readonly QueryDescription lineQuery = new QueryDescription().WithAll<Transform, LineRenderer>();
    private readonly PrimitiveBatch primitives = new(renderer);

    public void Draw(World world, Vector2 viewportSize, Action<World, PrimitiveBatch, Vector2>? drawOverlay = null)
    {
        world.Query(in meshQuery, (ref Transform transform, ref MeshRenderer meshRenderer) =>
        {
            if (meshRenderer.IsRenderable) renderer.DrawMesh(in transform, in meshRenderer);
        });
        world.Query(in lineQuery, (ref Transform transform, ref LineRenderer line) =>
        {
            if (line.IsRenderable) primitives.DrawPolyline(line.Positions.AsSpan(), transform.LocalToWorldMatrix, line.Color, line.Loop);
        });
        drawOverlay?.Invoke(world, primitives, viewportSize);
        primitives.Flush(viewportSize);
    }
}
