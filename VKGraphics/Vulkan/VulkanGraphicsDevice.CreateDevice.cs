
using OpenTK.Graphics.Vulkan;
using OpenTK.Platform;
using System.Diagnostics;
using static OpenTK.Graphics.Vulkan.Vk;
using StackListNI = VKGraphics.Vulkan.StackList<System.IntPtr>;

namespace VKGraphics.Vulkan;

internal unsafe partial class VulkanGraphicsDevice
{
    /// <summary>
    /// DeviceCreateState holds mutable intermediate state that will be (ultimately) used to construct the final VkGraphicsDevice, without
    /// having to pass around a huge mess of parameters.
    /// </summary>
    internal struct DeviceCreateState()
    {
        // Inputs
        public GraphicsDeviceOptions GdOptions;
        public VulkanDeviceOptions VkOptions;

        // Managed Handles
        public VkInstance Instance;
        public VkDebugReportCallbackEXT DebugCallbackHandle;
        public VkSurfaceKHR Surface;
        public VkDevice Device;

        // VkInstance extra information
        public VkVersion ApiVersion;
        public bool HasDeviceProperties2Ext;
        public bool HasDebugReportExt;
        public bool HasStdValidationLayer;
        public bool HasKhrValidationLayer;

        // Physical device information
        public VkPhysicalDevice PhysicalDevice;
        public VkPhysicalDeviceProperties PhysicalDeviceProperties;
        public VkPhysicalDeviceFeatures2 PhysicalDeviceFeatures2 = new();
        public VkPhysicalDeviceMemoryProperties PhysicalDeviceMemoryProperties;
        public QueueFamilyProperties QueueFamilyInfo = new();

        // VkDevice auxiliary information
        public VkQueue MainQueue;
        public bool HasDebugMarkerExt;
        public bool HasMaintenance1Ext;
        public bool HasMemReqs2Ext;
        public bool HasDedicatedAllocationExt;
        public bool HasDriverPropertiesExt;
        public bool HasDynamicRendering;
        public bool HasSync2Ext;
        public bool HasFifoLatestReady;
    }

    public static VulkanGraphicsDevice CreateDevice(GraphicsDeviceOptions gdOpts, VulkanDeviceOptions vkOpts, SwapchainDescription? swapchainDesc)
    {
        var dcs = new DeviceCreateState()
        {
            GdOptions = gdOpts,
            VkOptions = vkOpts,
        };

        try
        {
            dcs.Instance = CreateInstance(ref dcs, out var surfaceExtensionList);

            if (swapchainDesc is { } swdesc)
            {
                VulkanUtil.CheckResult(Toolkit.Vulkan.CreateWindowSurface(dcs.Instance, swdesc.Source, null, out dcs.Surface));
            }

            dcs.PhysicalDevice = SelectPhysicalDevice(dcs.Instance, out dcs.PhysicalDeviceProperties);
            VkPhysicalDeviceShaderSubgroupExtendedTypesFeatures subgroupFeatures = new();
            VkPhysicalDeviceShaderAtomicInt64Features intAtomic64 = new();
            VkPhysicalDeviceSynchronization2Features sync2 = new();
            VkPhysicalDeviceDynamicRenderingFeatures dynamicRender = new();
            VkPhysicalDeviceVulkan11Features vulkan11Features = new();
            dcs.PhysicalDeviceFeatures2.pNext = &subgroupFeatures;
            subgroupFeatures.pNext = &sync2;
            sync2.pNext = &dynamicRender;
            dynamicRender.pNext = &intAtomic64;
            intAtomic64.pNext = &vulkan11Features;
            GetPhysicalDeviceFeatures2(dcs.PhysicalDevice, &dcs.PhysicalDeviceFeatures2);
            GetPhysicalDeviceMemoryProperties(dcs.PhysicalDevice, &dcs.PhysicalDeviceMemoryProperties);
            
            dcs.QueueFamilyInfo = IdentifyQueueFamilies(dcs.PhysicalDevice, dcs.Surface);
            dcs.Device = CreateLogicalDevice(ref dcs);

            return new VulkanGraphicsDevice(ref dcs, swapchainDesc, surfaceExtensionList);
        }
        finally
        {
            // if we reach here with non-Zero locals, then an error occurred and we should be good API users and clean up

            if (dcs.Device != VkDevice.Zero)
            {
                DestroyDevice(dcs.Device, null);
            }

            if (dcs.Surface != VkSurfaceKHR.Zero)
            {
                DestroySurfaceKHR(dcs.Instance, dcs.Surface, null);
            }

            if (dcs.DebugCallbackHandle != VkDebugReportCallbackEXT.Zero)
            {
                var DestroyDebugReportCallbackEXT =
                    (delegate* unmanaged<VkInstance, VkDebugReportCallbackEXT, VkAllocationCallbacks*, void>)
                    GetInstanceProcAddr(dcs.Instance, "vkDestroyDebugReportCallbackEXT"u8);
                DestroyDebugReportCallbackEXT(dcs.Instance, dcs.DebugCallbackHandle, null);
            }

            if (dcs.Instance != VkInstance.Zero)
            {
                DestroyInstance(dcs.Instance, null);
            }
        }
    }

