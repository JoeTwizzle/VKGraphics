using BrickEngine.Example.VoxelRenderer.Standard;
using OpenTK.Graphics;
using OpenTK.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Example;
internal sealed class Game : IDisposable
{
    public Metrics _metrics;
    public GameLoop _gameLoop;
    public WindowHandler _windowHandler;
    public OpenWindowHandle _mainWindowHandle;
    public WindowInfo _mainWindowInfo;
    public DynamicVoxelRenderer _renderer;

    public float DeltaTime => _metrics.DeltaTime;

    public Game()
    {
        Toolkit.Init(new ToolkitOptions() { ApplicationName = nameof(Example) });
        VKLoader.Init();
        _gameLoop = new GameLoop();
        _windowHandler = new WindowHandler();
        _mainWindowHandle = _windowHandler.Open();
        _mainWindowInfo = _windowHandler.GetInfo(_mainWindowHandle);
        _mainWindowInfo.EventQueue.EventDispatched += MainWindowEventHandler;
        _renderer = new DynamicVoxelRenderer(this, _mainWindowInfo.Handle);

        Initialize();
        _gameLoop.ShouldRun = true;
        _gameLoop.Run(ref _metrics, Update);
        Destroy();
    }

    private void MainWindowEventHandler(PalHandle? handle, PlatformEventType type, EventArgs args)
    {
        if (args is CloseEventArgs)
        {
            _gameLoop.ShouldRun = false;
        }
    }

    void Initialize()
    {

    }

    void Destroy()
    {
        _renderer.Dispose();
        _windowHandler.Close(_mainWindowHandle);
    }

    void Update()
    {
        _windowHandler.Update();
        _mainWindowInfo.EventQueue.DispatchEvents();
        _renderer.Update();
    }

    public void Dispose()
    {
        _gameLoop.ShouldRun = false;
    }
}
