//using BrickEngine.Example.VolumeRenderer;
//using System.Numerics;
//using System.Runtime.CompilerServices;
//using System.Runtime.InteropServices;

//namespace BrickEngine.Example.VoxelRenderer.Svo
//{
//    sealed class SparseVoxelOctreeRenderer : IVolumeRenderer
//    {
//        private const int ThreadGroupSizeX = 8;
//        private const int ThreadGroupSizeY = 8;
//        readonly GameWindow window;
//        readonly CommandList cl;

//        //camera
//        readonly DeviceBuffer cameraBuffer;
//        readonly ResourceSet cameraSet;

//        //Raytracing
//        readonly Pipeline raytracePipeline;
//        readonly ResourceLayout voxelLayout;
//        readonly ResourceSet voxelSet;

//        //Textures
//        Texture mainTex;
//        Texture depthTex;
//        //Render Target
//        readonly ResourceLayout renderBufferLayout;
//        ResourceSet renderSet;
//        //Blit
//        readonly ResourceLayout displayBufferLayout;
//        ResourceSet displaySet;
//        readonly Pipeline displayPipline;
//        public Camera Camera { get; }

//        static uint GetUBOSize(int size)
//        {
//            return (uint)(((size - 1) / 16 + 1) * 16);
//        }

//        public unsafe SparseVoxelOctreeRenderer(GameWindow window, SparseVoxelOctree treeData, FilteredVolumeInfo info, bool compression)
//        {
//            Camera = new();
//            this.window = window;
//            ResourceFactory rf = window.GraphicsContext.ResourceFactory;
//            var gd = window.GraphicsContext.GraphicsDevice;

//            //Create Camera buffer
//            Camera.AspectRatio = window.Window.Width / (float)window.Window.Height;
//            cameraBuffer = rf.CreateBuffer(new BufferDescription(GetUBOSize(Unsafe.SizeOf<CameraProperties>()), BufferUsage.UniformBuffer, 0));
//            var p = Camera.ProjectionMatrix;
//            Matrix4x4.Invert(p, out var pInv);
//            gd.UpdateBuffer(cameraBuffer, 0, new CameraProperties(Camera.Transform.WorldMatrix, pInv));

//            //Create resource layouts
//            var cameraBufferLayout = rf.CreateResourceLayout(new ResourceLayoutDescription(
//                new ResourceLayoutElementDescription("_CameraProperites", ResourceKind.UniformBuffer, ShaderStages.Compute))
//                );
//            cameraSet = rf.CreateResourceSet(new ResourceSetDescription(cameraBufferLayout, cameraBuffer));

//            // voxel layout
//            voxelLayout = rf.CreateResourceLayout(new ResourceLayoutDescription(
//                    new ResourceLayoutElementDescription("VoxGridInfo", ResourceKind.UniformBuffer, ShaderStages.Compute),
//                    new ResourceLayoutElementDescription("VoxDataBuf", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
//                    new ResourceLayoutElementDescription("VoxValueBuf", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute)
//                ));

//            var voxDataBuf = rf.CreateBuffer(new BufferDescription((uint)treeData.NodesCount * sizeof(int), BufferUsage.StructuredBufferReadOnly));
//            gd.UpdateBuffer(voxDataBuf, 0, (IntPtr)treeData.TreeInfo.Nodes, (uint)treeData.NodesCount * sizeof(int));
//            var voxValueBuf = rf.CreateBuffer(new BufferDescription((uint)treeData.VoxelCount * sizeof(float), BufferUsage.StructuredBufferReadOnly));
//            gd.UpdateBuffer(voxValueBuf, 0, (IntPtr)treeData.TreeInfo.Voxels, (uint)treeData.VoxelCount * sizeof(float));

//            int size = SparseVoxelOctreeBuilder.Pow2(info.Dimensions.Size.X);
//            int depth = BitOperations.Log2((uint)size);
//            Console.WriteLine(depth);
//            var voxGridInfo = rf.CreateBuffer(new BufferDescription(GetUBOSize(sizeof(int) * 4 + sizeof(int) * 4), BufferUsage.UniformBuffer));
//            ReadOnlySpan<float> minMax = [info.MinValue, info.MaxValue];
//            gd.UpdateBuffer(voxGridInfo, 0, minMax);
//            gd.UpdateBuffer(voxGridInfo, sizeof(int) * 2, size);
//            gd.UpdateBuffer(voxGridInfo, sizeof(int) * 3, depth);
//            gd.UpdateBuffer(voxGridInfo, sizeof(int) * 4, info.GlobalVolumeInfo.Dimensions.Size);

