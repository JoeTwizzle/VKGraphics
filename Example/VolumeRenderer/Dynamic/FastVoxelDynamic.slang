
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

float4 TraceBlock(int64_t chunk, float3 cases, float3 rayPos, float3 rayDir)
{
    rayDir = normalize(rayDir);
    rayPos = clamp(rayPos, float3(0.0001), float3(3.9999));
    uint3 mapPos = uint3(rayPos);
    float3 raySign = sign(rayDir);
    float3 deltaDist = 1.0 / rayDir;
    float3 sideDist = ((mapPos - rayPos) + 0.5 + raySign * 0.5) * deltaDist;
    int count = 12;
    while (all(mapPos <= 3 && mapPos >= 0) && count >= 0)
    {
        uint chunkTest = uint(mapPos.z * 4 * 4 + mapPos.y * 4 + mapPos.x);
        int64_t testPos = 1ull << chunkTest;
        if ((chunk & testPos) != 0ull)
        {
            float3 p = mapPos + .5 - raySign * .5;
            float normalIndex = cases.y + 2. * cases.z;
            float3 n = GetNormal(uint(normalIndex), rayDir);
            float t = (dot(n, p - rayPos)) / dot(n, rayDir) + 0.001;
            float3 intersect = (rayPos + rayDir * t);
            if (all(mapPos == int3(rayPos))) // Handle edge case where camera origin is inside of block
            {
                intersect = rayPos;
            }

            return float4(intersect, normalIndex);
        }
        float4 conds = step(sideDist.xxyy, sideDist.yzzx);
        cases.x = conds.x * conds.y;                   // if       x dir
        cases.y = (1.0 - cases.x) * conds.z * conds.w; // else if  y dir
        cases.z = (1.0 - cases.x) * (1.0 - cases.y);   // else     z dir
        mapPos += int3(cases * raySign);
        sideDist += cases * raySign * deltaDist;
        // sideDist += max((2.0 * cases - 1.0) * deltaDist, 0.0);
        count--;
    }
    if (count == 0)
    {
        return float4(0, 200, 0, 4);
    }
    return float4(0, 0, 0, -1);
}

float4 TraceRay(Ray ray)
{
    ray.origin /= 4.0;
    int3 gridPos = int3(ray.origin);

    // S is rayDir non-negative? 0 / 1
    const float3 S = step(0, ray.direction);

    const float3 stepDir = sign(ray.direction);

    const float3 deltaDist = 1.0 / abs(ray.direction);

    float3 sideDist = (S - stepDir * fract(ray.origin)) * deltaDist;

    // same as float4(sideDist.xxyy <= sideDist.yzzx);
    float4 conds = step(sideDist.xxyy, sideDist.yzzx);
    float3 cases;
    cases.x = conds.x * conds.y;                   // if       x dir
    cases.y = (1.0 - cases.x) * conds.z * conds.w; // else if  y dir
    cases.z = (1.0 - cases.x) * (1.0 - cases.y);   // else     z dir
    // sideDist += max((2. * cases - 1.) * deltaDist, 0.0);

    for (int i = 0; i < 512; i++)
    {
        if (any(float3(chunksPerAxis) <= gridPos) || any(gridPos < float3(0)))
        {
            break;
        }

        uint3 chunkPos = gridPos;
        uint chunkIndex = chunkPos.z * chunksPerAxis * chunksPerAxis + chunkPos.y * chunksPerAxis + chunkPos.x;

        uint64_t chunk = _VoxelData[chunkIndex];
        if (chunk != 0ull)
        {
            // return float4(0, 0, 0, 1);

            // Point on axis plane
            float3 p = gridPos + .5 - stepDir * .5;
            float normalIndex = cases.y + 2. * cases.z;
            float3 n = GetNormal(uint(normalIndex), ray.direction);
            float t = (dot(n, p - ray.origin)) / dot(n, ray.direction);
            float3 hit = (ray.origin + ray.direction * t);
            if (all(int3(ray.origin) == gridPos))
            {
                hit = ray.origin;
            }

            // float4 result = float4(hit, normalIndex);
            // float4 result = TraceChunk(chunk, gridPos * 4, hit * 4.0, ray.direction);
            float4 result = TraceBlock(chunk, cases, (hit - gridPos) * 4.0, ray.direction);
            result.xyz += gridPos * 4.0;
            if (result.w != -1)
            {
                return result;
            }
        }

        conds = step(sideDist.xxyy, sideDist.yzzx);
        cases.x = conds.x * conds.y;                   // if       x dir
        cases.y = (1.0 - cases.x) * conds.z * conds.w; // else if  y dir
        cases.z = (1.0 - cases.x) * (1.0 - cases.y);   // else     z dir
        sideDist += max((2.0 * cases - 1.0) * deltaDist, 0.0);
        gridPos += int3(cases * stepDir);
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

        Ray shadowRay = Ray(position + normal * 0.001, dirToSun);
        float4 shadowResult = TraceRay(shadowRay);

        const float3 directLight = float3(0.3, 0.9, 0.1);

        float brightness = shadowResult.w == -1 ? 1 : 0.2;

        float factor = saturate(dot(float3(normal), dirToSun) * 0.5 + 1);

        color = directLight * brightness;
        // color = shadowResult.xyz;
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
    float3 color = float3(0);
    color = getColorAtRay(ray, hitResult);
    if (hitResult.w != -1)
    {
        // color = hitResult.xyz;
        ////color = float3(1);
        ////////color = sign(ray.direction);
        ////// color = normal;
        float3 normal = GetNormal(uint(hitResult.w), ray.direction);
        ray = Ray(hitResult.xyz + normal, normalize(float3(reflect(ray.direction, normalize(normal)))));
        color += getColorAtRay(ray, hitResult) * 0.5;
        if (hitResult.w != -1)
        {
            ray = Ray(hitResult.xyz + normal, normalize(float3(reflect(ray.direction, normalize(normal)))));
            color += getColorAtRay(ray, hitResult) * 0.25;
        }
    }
    screen.Store(pixelPos, float4(color, 1.0));
}
