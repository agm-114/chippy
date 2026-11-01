using Chippy.Models;
using Chippy.Rendering;

namespace Chippy.Viewer;

internal static class RenderChecks
{
    public static void CheckModelPixels(ReadOnlySpan<byte> pixels)
    {
        var visible = 0;
        for (var i = 0; i < pixels.Length; i += 4)
            if (Math.Abs(pixels[i] - pixels[0]) + Math.Abs(pixels[i + 1] - pixels[1])
                + Math.Abs(pixels[i + 2] - pixels[2]) > 15)
                visible++;
        if (visible < 100)
            throw new InvalidOperationException($"Render check failed: {visible} visible model pixels.");
        Console.WriteLine($"Render check passed: {visible} visible model pixels.");
    }

    public static void CheckMeshSharing(IRenderSystem renderer, MeshInstance[] instances)
    {
        var expected = instances.Where(instance => instance.Visible).Select(instance => instance.SharedMesh).Distinct().Count();
        if (renderer.UploadedMeshCount != expected)
            throw new InvalidOperationException("Mesh GPU resource sharing check failed.");
        Console.WriteLine($"Mesh check passed: {instances.Length} instances share {renderer.UploadedMeshCount} uploaded meshes.");
    }
}
