namespace VKGraphics.Vulkan;

internal interface IResourceRefCountTarget
{
    ResourceRefCount RefCount { get; }
    void RefZeroed();
}
