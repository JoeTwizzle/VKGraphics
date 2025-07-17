struct VertexOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};
[[vk::binding(0, 0)]]
SamplerState _MainSampler;
[[vk::binding(1, 0)]]
texture2D _MainTexture;

float3 Tonemap_ACES(float3 x)
{
    // Narkowicz 2015, "ACES Filmic Tone Mapping Curve"
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return (x * (a * x + b)) / (x * (c * x + d) + e);
}
// https://www.shadertoy.com/view/WltSRB
// https://twitter.com/jimhejl/status/1137559578030354437
float3 ToneMapFilmicALU(float3 x)
{
    x *= 0.665;

    x = max(float3(0.0), x);
    x = (x * (6.2 * x + 0.5)) / (x * (6.2 * x + 1.7) + 0.06);
    x = pow(x, float3(2.2)); // using gamma instead of sRGB_EOTF + without x - 0.004f looks about the same

    return x;
}

static const float3x3 ACESOutputMat =
    float3x3(1.60475, -0.53108, -0.07367, -0.10208, 1.10813, -0.00605, -0.00327, -0.07276, 1.07602);

static const float3x3 RRT_SAT =
    float3x3(0.970889, 0.026963, 0.002148, 0.010889, 0.986963, 0.002148, 0.010889, 0.026963, 0.962148);

float3 ToneTF2(float3 x)
{
    float3 a = (x + 0.0822192) * x;
    float3 b = (x * 0.983521 + 0.5001330) * x + 0.274064;

    return a / b;
}

float3 Tonemap_ACESFitted2(float3 acescg)
{
    float3 color = mul(acescg, RRT_SAT);

    color = ToneTF2(color);

    color = mul(color, ACESOutputMat);
    // color = ToneMapFilmicALU(color);

    return color;
}

float3 filmicToneMapping(float3 color)
{
    color = max(float3(0.), color - float3(0.004));
    color = (color * (6.2 * color + .5)) / (color * (6.2 * color + 1.7) + 0.06);
    return color;
}

[shader("pixel")]
float4 main(VertexOutput fsIn)
{
    float3 rawColor = _MainTexture.Sample(_MainSampler, fsIn.uv).rgb;
    float4 color = float4(filmicToneMapping(rawColor), 1.0);
    return color;
}
