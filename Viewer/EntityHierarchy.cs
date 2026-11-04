using Arch.Core;
using Arch.Core.Extensions;
using Brutal.ImGuiApi;
using Chippy.Components;

namespace Chippy.Viewer;

internal sealed class EntityHierarchy(ViewerState state)
{
    private static readonly QueryDescription AllEntities = new();
    private readonly List<Entity> entities = [];
    private readonly List<Entity> roots = [];
    private readonly Dictionary<Entity, List<Entity>> children = [];
    private readonly HashSet<Entity> revealPath = [];
    private Entity? lastSelection;

    public IReadOnlyList<Entity> Entities => entities;

    public void Draw()
    {
        entities.Clear();
        state.World.Query(in AllEntities, entities.Add);
        entities.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        if (state.SelectedEntity is { } selected && (selected.WorldId != state.World.Id || !state.World.IsAlive(selected)))
            state.SelectedEntity = null;

        var display = ImGui.GetIO().DisplaySize;
        if (state.HierarchyWindow.Begin("Hierarchy"u8, new(12, 12), new(245, Math.Clamp(display.Y * 0.46f, 140, 330))))
        {
            DrawScenePicker();
            BuildTree();
            var reveal = state.SelectedEntity != lastSelection;
            revealPath.Clear();
            if (reveal && state.SelectedEntity is { } selection)
                for (var parent = GetParent(selection); parent is { } ancestor && revealPath.Add(ancestor); parent = GetParent(ancestor)) { }

            ImGui.Text($"{entities.Count:N0} entities");
            ImGui.Separator();
            foreach (var root in roots) DrawNode(root, reveal);
            lastSelection = state.SelectedEntity;
        }
        ImGui.End();
    }

    private void DrawScenePicker()
    {
        var names = state.SceneNames;
        if (names.Count <= 1) return;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.7f);
        if (!ImGui.BeginCombo("Scene"u8, names[state.SceneIndex])) return;
        for (var i = 0; i < names.Count; i++)
        {
            var selected = i == state.SceneIndex;
            if (ImGui.Selectable($"{names[i]}##scene{i}", selected) && !selected)
                state.RequestedScene = i;
            if (selected) ImGui.SetItemDefaultFocus();
        }
        ImGui.EndCombo();
    }

    private void BuildTree()
    {
        roots.Clear();
        children.Clear();
        foreach (var entity in entities)
        {
            if (GetParent(entity) is not { } parent)
            {
                roots.Add(entity);
                continue;
            }
            if (!children.TryGetValue(parent, out var siblings))
                children.Add(parent, siblings = []);
            siblings.Add(entity);
        }
    }

    private Entity? GetParent(Entity entity) =>
        entity.TryGet<Transform>(out var transform) && transform.Parent is { } parent
        && parent.WorldId == state.World.Id && state.World.IsAlive(parent) && parent.Has<Transform>() ? parent : null;

    private void DrawNode(Entity entity, bool reveal)
    {
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.OpenOnDoubleClick | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (entity == state.SelectedEntity) flags |= ImGuiTreeNodeFlags.Selected;
        if (!children.TryGetValue(entity, out var descendants))
            flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        else if (revealPath.Contains(entity)) ImGui.SetNextItemOpen(true);

        var expanded = ImGui.TreeNodeEx(Label(entity), flags);
        if (reveal && entity == state.SelectedEntity) ImGui.SetScrollHereY(0.5f);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && !ImGui.IsItemToggledOpen()) state.SelectedEntity = entity;
        if (expanded && descendants is not null)
        {
            foreach (var child in descendants) DrawNode(child, reveal);
            ImGui.TreePop();
        }
    }

    public static string DisplayName(Entity entity) => entity.TryGet<Name>(out var name) && !string.IsNullOrWhiteSpace(name.Value)
        ? name.Value : $"Entity {entity.Id}";

    public static string Label(Entity entity) => $"{DisplayName(entity)}##{entity.Id}";
}
