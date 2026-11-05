using System.Collections.Concurrent;
using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Brutal.ImGuiApi;
using Chippy.Components;
using Chippy.Models;
using Chippy.Rendering;

namespace Chippy.Viewer;

internal partial class MeshBuilder
{
    private readonly record struct Socket(Vector3 Position, Vector3 Normal, string Label);
    private record Part(string Name, Mesh Mesh, Socket[] Sockets, int RadialSockets = 0, int? TipPercent = null);
    private readonly record struct Placement(Part Part, Entity Parent, int Target, int Mount, float Roll, Vector3 Offset, int Link = 0);
    private struct PartSettings(Part part, Vector3 size, Vector3 offset, int mount, float roll, Vector4 color)
    {
        public Part Part = part;
        public Vector3 Size = size;
        public Vector3 Offset = offset;
        public int Mount = mount;
        public float Roll = roll;
        public Vector4 Color = color;
    }
    private const int DefaultRadialSockets = 4;
    private const int DefaultTipPercent = 24;
    private const float MaxPartOffset = 10_000_000;
    private static readonly int[] RadialCounts = [4, 8, 16];
    private static readonly Vector4 DefaultColor = new(0.65f, 0.65f, 0.65f, 1);
    private static readonly Socket[] RootSockets = [new(Vector3.Zero, Vector3.UnitY, "Origin")];

    private static readonly ConcurrentDictionary<int, Part> TruncatedCones = new();
    private static readonly Part[] Palette = CreatePalette();
    private readonly Dictionary<Entity, Placement> parts = [];
    private readonly HashSet<(Entity, int)> occupied = [];
    private readonly List<Entity> history = [];
    private readonly List<(int Socket, Vector2 Screen)> visible = [];
    private readonly Entity root;
    private Entity? drawnPart;
    private EditorWindowLayout window;
    private PartSettings next;
    private Vector3 worldRotation;
    private string? partMessage;
    public ViewerState State { get; }

    private static Part[] CreatePalette()
    {
        (Vector3, string)[] axes =
        [
            (-Vector3.UnitY, "Bottom"), (Vector3.UnitY, "Top"),
            (-Vector3.UnitZ, "Back"), (Vector3.UnitZ, "Front"),
            (-Vector3.UnitX, "Left"), (Vector3.UnitX, "Right")
        ];
        (Vector3, string)[] prismFaces =
        [
            (-Vector3.UnitX, "Left face"), (new(0.5f, 0, 0.8660254f), "Front face"),
            (new(0.5f, 0, -0.8660254f), "Back face"), axes[0], axes[1]
        ];
        return
        [
            CreatePart("Cube", Mesh.CreateCube(), axes),
            CreatePart("Capsule", Mesh.CreateCapsule(height: 2.6f, segments: 12, hemisphereRings: 3), axes, DefaultRadialSockets),
            CreatePart("Cylinder", Mesh.CreateCylinder(segments: 12), axes, DefaultRadialSockets),
            CreatePart("Cone", Mesh.CreateCone(segments: 12), [axes[0]]),
            GetTruncatedCone(DefaultTipPercent),
            CreatePart("Triangular Prism", Mesh.CreatePrism(radius: 0.9f, height: 0.16f), prismFaces),
            CreatePart("Icosphere", Mesh.CreateIcosphere(subdivisions: 1), axes, DefaultRadialSockets)
        ];
    }

    public MeshBuilder(World world, RenderBackend renderer)
    {
        next = DefaultSettings(Palette[0]);
        root = world.Create(new Name("Root"), new Transform());
        world.Create(new Name("Key light"), new Transform { LocalRotation = Quaternion.CreateFromYawPitchRoll(0.6f, -0.9f, 0) },
            new Light { Type = LightType.Directional, Intensity = 1.1f });
        State = new(world, renderer)
        {
            MeshBuilder = this,
            SelectedEntity = root,
            FollowSelected = true,
            Camera = new(distance: 6.2f, pitch: 0.5f, yaw: 0.65f)
        };
        State.Simulation.Paused = true;
    }

