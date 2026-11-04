using System.Numerics;
using Chippy.Components;

namespace Chippy.Rendering;

// MonoGame's BasicEffect supports three directional contributions per mesh.
// Both backends use the same strongest-three selection and point-light approximation.
public class Lighting
{
    private readonly List<(Transform Transform, Light Light)> sources = [];
    private readonly (Vector3 Direction, Vector3 Color)[] samples = new (Vector3, Vector3)[3];
    public void SetLights(ReadOnlySpan<(Transform Transform, Light Light)> lights)
    {
        sources.Clear();
        foreach (var light in lights) sources.Add(light);
    }

    public ReadOnlySpan<(Vector3 Direction, Vector3 Color)> Sample(Vector3 position)
    {
        Array.Fill(samples, (Vector3.UnitY, Vector3.Zero));
        foreach (var (transform, light) in sources)
        {
            if (!light.IsActive) continue;
            var sample = light.Sample(transform, position);
            for (var i = 0; i < samples.Length; i++)
                if (sample.Color.LengthSquared() > samples[i].Color.LengthSquared())
                {
                    for (var j = samples.Length - 1; j > i; j--) samples[j] = samples[j - 1];
                    samples[i] = sample;
                    break;
                }
        }
        return samples;
    }
}
