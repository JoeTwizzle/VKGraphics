[[vk::binding(0, 0)]]
RWStructuredBuffer<uint64_t, Std430DataLayout> _VoxelDataRW;
static const uint chunksPerAxis = (1024 / 4);

groupshared uint64_t sharedChunk; // Shared memory for the whole workgroup

[shader("compute")]
[numthreads(4, 4, 4)] // 64 threads per workgroup
void BuildWorld(uint3 groupId: SV_GroupID, uint3 threadId: SV_GroupThreadID, uint threadIndex: SV_GroupIndex)
{
    uint64_t chunk = 0ull;

    float xFinal = (groupId.x * 4.0 + threadId.x) / 20.0;
    float zFinal = (groupId.z * 4.0 + threadId.z) / 20.0;

    float zfactor = cos(zFinal) * 0.5f;
    float xfactor = sin(xFinal) * 0.5f;
    float term = (xfactor + 1 + zfactor + 1) * 40.0 + 256.0;
    float yFinal = (groupId.y * 4.0 + threadId.y);

    if (term > yFinal)
    {
        uint32_t chunkIndex = (threadId.z * 4 * 4) + threadId.y * 4 + threadId.x;
        chunk = (1ull << chunkIndex);
    }

    // Initialize shared memory only once
    if (threadIndex == 0)
        sharedChunk = 0;

    GroupMemoryBarrierWithGroupSync(); // Make sure shared memory is initialized

    // Atomically OR all chunk values into shared memory
    InterlockedOr(sharedChunk, chunk);

    GroupMemoryBarrierWithGroupSync(); // Ensure all writes are complete

    // Only one thread writes the final value to global memory
    if (threadIndex == 0)
    {
        _VoxelDataRW[(groupId.z * chunksPerAxis * chunksPerAxis) + groupId.y * chunksPerAxis + groupId.x] = sharedChunk;
    }
}
