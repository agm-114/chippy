using System.Numerics;
using Brutal.ImGuiApi;
using Brutal.Numerics;

namespace Chippy.UI.Primitives;

public readonly record struct FlowNode(string Name, Vector2 Position, string Detail, uint Color);
public readonly record struct FlowLink(int From, int To, string Label);
public readonly record struct QueueItem(string Name, string State, float Progress);
public readonly record struct ScheduleBlock(string Lane, string Name, double Start, double Duration, uint Color);

public static partial class ProductionWidgets
{
    public static int FlowDiagram(string id, ReadOnlySpan<FlowNode> nodes, ReadOnlySpan<FlowLink> links,
        int selected = -1, float height = 310)
    {
        var origin = ImGui.GetCursorScreenPos();
        var size = new float2(Math.Max(1, ImGui.GetContentRegionAvail().X), height);
        var nodeSize = new float2(Math.Min(170, size.X / 3), 68);
        ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var clicked = hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        var mouse = ImGui.GetIO().MousePos;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(origin, origin + size, true);
        foreach (var link in links)
        {
            if ((uint)link.From >= nodes.Length || (uint)link.To >= nodes.Length) continue;
            var a = Position(nodes[link.From].Position) + nodeSize / 2;
            var b = Position(nodes[link.To].Position) + nodeSize / 2;
            var direction = b - a;
            var length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length < 1) continue;
            direction /= length;
            var extent = Math.Min(Math.Abs(direction.X) > 0.001f ? nodeSize.X / 2 / Math.Abs(direction.X) : float.MaxValue,
                Math.Abs(direction.Y) > 0.001f ? nodeSize.Y / 2 / Math.Abs(direction.Y) : float.MaxValue);
            a += direction * extent;
            b -= direction * extent;
            draw.AddLine(a, b, 0xffa59a7d, 2);
            var side = new float2(-direction.Y, direction.X) * 4;
            draw.AddTriangleFilled(b, b - direction * 9 + side, b - direction * 9 - side, 0xffa59a7d);
            draw.AddText((a + b) / 2 + new float2(3, -19), 0xffd9ceba, link.Label);
        }
        for (var i = 0; i < nodes.Length; i++)
        {
            var p = Position(nodes[i].Position);
            var hit = hovered && mouse.X >= p.X && mouse.X <= p.X + nodeSize.X && mouse.Y >= p.Y && mouse.Y <= p.Y + nodeSize.Y;
            if (hit && clicked) selected = i;
            draw.AddRectFilled(p, p + nodeSize, i == selected ? 0xff574b32 : 0xff302a24, 6);
            draw.AddRectFilled(p, p + new float2(4, nodeSize.Y), nodes[i].Color, 2);
            draw.AddText(p + new float2(12, 10), 0xfff4ead9, nodes[i].Name);
            draw.AddText(p + new float2(12, 35), 0xffbdb4a5, nodes[i].Detail);
        }
        draw.PopClipRect();
        return selected;

        float2 Position(Vector2 normalized) => origin + new float2(
            Math.Clamp(normalized.X, 0, 1) * Math.Max(0, size.X - nodeSize.X),
            Math.Clamp(normalized.Y, 0, 1) * Math.Max(0, size.Y - nodeSize.Y));
    }

    public static int QueueBoard(string id, ReadOnlySpan<string> columns, ReadOnlySpan<QueueItem> items, int selected = -1)
    {
        if (columns.IsEmpty || !ImGui.BeginTable(id, columns.Length, ImGuiTableFlags.BordersInnerV)) return selected;
        Headers(columns);
        ImGui.TableNextRow();
        foreach (var column in columns)
        {
            ImGui.TableNextColumn();
            for (var i = 0; i < items.Length; i++)
            {
                if (items[i].State != column) continue;
                ImGui.PushID(i);
                if (ImGui.Selectable(items[i].Name, selected == i)) selected = i;
                ImGui.ProgressBar(Math.Clamp(items[i].Progress, 0, 1), new float2(-1, 0), $"{items[i].Progress:P0}");
                ImGui.PopID();
                ImGui.Spacing();
            }
        }
        ImGui.EndTable();
        return selected;
    }

    public static int Schedule(string id, ReadOnlySpan<ScheduleBlock> blocks, double from, double to,
        int selected = -1, double? cursor = null)
    {
        if (to <= from) return selected;
        var lanes = blocks.ToArray().Select(block => block.Lane).Distinct().ToArray();
        var p = ImGui.GetCursorScreenPos();
        var size = new float2(Math.Max(150, ImGui.GetContentRegionAvail().X), lanes.Length * 32 + 28);
        const float labelWidth = 120;
        var width = size.X - labelWidth;
        ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();
        var mouse = ImGui.GetIO().MousePos;
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(p, p + size, true);
        for (var i = 0; i <= 4; i++)
        {
            var x = p.X + labelWidth + width * i / 4;
            draw.AddLine(new float2(x, p.Y), new float2(x, p.Y + size.Y - 22), 0xff443c32);
            var label = $"{from + (to - from) * i / 4:0.#}";
            var labelWidthPixels = ImGui.CalcTextSize(label).X;
            draw.AddText(new float2(Math.Clamp(x - labelWidthPixels / 2, p.X + labelWidth, p.X + size.X - labelWidthPixels),
                p.Y + size.Y - 18), 0xffd4c7b4, label);
        }
        for (var lane = 0; lane < lanes.Length; lane++)
            draw.AddText(p + new float2(0, lane * 32 + 7), 0xffe1d7c5, lanes[lane]);
        for (var i = 0; i < blocks.Length; i++)
        {
            var block = blocks[i];
            if (block.Start >= to || block.Start + block.Duration <= from || block.Duration <= 0) continue;
            var y = p.Y + Array.IndexOf(lanes, block.Lane) * 32 + 4;
            var a = new float2(p.X + labelWidth + (float)Math.Clamp((block.Start - from) / (to - from), 0, 1) * width, y);
            var b = new float2(p.X + labelWidth + (float)Math.Clamp((block.Start + block.Duration - from) / (to - from), 0, 1) * width, y + 25);
            var hit = hovered && mouse.X >= a.X && mouse.X <= b.X && mouse.Y >= a.Y && mouse.Y <= b.Y;
            if (hit && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) selected = i;
            draw.AddRectFilled(a, b, selected == i ? 0xffe0cb79 : block.Color, 3);
            if (b.X - a.X > ImGui.CalcTextSize(block.Name).X + 8) draw.AddText(a + new float2(4), 0xff191919, block.Name);
            if (hit) Tooltip($"{block.Name}\n{block.Start:0.##} to {block.Start + block.Duration:0.##}");
        }
        if (cursor is { } time && time >= from && time <= to)
        {
            var x = p.X + labelWidth + (float)((time - from) / (to - from)) * width;
            draw.AddLine(new float2(x, p.Y), new float2(x, p.Y + size.Y - 22), 0xfff4e7a6, 2);
        }
        draw.PopClipRect();
        return selected;
    }
}
