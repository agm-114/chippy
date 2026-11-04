using System.Numerics;
using System.Reflection;
using Arch.Core;
using Arch.Core.Extensions;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Chippy.Components;

namespace Chippy.Viewer;

internal sealed class EntityInspector(ViewerState state, EntityHierarchy hierarchy)
{
    public bool Draw()
    {
        var changed = false;
        var display = ImGui.GetIO().DisplaySize;
        const float width = 320;
        if (state.InspectorWindow.Begin("Inspector"u8, new(Math.Max(12, display.X - width - 12), 12),
            new(width, Math.Max(170, display.Y - 224))))
        {
            if (state.SelectedEntity is { } entity)
            {
                ImGui.SeparatorText(EntityHierarchy.DisplayName(entity));
                ImGui.Text($"Entity ID: {entity.Id}");
                var components = entity.GetComponentTypes();
                if (components.Count == 0) ImGui.Text("No components."u8);
                ImGui.PushID(entity.Id);
                ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X * 0.55f);
                if (entity.Has<Name>()) changed |= DrawSection(entity, typeof(Name));
                if (entity.Has<Transform>()) changed |= DrawSection(entity, typeof(Transform));
                foreach (var component in components)
                    if (component.Type != typeof(Name) && component.Type != typeof(Transform))
                        changed |= DrawSection(entity, component);
                ImGui.PopItemWidth();
                ImGui.PopID();
            }
            else ImGui.Text("Select an entity in the Hierarchy."u8);
        }
        ImGui.End();
        return changed;
    }

    private bool DrawSection(Entity entity, ComponentType component)
    {
        var type = component.Type;
        ImGui.PushID(component.Id);
        var changed = ImGui.CollapsingHeader(type.Name, ImGuiTreeNodeFlags.DefaultOpen) && DrawComponent(entity, component, type);
        ImGui.PopID();
        return changed;
    }

    private bool DrawComponent(Entity entity, ComponentType component, Type type)
    {
        if (type == typeof(Transform)) return DrawTransform(entity, ref entity.Get<Transform>());
        if (type == typeof(MeshRenderer)) return DrawMesh(ref entity.Get<MeshRenderer>());
        if (type == typeof(LineRenderer)) return DrawLine(ref entity.Get<LineRenderer>());
        if (type == typeof(Light)) return DrawLight(ref entity.Get<Light>());
        if (type == typeof(DistantMarker))
        {
            ref var marker = ref entity.Get<DistantMarker>();
            var color = marker.Color;
            if (!EditColor("Color"u8, ref color)) return false;
            marker = new(color);
            return true;
        }

        DrawReadOnly(type, entity.Get(component));
        return false;
    }

    private bool DrawTransform(Entity entity, ref Transform transform)
    {
        var changed = DrawParent(entity, transform.Parent);
        if (transform.Parent is not null)
            changed |= ImGui.Checkbox("Ignore parent scale"u8, ref transform.IgnoreParentScale);
        changed |= DragVector3("Position"u8, ref transform.LocalPosition, 0.01f);
        var angles = transform.LocalEulerAngles;
        if (DragVector3("Rotation (deg)"u8, ref angles, 0.5f))
        {
            transform.LocalEulerAngles = angles;
            changed = true;
        }
        changed |= DragVector3("Scale"u8, ref transform.LocalScale, 0.01f, 0.01f, Math.Max(100, state.Camera.MaxDistance));
        return changed;
    }

    private bool DrawParent(Entity entity, Entity? parent)
    {
        var name = parent is { } current && current.WorldId == state.World.Id && state.World.IsAlive(current)
            ? EntityHierarchy.DisplayName(current) : "None";
        if (!ImGui.BeginCombo("Parent"u8, name)) return false;
        var changed = false;
        if (ImGui.Selectable("None"u8, parent is null)) changed |= entity.TrySetParent(null);
        foreach (var candidate in hierarchy.Entities)
            if (entity.CanSetParent(candidate) && ImGui.Selectable(EntityHierarchy.Label(candidate), candidate == parent))
                changed |= entity.TrySetParent(candidate);
        ImGui.EndCombo();
        return changed;
    }

    private static bool DrawMesh(ref MeshRenderer renderer)
    {
        if (renderer.SharedMesh is { } mesh)
            ImGui.Text($"{mesh.SubMeshes.Count:N0} submeshes · {mesh.TriangleCount:N0} triangles");
        else ImGui.Text("No mesh."u8);
        var changed = ImGui.Checkbox("Enabled"u8, ref renderer.Enabled);
        changed |= ImGui.Checkbox("Unlit"u8, ref renderer.Unlit);
        changed |= EditColor("Tint"u8, ref renderer.Tint);
        return changed;
    }

    private static bool DrawLine(ref LineRenderer line)
    {
        ImGui.Text($"{(line.Positions.IsDefault ? 0 : line.Positions.Length):N0} line points");
        var changed = ImGui.Checkbox("Enabled"u8, ref line.Enabled);
        changed |= ImGui.Checkbox("Loop"u8, ref line.Loop);
        changed |= ImGui.Checkbox("Use world space"u8, ref line.UseWorldSpace);
        changed |= EditColor("Color"u8, ref line.Color);
        return changed;
    }

    private static bool DrawLight(ref Light light)
    {
        ImGui.Text($"Type: {light.Type}");
        var changed = ImGui.Checkbox("Enabled"u8, ref light.Enabled);
        var color = new Vector4(light.Color, 1);
        if (EditColor("Color"u8, ref color))
        {
            light.Color = new(color.X, color.Y, color.Z);
            changed = true;
        }
        changed |= ImGui.DragFloat("Intensity"u8, ref light.Intensity, Math.Max(0.01f, light.Intensity * 0.01f), 0, float.MaxValue, "%.3g"u8);
        if (light.Type == LightType.Point)
        {
            changed |= ImGui.DragFloat("Range"u8, ref light.Range, 1, 0, float.MaxValue, "%.3g"u8);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("0 means unlimited."u8);
        }
        return changed;
    }

    private static void DrawReadOnly(Type type, object? value)
    {
        if (value is null) { ImGui.Text("Null"u8); return; }
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Public;
        var fields = type.GetFields(members);
        var properties = type.GetProperties(members);
        foreach (var field in fields)
            ImGui.TextWrapped($"{field.Name}: {field.GetValue(value)}");
        var hasValues = fields.Length > 0;
        foreach (var property in properties)
        {
            if (property.GetMethod is not { IsPublic: true } || property.GetIndexParameters().Length != 0) continue;
            hasValues = true;
            try { ImGui.TextWrapped($"{property.Name}: {property.GetValue(value)}"); }
            catch (TargetInvocationException) { ImGui.Text($"{property.Name}: unavailable"); }
        }
        if (!hasValues) ImGui.TextWrapped(value.ToString() ?? type.Name);
    }

    private static bool DragVector3(ReadOnlySpan<byte> label, ref Vector3 value, float speed, float min = 0, float max = 0)
    {
        var edit = new float3(value.X, value.Y, value.Z);
        if (!ImGui.DragFloat3(label, ref edit, speed, min, max, min < max ? "%.2f"u8 : "%.3f"u8,
            min < max ? ImGuiSliderFlags.AlwaysClamp : ImGuiSliderFlags.None)) return false;
        value = new(edit.X, edit.Y, edit.Z);
        return true;
    }

    internal static bool EditColor(ReadOnlySpan<byte> label, ref Vector4 value)
    {
        var edit = new float4(value.X, value.Y, value.Z, value.W);
        if (!ImGui.ColorEdit4(label, ref edit, ImGuiColorEditFlags.NoAlpha)) return false;
        value = new(edit.X, edit.Y, edit.Z, edit.W);
        return true;
    }
}
