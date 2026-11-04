using System.Numerics;
using System.Text;
using Arch.Core;
using Arch.Core.Extensions;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using Chippy.Components;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Chippy.Viewer;

internal partial class MeshBuilder
{
    private class ShipYaml
    {
        public float[]? WorldRotation { get; init; }
        public required PartYaml[] Parts { get; init; }
    }

    private class PartYaml
    {
        public required int Id { get; init; }
        public required string Primitive { get; init; }
        public required int Parent { get; init; }
        public required string Socket { get; init; }
        public required string Mount { get; init; }
        public float[]? Size { get; init; }
        public float[]? Offset { get; init; }
        public float? Roll { get; init; }
        public float[]? Color { get; init; }
        public int? RadialSockets { get; init; }
        public int? TipPercent { get; init; }
        public int? Link { get; init; }
    }

    private class VectorYamlConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(float[]);

        public object? ReadYaml(IParser parser, Type type, ObjectDeserializer deserialize)
        {
            if (parser.Accept<Scalar>(out _))
            {
                var value = (float?)deserialize(typeof(float?));
                return value is { } number ? new[] { number, number, number } : null;
            }

            parser.Consume<SequenceStart>();
            float[] values = [(float)deserialize(typeof(float))!, (float)deserialize(typeof(float))!, (float)deserialize(typeof(float))!];
            parser.Consume<SequenceEnd>();
            return values;
        }

