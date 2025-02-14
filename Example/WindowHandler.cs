using OpenTK.Mathematics;
using OpenTK.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Example;
public readonly struct WindowInfo
{
    public readonly WindowHandle Handle;
    public readonly EventQueue EventQueue;

    public WindowInfo(WindowHandle handle, EventQueue eventQueue)
    {
        Handle = handle;
        EventQueue = eventQueue;
    }
}

struct OpenWindowHandle
{
    public readonly int Handle;

    public OpenWindowHandle(int handle)
    {
        Handle = handle;
    }
}

internal class WindowHandler
{
    int _createWindows = 0;
    private readonly Queue<int> _recycledHandles;
    private readonly Dictionary<int, WindowInfo> _windows;
    public WindowHandler()
    {
        _recycledHandles = new();
        _windows = [];
    }

    public WindowInfo GetInfo(OpenWindowHandle handle)
    {
        return _windows[handle.Handle];
    }

    public OpenWindowHandle Open(string? title = null)
    {
        GraphicsApiHints contextSettings = new VulkanGraphicsApiHints();
        var window = Toolkit.Window.Create(contextSettings);
        Toolkit.Window.SetTitle(window, title ?? $"{nameof(Example)} Window");
        Toolkit.Window.SetClientSize(window, new(800, 600));
        Toolkit.Window.SetBorderStyle(window, WindowBorderStyle.ResizableBorder);
        Toolkit.Window.SetMode(window, WindowMode.Normal);

        var eventQueue = EventQueue.Subscribe(window);
        int handle;
        if (_recycledHandles.Count > 0)
        {
            handle = _recycledHandles.Dequeue();
        }
        else
        {
            handle = ++_createWindows;
        }
        _windows.Add(handle, new WindowInfo(window, eventQueue));
        return new OpenWindowHandle(handle);
    }

    public void Update()
    {
        Toolkit.Window.ProcessEvents(false);
    }

    public void Close(OpenWindowHandle handle)
    {
        if (!_windows.TryGetValue(handle.Handle, out var info))
        {
            return;
        }

        info.EventQueue.Dispose();
        Toolkit.Window.Destroy(info.Handle);

        _recycledHandles.Enqueue(handle.Handle);
    }
}
