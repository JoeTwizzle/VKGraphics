namespace BrickEngine.Example.VolumeRenderer
{
    interface IVolumeRenderer : IDisposable
    {
        void Resize();
        void Update();
        void Render();
    }
}