//            voxelSet = rf.CreateResourceSet(new ResourceSetDescription(voxelLayout, voxGridInfo, voxDataBuf, voxValueBuf));


//            renderBufferLayout = rf.CreateResourceLayout(new ResourceLayoutDescription(
//                new ResourceLayoutElementDescription("screen", ResourceKind.TextureReadWrite, ShaderStages.Compute),
//                new ResourceLayoutElementDescription("depth", ResourceKind.TextureReadWrite, ShaderStages.Compute))
//                );

//            //Create shader
//            //var shaderResult = SpirvCompilation.CompileGlslToSpirv(File.ReadAllText("VolumeRenderer\\Svo\\SparseVoxelOctreeBase.comp"), "SparseVoxelOctreeBase.comp", ShaderStages.Compute, new GlslCompileOptions(window.GameSettings.Debug));
//            var shaderResult = SpirvCompilation.CompileGlslToSpirv(File.ReadAllText("VolumeRenderer\\Svo\\SparseVoxelOctree.comp"), "SparseVoxelOctree.comp", ShaderStages.Compute, new GlslCompileOptions(window.GameSettings.Debug));
//            var shader = rf.CreateShader(new ShaderDescription(ShaderStages.Compute, shaderResult.SpirvBytes, "main", window.GameSettings.Debug));

//            //Create Pipeline
//            raytracePipeline = rf.CreateComputePipeline(new ComputePipelineDescription(shader, [cameraBufferLayout, voxelLayout, renderBufferLayout], ThreadGroupSizeX, ThreadGroupSizeY, 1, [new SpecializationConstant(0, compression)]));


//            //-----------DISPLAY-------------
//            const string fsTriVert = """
//            #version 460
//            layout(location = 0) out vec2 texCoord;

//            void main()
//            {
//                texCoord = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
//                vec2 xy = fma(texCoord, vec2(2.0) , vec2(-1.0));
//                gl_Position = vec4(xy, 0.0, 1.0);
//            }
//            """;
//            const string fsTriFrag = """
//            #version 460
//            layout(location = 0) in vec2 texCoord;
//            layout(location = 0) out vec4 color;
//            layout(set = 0, binding = 0) uniform sampler _MainSampler;
//            layout(set = 0, binding = 1) uniform texture2D _MainTexture;

//            void main()
//            {
//              color = vec4(texture(sampler2D(_MainTexture, _MainSampler), texCoord).rgb, 1.0);
//            }
//            """;

//            var vertShaderResult = SpirvCompilation.CompileGlslToSpirv(fsTriVert, "fsTriVert.glsl", ShaderStages.Vertex, GlslCompileOptions.Default);
//            var vertShader = rf.CreateShader(new ShaderDescription(ShaderStages.Vertex, vertShaderResult.SpirvBytes, "main"));
//            var fragResult = SpirvCompilation.CompileGlslToSpirv(fsTriFrag, "fsTriFrag.glsl", ShaderStages.Fragment, GlslCompileOptions.Default);
//            var fragShader = rf.CreateShader(new ShaderDescription(ShaderStages.Fragment, fragResult.SpirvBytes, "main"));

//            //Create 
//            displayBufferLayout = rf.CreateResourceLayout(new ResourceLayoutDescription(
//                  new ResourceLayoutElementDescription("_MainSampler", ResourceKind.Sampler, ShaderStages.Fragment),
//                  new ResourceLayoutElementDescription("_MainTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment))
//                  );

//            CreateTextures(window, rf);

//            var fsTriShaderSet = new ShaderSetDescription(null, [vertShader, fragShader]);
//            displayPipline = rf.CreateGraphicsPipeline(new GraphicsPipelineDescription(BlendStateDescription.SingleOverrideBlend, DepthStencilStateDescription.Disabled, RasterizerStateDescription.CullNone,
//                  PrimitiveTopology.TriangleList, fsTriShaderSet, displayBufferLayout, window.GraphicsContext.GraphicsDevice.MainSwapchain!.Framebuffer.OutputDescription));

