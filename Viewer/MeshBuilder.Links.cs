using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Brutal.ImGuiApi;
using Chippy.Components;

namespace Chippy.Viewer;

internal partial class MeshBuilder
{
    private bool DrawLinks(Entity entity)
    {
        var link = parts[entity].Link;
        var changed = false;
        if (ImGui.BeginCombo("Link to"u8, link == 0 ? "None" : $"Group {link}"))
        {
            foreach (var other in history)
            {
                if (other == entity || (link != 0 && parts[other].Link == link)) continue;
                if (ImGui.Selectable($"{parts[other].Part.Name} #{other.Id}"))
                {
                    LinkParts(entity, other);
                    changed = true;
                }
            }
            ImGui.EndCombo();
        }
        if (link != 0 && ImGui.Button("Unlink"u8))
        {
            parts[entity] = parts[entity] with { Link = 0 };
            ClearUnusedLinks();
            MarkShipChanged();
            changed = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Linked parts share XYZ scale and color."u8);
        return changed;
    }

    private void LinkParts(Entity source, Entity other)
    {
        var link = parts[source].Link;
        var otherLink = parts[other].Link;
        if (link == 0) link = otherLink;
        if (link == 0)
        {
            link = 1;
            while (parts.Values.Any(part => part.Link == link)) link++;
        }

        foreach (var entity in history)
        {
            var placement = parts[entity];
            if (entity == source || entity == other || (otherLink != 0 && placement.Link == otherLink))
                parts[entity] = placement with { Link = link };
        }
        SetAppearance(source, source.Get<Transform>().LocalScale, source.Get<MeshRenderer>().Tint);
    }

    private void SetAppearance(Entity entity, Vector3 scale, Vector4 tint)
    {
        var link = parts[entity].Link;
        foreach (var (part, placement) in parts)
        {
            if (part != entity && (link == 0 || placement.Link != link)) continue;
            part.Get<Transform>().LocalScale = scale;
            part.Get<MeshRenderer>().Tint = tint;
        }

        // Refresh the group and the sockets on which its immediate children sit.
        foreach (var (part, placement) in parts)
            if (part == entity || placement.Parent == entity || (link != 0 && (placement.Link == link
                || (parts.TryGetValue(placement.Parent, out var parent) && parent.Link == link))))
                UpdatePose(part, placement);
        visible.Clear();
        MarkShipChanged();
    }

    private void ClearUnusedLinks()
    {
        var singletons = parts.Values.Where(part => part.Link != 0).GroupBy(part => part.Link)
            .Where(group => group.Count() == 1).Select(group => group.Key).ToHashSet();
        if (singletons.Count == 0) return;
        foreach (var part in history)
            if (singletons.Contains(parts[part].Link)) parts[part] = parts[part] with { Link = 0 };
    }
}
