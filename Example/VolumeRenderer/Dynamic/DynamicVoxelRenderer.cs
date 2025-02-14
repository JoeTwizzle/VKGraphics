﻿global using VKGraphics;
using Example;
using OpenTK.Mathematics;
using OpenTK.Platform;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BrickEngine.Example.VoxelRenderer.Standard
{
    sealed class DynamicVoxelRenderer
    {
        readonly Game game;
        readonly WindowHandle window;
        readonly GraphicsDevice gd;
        readonly CommandList cl;
        const int pixelSize = 1;
        //camera
        readonly DeviceBuffer cameraBuffer;
        readonly ResourceSet cameraSet;

        //Raytracing
        readonly Pipeline raytracePipeline;
        readonly ResourceLayout voxelLayout;
        readonly ResourceSet voxelSet;

        //Textures
        Texture mainTex;
        //Render Target
        readonly ResourceLayout renderBufferLayout;
        ResourceSet renderSet;
        //Blit
        readonly ResourceLayout displayBufferLayout;
        ResourceSet displaySet;
        readonly Pipeline displayPipline;
        readonly Swapchain swapchain;
        public Camera Camera { get; }

        static uint GetUBOSize(int size)
        {
            return (uint)(((size - 1) / 16 + 1) * 16);
        }

        static ulong[] CreateBricks(int size, float scale)
        {

            int chunksPerAxis = (size / 4);
            ulong[] chunks = new ulong[chunksPerAxis * chunksPerAxis * chunksPerAxis];
            for (int z = 0; z < chunksPerAxis; z++)
            {
                for (int y = 0; y < chunksPerAxis; y++)
                {
                    for (int x = 0; x < chunksPerAxis; x++)
                    {
                        ulong chunk = 0;
                        for (int lz = 0; lz < 4; lz++)
                        {
                            float zFinal = (z * 4 + lz) / scale;
                            float zfactor = MathF.Cos(zFinal) * 0.5f;
                            for (int lx = 0; lx < 4; lx++)
                            {
                                float xFinal = (x * 4 + lx) / scale;
                                float term = (MathF.Sin(xFinal) * 0.5f + 1 + zfactor + 1) * 40 + 256;
                                for (int ly = 0; ly < 4; ly++)
                                {
                                    int chunkIndex = (lz * 4 * 4) + ly * 4 + lx;
                                    float yFinal = (y * 4 + ly);

                                    if (term > yFinal)
                                    {
                                        chunk |= 1ul << (chunkIndex);
                                    }
                                    //chunk |= (MathF.Abs(OpenSimplex2.Noise3_ImproveXZ(532, xFinal, yFinal, zFinal) - OpenSimplex2.Noise3_ImproveXZ(543, xFinal, yFinal, zFinal)) > 0.35 ? 1ul : 0ul) << (chunkIndex);
                                }
                            }
                        }
                        chunks[(z * chunksPerAxis * chunksPerAxis) + y * chunksPerAxis + x] = chunk;
                    }
                }
            }
            return chunks;
        }


        Vector2i framebufferSize;

        public unsafe DynamicVoxelRenderer(Game game, WindowHandle window)
        {
            Camera = new();
            this.game = game;
            this.window = window;
            Toolkit.Window.GetFramebufferSize(window, out framebufferSize);
            gd = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true, null, false, ResourceBindingModel.Improved, true, false));
            ResourceFactory rf = gd.ResourceFactory;
            swapchain = rf.CreateSwapchain(new SwapchainDescription(window, (uint)800, 600, null, false));
            //Create Camera buffer
            Camera.AspectRatio = framebufferSize.X / (float)framebufferSize.Y;
            cameraBuffer = rf.CreateBuffer(new BufferDescription(GetUBOSize(Unsafe.SizeOf<CameraProperties>()), BufferUsage.UniformBuffer, 0));
            var p = Camera.ProjectionMatrix;
            Matrix4x4.Invert(p, out var pInv);
            gd.UpdateBuffer(cameraBuffer, 0, new CameraProperties(Camera.Transform.WorldMatrix, pInv));

            //Create resource layouts
            var cameraBufferLayout = rf.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("_CameraProperites", ResourceKind.UniformBuffer, ShaderStages.Compute))
                );
            cameraSet = rf.CreateResourceSet(new ResourceSetDescription(cameraBufferLayout, cameraBuffer));

            // voxel layout
            voxelLayout = rf.CreateResourceLayout(new ResourceLayoutDescription(
                    new ResourceLayoutElementDescription("VoxDataBuf", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute)
                ));

            var chunks = CreateBricks(1024, 20);
            var chunkBuffer = rf.CreateBuffer(new BufferDescription((uint)(sizeof(ulong) * chunks.Length), BufferUsage.StructuredBufferReadOnly));
            gd.UpdateBuffer(chunkBuffer, 0, ref chunks[0], (uint)(sizeof(ulong) * chunks.Length));

            voxelSet = rf.CreateResourceSet(new ResourceSetDescription(voxelLayout, chunkBuffer));


            renderBufferLayout = rf.CreateResourceLayout(new ResourceLayoutDescription(
                new ResourceLayoutElementDescription("screen", ResourceKind.TextureReadWrite, ShaderStages.Compute)
                ));

            //Create shader
            var shaderResult = SpirvCompiler.GetSpirvBytes("VolumeRenderer/Dynamic/FastVoxelDynamic.comp");
            var shader = rf.CreateShader(new ShaderDescription(ShaderStages.Compute, shaderResult, "main", true));

            //Create Pipeline
            raytracePipeline = rf.CreateComputePipeline(new ComputePipelineDescription(shader, [cameraBufferLayout, voxelLayout, renderBufferLayout], 8, 8, 1));

            var vertShaderResult = SpirvCompiler.GetSpirvBytes("fsTriVert.vert");
            var vertShader = rf.CreateShader(new ShaderDescription(ShaderStages.Vertex, vertShaderResult, "main"));
            var fragResult = SpirvCompiler.GetSpirvBytes("fsTriFrag.frag");
            var fragShader = rf.CreateShader(new ShaderDescription(ShaderStages.Fragment, fragResult, "main"));

            //Create 
            displayBufferLayout = rf.CreateResourceLayout(new ResourceLayoutDescription(
                  new ResourceLayoutElementDescription("_MainSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                  new ResourceLayoutElementDescription("_MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment))
                  );

            CreateTextures(framebufferSize, rf);

            var fsTriShaderSet = new ShaderSetDescription(null, [vertShader, fragShader]);
            displayPipline = rf.CreateGraphicsPipeline(new GraphicsPipelineDescription(BlendStateDescription.SingleOverrideBlend,
                DepthStencilStateDescription.Disabled, RasterizerStateDescription.CullNone,
                  PrimitiveTopology.TriangleList, fsTriShaderSet, displayBufferLayout, swapchain.Framebuffer.OutputDescription));

            cl = rf.CreateCommandList();
        }


        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct CameraProperties
        {
            public Matrix4x4 Model;
            public Matrix4x4 CameraInverseProjection;

            public CameraProperties(Matrix4x4 cameraToWorld, Matrix4x4 cameraInverseProjection)
            {
                Model = cameraToWorld;
                CameraInverseProjection = cameraInverseProjection;
            }
        }

        private void CreateTextures(Vector2i fbSize, ResourceFactory rf)
        {
            mainTex = rf.CreateTexture(new TextureDescription((uint)fbSize.X / pixelSize, (uint)fbSize.Y / pixelSize, 1, 1, 1, PixelFormat.R16G16B16A16Float, TextureUsage.Sampled | TextureUsage.Storage, TextureType.Texture2D));
            renderSet = rf.CreateResourceSet(new ResourceSetDescription(renderBufferLayout, mainTex));
            displaySet = rf.CreateResourceSet(new ResourceSetDescription(displayBufferLayout, gd.PointSampler, mainTex));
        }

        private void Resize()
        {
            Camera.AspectRatio = framebufferSize.X / (float)framebufferSize.Y;
            swapchain.Resize((uint)framebufferSize.X, (uint)framebufferSize.Y);
            CreateTextures(framebufferSize, gd.ResourceFactory);
        }

        public void Update()
        {
            Camera.Update(game.DeltaTime);
            Toolkit.Window.GetFramebufferSize(window, out var newFramebufferSize);

            if ((framebufferSize != newFramebufferSize))
            {
                framebufferSize = newFramebufferSize;
                Resize();
            }

            var p = Camera.PerspectiveMatrix;
            Matrix4x4.Invert(p, out var pInv);

            cl.Begin();
            cl.UpdateBuffer(cameraBuffer, 0, new CameraProperties(Camera.Transform.WorldMatrix, pInv));
            cl.SetPipeline(raytracePipeline);
            cl.SetComputeResourceSet(0, cameraSet);
            cl.SetComputeResourceSet(1, voxelSet);
            cl.SetComputeResourceSet(2, renderSet);
            uint worksizeX = BitOperations.RoundUpToPowerOf2((uint)framebufferSize.X) / pixelSize;
            uint worksizeY = BitOperations.RoundUpToPowerOf2((uint)framebufferSize.Y) / pixelSize;
            cl.Dispatch(worksizeX / 8, worksizeY / 8, 1);
            cl.SetPipeline(displayPipline);
            cl.SetGraphicsResourceSet(0, displaySet);
            cl.SetFramebuffer(swapchain.Framebuffer);
            cl.SetFullViewport(0);
            cl.Draw(3);
            cl.End();
            gd.SubmitCommands(cl);
            gd.SwapBuffers(swapchain);
        }

        public void Dispose()
        {
            cl.Dispose();

            displayPipline.Dispose();
            displayBufferLayout.Dispose();
            cameraBuffer.Dispose();
            cameraSet.Dispose();

            renderBufferLayout.Dispose();
            displaySet.Dispose();
            renderSet.Dispose();

            raytracePipeline.Dispose();
            voxelLayout.Dispose();
            voxelSet.Dispose();

            mainTex.Dispose();
        }
    }
}
