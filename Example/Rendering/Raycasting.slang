import CommonBindings;

public interface IRayCastable
{
    public HitInfo RayCast(int3 worldOrigin, float3 origin, float3 dir, bool coarse);
}

public struct HitInfo
{
    public float Dist;
    public float3 Pos;
    public float3 Normal;
    public float2 FaceUV;
    public uint MaterialId;

    public property bool Miss
    {
        get {
            return MaterialId == 0;
        }
    }
}

public float3 ClipRayToAABB(float3 origin, float3 dir, float3 bbMin, float3 bbMax)
{
    float3 invDir = 1.0 / dir;
    float3 t1 = (bbMin - origin) * invDir;
    float3 t2 = (bbMax - origin) * invDir;
    float3 temp = t1;
    t1 = min(temp, t2);
    t2 = max(temp, t2);

    float tmin = max(t1.x, max(t1.y, t1.z));
    float tmax = min(t2.x, min(t2.y, t2.z));

    return tmin > 0 && tmin < tmax ? origin + dir * tmin : origin;
}

// Returns linear index for 3D grid in XZY order. Sizes **must** be power of two.
public uint GetLinearIndex(uint3 pos, uint sizeXZ, uint sizeY)
{
    return (pos.x & (sizeXZ - 1)) + (pos.z & (sizeXZ - 1)) * sizeXZ + (pos.y & (sizeY - 1)) * (sizeXZ * sizeXZ);
}
// Returns linear index for 3D grid in XZY order. Sizes **must** be constant otherwise this will generate divisions.
public uint3 GetPosFromLinearIndex(uint idx, uint sizeXZ, uint sizeY)
{
    uint3 pos;
    pos.x = idx % sizeXZ;
    pos.z = (idx / sizeXZ) % sizeXZ;
    pos.y = (idx / sizeXZ) / sizeXZ;
    return pos;
}

public bool CheckBounds(uint3 pos, uint sizeXZ, uint sizeY)
{
    return (pos.x | pos.z) < sizeXZ && pos.y < sizeY;
}
