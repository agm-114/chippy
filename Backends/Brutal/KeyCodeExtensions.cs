using Brutal.GlfwApi;
using Chippy.Input;

namespace Chippy.Backends.Brutal;

public static class KeyCodeExtensions
{
    public static GlfwKey ToGlfw(this KeyCode key) => key switch
    {
        KeyCode.None => GlfwKey.Unknown,
        >= KeyCode.Alpha0 and <= KeyCode.Alpha9 => GlfwKey.Number0 + (key - KeyCode.Alpha0),
        >= KeyCode.A and <= KeyCode.Z => GlfwKey.A + (key - KeyCode.A),
        >= KeyCode.F1 and <= KeyCode.F24 => GlfwKey.F1 + (key - KeyCode.F1),
        >= KeyCode.Keypad0 and <= KeyCode.Keypad9 => GlfwKey.Kp0 + (key - KeyCode.Keypad0),
        KeyCode.Escape => GlfwKey.Escape,
        KeyCode.Backspace => GlfwKey.Backspace,
        KeyCode.Tab => GlfwKey.Tab,
        KeyCode.Return => GlfwKey.Enter,
        KeyCode.Space => GlfwKey.Space,
        KeyCode.Pause => GlfwKey.Pause,
        KeyCode.CapsLock => GlfwKey.CapsLock,
        KeyCode.NumLock => GlfwKey.NumLock,
        KeyCode.ScrollLock => GlfwKey.ScrollLock,
        KeyCode.PrintScreen => GlfwKey.PrintScreen,
        KeyCode.Insert => GlfwKey.Insert,
        KeyCode.Delete => GlfwKey.Delete,
        KeyCode.Home => GlfwKey.Home,
        KeyCode.End => GlfwKey.End,
        KeyCode.PageUp => GlfwKey.PageUp,
        KeyCode.PageDown => GlfwKey.PageDown,
        KeyCode.LeftArrow => GlfwKey.Left,
        KeyCode.RightArrow => GlfwKey.Right,
        KeyCode.UpArrow => GlfwKey.Up,
        KeyCode.DownArrow => GlfwKey.Down,
        KeyCode.Quote => GlfwKey.Apostrophe,
        KeyCode.Comma => GlfwKey.Comma,
        KeyCode.Minus => GlfwKey.Minus,
        KeyCode.Period => GlfwKey.Period,
        KeyCode.Slash => GlfwKey.Slash,
        KeyCode.Semicolon => GlfwKey.Semicolon,
        KeyCode.Equals => GlfwKey.Equal,
        KeyCode.LeftBracket => GlfwKey.LeftBracket,
        KeyCode.Backslash => GlfwKey.Backslash,
        KeyCode.RightBracket => GlfwKey.RightBracket,
        KeyCode.BackQuote => GlfwKey.GraveAccent,
        KeyCode.NonUsBackslash => GlfwKey.World2,
        KeyCode.KeypadPeriod => GlfwKey.KpDecimal,
        KeyCode.KeypadDivide => GlfwKey.KpDivide,
        KeyCode.KeypadMultiply => GlfwKey.KpMultiply,
        KeyCode.KeypadMinus => GlfwKey.KpSubtract,
        KeyCode.KeypadPlus => GlfwKey.KpAdd,
        KeyCode.LeftShift => GlfwKey.LeftShift,
        KeyCode.RightShift => GlfwKey.RightShift,
        KeyCode.LeftControl => GlfwKey.LeftControl,
        KeyCode.RightControl => GlfwKey.RightControl,
        KeyCode.LeftAlt => GlfwKey.LeftAlt,
        KeyCode.RightAlt => GlfwKey.RightAlt,
        KeyCode.LeftWindows => GlfwKey.LeftSuper,
        KeyCode.RightWindows => GlfwKey.RightSuper,
        KeyCode.Menu => GlfwKey.Menu,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown key code.")
    };
}