    private static ReadOnlySpan<byte> PhysicalDeviceTypePreference => [
            (byte)VkPhysicalDeviceType.PhysicalDeviceTypeDiscreteGpu,
            (byte)VkPhysicalDeviceType.PhysicalDeviceTypeVirtualGpu,
            (byte)VkPhysicalDeviceType.PhysicalDeviceTypeOther,
            (byte)VkPhysicalDeviceType.PhysicalDeviceTypeIntegratedGpu,
            (byte)VkPhysicalDeviceType.PhysicalDeviceTypeCpu,
        ];

    // TODO: maybe somehow expose device selection?
    private static VkPhysicalDevice SelectPhysicalDevice(VkInstance instance, out VkPhysicalDeviceProperties deviceProps)
    {
        uint deviceCount;
        VulkanUtil.CheckResult(EnumeratePhysicalDevices(instance, &deviceCount, null));
        if (deviceCount is 0)
        {
            throw new InvalidOperationException("No physical devices exist.");
        }

        var devices = new VkPhysicalDevice[deviceCount];
        fixed (VkPhysicalDevice* ptr = devices)
        {
            VulkanUtil.CheckResult(EnumeratePhysicalDevices(instance, &deviceCount, ptr));
        }

        VkPhysicalDevice selectedDevice = default;
        VkPhysicalDeviceProperties selectedDeviceProps = default;
        var selectedPreferenceIndex = uint.MaxValue;

        for (var i = 0; i < deviceCount; i++)
        {
            var device = devices[i];

            VkPhysicalDeviceProperties props;
            GetPhysicalDeviceProperties(device, &props);

            // we want to prefer items earlier in our list, breaking ties by the physical device order
            var preferenceIndex = (uint)PhysicalDeviceTypePreference.IndexOf((byte)props.deviceType);
            if (preferenceIndex < selectedPreferenceIndex)
            {
                selectedDevice = device;
                selectedDeviceProps = props;
                selectedPreferenceIndex = preferenceIndex;
            }
        }

        if (selectedDevice == VkPhysicalDevice.Zero)
        {
            throw new InvalidOperationException($"Physical device selection failed to select any of {deviceCount} devices");
        }

        // now that we've identified, we can return
        deviceProps = selectedDeviceProps;
        return selectedDevice;
    }

    internal struct QueueFamilyProperties()
    {
        public int MainGraphicsFamilyIdx = -1;
        public int PresentFamilyIdx = -1;
        public int MainComputeFamilyIdx = -1;
        //public int AsyncComputeFamilyIdx = -1;
    }

