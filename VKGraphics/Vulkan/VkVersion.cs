﻿
namespace VKGraphics.Vulkan;

internal struct VkVersion : IEquatable<VkVersion>
{
    public static uint API_VERSION_VARIANT(uint version) => (version) >> 29;
    public static uint API_VERSION_MAJOR(uint version) => (version >> 22) & 0x7FU;
    public static uint API_VERSION_MINOR(uint version) => (version >> 12) & 0x3FFU;
    public static uint API_VERSION_PATCH(uint version) => (version) & 0xFFFU;

    public override bool Equals(object? obj)
    {
        return obj is VkVersion version && Equals(version);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(value, Variant, Major, Minor, Patch);
    }

    public bool Equals(VkVersion other)
    {
        return value == other.value;
    }

    public uint value;

    public VkVersion(uint value)
    {
        this.value = value;
    }

    public VkVersion(uint major, uint minor, uint patch)
    {
        value = major << 22 | minor << 12 | patch;
    }

    public uint Variant => API_VERSION_VARIANT(value);

    public uint Major => API_VERSION_MAJOR(value);

    public uint Minor => API_VERSION_MINOR(value);

    public uint Patch => API_VERSION_PATCH(value);

    public static implicit operator uint(VkVersion version)
    {
        return version.value;
    }

    public static bool operator <(VkVersion a, VkVersion b) => a.value < b.value;
    public static bool operator >(VkVersion a, VkVersion b) => a.value > b.value;
    public static bool operator <=(VkVersion a, VkVersion b) => a.value <= b.value;
    public static bool operator >=(VkVersion a, VkVersion b) => a.value >= b.value;
    public static bool operator ==(VkVersion a, VkVersion b) => a.value == b.value;
    public static bool operator !=(VkVersion a, VkVersion b) => a.value != b.value;
}
