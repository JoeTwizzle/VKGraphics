
[[vk::binding(0, 0)]]
ConstantBuffer<CamBuf> _CameraProperites;

[[vk::binding(0, 1)]]
StructuredBuffer<uint64_t, Std430DataLayout> _VoxelData;

[[vk::binding(0, 2)]]
[format("rgba16f")]
RWTexture2D<float4> screen;

struct CamBuf
{
    float4x4 _CameraToWorld;
    float4x4 _CameraInverseProjection;
}

struct Ray
{
    float3 origin;
    float3 direction;
}

Ray CreateRay(float3 origin, float3 direction)
{
    Ray ray;
    ray.origin = origin;
    ray.direction = direction;
    return ray;
}

Ray CreateCameraRay(float2 rayCoords)
{
    // Transform the camera origin to world space
    const float3 origin = mul(_CameraProperites._CameraToWorld, float4(0.0, 0.0, 0.0, 1.0)).xyz;

    // Invert the perspective projection of the view-space position
    float3 direction = mul(_CameraProperites._CameraInverseProjection, float4(rayCoords, 0.0, 1.0)).xyz;

    // Transform the direction from camera to world space
    direction = mul(_CameraProperites._CameraToWorld, float4(direction, 0.0)).xyz;

    // Avoid divide by zero when rayDir element is zero
    //    if (any(equal(direction, float3(0.0))))
    //        direction += float3(equal(direction, float3(0.0))) * float3(0.00001);
    return Ray(origin, direction);
}

static const uint chunksPerAxis = (1024 / 4);
static const float3 dirToSun = normalize(float3(0.4, 0.5, 0.45));

float4 TraceRay(Ray ray)
{
    uint faceId = -1;

    int3 gridPos = int3(ray.origin);

    // S is rayDir non-negative? 0 / 1
    float3 S = step(0, ray.direction);

    float3 stepDir = sign(ray.direction);

    float3 deltaDist = 1.0 / abs(ray.direction);

    float3 sideDist = (S - stepDir * fract(ray.origin)) * deltaDist;

    // same as float4(sideDist.xxyy <= sideDist.yzzx);
    float4 conds = step(sideDist.xxyy, sideDist.yzzx);

    for (int i = 0; i < 512; i++)
    {
        if (any(float3(1024) <= gridPos) || any(gridPos < float3(0)))
        {
            break;
        }

        uint3 chunkPos = (gridPos >> 2);
        uint chunkIndex = chunkPos.z * chunksPerAxis * chunksPerAxis + chunkPos.y * chunksPerAxis + chunkPos.x;

        uint64_t chunk = _VoxelData[chunkIndex];
        do
        {
            if (chunk != 0ull)
            {
                uint3 localGridPos = gridPos & uint3(3, 3, 3);
                uint chunkTest = localGridPos.z * 4 * 4 + localGridPos.y * 4 + localGridPos.x;
                uint64_t testPos = 1ull << chunkTest;
                if ((chunk & testPos) != 0ull)
                {
                    float3 cases;
                    cases.x = conds.x * conds.y;                   // if       x dir
                    cases.y = (1.0 - cases.x) * conds.z * conds.w; // else if  y dir
                    cases.z = (1.0 - cases.x) * (1.0 - cases.y);   // else     z dir
                    sideDist += max((2. * cases - 1.) * deltaDist, 0.0);
                    float3 p = gridPos + .5 - stepDir * .5; // Point on axis plane
                    float result = cases.y + 2. * cases.z;
                    float3 n = GetNormal(uint(result), ray.direction);
                    // Solve ray plane intersection equation: dot(n, ro + t * rd - p) = 0.
                    // for t :
                    float t = (dot(n, p - ray.origin)) / dot(n, ray.direction);
                    float3 hit = ray.origin + ray.direction * t;
                    return float4(hit, cases.y + 2. * cases.z);
                }
            }
            conds = step(sideDist.xxyy, sideDist.yzzx);
            float3 cases;
            cases.x = conds.x * conds.y;                   // if       x dir
            cases.y = (1.0 - cases.x) * conds.z * conds.w; // else if  y dir
            cases.z = (1.0 - cases.x) * (1.0 - cases.y);   // else     z dir
            sideDist += max((2.0 * cases - 1.0) * deltaDist, 0.0);
            gridPos += int3(cases * stepDir);
        } while (all((gridPos >> 2) == chunkPos));
    }

    return float4(gridPos, -1);
}

float3 getColorAtRay(Ray ray, out float4 result)
{
    const float3 skyColor = float3(0.3, 0.3, 0.8);
    result = TraceRay(ray);
    float3 color = float3(0);
    if (result.w != -1)
    {
        float3 position = result.xyz;
        float3 normal = GetNormal(uint(result.w), ray.direction);

        Ray shadowRay = Ray(float3(position + normal), dirToSun);
        float4 shadowResult = TraceRay(shadowRay);
        const float3 directLight = float3(0.3, 0.9, 0.1);

        float brightness = shadowResult.w == -1 ? 1 : 0.2;

        float factor = saturate(dot(float3(normal), dirToSun) * 0.5 + 1);

        color = directLight * brightness;
    }
    else
    {
        if (dot(normalize(ray.direction), dirToSun) > 0.99)
        {
            color = float3(1, 1, 0.8) * 3;
        }
        else
        {
            color = skyColor;
        }
    }
    return color;
}

float3 GetNormal(uint side, float3 rayDir)
{
    return (float3(float(side == 0), float(side == 1), float(side == 2)) * sign(-rayDir));
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMain(uint3 threadId: SV_DispatchThreadID)
{
    int2 resolution;
    screen.GetDimensions(resolution.x, resolution.y);
    int2 pixelPos = int2(threadId.xy);
    if (any(pixelPos >= resolution))
    {
        return;
    }
    float2 uv = float2((pixelPos + float2(0.5, 0.5)) / float2(resolution.x, resolution.y));
    float2 rayCoords = uv * 2.0 - 1.0;

    Ray ray = CreateCameraRay(rayCoords);

    float4 hitResult;
    float3 color;
    color = getColorAtRay(ray, hitResult);
    if (hitResult.w != -1)
    {
        // color = fract(hitResult.xyz);
        // color = float3(1);
        float3 normal = GetNormal(uint(hitResult.w), ray.direction);
        //        color = sign(ray.direction);
        //        color = normal;
        ray = Ray(hitResult.xyz + normal, normalize(float3(reflect(ray.direction, normalize(normal)))));
        color += getColorAtRay(ray, hitResult) * 0.5;
        // if (hitResult.w != -1)
        //{
        //     ray = Ray(hitResult.xyz + normal, normalize(float3(reflect(ray.direction, normalize(normal)))));
        //     color += getColorAtRay(ray, hitResult);
        // }
    }
    screen.Store(pixelPos, float4(color, 1.0));
}
