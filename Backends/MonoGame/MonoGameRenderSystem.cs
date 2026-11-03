using System.Numerics;
using Chippy.Components;
using Chippy.Models;
using Chippy.Rendering;
using Microsoft.Xna.Framework.Graphics;
using Xna = Microsoft.Xna.Framework;

namespace Chippy.Backends.MonoGame;

public class MonoGameRenderSystem : IRenderSystem
{
    private readonly GraphicsDevice device;
    private readonly Dictionary<Mesh, MonoGameMesh[]> uploaded = new(ReferenceEqualityComparer.Instance);
    private readonly BasicEffect effect;
    private readonly MonoGamePrimitives primitives;
    private Matrix4x4 viewProjection;
    private readonly RasterizerState solid = new() { CullMode = CullMode.None };
    private readonly RasterizerState wireframe = new() { CullMode = CullMode.None, FillMode = FillMode.WireFrame };
    private bool disposed;
    public int UploadedMeshCount => uploaded.Count;

    public MonoGameRenderSystem(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        this.device = device;
        primitives = new(device);
        effect = new BasicEffect(device)
        {
            LightingEnabled = true,
            PreferPerPixelLighting = true,
            AmbientLightColor = new Xna.Vector3(0.28f)
        };
        effect.DirectionalLight0.Enabled = true;
        effect.DirectionalLight0.Direction = Xna.Vector3.Normalize(new(0.5f, -0.8f, -0.6f));
        effect.DirectionalLight0.DiffuseColor = new Xna.Vector3(0.72f);
    }

    public void BeginFrame(in Matrix4x4 view, in Matrix4x4 projection, bool wireframe = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        device.BlendState = BlendState.Opaque;
        device.DepthStencilState = DepthStencilState.Default;
        device.RasterizerState = wireframe ? this.wireframe : solid;
        effect.View = view.ToXna();
        effect.Projection = projection.ToXna();
        viewProjection = view * projection;
    }

    public void DrawMesh(in Transform transform, in MeshRenderer meshRenderer)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var mesh = meshRenderer.SharedMesh;
        ArgumentNullException.ThrowIfNull(mesh);
        var matrix = transform.LocalToWorldMatrix;
        matrix.GetNormalMatrix();
        if (!uploaded.TryGetValue(mesh, out var meshes))
        {
            List<MonoGameMesh> created = [];
            try
            {
                foreach (var subMesh in mesh.SubMeshes)
                    created.Add(new MonoGameMesh(device, subMesh));
                meshes = [.. created];
                uploaded.Add(mesh, meshes);
            }
            catch
            {
                created.ForEach(gpuMesh => gpuMesh.Dispose());
                throw;
            }
        }
        effect.World = matrix.ToXna();
        effect.LightingEnabled = !meshRenderer.Unlit;
        foreach (var gpuMesh in meshes)
        {
            var color = gpuMesh.Color * meshRenderer.Tint;
            effect.DiffuseColor = new(color.X, color.Y, color.Z);
            effect.Alpha = 1;
            foreach (var pass in effect.CurrentTechnique.Passes)
            {
                pass.Apply();
                gpuMesh.Draw(device);
            }
        }
    }

    public bool ReleaseMesh(Mesh mesh)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(mesh);
        if (!uploaded.Remove(mesh, out var meshes))
            return false;
        Array.ForEach(meshes, gpuMesh => gpuMesh.Dispose());
        return true;
    }

    public void DrawPrimitives(ReadOnlySpan<PrimitiveVertex> vertices, PrimitiveTopology topology, bool screenSpace = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        primitives.Draw(vertices, topology, screenSpace ? Matrix4x4.Identity : viewProjection, screenSpace);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        foreach (var meshes in uploaded.Values)
            Array.ForEach(meshes, mesh => mesh.Dispose());
        uploaded.Clear();
        effect.Dispose();
        primitives.Dispose();
        wireframe.Dispose();
        solid.Dispose();
        disposed = true;
    }
}
