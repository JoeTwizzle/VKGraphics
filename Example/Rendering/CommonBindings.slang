import Node;
import Material;

struct CamBuf
{
    float4x4 _CameraToWorld;
    float4x4 _CameraInverseProjection;
}
struct VoxelTree
{
    Node *NodePool;
    uint8_t *LeafData;
    Material *Palette;

    public HitInfo RayCast(float3 origin, float3 dir)
    {
        // Magic goes here
    }
}

[[vk::binding(0, 0)]]
ConstantBuffer<CamBuf> _CameraProperites;

[[vk::binding(0, 1)]]
StructuredBuffer<uint64_t, Std430DataLayout> _VoxelData;

[[vk::binding(0, 2)]]
[format("rgba16f")]
RWTexture2D<float4> screen;
