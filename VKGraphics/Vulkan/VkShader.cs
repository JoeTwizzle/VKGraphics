using static VKGraphics.Vulkan.VulkanUtil;

namespace VKGraphics.Vulkan;

internal unsafe class VkShader : Shader
{
    public VkShaderModule ShaderModule => shaderModule;

    public override bool IsDisposed => disposed;

    public override string Name
    {
        get => name;
        set
        {
            name = value;
            gd.SetResourceName(this, value);
        }
    }

    private readonly VkGraphicsDevice gd;
    private readonly VkShaderModule shaderModule;
    private bool disposed;
    private string name;

    public VkShader(VkGraphicsDevice gd, ref ShaderDescription description)
        : base(description.Stage, description.EntryPoint)
    {
        this.gd = gd;

        var shaderModuleCi = new VkShaderModuleCreateInfo();

        fixed (byte* codePtr = description.ShaderBytes)
        {
            shaderModuleCi.codeSize = (UIntPtr)description.ShaderBytes.Length;
            shaderModuleCi.pCode = (uint*)codePtr;
            VkShaderModule vkShaderModule;
            var result = Vk.CreateShaderModule(gd.Device, &shaderModuleCi, null, &vkShaderModule);
            shaderModule = vkShaderModule;
            CheckResult(result);
        }
    }

    #region Disposal

    public override void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            Vk.DestroyShaderModule(gd.Device, ShaderModule, null);
        }
    }

    #endregion
}
