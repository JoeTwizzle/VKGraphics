// Fullscreen triangle vertex shader
struct VertexInput
{
    uint vertexID : SV_VertexID;
};

struct VertexOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD0;
};

[shader("vertex")]
VertexOutput main(VertexInput input)
{
    VertexOutput output;

    float2 raw = float2((input.vertexID << 1) & 2, // x: 0 for indices 0,2; 2 for index 1
                        input.vertexID & 2         // y: 0 for indices 0,1; 2 for index 2
    );

    // Calculate position (NDC coordinates)
    float2 xy = raw * 2 - 1;

    // Invert Y for proper screen mapping
    output.position = float4(xy.x, -xy.y, 0.0f, 1.0f);

    // Select position and UV based on vertex ID
    output.uv = raw;

    return output;
}
