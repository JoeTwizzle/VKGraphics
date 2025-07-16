namespace VKGraphics.Vulkan;

internal interface ISynchronizedResource : IResourceRefCountTarget
{
    //ref SyncState SyncState { get; }
    Span<SyncState> AllSyncStates { get; }
    SyncSubresource SubresourceCounts { get; }
    ref SyncState SyncStateForSubresource(SyncSubresource subresource);
}
