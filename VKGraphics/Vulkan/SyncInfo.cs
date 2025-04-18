﻿using OpenTK.Graphics.Vulkan;

namespace VKGraphics.Vulkan;

internal struct SyncBarrierMasks
{
    public VkAccessFlagBits AccessMask;
    public VkPipelineStageFlagBits StageMask;
}

internal struct SyncRequest
{
    public SyncBarrierMasks BarrierMasks;
    // requested layout (for images only)
    public VkImageLayout Layout;
}

internal struct SyncState
{
    public SyncBarrierMasks LastWriter;
    public SyncBarrierMasks OngoingReaders;
    // Bitfield marking which accesses in which shader stages stages have seen the last write
    public uint PerStageReaders; // TODO: turn this into a concrete layout
    public VkImageLayout CurrentImageLayout;
}

internal struct SubresourceSyncInfo()
{
    public SyncRequest Expected;
    public SyncState LocalState;
    public bool HasBarrier;
}

internal record struct ResourceBarrierInfo
{
    public VkAccessFlagBits SrcAccess;
    public VkAccessFlagBits DstAccess;
    public VkPipelineStageFlagBits SrcStageMask;
    public VkPipelineStageFlagBits DstStageMask;
    public VkImageLayout SrcLayout;
    public VkImageLayout DstLayout;
}

internal readonly record struct SyncSubresource(uint Layer, uint Mip);
internal readonly record struct SyncSubresourceRange(uint BaseLayer, uint BaseMip, uint NumLayers, uint NumMips);
