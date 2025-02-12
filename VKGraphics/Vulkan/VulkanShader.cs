using OpenTK.Graphics.Vulkan;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal sealed class VulkanShader : Shader, IResourceRefCountTarget
{
    private readonly VulkanGraphicsDevice _gd;
    private readonly VkShaderModule _shaderModule;
    private string? _name;

    public ResourceRefCount RefCount { get; }
    public override bool IsDisposed => RefCount.IsDisposed;

    public VkShaderModule ShaderModule => _shaderModule;

    public VulkanShader(VulkanGraphicsDevice gd, in ShaderDescription description, VkShaderModule module)
        : base(description.Stage, description.EntryPoint)
    {
        _gd = gd;
        _shaderModule = module;

        RefCount = new(this);
    }

    public override void Dispose() => RefCount?.DecrementDispose();
    unsafe void IResourceRefCountTarget.RefZeroed()
    {
        DestroyShaderModule(_gd.Device, _shaderModule, null);
    }

    public override string? Name
    {
        get => _name;
        set
        {
            _name = value;
            _gd.SetDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeShaderModuleExt, _shaderModule.Handle, value);
        }
    }
}
