import Random;
import Raycasting;
import CommonBindings;

public void GetPrimaryRay(float2 rayCoords, out float3 rayPos, out float3 rayDir)
{
    // Transform the camera origin to world space
    rayPos = mul(_CameraProperites._CameraToWorld, float4(0.0, 0.0, 0.0, 1.0)).xyz;

    // Invert the perspective projection of the view-space position
    float3 direction = mul(_CameraProperites._CameraInverseProjection, float4(rayCoords, 0.0, 1.0)).xyz;

    // Transform the direction from camera to world space
    rayDir = mul(_CameraProperites._CameraToWorld, float4(direction, 0.0)).xyz;
}

[numthreads(8, 8)]
void ComputeMain(uint2 pixelPos: SV_DispatchThreadID)
{
    InitRandom(pixelPos, pc.FrameNo);
    int2 resolution;
    screen.GetDimensions(resolution.x, resolution.y);
    if (any(pixelPos >= resolution))
    {
        return;
    }
    float2 uv = float2((pixelPos + float2(0.5, 0.5)) / float2(resolution.x, resolution.y));
    float2 rayCoords = uv * 2.0 - 1.0;
    float3 rayDir, rayPos;
    GetPrimaryRay(rayCoords, rayPos, rayDir);

    float3 radiance = 0.0;
    float3 throughput = 1.0;

    for (int bounceNo = 0; bounceNo < 3; bounceNo++)
    {
        HitInfo hit = pc.Scene.RayCast(rayPos, rayDir);

        if (!hit.Miss)
        {
            let material = pc.Scene.Palette[hit.MaterialId];
            throughput *= material.Color;
            radiance += throughput * material.Emission;
        }
        else
        {
            radiance += throughput * GetSkyColor(rayDir);
            break;
        }
        rayDir = normalize(hit.Normal + GetRandomSphereDir());
        rayPos = hit.Pos;
    }

    float3 prevRadiance = screen.Load(pixelPos).rgb;
    float weight = 1.0 / (pc.FrameNo + 1);
    screen.Store(pixelPos, float4(lerp(prevRadiance, radiance, weight), 1));
}
