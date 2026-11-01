namespace Chippy.Input;

public interface IInputSystem
{
    bool IsFocused { get; }
    // Held-key state for the current frame. Hosts poll their native input before updating the viewer.
    bool GetKey(KeyCode key);
    float GetAxis(KeyCode negative, KeyCode positive) => (GetKey(positive) ? 1 : 0) - (GetKey(negative) ? 1 : 0);
}
