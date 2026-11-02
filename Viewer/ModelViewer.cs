using System.Numerics;
using Chippy.Input;
using Chippy.Rendering;
using Chippy.Systems;

namespace Chippy.Viewer;

public class ModelViewer(IRenderSystem renderSystem, IInputSystem input, ViewerState state)
{
    private readonly TransformSystem transforms = new(state.World);
    private readonly MeshRenderSystem meshes = new(state.World, renderSystem);
    public ViewerState State { get; } = state;

    // Returns false when the viewer should close.
    public bool Update(float delta, bool keyboardCaptured = false)
    {
        var cameraInput = Vector3.Zero;
        if (input.IsFocused && !keyboardCaptured)
        {
            if (input.GetKey(KeyCode.Escape))
                return false;
            cameraInput = new(input.GetAxis(KeyCode.LeftArrow, KeyCode.RightArrow),
                input.GetAxis(KeyCode.DownArrow, KeyCode.UpArrow),
                Math.Clamp(input.GetAxis(KeyCode.Equals, KeyCode.Minus)
                    + input.GetAxis(KeyCode.KeypadPlus, KeyCode.KeypadMinus), -1, 1));
        }
        delta = Math.Clamp(delta, 0, 0.1f);
        State.Camera.Move((cameraInput.X + (State.AutoRotate ? 0.35f : 0)) * delta,
            cameraInput.Y * delta, cameraInput.Z * delta);
        return true;
    }

    public void Draw(float aspectRatio)
    {
        transforms.Update();
        var view = Matrix4x4.CreateLookAt(State.Camera.Eye, Vector3.Zero, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 4, aspectRatio, 0.01f, 100f);
        renderSystem.BeginFrame(view, projection, State.Wireframe);
        meshes.Draw();
    }
}
