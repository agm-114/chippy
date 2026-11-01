using Chippy.Input;
using Microsoft.Xna.Framework.Input;

namespace Chippy.Backends.MonoGame;

public static class KeyCodeExtensions
{
    public static Keys ToMonoGame(this KeyCode key) => key switch
    {
        KeyCode.None => Keys.None,
        >= KeyCode.Alpha0 and <= KeyCode.Alpha9 => Keys.D0 + (key - KeyCode.Alpha0),
        >= KeyCode.A and <= KeyCode.Z => Keys.A + (key - KeyCode.A),
        >= KeyCode.F1 and <= KeyCode.F24 => Keys.F1 + (key - KeyCode.F1),
        >= KeyCode.Keypad0 and <= KeyCode.Keypad9 => Keys.NumPad0 + (key - KeyCode.Keypad0),
        KeyCode.Escape => Keys.Escape,
        KeyCode.Backspace => Keys.Back,
        KeyCode.Tab => Keys.Tab,
        KeyCode.Return => Keys.Enter,
        KeyCode.Space => Keys.Space,
        KeyCode.Pause => Keys.Pause,
        KeyCode.CapsLock => Keys.CapsLock,
        KeyCode.NumLock => Keys.NumLock,
        KeyCode.ScrollLock => Keys.Scroll,
        KeyCode.PrintScreen => Keys.PrintScreen,
        KeyCode.Insert => Keys.Insert,
        KeyCode.Delete => Keys.Delete,
        KeyCode.Home => Keys.Home,
        KeyCode.End => Keys.End,
        KeyCode.PageUp => Keys.PageUp,
        KeyCode.PageDown => Keys.PageDown,
        KeyCode.LeftArrow => Keys.Left,
        KeyCode.RightArrow => Keys.Right,
        KeyCode.UpArrow => Keys.Up,
        KeyCode.DownArrow => Keys.Down,
        KeyCode.Quote => Keys.OemQuotes,
        KeyCode.Comma => Keys.OemComma,
        KeyCode.Minus => Keys.OemMinus,
        KeyCode.Period => Keys.OemPeriod,
        KeyCode.Slash => Keys.OemQuestion,
        KeyCode.Semicolon => Keys.OemSemicolon,
        KeyCode.Equals => Keys.OemPlus,
        KeyCode.LeftBracket => Keys.OemOpenBrackets,
        KeyCode.Backslash => Keys.OemPipe,
        KeyCode.RightBracket => Keys.OemCloseBrackets,
        KeyCode.BackQuote => Keys.OemTilde,
        KeyCode.NonUsBackslash => Keys.OemBackslash,
        KeyCode.KeypadPeriod => Keys.Decimal,
        KeyCode.KeypadDivide => Keys.Divide,
        KeyCode.KeypadMultiply => Keys.Multiply,
        KeyCode.KeypadMinus => Keys.Subtract,
        KeyCode.KeypadPlus => Keys.Add,
        KeyCode.LeftShift => Keys.LeftShift,
        KeyCode.RightShift => Keys.RightShift,
        KeyCode.LeftControl => Keys.LeftControl,
        KeyCode.RightControl => Keys.RightControl,
        KeyCode.LeftAlt => Keys.LeftAlt,
        KeyCode.RightAlt => Keys.RightAlt,
        KeyCode.LeftWindows => Keys.LeftWindows,
        KeyCode.RightWindows => Keys.RightWindows,
        KeyCode.Menu => Keys.Apps,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown key code.")
    };
}
