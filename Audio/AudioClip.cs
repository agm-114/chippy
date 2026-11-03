using System.Buffers.Binary;

namespace Chippy.Audio;

// Immutable mono PCM; generated without an audio device and shared by all playback sources.
public class AudioClip
{
    internal byte[] Pcm { get; }
    public int SampleRate { get; }
    public int SampleCount => Pcm.Length / sizeof(short);
    public double Duration => SampleCount / (double)SampleRate;

    private AudioClip(byte[] pcm, int sampleRate) => (Pcm, SampleRate) = (pcm, sampleRate);

    public static AudioClip Generate(double duration, Func<double, float> sample, int sampleRate = 44100)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (!double.IsFinite(duration) || duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration));
        if (sampleRate is < 8000 or > 48000) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        var count = checked((int)Math.Ceiling(duration * sampleRate));
        var pcm = new byte[checked(count * sizeof(short))];
        for (var i = 0; i < count; i++)
        {
            var value = sample(i / (double)sampleRate);
            if (!float.IsFinite(value)) throw new ArgumentException("Audio samples must be finite.", nameof(sample));
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2, 2), (short)Math.Round(Math.Clamp(value, -1, 1) * short.MaxValue));
        }
        return new(pcm, sampleRate);
    }

    public static AudioClip Tone(double frequency, double duration = 0.25)
    {
        if (!double.IsFinite(frequency) || frequency <= 0 || frequency >= 22050)
            throw new ArgumentOutOfRangeException(nameof(frequency));
        return Generate(duration, t => (float)(Math.Sin(Math.Tau * frequency * t) * Math.Sin(Math.PI * t / duration) * 0.5));
    }
}
