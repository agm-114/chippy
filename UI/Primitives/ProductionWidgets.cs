using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Chippy.UI.Primitives;

public readonly record struct CapacitySegment(string Label, double Amount, uint Color);
public readonly record struct InventoryRow(string Name, double Stored, double Capacity, double Incoming, double Outgoing, string Unit);
public readonly record struct ProcessRow(string Name, float Utilization, int Queued, string Status);
public readonly record struct Requirement(string Name, double Quantity, string Unit, Requirement[]? Children = null);
public readonly record struct ActivityRow(string Time, string Message, uint Color);
public struct QuantityField(string name, string unit, float value)
{
    public string Name = name;
    public string Unit = unit;
    public float Value = value;
}

public static partial class ProductionWidgets
{
    public static void Metric(string label, string value, string change)
    {
        ImGui.BeginGroup();
        MutedText(label);
        ImGui.Text(value);
        MutedText(change);
        ImGui.EndGroup();
    }

    public static bool Badge(string label, uint color)
    {
        var p = ImGui.GetCursorScreenPos();
        var text = ImGui.CalcTextSize(label);
        var clicked = ImGui.InvisibleButton(label, text + new float2(20, 10));
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(p, p + text + new float2(20, 10), color, 5);
        draw.AddText(p + new float2(10, 5), 0xfff5f1e9, label);
        return clicked;
    }

