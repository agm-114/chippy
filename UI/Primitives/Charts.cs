using System.Text;
using Brutal.ImPlotApi;
using Brutal.Numerics;
using Brutal.Pointers;

namespace Chippy.UI.Primitives;

public readonly record struct ChartSeries(string Name, double[] Values);

public static class Charts
{
    public static void History(string title, string xLabel, string yLabel, ReadOnlySpan<double> x,
        ReadOnlySpan<ChartSeries> series, bool steps = false, float height = 300)
    {
        if (!ImPlot.BeginPlot(title, new float2(-1, height))) return;
        ImPlot.SetupAxes(xLabel, yLabel, yFlags: ImPlotAxisFlags.AutoFit);
        foreach (var item in series)
        {
            var count = Math.Min(x.Length, item.Values.Length);
            if (steps) ImPlot.PlotStairs(item.Name, x[..count], item.Values.AsSpan(0, count));
            else ImPlot.PlotLine(item.Name, x[..count], item.Values.AsSpan(0, count));
        }
        ImPlot.EndPlot();
    }

    public static void StackedHistory(string title, string xLabel, string yLabel, ReadOnlySpan<double> x,
        ReadOnlySpan<ChartSeries> series, float height = 300)
    {
        if (!ImPlot.BeginPlot(title, new float2(-1, height))) return;
        ImPlot.SetupAxes(xLabel, yLabel, yFlags: ImPlotAxisFlags.AutoFit);
        var count = x.Length;
        foreach (var item in series) count = Math.Min(count, item.Values.Length);
        var lower = new double[count];
        var upper = new double[count];
        foreach (var item in series)
        {
            for (var i = 0; i < count; i++) upper[i] = lower[i] + Math.Max(0, item.Values[i]);
            ImPlot.PlotShaded(item.Name, x[..count], lower.AsSpan(0, count), upper.AsSpan(0, count));
            (lower, upper) = (upper, lower);
        }
        ImPlot.EndPlot();
    }

    public static unsafe void GroupedBars(string title, string yLabel, ReadOnlySpan<string> categories,
        ReadOnlySpan<ChartSeries> series, float height = 300)
    {
        if (categories.IsEmpty || series.IsEmpty) return;
        if (!ImPlot.BeginPlot(title, new float2(-1, height))) return;
        ImPlot.SetupAxes(""u8, yLabel, yFlags: ImPlotAxisFlags.AutoFit);
        ImPlot.SetupAxisLimits(ImAxis.X1, -0.6, categories.Length - 0.4, ImPlotCond.Always);
        var bytes = Encoding.UTF8.GetBytes(string.Join('\0', categories.ToArray()) + '\0');
        var positions = new double[categories.Length];
        var labels = new Ptr<byte>[categories.Length];
        fixed (byte* start = bytes)
        {
            var offset = 0;
            for (var i = 0; i < categories.Length; i++)
            {
                positions[i] = i;
                labels[i] = start + offset;
                offset += Encoding.UTF8.GetByteCount(categories[i]) + 1;
            }
            ImPlot.SetupAxisTicks(ImAxis.X1, positions, labels);
            var barWidth = 0.75 / series.Length;
            for (var i = 0; i < series.Length; i++)
                ImPlot.PlotBars(series[i].Name, series[i].Values.AsSpan(0, Math.Min(categories.Length, series[i].Values.Length)),
                    barWidth, -0.375 + barWidth * (i + 0.5));
            ImPlot.EndPlot();
        }
    }

    public static void Histogram(string title, string xLabel, ReadOnlySpan<double> values, float height = 300)
    {
        if (!ImPlot.BeginPlot(title, new float2(-1, height))) return;
        ImPlot.SetupAxes(xLabel, "Count"u8);
        ImPlot.PlotHistogram("Samples"u8, values);
        ImPlot.EndPlot();
    }

    public static void Scatter(string title, string xLabel, string yLabel, ReadOnlySpan<double> x,
        ReadOnlySpan<ChartSeries> series, float height = 300)
    {
        if (!ImPlot.BeginPlot(title, new float2(-1, height))) return;
        ImPlot.SetupAxes(xLabel, yLabel);
        foreach (var item in series)
        {
            var count = Math.Min(x.Length, item.Values.Length);
            ImPlot.PlotScatter(item.Name, x[..count], item.Values.AsSpan(0, count));
        }
        ImPlot.EndPlot();
    }
}
