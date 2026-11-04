using System.Numerics;
using System.Runtime.InteropServices;
using Arch.Core;
using Chippy.Components;

namespace Chippy.Rendering;

// World matrices must be resolved before drawing. GPU resources belong to the injected backend.
public class SceneRenderSystem(IRenderSystem renderer)
{
    private readonly QueryDescription meshQuery = new QueryDescription().WithAll<Transform, MeshRenderer>();
    private readonly QueryDescription lineQuery = new QueryDescription().WithAll<Transform, LineRenderer>();
    private readonly QueryDescription lightQuery = new QueryDescription().WithAll<Transform, Light>();
    private readonly List<(Transform, Light)> lights = [];
    private readonly PrimitiveBatch primitives = new(renderer);

    public void Draw(World world, Vector2 viewportSize, Action<World, PrimitiveBatch, Vector2>? drawOverlay = null, Vector3 origin = default)
    {
        lights.Clear();
        world.Query(in lightQuery, (ref Transform transform, ref Light light) =>
        {
            if (!light.IsActive) return;
            var copy = RelativeTo(transform, origin);
            lights.Add((copy, light));
        });
        renderer.SetLights(CollectionsMarshal.AsSpan(lights));
        world.Query(in meshQuery, (ref Transform transform, ref MeshRenderer meshRenderer) =>
        {
            if (!meshRenderer.IsRenderable) return;
            var copy = RelativeTo(transform, origin);
            renderer.DrawMesh(in copy, in meshRenderer);
        });
        world.Query(in lineQuery, (ref Transform transform, ref LineRenderer line) =>
        {
            if (line.IsRenderable) primitives.DrawPolyline(line.Positions.AsSpan(), transform.LocalToWorldMatrix, line.Color, line.Loop);
        });
        drawOverlay?.Invoke(world, primitives, viewportSize);
        primitives.Flush(viewportSize, origin);
    }

    private static Transform RelativeTo(Transform transform, Vector3 origin)
    {
        var matrix = transform.LocalToWorldMatrix;
        matrix.Translation -= origin;
        transform.LocalToWorldMatrix = matrix;
        return transform;
    }
}
