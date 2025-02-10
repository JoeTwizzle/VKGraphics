using BrickEngine.Example.VolumeRenderer;
using BrickEngine.Example.VoxelRenderer.Standard;
using OpenTK.Mathematics;
using OpenTK.Platform;
using OpenTK.Windowing.Common;
using System.Diagnostics;

namespace BrickEngine.Example
{
    class MyGameWindow : GameWindow
    {
        public string VolumeFilePath { get; private set; }
        public string CatalogueFilePath { get; private set; }

        public MyGameWindow(string volumeFilePath, string catalogueFilePath, ActiveVolumeRenderer activeVolumeRenderer) : base(GraphicsApi.Vulkan)
        {
            VolumeFilePath = volumeFilePath;
            CatalogueFilePath = catalogueFilePath;
            //renderer = new DynamicVoxelRenderer(this);
            this.activeVolumeRenderer = activeVolumeRenderer;
        }

        public bool IsEnabled { get; set; } = true;

        float accum = 0;
        int frameCount = 0;
        IVolumeRenderer? renderer;
        private readonly ActiveVolumeRenderer activeVolumeRenderer;

        protected override void Render()
        {
            frameCount++;
            accum += DeltaTime;
            if (accum > 20f)
            {
                float fps = (frameCount / 20f);
                float mspf = 1000f / (fps);
                Console.WriteLine($"fps: {fps} ms: {mspf}");
                accum -= 20f;
                frameCount = 0;
            }
            //if (Input.GetKeyDown(Key.X))
            //{
            //    GraphicsContext.GraphicsDevice.WaitForIdle();
            //    Window.Close();
            //}
            renderer?.Update();
            renderer?.Render();
        }

        protected override void FramebufferResized(Vector2i newSize)
        {
            renderer?.Resize();
        }

        protected override void InitRenderer()
        {
            renderer = new DynamicVoxelRenderer(this);

        }
    }
}