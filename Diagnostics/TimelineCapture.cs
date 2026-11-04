using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chippy.Diagnostics;

internal class TimelineCapture(string? path, double? seconds) : IDisposable
{
    private readonly TimelineEvent[]? events = path is null ? null : new TimelineEvent[160_000];
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly Lock recentLock = new();
    private volatile TimelineEvent[]? recent;
    private long recentCount;
    private long count, snapshot, ui;

    public bool IsEnabled => events is not null || recent is not null;
    public double ElapsedMs => Milliseconds(Stopwatch.GetTimestamp() - started);
    public bool IsComplete => seconds is { } limit && Stopwatch.GetElapsedTime(started).TotalSeconds >= limit;
    public long NextSnapshot() => Interlocked.Increment(ref snapshot);
    public long NextUi() => Interlocked.Increment(ref ui);

    public Span? Measure(string kind, long? tick = null, long? snapshot = null, long? ui = null, string? detail = null) =>
        IsEnabled ? new(this, kind, Stopwatch.GetTimestamp(), tick, snapshot, ui, detail) : null;

    public void Mark(string kind, long? tick = null, long? snapshot = null, long? ui = null, string? detail = null)
    {
        if (IsEnabled) Record(kind, Stopwatch.GetTimestamp(), null, tick, snapshot, ui, detail);
    }

    public void MarkTarget(string kind, double secondsFromNow, long? tick = null)
    {
        if (!IsEnabled) return;
        var time = Stopwatch.GetTimestamp() + (long)(secondsFromNow * Stopwatch.Frequency);
        if (time >= started) Record(kind, time, null, tick);
    }

    public void Record(string kind, long from, long? to, long? tick = null, long? snapshot = null, long? ui = null, string? detail = null)
    {
        if (!IsEnabled) return;
        var entry = new TimelineEvent(kind, Milliseconds(from - started), to is { } end ? Milliseconds(end - from) : null,
            Environment.CurrentManagedThreadId, tick, snapshot, ui, detail);
        if (events is not null)
        {
            var index = Interlocked.Increment(ref count) - 1;
            if (index < events.Length) events[index] = entry;
        }
        if (recent is null || kind is "settings" or "ui-request") return;
        lock (recentLock)
        {
            if (recent is { } buffer) buffer[recentCount++ % buffer.Length] = entry;
        }
    }

    public void SetLive(bool enabled)
    {
        if (enabled == (recent is not null)) return;
        lock (recentLock)
        {
            if (enabled == (recent is not null)) return;
            recent = enabled ? new TimelineEvent[32_768] : null;
            recentCount = 0;
        }
    }

    public void CopyRecent(List<TimelineEvent> destination)
    {
        destination.Clear();
        lock (recentLock)
        {
            if (recent is not { } buffer) return;
            destination.EnsureCapacity((int)Math.Min(recentCount, buffer.Length));
            for (var index = Math.Max(0, recentCount - buffer.Length); index < recentCount; index++)
                destination.Add(buffer[index % buffer.Length]);
        }
    }

    private static double Milliseconds(long ticks) => Math.Round(1000.0 * ticks / Stopwatch.Frequency, 4);

    public void Dispose()
    {
        if (events is null || path is null) return;
        var recorded = (int)Math.Min(count, events.Length);
        Array.Sort(events, 0, recorded, Comparer<TimelineEvent>.Create((left, right) => left.StartMs.CompareTo(right.StartMs)));
        var capture = new
        {
            version = 1,
            durationMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            dropped = count - recorded,
            events = new ArraySegment<TimelineEvent>(events, 0, recorded)
        };
        using var file = File.Create(path);
        JsonSerializer.Serialize(file, capture, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
        Console.WriteLine($"Saved {recorded:N0} timeline events to {Path.GetFullPath(path)}.");
    }

    internal readonly record struct Span(TimelineCapture Capture, string Kind, long Start,
        long? Tick, long? Snapshot, long? Ui, string? Detail) : IDisposable
    {
        public void Dispose() => Capture.Record(Kind, Start, Stopwatch.GetTimestamp(), Tick, Snapshot, Ui, Detail);
    }

    internal readonly record struct TimelineEvent(string Kind, double StartMs, double? DurationMs, int Thread,
        long? Tick, long? Snapshot, long? Ui, string? Detail);
}
