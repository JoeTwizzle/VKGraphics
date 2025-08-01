#if !EXCLUDE_VULKAN_BACKEND
using OpenTK.Graphics.Vulkan;
using System.Collections.ObjectModel;
using VKGraphics.Vulkan;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics;

/// <summary>
/// Exposes Vulkan-specific functionality,
/// useful for interoperating with native components which interface directly with Vulkan.
/// Can only be used on <see cref="GraphicsBackend.Vulkan"/>.
/// </summary>
public unsafe class BackendInfoVulkan
{
    private readonly VulkanGraphicsDevice _gd;
    private readonly ReadOnlyCollection<string> _instanceLayers;
    private readonly ReadOnlyCollection<string> _instanceExtensions;
    private readonly Lazy<ReadOnlyCollection<ExtensionProperties>> _deviceExtensions;

    internal BackendInfoVulkan(VulkanGraphicsDevice gd)
    {
        _gd = gd;
        _instanceLayers = new ReadOnlyCollection<string>(VulkanUtil.EnumerateInstanceLayers());
        _instanceExtensions = new ReadOnlyCollection<string>(VulkanUtil.EnumerateInstanceExtensions());
        _deviceExtensions = new Lazy<ReadOnlyCollection<ExtensionProperties>>(EnumerateDeviceExtensions);
    }

    /// <summary>
    /// Gets the underlying VkInstance used by the GraphicsDevice.
    /// </summary>
    public IntPtr Instance => (IntPtr)_gd._deviceCreateState.Instance.Handle;

    /// <summary>
    /// Gets the underlying VkDevice used by the GraphicsDevice.
    /// </summary>
    public IntPtr Device => (IntPtr)_gd.Device.Handle;

    /// <summary>
    /// Gets the underlying VkPhysicalDevice used by the GraphicsDevice.
    /// </summary>
    public IntPtr PhysicalDevice => (IntPtr)_gd._deviceCreateState.PhysicalDevice.Handle;

    /// <summary>
    /// Gets the VkQueue which is used by the GraphicsDevice to submit graphics work.
    /// </summary>
    public IntPtr GraphicsQueue => (IntPtr)_gd._deviceCreateState.MainQueue.Handle;

    /// <summary>
    /// Gets the queue family index of the graphics VkQueue.
    /// </summary>
    public uint GraphicsQueueFamilyIndex => (uint)_gd._deviceCreateState.QueueFamilyInfo.MainGraphicsFamilyIdx;

    /// <summary>
    /// Gets the driver name of the device. May be null.
    /// </summary>
    public string? DriverName => _gd.DriverName;

    /// <summary>
    /// Gets the driver information of the device. May be null.
    /// </summary>
    public string? DriverInfo => _gd.DriverInfo;

    public ReadOnlyCollection<string> AvailableInstanceLayers => _instanceLayers;

    public ReadOnlyCollection<string> AvailableInstanceExtensions => _instanceExtensions;

    public ReadOnlyCollection<ExtensionProperties> AvailableDeviceExtensions => _deviceExtensions.Value;

    /// <summary>
    /// Overrides the current VkImageLayout tracked by the given Texture. This should be used when a VkImage is created by
    /// an external library to inform Veldrid about its initial layout.
    /// </summary>
    /// <param name="texture">The Texture whose currently-tracked VkImageLayout will be overridden.</param>
    /// <param name="layout">The new VkImageLayout value.</param>
    public void OverrideImageLayout(Texture texture, uint layout)
    {
        var vkTex = Util.AssertSubtype<Texture, VulkanTexture>(texture);
        vkTex.AllSyncStates.Fill(new() { CurrentImageLayout = (VkImageLayout)layout });
    }

    /// <summary>
    /// Gets the underlying VkImage wrapped by the given Veldrid Texture. This method can not be used on Textures with
    /// TextureUsage.Staging.
    /// </summary>
    /// <param name="texture">The Texture whose underlying VkImage will be returned.</param>
    /// <returns>The underlying VkImage for the given Texture.</returns>
    public ulong GetVkImage(Texture texture)
    {
        var vkTexture = Util.AssertSubtype<Texture, VulkanTexture>(texture);
        if ((vkTexture.Usage & TextureUsage.Staging) != 0)
        {
            throw new VeldridException(
                $"{nameof(GetVkImage)} cannot be used if the {nameof(Texture)} " +
                $"has {nameof(TextureUsage)}.{nameof(TextureUsage.Staging)}.");
        }

        return vkTexture.DeviceImage.Handle;
    }

