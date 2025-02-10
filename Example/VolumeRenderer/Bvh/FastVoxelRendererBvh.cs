//global using VKGraphics;
//using System.Numerics;
//using System.Runtime.CompilerServices;
//using System.Runtime.InteropServices;
//using BrickEngine.Example.DataStructures.Bvh;
//using BrickEngine.Example.VolumeRenderer.Data;

//namespace BrickEngine.Example.VoxelRenderer.Bvh
//{
//    sealed class FastVoxelRendererBvh
//    {
//        readonly GameWindow window;
//        readonly CommandList cl;

//        //camera
//        readonly DeviceBuffer cameraBuffer;
//        readonly ResourceSet cameraSet;

//        //Raytracing
//        readonly Pipeline raytracePipeline;
//        readonly ResourceLayout voxelLayout;
//        ResourceSet voxelSet;

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


//        public unsafe FastVoxelRendererBvh(GameWindow window, long voxelCount, float[] voxelData, Box3 volBox, List<SourceRegion> regions)
//        {
//            Camera = new();
//            this.window = window;
//            ResourceFactory rf = window.GraphicsContext.ResourceFactory;
//            var gd = window.GraphicsContext.GraphicsDevice;


//            BoundingBox[] boundingBoxes = new BoundingBox[regions.Count];
//            Vector3[] centers = new Vector3[regions.Count];

//            for (int i = 0; i < regions.Count; i++)
//            {
//                var region = regions[i];
//                boundingBoxes[i] = new BoundingBox(region.SourceDimensions.Min, region.SourceDimensions.Max);
//                centers[i] = (region.SourceDimensions.Min + region.SourceDimensions.Max).ToVector3() / 2f;
//            }

//            var bvh = BVHBuilder.Build(boundingBoxes, centers, regions.Count, new BuildConfig(1, 1, 1f));





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
//                    new ResourceLayoutElementDescription("VoxDataBuf", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
//                    new ResourceLayoutElementDescription("VoxGridInfo", ResourceKind.UniformBuffer, ShaderStages.Compute),
//                    new ResourceLayoutElementDescription("VoxSourceBuf", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
//                    new ResourceLayoutElementDescription("BVHNodeBuf", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute),
//                    new ResourceLayoutElementDescription("BVHIndexBuf", ResourceKind.StructuredBufferReadOnly, ShaderStages.Compute)
//                ));
//            var voxDataBuf = rf.CreateBuffer(new BufferDescription(sizeof(float) * (uint)voxelData.Length, BufferUsage.StructuredBufferReadOnly));
//            gd.UpdateBuffer(voxDataBuf, 0, voxelData);

//            var voxGridInfo = rf.CreateBuffer(new BufferDescription(GetUBOSize(sizeof(float) * 4 + sizeof(int) * 3), BufferUsage.UniformBuffer));
//            ReadOnlySpan<float> minMax = stackalloc float[2] { voxelData.Min(), voxelData.Max() };
//            gd.UpdateBuffer(voxGridInfo, 0, minMax);
//            gd.UpdateBuffer(voxGridInfo, sizeof(float) * 4, volBox.Size.ToVector3i());

//            var voxSourceBuf = rf.CreateBuffer(new BufferDescription((uint)(sizeof(SourceRegion) * regions.Count), BufferUsage.StructuredBufferReadOnly));
//            gd.UpdateBuffer(voxSourceBuf, 0, regions.ToArray());

//            var bvhNodeBuf = rf.CreateBuffer(new BufferDescription((uint)(sizeof(Node) * bvh.Nodes.Length), BufferUsage.StructuredBufferReadOnly));
//            gd.UpdateBuffer(bvhNodeBuf, 0, bvh.Nodes);

//            var BVHIndexBuf = rf.CreateBuffer(new BufferDescription((uint)(sizeof(int) * bvh.PrimitiveIndices.Length), BufferUsage.StructuredBufferReadOnly));
//            gd.UpdateBuffer(BVHIndexBuf, 0, bvh.PrimitiveIndices);

//            voxelSet = rf.CreateResourceSet(new ResourceSetDescription(voxelLayout, voxDataBuf, voxGridInfo, voxSourceBuf, bvhNodeBuf, BVHIndexBuf));

