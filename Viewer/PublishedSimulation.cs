using Chippy.Rendering;

namespace Chippy.Viewer;

internal class PublishedSimulation : IDisposable
{
    private readonly ViewerState state;
    private readonly TimeProvider clock;
    private readonly Lock worldAccess = new();
    private readonly AutoResetEvent wake = new(false);
    private readonly Task loop;
    private long lastUpdate;
    private volatile bool stopping, uiPending, stepping, earlyTick;

    public RenderWorld Render { get; }
    public bool IsStepping => stepping;
    public bool IsWaitingForUi => uiPending;
    public bool HasPreparedTick => earlyTick && !stepping;

    public PublishedSimulation(ViewerState state, TimeProvider clock)
    {
        this.state = state;
        this.clock = clock;
        Render = new(state.World);
        try
        {
            Render.Prepare(state.Simulation.Tick);
            Render.Publish();
            lastUpdate = clock.GetTimestamp();
            loop = Task.Factory.StartNew(Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        catch
        {
            Render.Dispose();
            wake.Dispose();
            throw;
        }
    }

    public void SetUiPending(bool pending)
    {
        var wasPending = uiPending;
        uiPending = pending;
        if (wasPending && !pending) wake.Set();
    }

    public void CheckForFailure()
    {
        if (loop.IsFaulted) loop.GetAwaiter().GetResult();
    }

    // ImGui stays on the window thread. Try the gate so an active tick never stalls a draw.
    public bool TryUseWorld(Func<bool> edit)
    {
        if (!worldAccess.TryEnter()) return false;
        try
        {
            var simulation = state.Simulation;
            var speed = simulation.Speed;
            var paused = simulation.Paused;
            var preemptive = state.PreemptiveTicking;
            var publishing = state.PublishRenderData;
            var tick = simulation.Tick;
            var wasEarly = earlyTick;
            UpdateClock();
            var changed = edit();
            var refresh = changed || tick != simulation.Tick || wasEarly != earlyTick;
            if (refresh)
            {
                Render.Prepare(simulation.Tick);
                PublishIfDue();
            }
            if (refresh || speed != simulation.Speed || paused != simulation.Paused
                || preemptive != state.PreemptiveTicking || publishing != state.PublishRenderData)
                wake.Set();
            return true;
        }
        finally { worldAccess.Exit(); }
    }

    public bool TryStep()
    {
        if (worldAccess.IsHeldByCurrentThread)
        {
            Step();
            return true;
        }
        return TryUseWorld(() => { Step(); return false; });
    }

    private void Step()
    {
        if (earlyTick) state.Simulation.CommitEarlyTick();
        else state.Simulation.Step();
        earlyTick = false;
    }

    private void Run()
    {
        var simulation = state.Simulation;
        while (!stopping)
        {
            double delay;
            lock (worldAccess)
            {
                if (stopping) return;
                UpdateClock();
                PublishIfDue();
                if (state.PublishRenderData && !uiPending && !Render.HasPrepared
                    && simulation.TryTakeTick(state.PreemptiveTicking))
                {
                    stepping = true;
                    try
                    {
                        earlyTick = !simulation.ReservedTickIsDue;
                        simulation.Step();
                        Render.Prepare(simulation.Tick);
                    }
                    finally { stepping = false; }
                    continue;
                }

                delay = Render.HasPrepared ? simulation.SecondsUntilPublication
                    : uiPending || !state.PublishRenderData ? double.PositiveInfinity : simulation.SecondsUntilNextTick;
                delay -= clock.GetElapsedTime(lastUpdate).TotalSeconds;
            }
            if (delay > 0)
                wake.WaitOne(double.IsPositiveInfinity(delay) ? Timeout.Infinite
                    : (int)Math.Min(Math.Ceiling(delay * 1000), int.MaxValue));
        }
    }

    private void PublishIfDue()
    {
        var simulation = state.Simulation;
        if (earlyTick && !simulation.IsRunning) simulation.CommitEarlyTick();
        if (Render.HasPrepared && (!earlyTick || simulation.ReservedTickIsDue))
        {
            Render.PublishLatest();
            earlyTick = false;
        }
    }

    private void UpdateClock()
    {
        var now = clock.GetTimestamp();
        state.Simulation.Accumulate(clock.GetElapsedTime(lastUpdate, now).TotalSeconds);
        lastUpdate = now;
    }

    public void Dispose()
    {
        stopping = true;
        wake.Set();
        try { loop.GetAwaiter().GetResult(); }
        finally
        {
            state.Simulation.CommitEarlyTick();
            Render.Dispose();
            wake.Dispose();
        }
    }
}
