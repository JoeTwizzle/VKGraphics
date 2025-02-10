using OpenTK.Core.Utility;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Platform;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

namespace BrickEngine.Example;
internal abstract class GameWindow
{
    protected PalHandle contextHandle;
    public Vector2i FramebufferSize { get; private set; }
    public WindowHandle Window { get; protected set; }
    bool initialized;
    public GameWindow(GraphicsApi graphicsApi)
    {

        ToolkitOptions options = new()
        {
            // ApplicationName is the name of the application
            // this is used on some platforms to show the application name.
            ApplicationName = "OpenTK tutorial",
            // Set the logger to use
            Logger = new ConsoleLogger()
        };
        Toolkit.Init(options);
        if (graphicsApi == GraphicsApi.Vulkan)
        {
            VKLoader.Init();
        }
        GraphicsApiHints contextSettings = graphicsApi switch
        {
            GraphicsApi.Vulkan => new VulkanGraphicsApiHints(),
            GraphicsApi.OpenGL => new OpenGLGraphicsApiHints()
            {
                // Here different options of the opengl context can be set.
                Version = new Version(4, 6),
                Profile = OpenGLProfile.Core,
                DebugFlag = true,
                DepthBits = ContextDepthBits.Depth24,
                StencilBits = ContextStencilBits.Stencil8,
            },
            _ => throw new NotImplementedException(),
        };
        Window = Toolkit.Window.Create(contextSettings);

        switch (graphicsApi)
        {
            case GraphicsApi.OpenGL:
                {
                    var handle = Toolkit.OpenGL.CreateFromWindow(Window);
                    contextHandle = handle;
                    // Set the current opengl context and load the bindings.
                    Toolkit.OpenGL.SetCurrentContext(handle);
                    GLLoader.LoadBindings(Toolkit.OpenGL.GetBindingsContext(handle));
                }
                break;
            case GraphicsApi.Vulkan:
                {
                    //TODO
                }
                break;
            default:
                break;
        }
        //Toolkit.Keyboard.
        //Register event handlers
        EventQueue.EventRaised += EventRaised;

    }

    void EventRaised(PalHandle? handle, PlatformEventType type, EventArgs args)
    {
        if (args is CloseEventArgs closeArgs)
        {
            // Destroy the Window that the user wanted to close.
            Toolkit.Window.Destroy(closeArgs.Window);
        }
        else if (args is WindowFramebufferResizeEventArgs framebufferResizeEventArgs)
        {
            if (initialized)
            {
                FramebufferSize = framebufferResizeEventArgs.NewFramebufferSize;
                FramebufferResized(new(framebufferResizeEventArgs.NewFramebufferSize.X, framebufferResizeEventArgs.NewFramebufferSize.Y));
            }
        }
    }
    public double DeltaTimeFull;
    public float DeltaTime;
    public void Run()
    {
        const double TicksToSeconds = 1e-7;
        DeltaTimeFull = double.Epsilon;
        DeltaTime = MathF.Max(DeltaTime, float.Epsilon);
        Toolkit.Window.GetFramebufferSize(Window, out var a);
        FramebufferSize = a;
        long prev = Stopwatch.GetTimestamp();
        // Set the title of the Window
        Toolkit.Window.SetTitle(Window, "OpenTK Window");
        // Set the size of the Window
        Toolkit.Window.SetClientSize(Window, new(800, 600));
        Toolkit.Window.SetBorderStyle(Window, WindowBorderStyle.ResizableBorder);
        // Bring the Window out of the default Hidden Window mode
        Toolkit.Window.SetMode(Window, WindowMode.Normal);
        Toolkit.Window.GetFramebufferSize(Window, out a);
        //Toolkit.Window.ProcessEvents(false);
        //Toolkit.Window.ProcessEvents(false);
        //Toolkit.Window.ProcessEvents(false);
        //Toolkit.Window.ProcessEvents(false);
        //Toolkit.Window.ProcessEvents(false);
        //Toolkit.Window.ProcessEvents(false);
        //Toolkit.Window.ProcessEvents(false);
        //Toolkit.Window.ProcessEvents(false);
        //Toolkit.Window.ProcessEvents(false);
        //Toolkit.Window.ProcessEvents(false);
        //Toolkit.Window.ProcessEvents(false);
        //Toolkit.Window.ProcessEvents(false);
        //Toolkit.Window.ProcessEvents(false);
        FramebufferResized(a);
        InitRenderer();
        initialized = true;
        while (true)
        {
            long current = Stopwatch.GetTimestamp();
            DeltaTimeFull = (current - prev) * TicksToSeconds; //Ticks to seconds constant
            DeltaTime = (float)DeltaTimeFull;
            prev = current;
            // This will process events for all windows and
            //  post those events to the event queue.
            Toolkit.Window.ProcessEvents(false);
            // Check if the Window was destroyed after processing events.
            if (Toolkit.Window.IsWindowDestroyed(Window))
            {
                break;
            }
            Render();
        }
    }

    protected abstract void Render();
    protected abstract void FramebufferResized(Vector2i newSize);
    protected abstract void InitRenderer();
}