    private static QueueFamilyProperties IdentifyQueueFamilies(VkPhysicalDevice device, VkSurfaceKHR optSurface)
    {
        var result = new QueueFamilyProperties();

        uint familyCount = 0;
        GetPhysicalDeviceQueueFamilyProperties(device, &familyCount, null);
        var families = new VkQueueFamilyProperties[familyCount];
        fixed (VkQueueFamilyProperties* pFamilies = families)
        {
            GetPhysicalDeviceQueueFamilyProperties(device, &familyCount, pFamilies);
        }

        for (var i = 0; i < familyCount; i++)
        {
            ref var props = ref families[i];
            if (props.queueCount <= 0) continue;

            var used = false;

            if (result.MainGraphicsFamilyIdx < 0 && (props.queueFlags & VkQueueFlagBits.QueueGraphicsBit) != 0)
            {
                used = true;
                result.MainGraphicsFamilyIdx = i;
            }

            if (result.MainComputeFamilyIdx < 0 && (props.queueFlags & VkQueueFlagBits.QueueComputeBit) != 0)
            {
                used = true;
                result.MainComputeFamilyIdx = i;
            }

            // TODO: how can we identify a valid presentation queue when we're started with no target surface?

            // we only care about present queues which are ALSO main queues
            if (used && result.PresentFamilyIdx < 0 && optSurface != VkSurfaceKHR.Zero)
            {
                int presentSupported;
                var vkr = GetPhysicalDeviceSurfaceSupportKHR(device, (uint)i, optSurface, &presentSupported);
                if (vkr is VkResult.Success && presentSupported != 0)
                {
                    result.PresentFamilyIdx = i;
                }
            }

            if (used)
            {
                // mark this queue as having been used once
                props.queueCount--;
            }

            // TODO: identify an async compute family

            // check for early exit (all relevant family indicies have been found
            if (result.MainGraphicsFamilyIdx >= 0 && result.MainComputeFamilyIdx >= 0)
            {
                if (optSurface == VkSurfaceKHR.Zero || result.PresentFamilyIdx >= 0)
                {
                    // we have everything we need
                    break;
                }
            }
        }

        // note: at the moment we only actually support outputting to one queue
        Debug.Assert(result.MainGraphicsFamilyIdx >= 0);
        Debug.Assert(result.PresentFamilyIdx is -1 || result.MainGraphicsFamilyIdx == result.PresentFamilyIdx);
        Debug.Assert(result.MainComputeFamilyIdx is -1 || result.MainGraphicsFamilyIdx == result.MainComputeFamilyIdx);

        return result;
    }

