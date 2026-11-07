using Brutal.ImGuiApi;
using Brutal.Numerics;
using Chippy.UI.Primitives;

namespace Chippy.Viewer;

internal class ProductionUiDemo(ViewerState state)
{
    private static readonly (string Group, string Name)[] Examples =
    [
        ("Indicators", "Metric cards"), ("Indicators", "Gauges"), ("Indicators", "Capacity segments"),
        ("Indicators", "Flow balance"), ("Indicators", "Status filters"),
        ("Charts", "History"), ("Charts", "Step history"), ("Charts", "Stacked history"),
        ("Charts", "Grouped bars"), ("Charts", "Distribution"), ("Charts", "Correlation"), ("Charts", "Allocation ring"),
        ("Operations", "Inventory table"), ("Operations", "Process table"), ("Operations", "Flow diagram"),
        ("Operations", "Queue board"), ("Operations", "Schedule"), ("Operations", "Requirements tree"),
        ("Controls", "Quantity editor"), ("Controls", "Activity feed"), ("Controls", "Threshold controls"),
        ("Composition", "Dashboard")
    ];
    private static readonly double[] Hours = Enumerable.Range(0, 97).Select(i => i / 4.0).ToArray();
    private static readonly ChartSeries[] Trends =
    [
        new("Incoming", Hours.Select(hour => 30 + 8 * Math.Sin(hour * 0.5)).ToArray()),
        new("Outgoing", Hours.Select(hour => 23 + 6 * Math.Cos(hour * 0.8)).ToArray())
    ];
    private static readonly InventoryRow[] Inventory =
    [
        new("Feedstock A", 240, 500, 18, 12, "kg"), new("Feedstock B", 80, 200, 4, 9, "kg"),
        new("Intermediate", 160, 400, 12, 10, "units"), new("Finished goods", 42, 120, 10, 8, "units")
    ];
    private static readonly ProcessRow[] Processes =
    [new("Line A", 0.72f, 14, "Running"), new("Line B", 0.98f, 38, "At capacity"), new("Line C", 0.18f, 2, "Waiting")];
    private static readonly CapacitySegment[] Segments =
    [new("Allocated", 45, 0xffb5cb74), new("Reserved", 22, 0xffe4b57e), new("Unavailable", 8, 0xff7e83e1)];
    private static readonly FlowNode[] Nodes =
    [
        new("Source", new(0, 0.45f), "120 in storage", 0xffb5cb74),
        new("Process A", new(0.5f, 0), "72% load", 0xffb5cb74),
        new("Process B", new(0.5f, 0.9f), "Waiting", 0xffe4b57e),
        new("Output", new(1, 0.45f), "42 available", 0xffe4b57e)
    ];
    private static readonly FlowLink[] Links = [new(0, 1, "12/h"), new(0, 2, "4/h"), new(1, 3, "10/h"), new(2, 3, "2/h")];
    private static readonly QueueItem[] Jobs =
    [
        new("Order 104", "Queued", 0), new("Order 105", "Queued", 0), new("Order 101", "Running", 0.64f),
        new("Order 102", "Running", 0.32f), new("Order 103", "Blocked", 0.18f), new("Order 100", "Done", 1)
    ];
    private static readonly ScheduleBlock[] Schedule =
    [
        new("Machine A", "Batch 1", 1, 5, 0xffb5cb74), new("Machine A", "Batch 2", 8, 6, 0xffe4b57e),
        new("Machine A", "Service", 17, 3, 0xff7e83e1), new("Machine B", "Batch 3", 3, 8, 0xffa8d5ac),
        new("Machine B", "Batch 4", 13, 7, 0xffb5cb74), new("Machine C", "Batch 5", 6, 10, 0xffe4b57e)
    ];
    private static readonly Requirement[] Requirements =
    [
        new("Assembly", 1, "unit",
        [new("Module A", 2, "units", [new("Part A", 4, "units"), new("Material A", 0.5, "kg")]),
            new("Module B", 1, "unit", [new("Part B", 3, "units"), new("Material B", 0.2, "kg")]),
            new("Fasteners", 12, "units")])
    ];
    private static readonly ActivityRow[] Events =
    [new("14:06", "Order 101 started", 0xffb5cb74), new("14:03", "Line C waiting for input", 0xffe4b57e),
        new("14:01", "Buffer B below minimum", 0xff7e83e1), new("13:58", "Order 100 completed", 0xffb5cb74)];
    private static readonly double[] Samples = MakeSamples();
    private static readonly ChartSeries[] Correlation =
        [new("Output", Hours.Select((hour, i) => hour * 1.4 + Math.Sin(i * 17) * 3).ToArray())];
    private readonly ImInputString search = new(128, ""u8);
    private readonly QuantityField[] quantities = [new("Input A", "kg", 4), new("Input B", "units", 12), new("Output", "units", 2)];
    private int example, selection = -1;
    private float load = 72, cursor = 8, minimum = 20, target = 60, maximum = 90;
    private bool automatic = true;