//            cl = rf.CreateCommandList();

//            NativeMemory.Free(treeData.TreeInfo.Voxels);
//            NativeMemory.Free(treeData.TreeInfo.Nodes);
//        }


//        [StructLayout(LayoutKind.Sequential, Pack = 1)]
//        struct CameraProperties
//        {
//            public Matrix4x4 Model;
//            public Matrix4x4 CameraInverseProjection;

//            public CameraProperties(Matrix4x4 cameraToWorld, Matrix4x4 cameraInverseProjection)
//            {
//                Model = cameraToWorld;
//                CameraInverseProjection = cameraInverseProjection;
//            }
//        }

//        private void CreateTextures(GameWindow window, ResourceFactory rf)
//        {
//            mainTex = rf.CreateTexture(new TextureDescription((uint)window.Window.Width, (uint)window.Window.Height, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled | TextureUsage.Storage, TextureType.Texture2D));
//            depthTex = rf.CreateTexture(new TextureDescription((uint)window.Window.Width, (uint)window.Window.Height, 1, 1, 1, PixelFormat.R32_Float, TextureUsage.Sampled | TextureUsage.Storage, TextureType.Texture2D));
//            renderSet = rf.CreateResourceSet(new ResourceSetDescription(renderBufferLayout, mainTex, depthTex));
//            displaySet = rf.CreateResourceSet(new ResourceSetDescription(displayBufferLayout, window.GraphicsContext.GraphicsDevice.LinearSampler, mainTex));
//        }

//        public void Resize()
//        {
//            if (mainTex.Width != window.Window.Width || mainTex.Height != window.Window.Height)
//            {
//                Camera.AspectRatio = window.Window.Width / (float)window.Window.Height;
//                window.GraphicsContext.DisposeNextFrame(renderSet);
//                window.GraphicsContext.DisposeNextFrame(displaySet);
//                window.GraphicsContext.DisposeNextFrame(mainTex);
//                window.GraphicsContext.DisposeNextFrame(depthTex);
//                CreateTextures(window, window.GraphicsContext.ResourceFactory);
//            }
//        }

//        public void Update()
//        {
//            Camera.Update(window.Input, window.DeltaTime);
//            if (window.Input.GetKeyDown(Key.K))
//            {
//                Console.WriteLine(Camera.Transform.Position);
//            }

//            var p = Camera.PerspectiveMatrix;
//            Matrix4x4.Invert(p, out var pInv);
//            cl.Begin();
//            cl.UpdateBuffer(cameraBuffer, 0, new CameraProperties(Camera.Transform.WorldMatrix, pInv));
//            cl.End();
//            window.GraphicsContext.SubmitCommands(cl);
//        }

//        public void Render()
//        {

//            cl.Begin();
//            cl.SetPipeline(raytracePipeline);
//            cl.SetComputeResourceSet(0, cameraSet);
//            cl.SetComputeResourceSet(1, voxelSet);
//            cl.SetComputeResourceSet(2, renderSet);
//            uint worksizeX = BitOperations.RoundUpToPowerOf2((uint)window.Window.Width);
//            uint worksizeY = BitOperations.RoundUpToPowerOf2((uint)window.Window.Height);
//            cl.Dispatch(worksizeX / ThreadGroupSizeX, worksizeY / ThreadGroupSizeY, 1);
//            cl.SetPipeline(displayPipline);
//            cl.SetGraphicsResourceSet(0, displaySet);
//            cl.SetFramebuffer(window.GraphicsContext.GraphicsDevice.SwapchainFramebuffer!);
//            cl.SetFullViewport(0);
//            cl.Draw(3);
//            cl.End();
//            window.GraphicsContext.SubmitCommands(cl);
//        }

//        public void Dispose()
//        {
//            cl.Dispose();


//            displayPipline.Dispose();
//            displayBufferLayout.Dispose();
//            cameraBuffer.Dispose();
//            cameraSet.Dispose();

//            renderBufferLayout.Dispose();
//            displaySet.Dispose();
//            renderSet.Dispose();

//            raytracePipeline.Dispose();
//            voxelLayout.Dispose();
//            voxelSet.Dispose();

//            mainTex.Dispose();
//            depthTex.Dispose();
//        }
//    }
//}