    private static VkDevice CreateLogicalDevice(ref DeviceCreateState dcs)
    {
        // note: at the moment we only actually support outputting to one queue
        Debug.Assert(dcs.QueueFamilyInfo.MainGraphicsFamilyIdx >= 0);
        Debug.Assert(dcs.QueueFamilyInfo.PresentFamilyIdx is -1 || dcs.QueueFamilyInfo.MainGraphicsFamilyIdx == dcs.QueueFamilyInfo.PresentFamilyIdx);
        Debug.Assert(dcs.QueueFamilyInfo.MainComputeFamilyIdx is -1 || dcs.QueueFamilyInfo.MainGraphicsFamilyIdx == dcs.QueueFamilyInfo.MainComputeFamilyIdx);
        // IF ANY OF THE ABOVE CONDITIONS CHANGE, AND WE BEGIN TO CREATE MULTIPLE QUEUES, GetDeviceQueue BELOW MUST ALSO CHANGE
        // THERE ARE OTHER PLACES AROUND THE CODEBASE WHICH MUST ALSO CHANGE, INCLUDING VulkanSwapchain AND THE SYNCHRONIZATION CODE

        var queuePriority = 1f;
        var queueCreateInfo = new VkDeviceQueueCreateInfo()
        {
            queueFamilyIndex = (uint)dcs.QueueFamilyInfo.MainGraphicsFamilyIdx,
            queueCount = 1,
            pQueuePriorities = &queuePriority,
        };

        var requiredDeviceExtensions = new HashSet<string>(dcs.VkOptions.DeviceExtensions ?? []);

        uint numDeviceExtensions = 0;
        VulkanUtil.CheckResult(EnumerateDeviceExtensionProperties(dcs.PhysicalDevice, null, &numDeviceExtensions, null));
        var extensionProps = new VkExtensionProperties[numDeviceExtensions];
        var activeExtensions = new nint[numDeviceExtensions];
        uint activeExtensionCount = 0;

        VkDevice device;
        fixed (VkExtensionProperties* pExtensionProps = extensionProps)
        fixed (nint* pActiveExtensions = activeExtensions)
        {
            VulkanUtil.CheckResult(EnumerateDeviceExtensionProperties(dcs.PhysicalDevice, null, &numDeviceExtensions, pExtensionProps));

            // TODO: all of these version-gated options are technically conditional on a physical device feature. We should be using that instead.
            dcs.HasMemReqs2Ext = dcs.ApiVersion >= new VkVersion(1, 1, 0);
            dcs.HasMaintenance1Ext = dcs.ApiVersion >= new VkVersion(1, 1, 0);
            dcs.HasDedicatedAllocationExt = dcs.ApiVersion >= new VkVersion(1, 1, 0);
            dcs.HasDriverPropertiesExt = dcs.ApiVersion >= new VkVersion(1, 2, 0);
            dcs.HasDebugMarkerExt = false;
            dcs.HasDynamicRendering = dcs.ApiVersion >= new VkVersion(1, 3, 0);
            dcs.HasSync2Ext = dcs.ApiVersion >= new VkVersion(1, 3, 0);

            for (var i = 0; i < numDeviceExtensions; i++)
            {
                var name = Util.GetString(pExtensionProps[i].extensionName);
                switch (name)
                {
                    case "VK_EXT_debug_marker":
                        //case "VK_EXT_debug_utils":
                        dcs.HasDebugMarkerExt = true;
                        goto EnableExtension;

                    case "VK_KHR_swapchain":
                    case "VK_KHR_portability_subset":
                        goto EnableExtension;

                    case "VK_EXT_present_mode_fifo_latest_ready":
                        dcs.HasFifoLatestReady = true;
                        goto EnableExtension;

                    case "VK_KHR_maintenance1":
                        dcs.HasMaintenance1Ext = true;
                        goto EnableExtension;
                    case "VK_KHR_get_memory_requirements2":
                        dcs.HasMemReqs2Ext = true;
                        goto EnableExtension;
                    case "VK_KHR_dedicated_allocation":
                        dcs.HasDedicatedAllocationExt = true;
                        goto EnableExtension;
                    case "VK_KHR_driver_properties":
                        dcs.HasDriverPropertiesExt = true;
                        goto EnableExtension;

                    case "VK_KHR_dynamic_rendering":
                        dcs.HasDynamicRendering = true;
                        goto EnableExtension;
                    case "VK_KHR_synchronization2":
                        dcs.HasSync2Ext = true;
                        goto EnableExtension;

                    default:
                        if (requiredDeviceExtensions.Remove(name))
                        {
                            goto EnableExtension;
                        }
                        else
                        {
                            continue;
                        }

                    EnableExtension:
                        _ = requiredDeviceExtensions.Remove(name);
                        pActiveExtensions[activeExtensionCount++] = (nint)(&pExtensionProps[i].extensionName);
                        break;
                }
            }

            if (requiredDeviceExtensions.Count != 0)
            {
                var missingList = string.Join(", ", requiredDeviceExtensions);
                throw new VeldridException(
                    $"The following Vulkan device extensions were not available: {missingList}");
            }

            StackListNI layerNames = new();
            if (dcs.HasStdValidationLayer)
            {
                layerNames.Add(CommonStrings.StandardValidationLayerName);
            }
            if (dcs.HasKhrValidationLayer)
            {
                layerNames.Add(CommonStrings.KhronosValidationLayerName);
            }

            fixed (VkPhysicalDeviceFeatures2* pPhysicalDeviceFeatures = &dcs.PhysicalDeviceFeatures2)
            {
                var deviceCreateInfo = new VkDeviceCreateInfo()
                {
                    queueCreateInfoCount = 1,
                    pQueueCreateInfos = &queueCreateInfo,

                    enabledLayerCount = layerNames.Count,
                    ppEnabledLayerNames = (byte**)layerNames.Data,

                    enabledExtensionCount = activeExtensionCount,
                    ppEnabledExtensionNames = (byte**)pActiveExtensions,
                    pEnabledFeatures = null,
                    pNext = pPhysicalDeviceFeatures,
                };

                if (dcs.HasDynamicRendering)
                {
                    // make sure we enable dynamic rendering
                    var dynamicRenderingFeatures = new VkPhysicalDeviceDynamicRenderingFeatures()
                    {
                        pNext = deviceCreateInfo.pNext,
                        dynamicRendering = (VkBool32)true,
                    };

                    deviceCreateInfo.pNext = &dynamicRenderingFeatures;
                }

                if (dcs.HasSync2Ext)
                {
                    // make sure we enable synchronization2
                    var sync2Features = new VkPhysicalDeviceSynchronization2Features()
                    {
                        pNext = deviceCreateInfo.pNext,
                        synchronization2 = (VkBool32)true,
                    };

                    deviceCreateInfo.pNext = &sync2Features;
                }

                if (dcs.HasFifoLatestReady)
                {
                    var fifoLatestReady = new VkPhysicalDevicePresentModeFifoLatestReadyFeaturesEXT()
                    {
                        pNext = deviceCreateInfo.pNext,
                        presentModeFifoLatestReady = (VkBool32)true,
                    };
                    deviceCreateInfo.pNext = &fifoLatestReady;
                }

                VulkanUtil.CheckResult(Vk.CreateDevice(dcs.PhysicalDevice, &deviceCreateInfo, null, &device));
            }

            VkQueue localQueue;
            GetDeviceQueue(device, (uint)dcs.QueueFamilyInfo.MainGraphicsFamilyIdx, 0, &localQueue);
            dcs.MainQueue = localQueue;

            return device;
        }
    }
}
