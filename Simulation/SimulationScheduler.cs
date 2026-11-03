namespace Chippy.Simulation;

// Exclusive world access: do not overlap stepping with UI/world reads or schedule changes.
// Systems run sequentially in registration order.
public class SimulationScheduler
{
    private readonly List<(ISimulationSystem System, int Every)> systems = [];
    private double pendingTicks;
    private double speed = 1;
    private bool stepping;
    public int TicksPerSecond { get; }
    public int MaxTicksPerFrame { get; }
    public double TickDuration => 1.0 / TicksPerSecond;
    public long Tick { get; private set; }
    public double ElapsedTime => Tick * TickDuration;
    public bool Paused { get; set; }
    public bool IsRunning => !Paused && Speed > 0;
    public double SecondsUntilNextTick => !IsRunning ? double.PositiveInfinity
        : Math.Max(0, 1 - pendingTicks - 1e-9) / (Speed * TicksPerSecond);
    internal bool ReservedTickIsDue => pendingTicks >= -1e-9;
    internal double SecondsUntilPublication => !IsRunning ? double.PositiveInfinity
        : Math.Max(0, -pendingTicks) / (Speed * TicksPerSecond);
    public double Speed
    {
        get => speed;
        set
        {
            if (!double.IsFinite(value) || value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Speed must be finite and nonnegative.");
            speed = value;
        }
    }

    public SimulationScheduler(int ticksPerSecond = 10, int maxTicksPerFrame = 8)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticksPerSecond);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicksPerFrame);
        TicksPerSecond = ticksPerSecond;
        MaxTicksPerFrame = maxTicksPerFrame;
    }

    public void Add(ISimulationSystem system, int every = 1)
    {
        EnsureIdle();
        ArgumentNullException.ThrowIfNull(system);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(every);
        systems.Add((system, every));
    }

    // Retain catch-up debt; a slow frame never skips simulation ticks.
    public int Advance(double realDelta)
    {
        Accumulate(realDelta);
        var count = 0;
        while (count < MaxTicksPerFrame && TryTakeTick())
        {
            Step();
            count++;
        }
        return count;
    }

    internal void Accumulate(double realDelta)
    {
        EnsureIdle();
        if (!double.IsFinite(realDelta) || realDelta < 0)
            throw new ArgumentOutOfRangeException(nameof(realDelta), "Elapsed time must be finite and nonnegative.");
        if (!IsRunning)
            return;
        var accumulated = pendingTicks + realDelta * Speed * TicksPerSecond;
        if (!double.IsFinite(accumulated))
            throw new ArgumentOutOfRangeException(nameof(realDelta), "Scaled elapsed time is too large.");
        pendingTicks = accumulated;
    }

    internal bool CanTakeTick(bool early = false) => IsRunning && (early || pendingTicks >= 1 - 1e-9);

    internal bool TryTakeTick(bool early = false)
    {
        EnsureIdle();
        if (!CanTakeTick(early)) return false;
        pendingTicks -= 1;
        if (Math.Abs(pendingTicks) < 1e-9) pendingTicks = 0;
        return true;
    }

    // Pause/manual step can commit an already computed tick without running it twice.
    internal void CommitEarlyTick() => pendingTicks = Math.Max(0, pendingTicks);

    // Manual stepping ignores pause/speed and preserves any accumulated frame time.
    public void Step()
    {
        EnsureIdle();
        stepping = true;
        try
        {
            Tick++;
            foreach (var (system, every) in systems)
                if (Tick % every == 0)
                    system.Update(TickDuration * every);
        }
        finally
        {
            stepping = false;
        }
    }

    private void EnsureIdle()
    {
        if (stepping)
            throw new InvalidOperationException("Cannot change or advance the schedule from a running simulation system.");
    }
}
