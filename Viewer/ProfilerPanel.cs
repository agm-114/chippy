using Brutal.ImGuiApi;
using Brutal.Numerics;
using Event = Chippy.Diagnostics.TimelineCapture.TimelineEvent;

namespace Chippy.Viewer;

internal class ProfilerPanel(ViewerState state)
{
    private static readonly (string Label, uint Color)[] Lanes =
    [
        ("Simulation", 0xffedab73), ("Snapshot", 0xffbbd96b),
        ("ImGui build", 0xffeb9dcd), ("Scene CPU", 0xff76b7ff),
        ("ImGui submit", 0xffd485ee), ("Present", 0xff8ed8ff), ("Pacing sleep", 0xffbbb5a6)
    ];
    private enum Mark { Tick, TickTarget, Prepare, Publish, Acquire, Ui, UiWait, Frame, FrameTarget, Submit, Present, Sleep, Count }
    private readonly List<Event> events = [];
    private float windowMs = 200;
    private double cursor;
    private double targetTicks;
    private int? targetFrames;
    private bool frozen;

    public void Draw()
    {
        if (state.Timeline is not { } timeline) return;
        timeline.SetLive(state.ShowProfiler);
        if (!state.ShowProfiler)
        {
            frozen = false;
            return;
        }

        var display = ImGui.GetIO().DisplaySize;
        var width = Math.Min(680, Math.Max(310, display.X - 24));
        var height = Math.Min(390, Math.Max(180, display.Y - 24));
        if (state.ProfilerWindow.Begin("Profiler"u8, new(Math.Max(12, (display.X - width) / 2), Math.Max(12, display.Y - height - 12)), new(width, height)))
        {
            var simulation = state.Simulation;
            var minimum = (float)simulation.MinimumTickMilliseconds;
            ImGui.SetNextItemWidth(126);
            if (ImGui.DragFloat("Min tick (ms)"u8, ref minimum, 0.1f, 0, 100, "%.1f"u8, ImGuiSliderFlags.AlwaysClamp))
                simulation.MinimumTickMilliseconds = minimum;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Synthetic CPU load. 0 disables it."u8);
            ImGui.SetNextItemWidth(126);
            ImGui.SliderFloat("History"u8, ref windowMs, 10, 1000, "%.0f ms"u8, ImGuiSliderFlags.Logarithmic);
            ImGui.SameLine();
            if (ImGui.Button(frozen ? "Resume history"u8 : "Freeze history"u8)) frozen = !frozen;

            if (!frozen)
            {
                timeline.CopyRecent(events);
                cursor = timeline.ElapsedMs;
                targetTicks = simulation.IsRunning ? simulation.TargetTickRate : 0;
                targetFrames = state.WantsVSync ? null : state.TargetFrameRate;
            }
            DrawHistory();
        }
        ImGui.End();
    }