    public bool DrawPanel()
    {
        var changed = false;
        const float width = 335;
        var display = ImGui.GetIO().DisplaySize;
        if (window.Begin("Mesh Builder"u8, new(Math.Max(12, display.X - width - 12), 12), new(width, 500)))
        {
            ImGui.TextWrapped("Click a gold snap point to add a part. Ctrl+C copies selected settings to Next Part. Ctrl+V applies them to the selection."u8);
            var rotation = worldRotation;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X * 0.55f);
            if (EntityInspector.DragVector3("World rotation"u8, ref rotation, 0.5f, -360, 360))
                changed |= SetWorldRotation(rotation);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Rotate the entire ship around its root. XYZ in degrees."u8);
            ImGui.SeparatorText("Next Part"u8);
            ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X * 0.7f);
            if (ImGui.BeginCombo("Part"u8, next.Part.Name))
            {
                foreach (var part in Palette)
                    if (ImGui.Selectable(part.Name, next.Part.Name == part.Name) && next.Part.Name != part.Name)
                        next = DefaultSettings(part);
                ImGui.EndCombo();
            }
            DrawPartControls(ref next);
            if (State.SelectedEntity is { } selected && parts.TryGetValue(selected, out var placement))
                changed |= DrawSelectedPart(selected, placement);
            ImGui.PopItemWidth();
            if (ImGui.Button("Select root"u8))
            {
                State.SelectedEntity = root;
                State.Camera.Target = Vector3.Zero;
            }
            ImGui.SameLine();
            ImGui.BeginDisabled(history.Count == 0);
            if (ImGui.Button("Undo placement"u8)) changed |= Undo();
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.Text($"{parts.Count} parts");
            if (ImGui.Button(showYaml ? "Hide YAML"u8 : "Ship YAML"u8)) showYaml = !showYaml;
            if (partMessage is not null) ImGui.TextWrapped(partMessage);
            if (workingShipError is not null) ImGui.TextWrapped(workingShipError);
        }
        ImGui.End();
        if (showYaml) changed |= DrawYamlPanel();
        var io = ImGui.GetIO();
        if (!io.WantTextInput && !ImGui.IsAnyItemActive()
            && State.SelectedEntity is { } target && parts.TryGetValue(target, out var current))
        {
            if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.C, false))
            {
                next = GetSettings(target, current);
                partMessage = null;
            }
            if (io.KeyCtrl && ImGui.IsKeyPressed(ImGuiKey.V, false)) changed |= TryEditPart(target, next);
            if (ImGui.IsKeyPressed(ImGuiKey.Delete, false)) changed |= RemovePart(target);
        }
        if (!ImGui.IsAnyItemActive()) SaveWorkingShip();
        return changed;
    }

    private bool SetWorldRotation(Vector3 rotation)
    {
        if (worldRotation == rotation) return false;
        worldRotation = rotation;
        root.Get<Transform>().LocalEulerAngles = rotation;
        visible.Clear();
        MarkShipChanged();
        return true;
    }

    private static PartSettings DefaultSettings(Part part) => new(part, part.Mesh.Bounds.Size, Vector3.Zero, 0, 0, DefaultColor);

    private static PartSettings GetSettings(Entity entity, Placement placement) => new(placement.Part,
        entity.Get<Transform>().LocalScale * placement.Part.Mesh.Bounds.Size, placement.Offset,
        placement.Mount, placement.Roll, entity.Get<MeshRenderer>().Tint);

    private bool DrawSelectedPart(Entity entity, Placement placement)
    {
        ImGui.SeparatorText($"Selected {placement.Part.Name} #{entity.Id}");
        ImGui.PushID(entity.Id);
        var settings = GetSettings(entity, placement);
        var changed = DrawPartControls(ref settings, entity) && TryEditPart(entity, settings);
        changed |= DrawLinks(entity);
        ImGui.PopID();
        return changed;
    }

    private bool DrawPartControls(ref PartSettings settings, Entity? entity = null)
    {
        var changed = EntityInspector.DragVector3("Size XYZ"u8, ref settings.Size, 0.02f, 0.05f, 100);
        if (settings.Part.TipPercent is { } tip)
        {
            if (ImGui.SliderInt("Tip width"u8, ref tip, 0, 100, "%d%%"u8, ImGuiSliderFlags.AlwaysClamp))
            {
                settings.Part = GetTruncatedCone(tip);
                changed = true;
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Percent of base width. 0% is a point. 100% gives straight sides."u8);
        }
        changed |= EntityInspector.DragVector3("Offset XYZ"u8, ref settings.Offset, 0.02f, -MaxPartOffset, MaxPartOffset);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Move from the snap point along the parent's axes."u8);
        changed |= DrawRadialSockets(ref settings, entity);
        var sockets = settings.Part.Sockets;
        if (sockets.Length > 1 && ImGui.BeginCombo("Mount face"u8, sockets[settings.Mount].Label))
        {
            for (var i = 0; i < sockets.Length; i++)
            {
                var selected = i == settings.Mount;
                ImGui.BeginDisabled(!selected && entity is { } part && parts[part].Parent != root && occupied.Contains((part, i)));
                if (ImGui.Selectable(sockets[i].Label, selected) && !selected)
                {
                    settings.Mount = i;
                    changed = true;
                }
                ImGui.EndDisabled();
            }
            ImGui.EndCombo();
        }
        changed |= ImGui.DragFloat("Roll"u8, ref settings.Roll, 0.5f, -180, 180, "%.1f deg"u8, ImGuiSliderFlags.AlwaysClamp);
        changed |= EntityInspector.EditColor("Color"u8, ref settings.Color);
        return changed;
    }

    public void DrawSockets(SceneRenderSystem scene, PrimitiveBatch draw, Vector2 viewport, ModelViewer.FrameCamera frame)
    {
        visible.Clear();
        drawnPart = State.SelectedEntity;
        if (drawnPart is not { } selected) return;
        var isRoot = selected == root;
        var sockets = RootSockets;
        var matrix = Matrix4x4.CreateTranslation(-State.Camera.Target);
        if (!isRoot)
        {
            if (!parts.TryGetValue(selected, out var placement) || scene.GetDrawnMeshMatrix(selected) is not { } meshMatrix)
                return;
            sockets = placement.Part.Sockets;
            matrix = meshMatrix;
        }
        if (!Matrix4x4.Invert(matrix, out var inverse)) return;
        var normalMatrix = Matrix4x4.Transpose(inverse);

        for (var i = 0; i < sockets.Length; i++)
        {
            if (occupied.Contains((selected, i))) continue;
            var socket = sockets[i];
            var point = Vector3.Transform(socket.Position, matrix);
            var normal = Vector3.TransformNormal(socket.Normal, normalMatrix);
            var toPoint = point - frame.Eye;
            if (!isRoot && Vector3.Dot(normal, toPoint) >= 0) continue;
            var view = Vector3.Transform(point, frame.View);
            var depth = -view.Z;
            if (depth <= frame.Near || depth >= frame.Far) continue;
            var screen = new Vector2(0.5f) + new Vector2(view.X, -view.Y) * frame.ProjectionScale / (2 * depth);
            if (screen.X is < 0 or > 1 || screen.Y is < 0 or > 1
                || scene.Pick(frame.Eye, toPoint / depth, frame.Near, depth - 0.001f) is not null) continue;
            visible.Add((i, screen));
            draw.DrawCircle(screen * viewport, 8, new(0.12f, 0.10f, 0.08f, 1), filled: true, segments: 16);
            draw.DrawCircle(screen * viewport, 5.5f, new(1, 0.72f, 0.26f, 1), filled: true, segments: 16);
        }
    }

    public bool TryPlace(Vector2 pointer, Vector2 viewport)
    {
        if (drawnPart is not { } parent || State.SelectedEntity != parent) return false;
        var target = -1;
        var nearest = 11f * 11;
        foreach (var (socket, screen) in visible)
        {
            var distance = Vector2.DistanceSquared(pointer, screen * viewport);
            if (distance >= nearest || occupied.Contains((parent, socket))) continue;
            (target, nearest) = (socket, distance);
        }
        if (target < 0) return false;

        var child = AddPart(new(next.Part, parent, target, next.Mount, next.Roll, next.Offset),
            next.Size / next.Part.Mesh.Bounds.Size, next.Color);
        if (parent == root) State.SelectedEntity = child;
        return true;
    }

    private Entity AddPart(Placement placement, Vector3 scale, Vector4 tint)
    {
        var part = placement.Part;
        var child = State.World.Create(new Name(part.Name), new Transform
        {
            Parent = placement.Parent,
            IgnoreParentScale = true,
            LocalScale = scale
        }, new MeshRenderer(part.Mesh) { Tint = tint });
        parts.Add(child, placement);
        UpdatePose(child, placement);
        occupied.Add((placement.Parent, placement.Target));
        // The root sets the first part's pose without using up one of its sockets.
        if (placement.Parent != root) occupied.Add((child, placement.Mount));
        history.Add(child);
        MarkShipChanged();
        return child;
    }

    private bool TryEditPart(Entity entity, PartSettings settings)
    {
        var placement = parts[entity];
        List<(Entity Entity, Placement Placement)> children = [];
        foreach (var (child, attachment) in parts)
        {
            if (attachment.Parent != entity) continue;
            var label = placement.Part.Sockets[attachment.Target].Label;
            var target = Array.FindIndex(settings.Part.Sockets, socket => socket.Label == label);
            if (target < 0 || (placement.Parent != root && target == settings.Mount))
            {
                partMessage = $"Free the {label} attachment before applying these settings.";
                return false;
            }
            children.Add((child, attachment with { Target = target }));
        }

        occupied.RemoveWhere(socket => socket.Item1 == entity);
        if (placement.Parent != root) occupied.Add((entity, settings.Mount));
        foreach (var (child, attachment) in children)
        {
            parts[child] = attachment;
            occupied.Add((entity, attachment.Target));
        }
        parts[entity] = placement with
        {
            Part = settings.Part, Mount = settings.Mount, Roll = settings.Roll, Offset = settings.Offset
        };
        entity.Get<MeshRenderer>().SharedMesh = settings.Part.Mesh;
        entity.Set(new Name(settings.Part.Name));
        SetAppearance(entity, settings.Size / settings.Part.Mesh.Bounds.Size, settings.Color);
        partMessage = null;
        return true;
    }

    private void UpdatePose(Entity entity, Placement placement)
    {
        var parent = placement.Parent;
        var from = placement.Part.Sockets[placement.Mount];
        var to = (parent == root ? RootSockets : parts[parent].Part.Sockets)[placement.Target];
        ref var transform = ref entity.Get<Transform>();
        var parentScale = parent.Get<Transform>().LocalScale;
        (transform.LocalPosition, transform.LocalRotation) = GetAttachmentPose(
            from, to, transform.LocalScale, parentScale, placement.Roll, placement.Offset);
    }

    private static (Vector3 Position, Quaternion Rotation) GetAttachmentPose(
        Socket from, Socket to, Vector3 scale, Vector3 parentScale, float roll, Vector3 offset)
    {
        var normal = Vector3.Normalize(to.Normal / parentScale);
        var rotation = Align(Vector3.Normalize(from.Normal / scale), -normal);
        rotation = Quaternion.Concatenate(rotation, Quaternion.CreateFromAxisAngle(normal, roll * MathF.PI / 180));
        var position = to.Position * parentScale + offset - Vector3.Transform(from.Position * scale, rotation);
        return (position, rotation);
    }

    private bool Undo() => history.Count > 0 && RemovePart(history[^1]);

    private bool RemovePart(Entity entity)
    {
        if (!parts.TryGetValue(entity, out var selectedPart)) return false;

        // Placement order always visits parents before their children.
        HashSet<Entity> removed = [entity];
        foreach (var part in history)
            if (removed.Contains(parts[part].Parent)) removed.Add(part);

        foreach (var part in removed)
        {
            parts.Remove(part, out var placement);
            occupied.Remove((placement.Parent, placement.Target));
            occupied.Remove((part, placement.Mount));
            State.World.Destroy(part);
        }
        history.RemoveAll(removed.Contains);
        ClearUnusedLinks();
        if (State.SelectedEntity is { } selected && removed.Contains(selected))
            State.SelectedEntity = selectedPart.Parent;
        MarkShipChanged();
        return true;
    }

    private bool DrawRadialSockets(ref PartSettings settings, Entity? entity)
    {
        var count = settings.Part.RadialSockets;
        if (count == 0 || !ImGui.BeginCombo("Radial sockets"u8, count.ToString())) return false;
        var changed = false;
        foreach (var option in RadialCounts)
        {
            ImGui.BeginDisabled(entity is { } part && occupied.Any(socket => socket.Item1 == part && socket.Item2 >= 2 + option));
            if (ImGui.Selectable(option.ToString(), option == count) && option != count)
            {
                settings.Part = WithRadialSockets(settings.Part, option);
                if (settings.Mount >= settings.Part.Sockets.Length) settings.Mount = 0;
                changed = true;
            }
            ImGui.EndDisabled();
        }
        ImGui.EndCombo();
        return changed;
    }

    private static Part WithRadialSockets(Part part, int count)
    {
        if (part.RadialSockets == count) return part;
        // Keep the base sockets and add the new angles in order so existing indices stay valid.
        var sockets = new List<Socket>(part.Sockets.Take(2 + DefaultRadialSockets));
        for (var step = DefaultRadialSockets * 2; step <= count; step *= 2)
            for (var i = 1; i < step; i += 2)
            {
                var degrees = i * 360f / step;
                var (sine, cosine) = MathF.SinCos(degrees * MathF.PI / 180);
                sockets.Add(CreateSocket(part.Mesh, new(cosine, 0, sine), FormattableString.Invariant($"Radial {degrees:g}°")));
            }
        return part with { Sockets = [.. sockets], RadialSockets = count };
    }

    private static Part CreatePart(string name, Mesh mesh, (Vector3 Normal, string Label)[] faces, int radialSockets = 0) =>
        new(name, mesh, faces.Select(face => CreateSocket(mesh, face.Normal, face.Label)).ToArray(), radialSockets);

    private static Part GetTruncatedCone(int tipPercent) => TruncatedCones.GetOrAdd(tipPercent, static percent =>
    {
        const float height = 0.76f;
        var mesh = Mesh.CreateFrustum(0.5f, 0.5f * (percent / 100f), height, 12);
        return new("Truncated Cone", mesh,
            [new(-Vector3.UnitY * (height / 2), -Vector3.UnitY, "Bottom"),
             new(Vector3.UnitY * (height / 2), Vector3.UnitY, "Top")], TipPercent: percent);
    });

    private static Socket CreateSocket(Mesh mesh, Vector3 normal, string label)
    {
        var center = mesh.Bounds.Center;
        var depth = float.PositiveInfinity;
        foreach (var submesh in mesh.SubMeshes)
            for (var i = 0; i < submesh.Indices.Length; i += 3)
            {
                var hit = SceneRenderSystem.TriangleDepth(center, normal,
                    submesh.Vertices[submesh.Indices[i]].Position,
                    submesh.Vertices[submesh.Indices[i + 1]].Position,
                    submesh.Vertices[submesh.Indices[i + 2]].Position);
                if (hit > 0 && hit < depth) depth = hit;
            }
        if (!float.IsFinite(depth)) throw new InvalidOperationException($"No surface for the {label} snap point.");
        return new(center + normal * depth, normal, label);
    }

    private static Quaternion Align(Vector3 from, Vector3 to)
    {
        var axis = Vector3.Cross(from, to);
        var sine = axis.Length();
        var dot = Vector3.Dot(from, to);
        if (sine > 1e-6f) return Quaternion.CreateFromAxisAngle(axis / sine, MathF.Atan2(sine, dot));
        if (dot >= 0) return Quaternion.Identity;
        var perpendicular = Vector3.Cross(from, Math.Abs(from.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX);
        return new(Vector3.Normalize(perpendicular), 0);
    }
}