    public void Draw()
    {
        if (!state.ShowProductionUiDemo) return;
        var display = ImGui.GetIO().DisplaySize;
        if (state.ProductionUiDemoWindow.Begin("Production UI primitives"u8, new(24, 24),
            new(Math.Min(1060, display.X - 48), Math.Min(670, display.Y - 48))))
        {
            ImGui.TextDisabled("Demo values"u8);
            ImGui.SameLine();
            if (ImGui.Button("Close"u8)) state.ShowProductionUiDemo = false;
            if (ImGui.BeginTable("Catalog"u8, 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
            {
                ImGui.TableSetupColumn("Examples"u8, ImGuiTableColumnFlags.WidthFixed, 215);
                ImGui.TableSetupColumn("Preview"u8, ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.BeginChild("Navigation"u8, new float2(0, -1));
                ImGui.SetNextItemWidth(-1);
                ImGui.InputText("##Search"u8, search);
                var filter = search.ToString();
                var group = "";
                for (var i = 0; i < Examples.Length; i++)
                {
                    if (!Examples[i].Name.Contains(filter, StringComparison.OrdinalIgnoreCase)) continue;
                    if (group != Examples[i].Group) ImGui.SeparatorText(group = Examples[i].Group);
                    if (ImGui.Selectable(Examples[i].Name, example == i)) { example = i; selection = -1; }
                }
                ImGui.EndChild();
                ImGui.TableNextColumn();
                ImGui.BeginChild("Preview"u8, new float2(0, -1));
                ImGui.PushID(example);
                ImGui.SeparatorText(Examples[example].Name);
                DrawExample();
                ImGui.PopID();
                ImGui.EndChild();
                ImGui.EndTable();
            }
        }
        ImGui.End();
    }

    private void DrawExample()
    {
        switch (example)
        {
            case 0: DrawMetrics(); break;
            case 1:
                ImGui.SliderFloat("Load"u8, ref load, 0, 100, "%.0f%%"u8);
                ProductionWidgets.Gauge("Utilization", load, 100);
                break;
            case 2: ProductionWidgets.CapacityBar("Capacity", Segments, 100); break;
            case 3: ProductionWidgets.Balance("Buffer", 240, 18, 12, "kg"); break;
            case 4:
                string[] labels = ["Running", "Waiting", "Blocked", "Offline"];
                uint[] colors = [0xff5a7040, 0xff856c3e, 0xff515798, 0xff61584f];
                for (var i = 0; i < labels.Length; i++)
                {
                    if (i != 0) ImGui.SameLine();
                    if (ProductionWidgets.Badge(labels[i], colors[i])) selection = i;
                }
                if (selection >= 0) ImGui.Text($"Filter: {labels[selection]}");
                break;
            case 5: Charts.History("Rates", "Hour", "units/h", Hours, Trends); break;
            case 6: Charts.History("Stock changes", "Hour", "units", Hours, Trends, steps: true); break;
            case 7: Charts.StackedHistory("Allocation", "Hour", "kW", Hours, Trends); break;
            case 8:
                Charts.GroupedBars("Rates", "units/h", ["Line A", "Line B", "Line C"],
                    [new("Target", [20, 24, 18]), new("Actual", [18, 23, 6])]);
                break;
            case 9: Charts.Histogram("Cycle time", "Minutes", Samples); break;
            case 10: Charts.Scatter("Load vs output", "Load", "units/h", Hours, Correlation); break;
            case 11: selection = ProductionWidgets.AllocationRing("Allocation", Segments, selection); break;
            case 12:
                selection = ProductionWidgets.InventoryTable("Inventory", Inventory, selection);
                if (selection >= 0)
                {
                    var item = Inventory[selection];
                    ProductionWidgets.Balance(item.Name, item.Stored, item.Incoming, item.Outgoing, item.Unit);
                }
                break;
            case 13: selection = ProductionWidgets.ProcessTable("Processes", Processes, selection); break;
            case 14:
                selection = ProductionWidgets.FlowDiagram("Flow", Nodes, Links, selection);
                if (selection >= 0) ImGui.Text(Nodes[selection].Name);
                break;
            case 15: selection = ProductionWidgets.QueueBoard("Jobs", ["Queued", "Running", "Blocked", "Done"], Jobs, selection); break;
            case 16:
                ImGui.SliderFloat("Cursor"u8, ref cursor, 0, 24);
                selection = ProductionWidgets.Schedule("Schedule", Schedule, 0, 24, selection, cursor);
                break;
            case 17: ProductionWidgets.Requirements(Requirements); break;
            case 18: ProductionWidgets.QuantityEditor("Quantities", quantities); break;
            case 19: ProductionWidgets.ActivityFeed(Events); break;
            case 20:
                ImGui.Checkbox("Automatic"u8, ref automatic);
                ImGui.BeginDisabled(!automatic);
                ImGui.SliderFloat("Minimum"u8, ref minimum, 0, target);
                ImGui.SliderFloat("Target"u8, ref target, minimum, maximum);
                ImGui.SliderFloat("Maximum"u8, ref maximum, target, 100);
                ImGui.EndDisabled();
                ProductionWidgets.CapacityBar("Thresholds", [new("Minimum", minimum, 0xffb5cb74),
                    new("Working range", maximum - minimum, 0xffe4b57e)], 100);
                break;
            case 21:
                DrawMetrics();
                ImGui.Separator();
                Charts.History("Throughput", "Hour", "units/h", Hours, Trends, height: 220);
                ProductionWidgets.ProcessTable("Processes", Processes);
                break;
        }
    }

    private static void DrawMetrics()
    {
        if (!ImGui.BeginTable("Metrics"u8, 3)) return;
        ImGui.TableNextColumn();
        ProductionWidgets.Metric("Throughput", "218 / h", "+12% vs previous");
        ImGui.TableNextColumn();
        ProductionWidgets.Metric("Lead time", "4.2 h", "-0.6 h");
        ImGui.TableNextColumn();
        ProductionWidgets.Metric("Queued", "38 orders", "6 blocked");
        ImGui.EndTable();
    }

    private static double[] MakeSamples()
    {
        var random = new Random(17);
        return Enumerable.Range(0, 250).Select(_ => 20 + 3 * Math.Sqrt(-2 * Math.Log(1 - random.NextDouble()))
            * Math.Cos(Math.Tau * random.NextDouble())).ToArray();
    }
}
