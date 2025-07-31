
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
static const float epsilon = 0.001;
static const float PI = 3.14159265358979323846;
static const float3 sunRadiance = float3(1, 1, 0.8); // Physical sun radiance
static const float3 skyColor = float3(0.3, 0.3, 0.8) * 0.3;

float4 TraceBlock(int64_t chunk, float3 cases, float3 rayPos, float3 rayDir)
{
    rayDir = normalize(rayDir);
    rayPos = clamp(rayPos, float3(0.0001), float3(3.999));
    uint3 mapPos = uint3(rayPos);
    float3 raySign = sign(rayDir);
    float3 deltaDist = 1.0 / (rayDir);
    float3 sideDist = ((mapPos - rayPos) + 0.5 + raySign * 0.5) * deltaDist;
    int count = 12;
    while (all(mapPos <= 3 && mapPos >= 0) && count >= 0)
    {
        uint chunkTest = uint(mapPos.z * 4 * 4 + mapPos.y * 4 + mapPos.x);
        int64_t testPos = 1ull << chunkTest;
        if ((chunk & testPos) != 0ull)
        {
            float3 p = mapPos + 0.5 - raySign * 0.5;
            float normalIndex = cases.y + 2.0 * cases.z;
            float3 n = GetNormal(uint(normalIndex), rayDir);
            float t = (dot(n, p - rayPos)) / dot(n, rayDir);
            // float t = min(min(sideDist.x, sideDist.y), sideDist.z) + 0.001;

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
        if (all(cases == 0))
        {
            break;
        }
    }
    if (count == 0)
    {
        return float4(0, 200, 0, 4);
    }
    return float4(0, 0, 0, -1);
}

float4 TraceRay(Ray ray, int maxDist = 512)
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

    for (int i = 0; i < maxDist; i++)
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

float3 GetFixedDirection(int index, float3 normal)
{
    // Precomputed hemisphere directions (tangent space)
    static const float3 dirs[8] = { normalize(float3(0.577, 0.577, 0.577)),  normalize(float3(-0.577, 0.577, -0.577)),
                                    normalize(float3(-0.577, 0.577, 0.577)), normalize(float3(0.577, 0.577, -0.577)),
                                    normalize(float3(0.000, 0.577, 0.816)),  normalize(float3(0.000, 0.577, -0.816)),
                                    normalize(float3(0.816, 0.577, 0.000)),  normalize(float3(-0.816, 0.577, 0.000)) };

    // Build orthonormal basis
    float3 up = abs(normal.y) < 0.999 ? float3(0, 1, 0) : float3(0, 0, 1);
    float3 tangent = normalize(cross(up, normal));
    float3 bitangent = cross(normal, tangent);

    // Transform to world space
    float3 t = dirs[index];
    return tangent * t.x + normal * t.y + bitangent * t.z;
}

float3 GetSkyColor(float3 rd, float3 sunDir)
{
    // Normalize directions
    rd = normalize(rd);
    sunDir = normalize(sunDir);

    // Calculate sun position
    float sunHeight = sunDir.y;
    float dayTime = smoothstep(-0.1, 0.1, sunHeight);
    float duskDawn = smoothstep(-0.3, 0.3, sunHeight) * (1.0 - dayTime);
    float night = 1.0 - smoothstep(-0.3, 0.0, sunHeight);

    // Sky colors
    float3 dayZenith = float3(0.2, 0.4, 0.8);
    float3 dayHorizon = float3(0.8, 0.9, 1.0);
    float3 duskZenith = float3(0.2, 0.15, 0.3);
    float3 duskHorizon = float3(1.0, 0.5, 0.2);
    float3 nightZenith = float3(0.02, 0.02, 0.04);
    float3 nightHorizon = float3(0.04, 0.05, 0.08);

    // Horizon gradient
    float horizon = pow(1.0 - max(rd.y, 0.0), 6.0 + night);

    // Blend sky colors
    float3 dayColor = lerp(dayZenith, dayHorizon, horizon);
    float3 duskColor = lerp(duskZenith, duskHorizon, horizon);
    float3 nightColor = lerp(nightZenith, nightHorizon, horizon);

    float3 skyColor = lerp(nightColor, duskColor, duskDawn);
    skyColor = lerp(skyColor, dayColor, dayTime);

    // ----- SUN -----
    float sunDot = dot(rd, sunDir);

    // Sun glow (corona)
    float sunGlow = pow(max(0.0, sunDot), 128.0) * 2.0 * dayTime;

    // Sun disc (proper size)
    float sunDisc = smoothstep(0.9998, 0.9999, sunDot) * 100.0 * dayTime;

    // ----- MOON -----
    float3 moonDir = -sunDir;
    float moonDot = dot(rd, moonDir);

    // Moon glow
    float moonGlow = pow(max(0.0, moonDot), 128.0) * 0.5 * night;

    // Moon disc
    float moonDisc = smoothstep(0.9998, 0.9999, moonDot) * 10.0 * night;
    float3 moonlight = float3(0.6, 0.7, 0.9) * moonGlow;

    // ----- STARS -----
    float stars = 0.0;
    if (night > 0.01 && rd.y > 0.0)
    {
        // Generate star positions using hash function
        float2 uv = float2(atan2(rd.z, rd.x), asin(rd.y));
        float starSeed = frac(sin(dot(uv, float2(12.9898, 78.233))) * 43758.5453);

        // Create star pattern
        float starIntensity = pow(frac(starSeed * 12.345), 256.0);
        stars = step(0.999, starIntensity) * night * (1.0 - moonGlow);
    }

    // Combine all elements
    return skyColor + sunRadiance * (sunGlow + sunDisc) * dayTime + moonlight + float3(moonDisc) * night +
           float3(stars);
}
float3 getColorAtRay(Ray ray, out float4 result, bool includeGI)
{
    result = TraceRay(ray);
    float3 color = float3(0);

    if (result.w != -1) // Surface hit
    {
        float3 position = result.xyz;
        float3 normal = normalize(GetNormal(uint(result.w), ray.direction));

        // Shadow ray for direct sun light
        Ray shadowRay = Ray(position + normal * 0.001, dirToSun);
        float4 shadowResult = TraceRay(shadowRay);
        float shadow = shadowResult.w == -1 ? 1.0 : 0.1;

        // Get material albedo
        float4 albedo = SampleMaterialColor(position);

        // Direct lighting
        float NdotL = max(0.0, dot(normal, dirToSun));
        color = (albedo.rgb / PI) * sunRadiance * NdotL * shadow + albedo.rgb * (albedo.w - 1.0);

        // Global Illumination - ambient occlusion
        if (includeGI)
        {
            const int numGIRays = 8;
            float3 giLight = 0;
            float3 giPos = position + normal * 0.001;
            for (int i = 0; i < numGIRays; i++)
            {
                //TODO: Use low noise stochastic sampling with multi frame world space cache.
                float3 giDir = GetFixedDirection(i, normal);

                Ray giRay = Ray(giPos, giDir);
                float4 giHit = TraceRay(giRay, 24);
                if (giHit.w == -1)
                {
                    giLight += GetSkyColor(giRay.direction, dirToSun) * albedo.rgb + albedo.rgb * (albedo.w - 1.0);
                }
                else
                {
                    float4 giAlbedo = SampleMaterialColor(giHit.xyz);
                    giLight += giAlbedo.rgb * (giAlbedo.w - 1.0);
                }
            }

            giLight /= float(numGIRays);

            color += giLight;
        }
    }
    else // Sky
    {
        color = GetSkyColor(ray.direction, dirToSun);
    }
    return color;
}
float3 LinearToSrgb(float3 x)
{

    float3 xlo = 12.92 * x;
    float3 xhi = 1.055 * pow(x, float3(0.4166666666666667)) - 0.055;

    return lerp(xlo, xhi, step(float3(0.0031308), x));
}

float3 SrgbToLinear(float3 x)
{

    float3 xlo = x / 12.92;
    float3 xhi = pow((x + 0.055) / (1.055), float3(2.4));

    return lerp(xlo, xhi, step(float3(0.04045), x));
}

// Modified slightly From the Oklab post @ https://bottosson.github.io/posts/oklab/
// to actually be GLSL

float3 LinearToOklab(float3 c)
{
    float l = 0.4122214708 * c.r + 0.5363325363 * c.g + 0.0514459929 * c.b;
    float m = 0.2119034982 * c.r + 0.6806995451 * c.g + 0.1073969566 * c.b;
    float s = 0.0883024619 * c.r + 0.2817188376 * c.g + 0.6299787005 * c.b;

    //  Cube root each
    float l_ = pow(l, 1. / 3.);
    float m_ = pow(m, 1. / 3.);
    float s_ = pow(s, 1. / 3.);

    float3 result;

    result.r = 0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_;
    result.g = 1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_;
    result.b = 0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_;

    return result;
}

float3 OklabToLinear(float3 c)
{
    float l_ = c.x + 0.3963377774 * c.y + 0.2158037573 * c.z;
    float m_ = c.x - 0.1055613458 * c.y - 0.0638541728 * c.z;
    float s_ = c.x - 0.0894841775 * c.y - 1.2914855480 * c.z;

    float l = l_ * l_ * l_;
    float m = m_ * m_ * m_;
    float s = s_ * s_ * s_;

    float3 result;

    result.r = +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
    result.g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
    result.b = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;

    return result;
}

// Simplified physical material
float4 SampleMaterialColor(float3 position)
{
    float y = int(position.y - 0.01);
    float3 color;

    // Define terrain colors
    static const float3 lavaRed = LinearToOklab(SrgbToLinear(float3(1, 0.29, 0.07)));     // Below 150
    static const float3 stonyGray = LinearToOklab(SrgbToLinear(float3(0.2, 0.2, 0.2)));   // 150-200
    static const float3 brown = LinearToOklab(SrgbToLinear(float3(0.4, 0.3, 0.2)));       // 200-250
    static const float3 darkGreen = LinearToOklab(SrgbToLinear(float3(0.1, 0.4, 0.1)));   // 250-350
    static const float3 brightGreen = LinearToOklab(SrgbToLinear(float3(0.2, 0.8, 0.2))); // 350-450
    static const float3 lightGray = LinearToOklab(SrgbToLinear(float3(0.8, 0.8, 0.8)));   // Above 500

    float brightness = 1;
    // Blend colors based on height with smooth transitions
    if (y <= 180)
    {
        color = lavaRed;
        brightness = 8;
    }
    else if (y <= 200)
    {
        float t = (y - 180) / 20.0;
        color = lerp(lavaRed, stonyGray, t * t * t * t);
        brightness = lerp(8, 1, t * t * t * t);
    }
    else if (y <= 250)
    {
        float t = (y - 200) / 50.0;
        color = lerp(stonyGray, brown, t);
    }
    else if (y <= 300)
    {
        float t = (y - 250) / 50.0;
        color = lerp(brown, darkGreen, t);
    }
    else if (y <= 350)
    {
        float t = (y - 300) / 50.0;
        color = lerp(darkGreen, brightGreen, t);
    }
    else if (y <= 400)
    {
        float t = (y - 350) / 50.0;
        color = lerp(brightGreen, lightGray, t);
    }
    else
    {
        color = lightGray;
    }

    // Add simple height-based variation
    return float4(OklabToLinear(color), brightness);
}

float3 GetNormal(uint side, float3 rayDir)
{
    return (float3(float(side == 0), float(side == 1), float(side == 2)) * sign(-rayDir));
}

[shader("compute")]
[numthreads(8, 8, 1)]
void computeMains(uint3 threadId: SV_DispatchThreadID)
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
    float accumulatedReflectance = 1.0; // Tracks energy carried by current ray
    float4 hitResult;
    float3 color = float3(0);
    float3 throughput = float3(1.0, 1.0, 1.0);
    Ray currentRay = ray;
    const int MAX_BOUNCES = 1;
    const float epsilon = 0.001;

    for (int bounce = 0; bounce < MAX_BOUNCES; bounce++)
    {
        float4 hitResult;
        bool includeGI = (bounce == 0); // GI only on first hit
        float3 bounceColor = getColorAtRay(currentRay, hitResult, includeGI);

        color += throughput * bounceColor;

        if (hitResult.w == -1)
            break;

        float3 position = hitResult.xyz;
        float3 normal = normalize(GetNormal(uint(hitResult.w), currentRay.direction));
        // float3 albedo = SampleMaterialColor(position, normal, currentRay.origin);

        // Energy conservation
        float fac = 1 - max(dot(normal, dirToSun) * 0.25 + 0.5, 0);
        float reflectance = 0.05;
        // float reflectance = max(max(albedo.r, albedo.g), albedo.b) * 0.9;
        reflectance = min(reflectance * fac, 0.9);
        throughput *= reflectance;

        // Next reflection ray
        currentRay = Ray(position + normal * epsilon, reflect(currentRay.direction, normal));
    }

    screen.Store(pixelPos, float4(color, 1.0));
}