    public static void Gauge(string label, float value, float maximum, float size = 180)
    {
        var p = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new float2(size, size * 0.68f));
        var center = p + new float2(size / 2, size * 0.48f);
        var fraction = maximum > 0 ? Math.Clamp(value / maximum, 0, 1) : 0;
        var draw = ImGui.GetWindowDrawList();
        for (var i = 0; i < 40; i++)
        {
            var a = MathF.PI + MathF.PI * i / 40;
            var b = MathF.PI + MathF.PI * (i + 0.8f) / 40;
            draw.AddLine(center + new float2(MathF.Cos(a), MathF.Sin(a)) * size * 0.4f,
                center + new float2(MathF.Cos(b), MathF.Sin(b)) * size * 0.4f,
                i / 40f < fraction ? 0xffbcd476 : 0xff42372c, 8);
        }
        var angle = MathF.PI * (1 + fraction);
        draw.AddLine(center, center + new float2(MathF.Cos(angle), MathF.Sin(angle)) * size * 0.33f, 0xffeee6d5, 2);
        draw.AddCircleFilled(center, 4, 0xffeee6d5);
        draw.AddText(p + new float2(12, size * 0.52f), 0xffeee6d5, $"{label}  {value:0.#} / {maximum:0.#}");
    }

    public static void CapacityBar(string id, ReadOnlySpan<CapacitySegment> segments, double capacity)
    {
        var p = ImGui.GetCursorScreenPos();
        var size = new float2(ImGui.GetContentRegionAvail().X, 26);
        ImGui.InvisibleButton(id, size);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(p, p + size, 0xff302820, 4);
        var used = 0.0;
        foreach (var segment in segments)
        {
            var from = capacity > 0 ? Math.Clamp(used / capacity, 0, 1) : 0;
            used += Math.Max(0, segment.Amount);
            var to = capacity > 0 ? Math.Clamp(used / capacity, 0, 1) : 0;
            draw.AddRectFilled(p + new float2((float)from * size.X, 0), p + new float2((float)to * size.X, size.Y), segment.Color);
        }
        foreach (var segment in segments) ImGui.Text($"{segment.Label}: {segment.Amount:0.##}");
        ImGui.Text($"{used:0.##} / {capacity:0.##}");
    }

    public static void Balance(string label, double stored, double incoming, double outgoing, string unit)
    {
        ImGui.SeparatorText(label);
        if (!ImGui.BeginTable(label, 3)) return;
        Cell($"In  {incoming:0.##} {unit}/h");
        Cell($"Stored  {stored:0.##} {unit}");
        Cell($"Out  {outgoing:0.##} {unit}/h");
        ImGui.EndTable();
        ImGui.Text($"Net  {incoming - outgoing:+0.##;-0.##;0} {unit}/h");
    }

    public static int AllocationRing(string id, ReadOnlySpan<CapacitySegment> segments, int selected = -1, float size = 220)
    {
        var p = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, new float2(size));
        var hovered = ImGui.IsItemHovered();
        var center = p + new float2(size / 2);
        var mouse = ImGui.GetIO().MousePos - center;
        var distance = MathF.Sqrt(mouse.X * mouse.X + mouse.Y * mouse.Y);
        var mouseAngle = (MathF.Atan2(mouse.Y, mouse.X) + MathF.Tau) % MathF.Tau;
        var total = 0.0;
        foreach (var segment in segments) total += Math.Max(0, segment.Amount);
        var draw = ImGui.GetWindowDrawList();
        var angle = 0f;
        for (var i = 0; i < segments.Length; i++)
        {
            var end = angle + (total > 0 ? (float)(Math.Max(0, segments[i].Amount) / total) * MathF.Tau : 0);
            var hit = hovered && distance >= size * 0.28f && distance <= size * 0.46f && mouseAngle >= angle && mouseAngle < end;
            if (hit && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) selected = i;
            for (var a = angle; a < end; a += 0.035f)
            {
                var b = Math.Min(a + 0.035f, end);
                var x = new float2(MathF.Cos(a), MathF.Sin(a));
                var y = new float2(MathF.Cos(b), MathF.Sin(b));
                var inner = size * (selected == i ? 0.25f : 0.28f);
                draw.AddTriangleFilled(center + x * inner, center + x * size * 0.46f, center + y * size * 0.46f, segments[i].Color);
                draw.AddTriangleFilled(center + x * inner, center + y * size * 0.46f, center + y * inner, segments[i].Color);
            }
            if (hit) Tooltip($"{segments[i].Label}: {segments[i].Amount:0.##}");
            angle = end;
        }
        var text = $"{total:0.##}";
        draw.AddText(center - ImGui.CalcTextSize(text) / 2, 0xfff2e6d1, text);
        foreach (var segment in segments) ImGui.Text($"{segment.Label}  {segment.Amount:0.##}");
        return selected;
    }

    public static int InventoryTable(string id, ReadOnlySpan<InventoryRow> rows, int selected = -1)
    {
        if (!ImGui.BeginTable(id, 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.Resizable)) return selected;
        Headers(["Item", "Stored", "Capacity", "In / h", "Out / h", "Net / h"]);
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.PushID(i);
            if (ImGui.Selectable(row.Name, selected == i)) selected = i;
            ImGui.PopID();
            Cell($"{row.Stored:0.##} {row.Unit}");
            Cell($"{row.Capacity:0.##}");
            Cell($"{row.Incoming:0.##}");
            Cell($"{row.Outgoing:0.##}");
            Cell($"{row.Incoming - row.Outgoing:+0.##;-0.##;0}");
        }
        ImGui.EndTable();
        return selected;
    }

    public static int ProcessTable(string id, ReadOnlySpan<ProcessRow> rows, int selected = -1)
    {
        if (!ImGui.BeginTable(id, 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH)) return selected;
        Headers(["Process", "Utilization", "Queue", "State"]);
        for (var i = 0; i < rows.Length; i++)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.PushID(i);
            if (ImGui.Selectable(rows[i].Name, selected == i)) selected = i;
            ImGui.TableNextColumn();
            ImGui.ProgressBar(Math.Clamp(rows[i].Utilization, 0, 1), new float2(-1, 0), $"{rows[i].Utilization:P0}");
            ImGui.PopID();
            Cell($"{rows[i].Queued}");
            Cell(rows[i].Status);
        }
        ImGui.EndTable();
        return selected;
    }

    public static void Requirements(ReadOnlySpan<Requirement> rows)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            ImGui.PushID(i);
            if (row.Children is not { Length: > 0 })
                ImGui.BulletText($"{row.Name} · {row.Quantity:0.##} {row.Unit}".Replace("%", "%%"));
            else if (ImGui.TreeNode($"{row.Name} · {row.Quantity:0.##} {row.Unit}"))
            {
                Requirements(row.Children);
                ImGui.TreePop();
            }
            ImGui.PopID();
        }
    }

    public static void ActivityFeed(ReadOnlySpan<ActivityRow> rows)
    {
        var draw = ImGui.GetWindowDrawList();
        foreach (var row in rows)
        {
            var p = ImGui.GetCursorScreenPos();
            draw.AddCircleFilled(p + new float2(5, 8), 3, row.Color);
            ImGui.Indent(18);
            ImGui.Text($"{row.Time}  {row.Message}");
            ImGui.Unindent(18);
        }
    }

    public static bool QuantityEditor(string id, Span<QuantityField> fields)
    {
        var changed = false;
        ImGui.PushID(id);
        for (var i = 0; i < fields.Length; i++)
        {
            ImGui.PushID(i);
            ImGui.SetNextItemWidth(150);
            changed |= ImGui.DragFloat(fields[i].Name, ref fields[i].Value, 0.1f, 0, float.MaxValue, "%.2f"u8, ImGuiSliderFlags.AlwaysClamp);
            ImGui.SameLine();
            MutedText(fields[i].Unit);
            ImGui.PopID();
        }
        ImGui.PopID();
        return changed;
    }

    private static void Headers(ReadOnlySpan<string> labels)
    {
        foreach (var label in labels) ImGui.TableSetupColumn(label);
        ImGui.TableHeadersRow();
    }

    private static void Cell(string text)
    {
        ImGui.TableNextColumn();
        ImGui.Text(text);
    }

    // These ImGui calls take printf format strings; widget data is literal text.
    private static void MutedText(string text) => ImGui.TextDisabled(text.Replace("%", "%%"));
    private static void Tooltip(string text) => ImGui.SetTooltip(text.Replace("%", "%%"));
}
