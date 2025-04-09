
float3 GetRandomSphereDir()
{
    float2 u = (NextRandomU32() >> uint2(0, 16) & 65535) / 65536.0;

    float phi = u.x * 6.283185307179586;
    float y = u.y * 2 - 1;
    float r = sqrt(1.0 - y * y);
    return float3(sin(phi) * r, y, cos(phi) * r);
}

// PCG random
static uint g_RandomSeed = 0;
uint NextRandomU32()
{
    uint state = g_RandomSeed * 747796405u + 2891336453u;
    uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return g_RandomSeed = (word >> 22u) ^ word;
}
void InitRandom(uint2 pos, uint frameNo)
{
    g_RandomSeed = pos.x ^ (pos.y << 16);
    NextRandomU32();
    g_RandomSeed ^= frameNo * 1234;
}