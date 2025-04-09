
// Count number of set bits in variable range [0..width]
public uint PopCountVar64(uint64_t mask, uint width)
{
    // return PopCount64(mask & ((1ull << mask) - 1));
    uint himask = uint(mask);
    uint count = 0;

    if (width >= 32)
    {
        count = countbits(himask);
        himask = uint(mask >> 32);
    }
    uint m = 1u << (width & 31u);
    count += countbits(himask & (m - 1u));

    return count;
}

public uint PopCount64(uint64_t mask)
{
    uint a = countbits(uint(mask >> 0));
    uint b = countbits(uint(mask >> 32));
    return a + b;
}

public uint TrailingZeroCount64(uint64_t mask)
{
    uint a = uint(mask >> 0);
    uint b = uint(mask >> 32);
    uint c = firstbitlow(a != 0 ? a : b);
    return a != 0 ? c : c + 32;
}

public void InterlockedSetBit(__ref uint64_t dest, uint index, bool value)
{
    // Workaround for 64-bit atomics which are slightly fucked on slang's spirv target atm
    uint mask = 1 << (index & 31);
    uint *ptr = (uint *)&dest + (index / 32u);

    if (value)
    {
        InterlockedOr(*ptr, mask);
    }
    else
    {
        InterlockedAnd(*ptr, ~mask);
    }
}
