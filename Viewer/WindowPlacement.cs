namespace Chippy.Viewer;

// Screen position of the client area, plus its size in window coordinates (not framebuffer pixels).
public readonly record struct WindowPlacement(int X, int Y, int Width, int Height);
