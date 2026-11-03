using System.Reflection;
using System.Runtime.InteropServices;

namespace Chippy.Audio;

internal static unsafe class OpenAl
{
    private const string Library = "chippy-openal";
    private static nint library;

    static OpenAl() => NativeLibrary.SetDllImportResolver(typeof(OpenAl).Assembly, Resolve);

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library) return 0;
        if (library != 0) return library;
        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        var platform = OperatingSystem.IsWindows() ? $"win-{architecture}" : OperatingSystem.IsMacOS() ? "osx" : $"linux-{architecture}";
        var file = OperatingSystem.IsWindows() ? "openal.dll" : OperatingSystem.IsMacOS() ? "libopenal.dylib" : "libopenal.so";
        var path = Path.Combine(AppContext.BaseDirectory, "runtimes", platform, "native", file);
        return library = NativeLibrary.Load(path);
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint alcOpenDevice(nint name);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern byte alcCloseDevice(nint device);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern nint alcCreateContext(nint device, nint attributes);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern byte alcMakeContextCurrent(nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void alcDestroyContext(nint context);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int alGetError();
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void alGenBuffers(int count, out uint buffer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void alDeleteBuffers(int count, ref uint buffer);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void alBufferData(uint buffer, int format, void* data, int size, int frequency);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void alGenSources(int count, out uint source);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void alDeleteSources(int count, ref uint source);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void alSourcei(uint source, int parameter, int value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void alSourcef(uint source, int parameter, float value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void alSource3f(uint source, int parameter, float x, float y, float z);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void alGetSourcei(uint source, int parameter, out int value);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void alSourcePlay(uint source);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void alSourcePause(uint source);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void alSourceStop(uint source);
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void alListenerf(int parameter, float value);
}
