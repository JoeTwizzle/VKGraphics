global using VKGraphics;
using BrickEngine.Example.VolumeRenderer;
using Example;
using OpenTK.Platform;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VKGraphics.Windowing;

namespace BrickEngine.Example.VoxelRenderer.Standard
{
    sealed class DynamicVoxelRenderer : IVolumeRenderer
    {
        readonly GameWindow window;
        readonly GraphicsDevice gd;
        readonly CommandList cl;
        const int pixelSize = 1;
        //camera
        readonly DeviceBuffer cameraBuffer;
        readonly ResourceSet cameraSet;

        //Raytracing
        readonly Pipeline raytracePipeline;
        readonly ResourceLayout voxelLayout;
        ResourceSet voxelSet;

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


        public unsafe DynamicVoxelRenderer(GameWindow game)
        {
            Camera = new();
            this.window = game;
            gd = GraphicsDevice.CreateVulkan(new GraphicsDeviceOptions(true, null, false, ResourceBindingModel.Improved, true, true));
            ResourceFactory rf = gd.ResourceFactory;
            swapchain = rf.CreateSwapchain(new SwapchainDescription(window.Window, (uint)800, 600, null, false));

            //Create Camera buffer
            Camera.AspectRatio = game.FramebufferSize.X / (float)game.FramebufferSize.Y;
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

            //-----------DISPLAY-------------
            const string fsTriVert = """
            #version 460
            layout(location = 0) out vec2 texCoord;

            void main()
            {
                texCoord = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
                vec2 xy = fma(texCoord, vec2(2.0) , vec2(-1.0));
                gl_Position = vec4(xy, 0.0, 1.0);
            }
            """;
            const string fsTriFrag = """
            #version 460
            layout(location = 0) in vec2 texCoord;
            layout(set = 0, binding = 0) uniform sampler _MainSampler;
            layout(set = 0, binding = 1) uniform texture2D _MainTexture;
            layout(location = 0) out vec4 color;

            float Tonemap_ACES(float x)
            {
                // Narkowicz 2015, "ACES Filmic Tone Mapping Curve"
                const float a = 2.51;
                const float b = 0.03;
                const float c = 2.43;
                const float d = 0.59;
                const float e = 0.14;
                return (x * (a * x + b)) / (x * (c * x + d) + e);
            }
            // https://www.shadertoy.com/view/WltSRB
            // https://twitter.com/jimhejl/status/1137559578030354437
            vec3 ToneMapFilmicALU(vec3 x)
            {
                x *= 0.665;

                x = max(vec3(0.0), x);
                x = (x * (6.2 * x + 0.5)) / (x * (6.2 * x + 1.7) + 0.06);
                x = pow(x, vec3(2.2));// using gamma instead of sRGB_EOTF + without x - 0.004f looks about the same

                return x;
            }

            const mat3 ACESOutputMat = mat3
            (
                 1.60475, -0.53108, -0.07367,
                -0.10208,  1.10813, -0.00605,
                -0.00327, -0.07276,  1.07602
            );

            const mat3 RRT_SAT = mat3
            (
            	0.970889, 0.026963, 0.002148,
            	0.010889, 0.986963, 0.002148,
            	0.010889, 0.026963, 0.962148
            );

            vec3 ToneTF2(vec3 x)
            {
                vec3 a = (x            + 0.0822192) * x;
                vec3 b = (x * 0.983521 + 0.5001330) * x + 0.274064;

                return a / b;
            }

            vec3 Tonemap_ACESFitted2(vec3 acescg)
            {
                vec3 color = acescg * RRT_SAT;

                color = ToneTF2(color); 

                color = color * ACESOutputMat;
                //color = ToneMapFilmicALU(color);

                return color;
            }

            void main()
            {
                vec3 rawColor = texture(sampler2D(_MainTexture, _MainSampler), texCoord).rgb;
                color = vec4(Tonemap_ACESFitted2(rawColor), 1.0);
                //color = vec4(Tonemap_ACES(rawColor.x),Tonemap_ACES(rawColor.y),Tonemap_ACES(rawColor.z), 1.0);
                //color = vec4(rawColor, 1.0);
            }
            """;

            var vertShaderResult = SpirvCompiler.GetSpirvBytes("fsTriVert.vert");
            var vertShader = rf.CreateShader(new ShaderDescription(ShaderStages.Vertex, vertShaderResult, "main"));
            var fragResult = SpirvCompiler.GetSpirvBytes("fsTriFrag.frag");
            var fragShader = rf.CreateShader(new ShaderDescription(ShaderStages.Fragment, fragResult, "main"));

            //Create 
            displayBufferLayout = rf.CreateResourceLayout(new ResourceLayoutDescription(
                  new ResourceLayoutElementDescription("_MainSampler", ResourceKind.Sampler, ShaderStages.Fragment),
                  new ResourceLayoutElementDescription("_MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment))
                  );

            CreateTextures(game, rf);

            var fsTriShaderSet = new ShaderSetDescription(null, [vertShader, fragShader]);
            displayPipline = rf.CreateGraphicsPipeline(new GraphicsPipelineDescription(BlendStateDescription.SINGLE_OVERRIDE_BLEND,
                DepthStencilStateDescription.DISABLED, RasterizerStateDescription.CULL_NONE,
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

        private void CreateTextures(GameWindow window, ResourceFactory rf)
        {
            mainTex = rf.CreateTexture(new TextureDescription((uint)window.FramebufferSize.X / pixelSize, (uint)window.FramebufferSize.Y / pixelSize, 1, 1, 1, PixelFormat.R16G16B16A16Float, TextureUsage.Sampled | TextureUsage.Storage, TextureType.Texture2D));
            //depthTex = rf.CreateTexture(new TextureDescription((uint)window.Window.Width, (uint)window.Window.Height, 1, 1, 1, PixelFormat.R32_Float, TextureUsage.Sampled | TextureUsage.Storage, TextureType.Texture2D));
            renderSet = rf.CreateResourceSet(new ResourceSetDescription(renderBufferLayout, mainTex));
            displaySet = rf.CreateResourceSet(new ResourceSetDescription(displayBufferLayout, gd.PointSampler, mainTex));
        }

        public void Resize()
        {
            Camera.AspectRatio = window.FramebufferSize.X / (float)window.FramebufferSize.Y;
            //window.GraphicsContext.DisposeNextFrame(renderSet);
            //window.GraphicsContext.DisposeNextFrame(displaySet);
            //window.GraphicsContext.DisposeNextFrame(mainTex);
            //window.GraphicsContext.DisposeNextFrame(depthTex);
            CreateTextures(window, gd.ResourceFactory);
        }

        public void Update()
        {
            Camera.Update(window.DeltaTime);
            //if (window.Input.GetKeyDown(Key.K))
            //{
            //    Console.WriteLine(Camera.Transform.Position);
            //}

            var p = Camera.PerspectiveMatrix;
            Matrix4x4.Invert(p, out var pInv);

            cl.Begin();
            cl.UpdateBuffer(cameraBuffer, 0, new CameraProperties(Camera.Transform.WorldMatrix, pInv));
            cl.End();
            gd.SubmitCommands(cl);
        }

        public void Render()
        {
            cl.Begin();
            cl.SetPipeline(raytracePipeline);
            cl.SetComputeResourceSet(0, cameraSet);
            cl.SetComputeResourceSet(1, voxelSet);
            cl.SetComputeResourceSet(2, renderSet);
            uint worksizeX = BitOperations.RoundUpToPowerOf2((uint)window.FramebufferSize.X) / pixelSize;
            uint worksizeY = BitOperations.RoundUpToPowerOf2((uint)window.FramebufferSize.Y) / pixelSize;
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
