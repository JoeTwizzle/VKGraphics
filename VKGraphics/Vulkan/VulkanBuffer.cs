using OpenTK.Graphics.Vulkan;
using System.Diagnostics;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal unsafe sealed class VulkanBuffer : DeviceBuffer, IResourceRefCountTarget, ISynchronizedResource
{
    private readonly VulkanGraphicsDevice _gd;
    private readonly VkBuffer _buffer;
    private readonly VkMemoryBlock _memory;

    private SyncState _syncState;
    private string? _name;

    public ResourceRefCount RefCount { get; }
    public override bool IsDisposed => RefCount.IsDisposed;

    public Span<SyncState> AllSyncStates => new(ref _syncState);
    SyncSubresource ISynchronizedResource.SubresourceCounts => new(1, 1);
    ref SyncState ISynchronizedResource.SyncStateForSubresource(SyncSubresource subresource)
    {
        Debug.Assert(subresource == default);
        return ref _syncState;
    }

    public VkBuffer DeviceBuffer => _buffer;
    public ref readonly VkMemoryBlock Memory => ref _memory;

    internal VulkanBuffer(VulkanGraphicsDevice gd, in BufferDescription bd, VkBuffer buffer, VkMemoryBlock memory) : base(bd)
    {
        _gd = gd;
        _buffer = buffer;
        _memory = memory;
        RefCount = new(this);
    }

    public override void Dispose() => RefCount?.DecrementDispose();

    void IResourceRefCountTarget.RefZeroed()
    {
        DestroyBuffer(_gd.Device, _buffer, null);
        _gd.MemoryManager.Free(_memory);
    }

    public override string? Name
    {
        get => _name;
        set
        {
            _name = value;
            _gd.SetDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeBufferExt, _buffer.Handle, value);
        }
    }
}