    /// <summary>
    /// Transitions the given Texture's underlying VkImage into a new layout.
    /// </summary>
    /// <param name="commandList">The command list to record the image transition into.</param>
    /// <param name="texture">The Texture whose underlying VkImage will be transitioned.</param>
    /// <param name="layout">The new VkImageLayout value.</param>
    public void TransitionImageLayout(CommandList commandList, Texture texture, uint layout)
    {
        var vkCL = Util.AssertSubtype<CommandList, VulkanCommandList>(commandList);
        var vkTex = Util.AssertSubtype<Texture, VulkanTexture>(texture);

        vkCL.SyncResource(vkTex, new()
        {
            Layout = (VkImageLayout)layout
        });
    }

    /// <inheritdoc cref="TransitionImageLayout(CommandList, Texture, uint)"/>
    [Obsolete("Prefer using the overload taking a CommandList for proper synchronization.")]
    public void TransitionImageLayout(Texture texture, uint layout)
    {
        var vkTex = Util.AssertSubtype<Texture, VulkanTexture>(texture);
        var cl = _gd.GetAndBeginCommandList();
        cl.SyncResource(vkTex, new()
        {
            Layout = (VkImageLayout)layout
        });
        var (_, fence) = _gd.EndAndSubmitCommands(cl);
        _ = WaitForFences(_gd.Device, 1, &fence, 1, ulong.MaxValue);
        _gd.CheckFencesForCompletion();
    }

    /// <summary>
    /// Gets <paramref name="swapchain"/>'s preference for using the <c>PRESENT_MODE_FIFO_LATEST_READY</c> presentation mode when in VSync.
    /// </summary>
    /// <param name="swapchain">The swapchain to query.</param>
    /// <returns><see langword="true"/> if the swapchain will use <c>PRESENT_MODE_FIFO_LATEST_READY</c>; <see langword="false"/> otherwise.</returns>
    public bool GetUseFifoLatestIfAvailable(Swapchain swapchain)
        => Util.AssertSubtype<Swapchain, VulkanSwapchain>(swapchain).UseFifoLatestIfAvailable;

    /// <summary>
    /// Sets <paramref name="swapchain"/>'s preference for using the <c>PRESENT_MODE_FIFO_LATEST_READY</c> presentation mode when in VSync.
    /// </summary>
    /// <param name="swapchain">The swapchain to query.</param>
    /// <param name="value"><see langword="true"/> if the swapchain should use <c>PRESENT_MODE_FIFO_LATEST_READY</c> when VSync is enabled; <see langword="false"/> otherwise.</param>
    public void SetUseFifoLatestIfAvailable(Swapchain swapchain, bool value)
        => Util.AssertSubtype<Swapchain, VulkanSwapchain>(swapchain).UseFifoLatestIfAvailable = value;

    /// <summary>
    /// Gets the <see cref="VkFormat"/> of the image associated with <paramref name="texture"/>.
    /// </summary>
    /// <param name="texture">The texture to get the format of.</param>
    /// <returns>The <see cref="VkFormat"/> the image associated with <paramref name="texture"/> has.</returns>
    public VkFormat GetVkFormat(Texture texture)
    {
        var vkTexture = Util.AssertSubtype<Texture, VulkanTexture>(texture);
        return vkTexture.VkFormat;
    }

    /// <summary>
    /// Instructs the backend to poll waiting fences for completion and invoke any callbacks, potentially cleaning up unused resources.
    /// </summary>
    /// <remarks>
    /// This may be valuable in compute-heavy workloads, or in scenarios where a lot of CPU processing
    /// is necessary per-frame, or where resources are constrained.
    /// </remarks>
    public void CheckForCommandListCompletions()
    {
        _gd.CheckFencesForCompletion();
    }

    private unsafe ReadOnlyCollection<ExtensionProperties> EnumerateDeviceExtensions()
    {
        uint propertyCount = 0;
        VulkanUtil.CheckResult(EnumerateDeviceExtensionProperties(_gd._deviceCreateState.PhysicalDevice, null, &propertyCount, null));
        VkExtensionProperties[] vkProps = new VkExtensionProperties[(int)propertyCount];
        fixed (VkExtensionProperties* properties = vkProps)
        {
            VulkanUtil.CheckResult(EnumerateDeviceExtensionProperties(_gd._deviceCreateState.PhysicalDevice, null, &propertyCount, properties));
        }

        ExtensionProperties[] veldridProps = new ExtensionProperties[vkProps.Length];

        for (int i = 0; i < vkProps.Length; i++)
        {
            VkExtensionProperties prop = vkProps[i];
            veldridProps[i] = new ExtensionProperties(Util.GetString(prop.extensionName), prop.specVersion);
        }

        return new ReadOnlyCollection<ExtensionProperties>(veldridProps);
    }

    public readonly struct ExtensionProperties
    {
        public readonly string Name;
        public readonly uint SpecVersion;

        public ExtensionProperties(string name, uint specVersion)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            SpecVersion = specVersion;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
#endif
