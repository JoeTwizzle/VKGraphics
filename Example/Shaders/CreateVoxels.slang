float3 mod289(float3 x)
{
    return x - floor(x * (1.0 / 289.0)) * 289.0;
}

float4 mod289(float4 x)
{
    return x - floor(x * (1.0 / 289.0)) * 289.0;
}

float4 permute(float4 x)
{
    return mod289(((x * 34.0) + 1.0) * x);
}

float4 taylorInvSqrt(float4 r)
{
    return 1.79284291400159 - 0.85373472095314 * r;
}

float3 grad3(float h)
{
    // Gradients for 3D noise - 12 vectors of length sqrt(2)
    h = h % 12.0;
    float3 g = float3(0, 0, 0);
    if (h < 8.0)
        g.x = 1.0;
    if (h < 4.0)
        g.y = 1.0;
    else if (h == 12.0 || h == 14.0)
        g.x = 1.0;
    else
        g.z = 1.0;

    g.x = (h % 2.0 == 0.0) ? g.x : -g.x;
    g.y = (h % 4.0 < 2.0) ? g.y : -g.y;
    g.z = (h % 8.0 < 4.0) ? g.z : -g.z;
    return g;
}

float simplex3D(float3 v)
{
    // Skew/unskew factors
    const float F3 = 1.0 / 3.0;
    const float G3 = 1.0 / 6.0;

    // Skew the input space to determine simplex cell
    float s = dot(v, float3(F3, F3, F3));
    float3 i = floor(v + s);
    float t = dot(i, float3(G3, G3, G3));
    float3 x0 = v - (i - t);

    // Determine simplex traversal order
    float3 i1, i2;
    if (x0.x >= x0.y)
    {
        if (x0.y >= x0.z)
        {
            i1 = float3(1, 0, 0);
            i2 = float3(1, 1, 0);
        }
        else if (x0.x >= x0.z)
        {
            i1 = float3(1, 0, 0);
            i2 = float3(1, 0, 1);
        }
        else
        {
            i1 = float3(0, 0, 1);
            i2 = float3(1, 0, 1);
        }
    }
    else
    {
        if (x0.y < x0.z)
        {
            i1 = float3(0, 0, 1);
            i2 = float3(0, 1, 1);
        }
        else if (x0.x < x0.z)
        {
            i1 = float3(0, 1, 0);
            i2 = float3(0, 1, 1);
        }
        else
        {
            i1 = float3(0, 1, 0);
            i2 = float3(1, 1, 0);
        }
    }

    // Calculate the four simplex corners
    float3 x1 = x0 - i1 + G3;
    float3 x2 = x0 - i2 + 2.0 * G3;
    float3 x3 = x0 - 1.0 + 3.0 * G3;

    // Wrap integer indices at 289 to avoid precision issues
    float3 p = mod289(float3(i));
    float4 p_ = permute(permute(permute(p.z + float4(0.0, i1.z, i2.z, 1.0)) + p.y + float4(0.0, i1.y, i2.y, 1.0)) +
                        p.x + float4(0.0, i1.x, i2.x, 1.0));

    // Generate gradients using analytical method
    float3 g0 = grad3(p_.x);
    float3 g1 = grad3(p_.y);
    float3 g2 = grad3(p_.z);
    float3 g3 = grad3(p_.w);

    // Calculate noise contributions
    float4 m = max(0.5 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
    m = m * m;
    m = m * m;

    // Dot products of gradients and position vectors
    float4 n = float4(dot(g0, x0), dot(g1, x1), dot(g2, x2), dot(g3, x3));

    // Final noise value
    return 32.0 * dot(m, n);
}

float FBM(float3 pos, int iterations)
{
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;

    for (int i = 0; i < iterations; i++)
    {
        value += amplitude * simplex3D(pos * frequency);
        frequency *= 2.0;
        amplitude *= 0.5;
    }
    return value;
}

[[vk::binding(0, 0)]]
RWStructuredBuffer<uint64_t, Std430DataLayout> _VoxelDataRW;
static const uint chunksPerAxis = (1024 / 4);

groupshared uint64_t sharedChunk; // Shared memory for the whole workgroup

[shader("compute")]
[numthreads(4, 4, 4)]
void BuildWorld(uint3 groupId: SV_GroupID, uint3 threadId: SV_GroupThreadID, uint threadIndex: SV_GroupIndex)
{
    // World position calculations
    float3 pos = fma(groupId, 4.0, threadId);

    // Base terrain with waves
    float zfactor = cos(pos.z) * 0.5f;
    float xfactor = sin(pos.x) * 0.5f;

    // Add multi-scale noise (global coordinates ensure continuity)
    float2 terrainNoiseUV = pos.xz * 0.005;
    float seaLevelHeight =
        FBM(float3(terrainNoiseUV.x, 0.0, terrainNoiseUV.y) * 0.3 + float3(232, 632, 52), 2) * 800.0 + 256;
    float surfaceHeight = FBM(float3(terrainNoiseUV.x, 0.0, terrainNoiseUV.y), 4) * 140.0 + seaLevelHeight;
    // float surfaceHeight = (cos(terrainNoiseUV.x) + cos(terrainNoiseUV.y)) * 80.0 + 256;
    float density;

    if (surfaceHeight < pos.y)
    {
        density = 0;
    }
    else
    {
        float diff = saturate(1 - (pos.y / surfaceHeight) / 100);

        density = diff * fma(FBM((pos + float3(52132, 6432, 56442)) * 0.1, 1), 0.5, 0.5) * 10 + 20;
    }
    // Add cave systems with 3D noise (global coordinates)
    float caveNoise = simplex3D((float3(pos.x * 0.2, pos.y * 0.25, pos.z * 0.2)) * 0.1) * 64;
    // caveNoise = lerp(caveNoise, simplex3D((pos * 0.7 + float3(332, 682, 694)) * 0.1), 0.25);
    float erosion = fma(FBM(pos * 0.01, 2), 0.5, 0.5);

    if (abs(caveNoise) < 0.01 || caveNoise > 0.5)
    {
        density -= erosion * 50; // Carve out caves
    }

    //  Add bedrock layer
    if (pos.y < 20.0)
    {
        density = 100.0; // Force solid below y=20
    }

    uint64_t chunk = 0ull;

    // Set voxel if density is positive
    if (density > 0.0)
    {
        uint32_t chunkIndex = (threadId.z * 16) + (threadId.y * 4) + threadId.x;
        chunk = (1ull << chunkIndex);
    }
    // Initialize shared memory only once
    if (threadIndex == 0)
        sharedChunk = 0;

    GroupMemoryBarrierWithGroupSync();
    // Combine voxel data
    InterlockedOr(sharedChunk, chunk);
    GroupMemoryBarrierWithGroupSync();

    // Write to global memory
    if (threadIndex == 0)
    {
        uint index = (groupId.z * chunksPerAxis * chunksPerAxis) + groupId.y * chunksPerAxis + groupId.x;
        _VoxelDataRW[index] = sharedChunk;
    }
}
