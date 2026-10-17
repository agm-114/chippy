using Brutal.GlfwApi;
using Brutal.Numerics;

if (!Glfw.Init())
    throw new Exception("Failed to initialize GLFW");

Glfw.WindowHint(GlfwWindowHint.ClientApi, 0);
var window = Glfw.CreateWindow(new GlfwWindow.CreateInfo
{
    Title = "Hello BRUTAL",
    Size = new int2(1280, 720),
});

while (!window.ShouldClose)
    Glfw.PollEvents();

Glfw.Terminate();