//            renderBufferLayout = rf.CreateResourceLayout(new ResourceLayoutDescription(
//                new ResourceLayoutElementDescription("screen", ResourceKind.TextureReadWrite, ShaderStages.Compute),
//                new ResourceLayoutElementDescription("depth", ResourceKind.TextureReadWrite, ShaderStages.Compute))
//                );

//            //Create shader
//            var shaderResult = SpirvCompilation.CompileGlslToSpirv(File.ReadAllText("VolumeRenderer/Bvh/FastVoxelBvh.comp"), "FastVoxelBvh.comp", ShaderStages.Compute, new GlslCompileOptions(true));
//            var shader = rf.CreateShader(new ShaderDescription(ShaderStages.Compute, shaderResult.SpirvBytes, "main", window.GameSettings.Debug));

//            //Create Pipeline
//            raytracePipeline = rf.CreateComputePipeline(new ComputePipelineDescription(shader, new ResourceLayout[] { cameraBufferLayout, voxelLayout, renderBufferLayout }, 8, 8, 1));


//            //-----------DISPLAY-------------
//            const string fsTriVert = """
//            #version 460
//            layout(location = 0) out vec2 texCoord;

//            void main()
//            {
//                float x = -1.0 + float((gl_VertexIndex & 1) << 2);
//                float y = -1.0 + float((gl_VertexIndex & 2) << 1);
//                texCoord.x = (x+1.0)*0.5;
//                texCoord.y = (y+1.0)*0.5;
//                gl_Position = vec4(x, y, 0, 1);
//            }
//            """;
//            const string fsTriFrag = """
//            #version 460
//            layout(location = 0) in vec2 texCoord;
//            layout(set = 0, binding = 0) uniform sampler _MainSampler;
//            layout(set = 0, binding = 1) uniform texture2D _MainTexture;
//            layout(location = 0) out vec4 color;

//            float Tonemap_ACES(float x)
//            {
//                // Narkowicz 2015, "ACES Filmic Tone Mapping Curve"
//                const float a = 2.51;
//                const float b = 0.03;
//                const float c = 2.43;
//                const float d = 0.59;
//                const float e = 0.14;
//                return (x * (a * x + b)) / (x * (c * x + d) + e);
//            }

//            void main()
//            {
//              vec3 rawColor = texture(sampler2D(_MainTexture, _MainSampler), texCoord).rgb;

//              //color = vec4(Tonemap_ACES(rawColor.x),Tonemap_ACES(rawColor.y),Tonemap_ACES(rawColor.z), 1.0);
//              color = vec4(rawColor, 1.0);
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

//            var fsTriShaderSet = new ShaderSetDescription(null, new Shader[] { vertShader, fragShader });
//            displayPipline = rf.CreateGraphicsPipeline(new GraphicsPipelineDescription(BlendStateDescription.SingleOverrideBlend, DepthStencilStateDescription.Disabled, RasterizerStateDescription.CullNone,
//                  PrimitiveTopology.TriangleList, fsTriShaderSet, displayBufferLayout, window.GraphicsContext.GraphicsDevice.MainSwapchain!.Framebuffer.OutputDescription));

//            cl = rf.CreateCommandList();
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
//            mainTex = rf.CreateTexture(new TextureDescription((uint)window.Window.Width, (uint)window.Window.Height, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float, TextureUsage.Sampled | TextureUsage.Storage, TextureType.Texture2D));
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
//            //window.Input.GetKey(Key.K);
//            //window.DeltaTime
//            Camera.Update(window.Input, window.DeltaTime);

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
//            cl.Dispatch(worksizeX / 8, worksizeY / 8, 1);
//            cl.SetPipeline(displayPipline);
//            cl.SetGraphicsResourceSet(0, displaySet);
//            cl.SetFramebuffer(window.GraphicsContext.GraphicsDevice.SwapchainFramebuffer!);
//            cl.SetFullViewport(0);
//            cl.ClearColorTarget(0, RgbaFloat.Blue);
//            cl.Draw(3);
//            cl.End();
//            window.GraphicsContext.SubmitCommands(cl);
//        }
//    }
//}
