using OpenTK.Graphics.Vulkan;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal sealed class VulkanPipeline : Pipeline, IResourceRefCountTarget
{
    private readonly VulkanGraphicsDevice _gd;
    private readonly VkPipeline _devicePipeline;
    private readonly VkPipelineLayout _pipelineLayout;
    private string? _name;

    public VkPipeline DevicePipeline => _devicePipeline;
    public VkPipelineLayout PipelineLayout => _pipelineLayout;
    public uint ResourceSetCount { get; }
    public int DynamicOffsetsCount { get; }
    public uint VertexLayoutCount { get; }
    public override bool IsComputePipeline { get; }

    public ResourceRefCount RefCount { get; }
    public sealed override bool IsDisposed => RefCount.IsDisposed;

    public VulkanPipeline(VulkanGraphicsDevice device, in GraphicsPipelineDescription description, ref VkPipeline pipeline, ref VkPipelineLayout layout) : base(description)
    {
        _gd = device;
        _devicePipeline = pipeline;
        _pipelineLayout = layout;

        pipeline = default;
        layout = default;

        RefCount = new(this);

        IsComputePipeline = false;
        ResourceSetCount = (uint)description.ResourceLayouts.Length;
        DynamicOffsetsCount = 0;
        foreach (var resLayout in description.ResourceLayouts)
        {
            DynamicOffsetsCount += Util.AssertSubtype<ResourceLayout, VulkanResourceLayout>(resLayout).DynamicBufferCount;
        }
        VertexLayoutCount = (uint)description.ShaderSet.VertexLayouts.AsSpan().Length;
    }

    public VulkanPipeline(VulkanGraphicsDevice device, in ComputePipelineDescription description, ref VkPipeline pipeline, ref VkPipelineLayout layout) : base(description)
    {
        _gd = device;
        _devicePipeline = pipeline;
        _pipelineLayout = layout;

        pipeline = default;
        layout = default;

        RefCount = new(this);

        IsComputePipeline = true;
        ResourceSetCount = (uint)description.ResourceLayouts.Length;
        DynamicOffsetsCount = 0;
        foreach (var resLayout in description.ResourceLayouts)
        {
            DynamicOffsetsCount += Util.AssertSubtype<ResourceLayout, VulkanResourceLayout>(resLayout).DynamicBufferCount;
        }
    }

    public sealed override void Dispose() => RefCount?.DecrementDispose();

    unsafe void IResourceRefCountTarget.RefZeroed()
    {
        DestroyPipeline(_gd.Device, _devicePipeline, null);
        DestroyPipelineLayout(_gd.Device, _pipelineLayout, null);
    }

    public override string? Name
    {
        get => _name;
        set
        {
            _name = value;
            _gd.SetDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypePipelineExt, _devicePipeline.Handle, value);
            _gd.SetDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypePipelineLayoutExt, _pipelineLayout.Handle, value + " (Pipeline Layout)");
        }
    }
}
