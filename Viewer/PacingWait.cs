using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Chippy.Viewer;

internal partial class PacingWait : IDisposable
{
    private const uint HighResolution = 0x00000002;
    private const uint ModifyTimer = 0x00000002;
    private const uint Synchronize = 0x00100000;
    private const uint WaitSignaled = 0;
    private readonly SafeWaitHandle? timer = OperatingSystem.IsWindows()
        ? CreateWaitableTimerEx(0, 0, HighResolution, ModifyTimer | Synchronize)
        : null;

    public void Wait(TimeSpan delay)
    {
        if (delay > TimeSpan.Zero && timer is { IsInvalid: false })
        {
            // Negative TimeSpan ticks are a relative deadline in Win32's 100 ns units.
            var dueTime = -delay.Ticks;
            if (SetWaitableTimer(timer, in dueTime, 0, 0, 0, false)
                && WaitForSingleObject(timer, uint.MaxValue) == WaitSignaled)
                return;
        }

        Thread.Sleep(delay);
    }

    public void Dispose() => timer?.Dispose();

    [LibraryImport("kernel32.dll", EntryPoint = "CreateWaitableTimerExW")]
    private static partial SafeWaitHandle CreateWaitableTimerEx(nint attributes, nint name, uint flags, uint access);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWaitableTimer(SafeWaitHandle timer, in long dueTime, int period,
        nint completionRoutine, nint context, [MarshalAs(UnmanagedType.Bool)] bool resume);

    [LibraryImport("kernel32.dll")]
    private static partial uint WaitForSingleObject(SafeWaitHandle handle, uint milliseconds);
}
