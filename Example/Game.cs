using OpenTK.Graphics;
using OpenTK.Platform;
using Example.VolumeRenderer.Dynamic;

namespace Example;
internal sealed class Game : IDisposable
{
    private readonly Metrics _metrics;
    private readonly GameLoop _gameLoop;
    private readonly WindowHandler _windowHandler;
    private readonly OpenWindowHandle _mainWindowHandle;
    private readonly WindowInfo _mainWindowInfo;
    private readonly DynamicVoxelRenderer _renderer;

    public float DeltaTime => _metrics.DeltaTime;
    public double DeltaTimeFull => _metrics.DeltaTimeFull;
    public Input Input => _mainWindowInfo.Input;


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
        _renderer.Init();
    }

    void Update()
    {
        _windowHandler.Update();
        Toolkit.Window.ProcessEvents(false);
        _mainWindowInfo.EventQueue.DispatchEvents();
        _renderer.Update();
    }

    void Destroy()
    {
        _renderer.Dispose();
        _windowHandler.Close(_mainWindowHandle);
    }

    public void Dispose()
    {
        _gameLoop.ShouldRun = false;
    }
}