        public void WriteYaml(IEmitter emitter, object? value, Type type, ObjectSerializer serialize)
        {
            var values = (float[])value!;
            if (values[0] == values[1] && values[0] == values[2])
            {
                serialize(values[0], typeof(float));
                return;
            }
            emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, true, SequenceStyle.Flow));
            foreach (var number in values) serialize(number, typeof(float));
            emitter.Emit(new SequenceEnd());
        }
    }

    private static readonly ISerializer YamlWriter = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance).WithIndentedSequences()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .WithTypeConverter(new VectorYamlConverter()).Build();
    private static readonly IDeserializer YamlReader = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance).WithDuplicateKeyChecking()
        .WithEnforceRequiredMembers().WithMaximumRecursion(16)
        .WithTypeConverter(new VectorYamlConverter()).Build();

    private EditorWindowLayout yamlWindow;
    private ImInputString? yamlText;
    private bool showYaml, yamlDirty, yamlStale = true;
    private string? yamlMessage;

    private bool DrawYamlPanel()
    {
        var text = yamlText ??= new(256 * 1024, ""u8);
        if (yamlStale && !yamlDirty) ReloadYaml(text);
        var applied = false;
        var display = ImGui.GetIO().DisplaySize;
        var width = Math.Min(540, Math.Max(200, display.X - 24));
        var height = Math.Min(420, Math.Max(180, display.Y - 24));
        if (yamlWindow.Begin("Ship YAML"u8, new(Math.Max(12, (display.X - width) / 2), Math.Max(12, display.Y - height - 12)), new(width, height)))
        {
            ImGui.BeginDisabled(!yamlDirty);
            if (ImGui.Button("Apply"u8))
            {
                try
                {
                    ApplyYaml(text.ToString());
                    applied = true;
                    yamlDirty = yamlStale = false;
                    yamlMessage = "Applied to ship.";
                }
                catch (YamlException error)
                {
                    yamlMessage = $"Line {error.Start.Line}: {error.InnerException?.Message ?? error.Message}";
                }
                catch (InvalidDataException error)
                {
                    yamlMessage = error.Message;
                }
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Reload from ship"u8)) ReloadYaml(text);
            ImGui.SameLine();
            if (ImGui.Button("Close"u8)) showYaml = false;
            ImGui.TextWrapped(yamlMessage ?? (yamlStale && yamlDirty
                ? "The ship changed. Applying this draft replaces it."
                : "List parents before children. Use parent 0 for the root. Linked parts inherit scale and color from the first part in their group."));
            if (ImGui.InputTextMultiline("##ShipYaml"u8, text, new float2(-1, -1)))
            {
                yamlDirty = true;
                yamlMessage = null;
            }
        }
        ImGui.End();
        return applied;
    }

    private void ReloadYaml(ImInputString text)
    {
        var yaml = ExportYaml();
        yamlDirty = yamlStale = false;
        yamlMessage = null;
        if (Encoding.UTF8.GetByteCount(yaml) < text.Capacity)
            text.SetValue(yaml.AsSpan());
        else
        {
            text.Clear();
            yamlMessage = "This ship is too large for the YAML editor.";
        }
    }

    private string ExportYaml()
    {
        var ids = new Dictionary<Entity, int> { [root] = 0 };
        var entries = new PartYaml[history.Count];
        HashSet<int> writtenLinks = [];
        for (var i = 0; i < history.Count; i++)
        {
            var entity = history[i];
            var part = parts[entity];
            var parentSockets = part.Parent == root ? RootSockets : parts[part.Parent].Part.Sockets;
            var bounds = part.Part.Mesh.Bounds.Size;
            var scale = entity.Get<Transform>().LocalScale;
            var size = scale * bounds;
            var tint = entity.Get<MeshRenderer>().Tint;
            var writeAppearance = part.Link == 0 || writtenLinks.Add(part.Link);
            ids.Add(entity, i + 1);
            entries[i] = new()
            {
                Id = i + 1, Primitive = part.Part.Name, Parent = ids[part.Parent],
                Socket = parentSockets[part.Target].Label, Mount = part.Part.Sockets[part.Mount].Label,
                Size = !writeAppearance || scale == Vector3.One / bounds ? null : [size.X, size.Y, size.Z],
                Offset = part.Offset == Vector3.Zero ? null : [part.Offset.X, part.Offset.Y, part.Offset.Z],
                Roll = part.Roll == 0 ? null : part.Roll,
                Color = !writeAppearance || tint == DefaultColor ? null : [tint.X, tint.Y, tint.Z],
                RadialSockets = part.Part.RadialSockets > DefaultRadialSockets ? part.Part.RadialSockets : null,
                TipPercent = part.Part.TipPercent == DefaultTipPercent ? null : part.Part.TipPercent,
                Link = part.Link == 0 ? null : part.Link
            };
        }
        return YamlWriter.Serialize(new ShipYaml
        {
            WorldRotation = worldRotation == Vector3.Zero ? null : [worldRotation.X, worldRotation.Y, worldRotation.Z],
            Parts = entries
        });
    }

    private void ApplyYaml(string yaml)
    {
        var document = YamlReader.Deserialize<ShipYaml>(yaml);
        if (document?.Parts is not { } entries)
            throw new InvalidDataException("Expected a parts list. Use parts: [] for an empty ship.");
        var rotation = ReadVector(document.WorldRotation, Vector3.Zero, -360, 360, "World rotation");

        var socketsById = new Dictionary<int, Socket[]> { [0] = RootSockets };
        var usedSockets = new HashSet<(int Part, int Socket)>();
        var linkedAppearance = new Dictionary<int, (Vector3 Scale, Vector4 Color)>();
        var validated = new List<(int Id, int Parent, Part Part, int Target, int Mount, Vector3 Scale, Vector3 Offset, float Roll, Vector4 Color, int Link)>();
        foreach (var entry in entries)
        {
            if (entry is null) throw new InvalidDataException("Each entry in parts must describe a part.");
            if (entry.Id <= 0 || socketsById.ContainsKey(entry.Id))
                throw new InvalidDataException($"Part IDs must be positive and unique. Check ID {entry.Id}.");
            if (!socketsById.TryGetValue(entry.Parent, out var parentSockets))
                throw new InvalidDataException($"Part {entry.Id} needs parent {entry.Parent} listed before it.");
            var part = string.Equals(entry.Primitive, "Frustum", StringComparison.OrdinalIgnoreCase)
                ? GetTruncatedCone(47)
                : Array.Find(palette, candidate => string.Equals(candidate.Name, entry.Primitive, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException($"Part {entry.Id} has unknown primitive '{entry.Primitive}'.");
            if (entry.RadialSockets is { } count)
            {
                if (part.RadialSockets == 0 || !RadialCounts.Contains(count))
                    throw new InvalidDataException($"Part {entry.Id} does not support {count} radial sockets.");
                part = WithRadialSockets(part, count);
            }
            if (entry.TipPercent is { } tip)
            {
                if (part.TipPercent is null || tip is < 0 or > 100)
                    throw new InvalidDataException($"Part {entry.Id} needs a truncated cone with a tip width from 0 to 100 percent.");
                part = GetTruncatedCone(tip);
            }
            var target = FindSocket(parentSockets, entry.Socket, entry.Id, "parent socket");
            var mount = FindSocket(part.Sockets, entry.Mount, entry.Id, "mount face");
            if (!usedSockets.Add((entry.Parent, target)))
                throw new InvalidDataException($"Part {entry.Id} uses an occupied parent socket '{entry.Socket}'.");
            var size = ReadVector(entry.Size, Vector3.One, 0.001f, 10000, $"Part {entry.Id} size");
            var offset = ReadVector(entry.Offset, Vector3.Zero, -float.MaxValue, float.MaxValue, $"Part {entry.Id} offset");
            var color = ReadVector(entry.Color, new(DefaultColor.X), 0, 1, $"Part {entry.Id} color");
            var roll = entry.Roll ?? 0;
            if (!float.IsFinite(roll) || roll is < -180 or > 180)
                throw new InvalidDataException($"Part {entry.Id} needs a roll from -180 to 180 degrees.");
            if (entry.Link is <= 0)
                throw new InvalidDataException($"Part {entry.Id} needs a positive link group.");
            var link = entry.Link ?? 0;
            var appearance = (Scale: size / part.Mesh.Bounds.Size, Color: new Vector4(color, 1));
            if (link != 0 && !linkedAppearance.TryAdd(link, appearance)) appearance = linkedAppearance[link];

            socketsById.Add(entry.Id, part.Sockets);
            if (entry.Parent != 0) usedSockets.Add((entry.Id, mount));
            validated.Add((entry.Id, entry.Parent, part, target, mount, appearance.Scale, offset, roll, appearance.Color, link));
        }

        if (history.Count > 0) RemovePart(history[0]);
        SetWorldRotation(rotation);
        var entities = new Dictionary<int, Entity> { [0] = root };
        foreach (var item in validated)
        {
            var placement = new Placement(item.Part, entities[item.Parent], item.Target, item.Mount, item.Roll, item.Offset, item.Link);
            entities.Add(item.Id, AddPart(placement, item.Scale, item.Color));
        }
        ClearUnusedLinks();
        State.SelectedEntity = history.Count == 0 ? root : history[0];
        if (history.Count == 0) State.Camera.Target = Vector3.Zero;
    }

    private static int FindSocket(Socket[] sockets, string name, int id, string field)
    {
        var index = Array.FindIndex(sockets, socket => string.Equals(socket.Label, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : throw new InvalidDataException($"Part {id} has unknown {field} '{name}'.");
    }

    private static Vector3 ReadVector(float[]? values, Vector3 fallback, float min, float max, string field)
    {
        if (values is null) return fallback;
        if (values.Length != 3 || values.Any(value => !float.IsFinite(value) || value < min || value > max))
            throw new InvalidDataException($"{field} must have three values from {min:g} to {max:g}.");
        return new(values[0], values[1], values[2]);
    }
}