    private void DrawHistory()
    {
        const float rowHeight = 22;
        const float labelWidth = 105;
        var lanesHeight = rowHeight * Lanes.Length;
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var left = origin.X + labelWidth;
        var right = origin.X + width;
        if (right - left < 12) return;
        var plotWidth = right - left;
        var from = Math.Max(0, cursor - windowMs);
        var oldest = cursor;
        foreach (var entry in events)
            if (entry.DurationMs is not null) oldest = Math.Min(oldest, entry.StartMs);
        if (events.Count > 0) from = Math.Max(from, oldest);
        var duration = Math.Max(0.01, cursor - from);
        ImGui.Dummy(new float2(width, lanesHeight + 20));
        var mouse = ImGui.GetIO().MousePos;
        var hoverLane = ImGui.IsItemHovered() && mouse.X >= left && mouse.Y < origin.Y + lanesHeight
            ? (int)((mouse.Y - origin.Y) / rowHeight) : -1;
        Event? hovered = null;
        var hoverDistance = float.PositiveInfinity;
        var draw = ImGui.GetWindowDrawList();
        var text = ImGui.GetColorU32(ImGuiCol.Text);
        var background = ImGui.GetColorU32(ImGuiCol.FrameBg);
        var border = ImGui.GetColorU32(ImGuiCol.Border);
        for (var lane = 0; lane < Lanes.Length; lane++)
        {
            var y = origin.Y + lane * rowHeight;
            draw.AddText(new float2(origin.X, y + 2), text, Lanes[lane].Label);
            draw.AddRectFilled(new float2(left, y), new float2(right, y + rowHeight - 1), background);
        }
        for (var i = 1; i < 4; i++)
        {
            var x = left + plotWidth * i / 4;
            draw.AddLine(new float2(x, origin.Y), new float2(x, origin.Y + lanesHeight), border);
        }

        var tickCount = 0;
        var frameCount = 0;
        var tickTotal = 0.0;
        var tickPeak = 0.0;
        var framePeak = 0.0;
        var snapshotPeak = 0.0;
        var uiPeak = 0.0;
        var uiWait = 0.0;
        var submitPeak = 0.0;
        var presentPeak = 0.0;
        var sleepPeak = 0.0;
        Span<int> lastPixel = stackalloc int[(int)Mark.Count];
        lastPixel.Fill(-1);
        foreach (var entry in events)
        {
            var length = entry.DurationMs ?? 0;
            var end = entry.StartMs + length;
            if (end < from || entry.StartMs > cursor) continue;
            var (lane, mark) = entry.Kind switch
            {
                "tick" => (0, Mark.Tick), "tick-target" => (0, Mark.TickTarget),
                "prepare" => (1, Mark.Prepare), "publish" => (1, Mark.Publish), "acquire" => (1, Mark.Acquire),
                "ui" => (2, Mark.Ui), "ui-wait" => (2, Mark.UiWait),
                "frame" => (3, Mark.Frame), "frame-target" => (3, Mark.FrameTarget),
                "ui-submit" => (4, Mark.Submit), "present" => (5, Mark.Present), "sleep" => (6, Mark.Sleep),
                _ => (-1, Mark.Count)
            };
            if (lane < 0) continue;
            if (entry.StartMs >= from)
            {
                switch (mark)
                {
                    case Mark.Tick: tickCount++; tickTotal += length; tickPeak = Math.Max(tickPeak, length); break;
                    case Mark.Frame: frameCount++; framePeak = Math.Max(framePeak, length); break;
                    case Mark.Prepare: snapshotPeak = Math.Max(snapshotPeak, length); break;
                    case Mark.Ui: uiPeak = Math.Max(uiPeak, length); break;
                    case Mark.UiWait: uiWait = Math.Max(uiWait, length); break;
                    case Mark.Submit: submitPeak = Math.Max(submitPeak, length); break;
                    case Mark.Present: presentPeak = Math.Max(presentPeak, length); break;
                    case Mark.Sleep: sleepPeak = Math.Max(sleepPeak, length); break;
                }
            }
            var x = left + (float)((Math.Max(from, entry.StartMs) - from) / duration) * plotWidth;
            var xEnd = Math.Min(right, left + (float)((end - from) / duration) * plotWidth);
            var center = origin.Y + lane * rowHeight + rowHeight / 2;
            var target = mark is Mark.TickTarget or Mark.FrameTarget;
            var thin = xEnd - x < (mark == Mark.Tick ? 2 : 1);
            var color = Lanes[lane].Color;
            var halfHeight = 4f;
            if (target)
            {
                color = (color & 0x00ffffff) | 0x90000000;
                center -= 7;
                halfHeight = 2;
            }
            else if (mark == Mark.UiWait) { center -= 7; halfHeight = 1; }
            else if (mark == Mark.Acquire) { center += 6; halfHeight = 2; }
            else if (mark == Mark.Tick && thin) { center += 1; halfHeight = 5; }

            var pixel = (int)MathF.Floor(x - left);
            if (thin)
            {
                var markerWidth = mark == Mark.Tick ? 2 : 1;
                x = Math.Clamp(MathF.Floor(x), left, right - markerWidth);
                xEnd = x + markerWidth;
            }
            if (!thin || lastPixel[(int)mark] != pixel)
            {
                draw.AddRectFilled(new float2(x, center - halfHeight), new float2(xEnd, center + halfHeight), color);
                lastPixel[(int)mark] = pixel;
            }
            if (lane != hoverLane || mouse.X < x - 3 || mouse.X > xEnd + 3) continue;
            var distance = Math.Abs(mouse.X - (x + xEnd) / 2) + Math.Abs(mouse.Y - center);
            if (distance >= hoverDistance) continue;
            hoverDistance = distance;
            hovered = entry;
        }
        draw.AddText(new float2(left, origin.Y + lanesHeight + 2), text, $"−{duration:0.#} ms");
        draw.AddText(new float2(right - 39, origin.Y + lanesHeight + 2), text, "0 ms"u8);
        if (hovered is { } item)
        {
            draw.AddLine(new float2(mouse.X, origin.Y), new float2(mouse.X, origin.Y + lanesHeight), text);
            var info = item.Tick is { } tick ? $" · tick {tick:N0}" : "";
            if (item.Snapshot is { } snapshot) info += $" · snapshot {snapshot:N0}";
            if (item.DurationMs is { } milliseconds)
                info += milliseconds < 0.1 ? $" · {milliseconds * 1000:0.#} µs" : $" · {milliseconds:0.###} ms";
            if (item.Detail is { } detail) info += $" · {detail}";
            ImGui.SetTooltip($"{item.Kind}{info}");
        }
        var actual = tickCount * 1000 / duration;
        var fps = frameCount * 1000 / duration;
        var frameTarget = targetFrames is { } targetFps ? $"{fps:0.#} / {targetFps} FPS" : $"{fps:0.#} FPS (VSync)";
        ImGui.TextWrapped($"Ticks {actual:N0} / {targetTicks:N0} Hz   ·   Frames {frameTarget}");
        var tickAverage = tickCount > 0 ? tickTotal / tickCount : 0;
        var tickBudget = targetTicks > 0 ? $" / {1000 / targetTicks:0.###} ms budget" : "";
        ImGui.TextWrapped($"Tick CPU avg {tickAverage:0.###} ms, max {tickPeak:0.###} ms{tickBudget}");
        ImGui.TextWrapped($"Max CPU  Scene {framePeak:0.###} ms   ·   Snapshot {snapshotPeak:0.###} ms   ·   ImGui {uiPeak:0.###} ms");
        ImGui.TextWrapped($"Max ImGui wait  {uiWait:0.###} ms");
        ImGui.TextWrapped($"Max after scene  Submit {submitPeak:0.###} ms   ·   Present {presentPeak:0.###} ms   ·   Sleep {sleepPeak:0.###} ms");
    }
}
