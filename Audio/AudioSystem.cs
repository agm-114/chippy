namespace Chippy.Audio;

public enum PlaybackState { Stopped, Playing, Paused }

// Owns its OpenAL context independently of the graphics backend. Call from the window thread.
public class AudioSystem : IDisposable
{
    private const int Gain = 0x100A, State = 0x1010, Playing = 0x1012, Paused = 0x1013;
    private readonly int ownerThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<AudioClip, uint> buffers = new(ReferenceEqualityComparer.Instance);
    private readonly List<(uint Source, float Volume)> effects = [];
    private nint device, context;
    private uint music;
    private bool disposed;
    public bool IsAvailable => context != 0 && !disposed;
    public string? Error { get; private set; }
    public PlaybackState MusicState
    {
        get
        {
            if (!MakeCurrent() || music == 0) return PlaybackState.Stopped;
            OpenAl.alGetSourcei(music, State, out var state);
            return state == Playing ? PlaybackState.Playing : state == Paused ? PlaybackState.Paused : PlaybackState.Stopped;
        }
    }
    public float MasterVolume
    {
        get;
        set { ValidateVolume(value); field = value; if (MakeCurrent()) OpenAl.alListenerf(Gain, value); }
    } = 0.5f;
    public float MusicVolume
    {
        get;
        set { ValidateVolume(value); field = value; if (MakeCurrent() && music != 0) OpenAl.alSourcef(music, Gain, value); }
    } = 0.2f;
    public float EffectsVolume
    {
        get;
        set
        {
            ValidateVolume(value);
            field = value;
            if (MakeCurrent())
                foreach (var effect in effects) OpenAl.alSourcef(effect.Source, Gain, effect.Volume * value);
        }
    } = 0.7f;

    public AudioSystem()
    {
        try
        {
            device = OpenAl.alcOpenDevice(0);
            if (device == 0) { Error = "No audio output device is available."; return; }
            context = OpenAl.alcCreateContext(device, 0);
            if (context == 0 || OpenAl.alcMakeContextCurrent(context) == 0)
                throw new InvalidOperationException("Unable to create the audio context.");
            OpenAl.alListenerf(Gain, MasterVolume);
        }
        catch (Exception error) when (error is DllNotFoundException or BadImageFormatException or InvalidOperationException)
        {
            Error = error.Message;
            if (context != 0) OpenAl.alcDestroyContext(context);
            if (device != 0) OpenAl.alcCloseDevice(device);
            context = device = 0;
        }
    }

    public bool PlayMusic(AudioClip clip, bool loop = true)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (!MakeCurrent()) return false;
        StopMusic();
        music = CreateSource(clip, MusicVolume, 1, 0, loop);
        OpenAl.alSourcePlay(music);
        CheckError();
        return true;
    }

    public void PauseMusic() { if (MakeCurrent() && music != 0) OpenAl.alSourcePause(music); }
    public void ResumeMusic() { if (MakeCurrent() && music != 0) OpenAl.alSourcePlay(music); }
    public void StopMusic() { if (MakeCurrent()) DeleteSource(ref music); }

    public bool PlayOneShot(AudioClip clip, float volume = 1, float pitch = 1, float pan = 0)
    {
        ArgumentNullException.ThrowIfNull(clip);
        ValidateVolume(volume);
        if (!float.IsFinite(pitch) || pitch <= 0) throw new ArgumentOutOfRangeException(nameof(pitch));
        if (!float.IsFinite(pan) || pan is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(pan));
        if (!MakeCurrent()) return false;
        Update();
        if (effects.Count == 32)
        {
            var oldest = effects[0].Source;
            DeleteSource(ref oldest);
            effects.RemoveAt(0);
        }
        var source = CreateSource(clip, volume * EffectsVolume, pitch, pan, false);
        effects.Add((source, volume));
        OpenAl.alSourcePlay(source);
        CheckError();
        return true;
    }

    public void Update()
    {
        if (!MakeCurrent() || effects.Count == 0) return;
        for (var i = effects.Count - 1; i >= 0; i--)
        {
            var source = effects[i].Source;
            OpenAl.alGetSourcei(source, State, out var state);
            if (state == Playing) continue;
            DeleteSource(ref source);
            effects.RemoveAt(i);
        }
    }

    private unsafe uint CreateSource(AudioClip clip, float volume, float pitch, float pan, bool loop)
    {
        if (!buffers.TryGetValue(clip, out var buffer))
        {
            OpenAl.alGenBuffers(1, out buffer);
            fixed (byte* data = clip.Pcm) OpenAl.alBufferData(buffer, 0x1101, data, clip.Pcm.Length, clip.SampleRate);
            if (OpenAl.alGetError() != 0)
            {
                OpenAl.alDeleteBuffers(1, ref buffer);
                throw new InvalidOperationException("Unable to upload the generated audio clip.");
            }
            buffers.Add(clip, buffer);
        }
        OpenAl.alGenSources(1, out var source);
        OpenAl.alSourcei(source, 0x1009, (int)buffer); // AL_BUFFER
        OpenAl.alSourcei(source, 0x1007, loop ? 1 : 0); // AL_LOOPING
        OpenAl.alSourcei(source, 0x202, 1); // AL_SOURCE_RELATIVE
        OpenAl.alSourcef(source, 0x1021, 0); // AL_ROLLOFF_FACTOR
        OpenAl.alSourcef(source, Gain, volume);
        OpenAl.alSourcef(source, 0x1003, pitch); // AL_PITCH
        OpenAl.alSource3f(source, 0x1004, pan, 0, -MathF.Sqrt(1 - pan * pan)); // AL_POSITION
        if (OpenAl.alGetError() == 0) return source;
        DeleteSource(ref source);
        throw new InvalidOperationException("Unable to create an audio source.");
    }

    private bool MakeCurrent()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Environment.CurrentManagedThreadId != ownerThread)
            throw new InvalidOperationException("Use audio from the window thread.");
        return context != 0 && OpenAl.alcMakeContextCurrent(context) != 0;
    }

    private static void ValidateVolume(float value)
    {
        if (!float.IsFinite(value) || value is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static void CheckError()
    {
        if (OpenAl.alGetError() != 0) throw new InvalidOperationException("Audio playback failed.");
    }

    private static void DeleteSource(ref uint source)
    {
        if (source == 0) return;
        OpenAl.alSourceStop(source);
        OpenAl.alDeleteSources(1, ref source);
        source = 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        if (MakeCurrent())
        {
            DeleteSource(ref music);
            foreach (var effect in effects) { var source = effect.Source; DeleteSource(ref source); }
            foreach (var value in buffers.Values) { var buffer = value; OpenAl.alDeleteBuffers(1, ref buffer); }
            OpenAl.alcMakeContextCurrent(0);
        }
        if (context != 0) OpenAl.alcDestroyContext(context);
        if (device != 0) OpenAl.alcCloseDevice(device);
        context = device = 0;
        buffers.Clear();
        effects.Clear();
        disposed = true;
    }
}
