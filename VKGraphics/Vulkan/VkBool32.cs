
namespace VKGraphics.Vulkan;

public readonly partial struct VkBool32 : IComparable, IComparable<VkBool32>, IEquatable<VkBool32>, IFormattable
{
    public readonly uint Handle;

    public VkBool32(uint value)
    {
        Handle = value;
    }

    public static VkBool32 FALSE => new VkBool32(0);

    public static VkBool32 TRUE => new VkBool32(1);

    public static bool operator ==(VkBool32 left, VkBool32 right) => left.Handle == right.Handle;

    public static bool operator !=(VkBool32 left, VkBool32 right) => left.Handle != right.Handle;

    public static bool operator <(VkBool32 left, VkBool32 right) => left.Handle < right.Handle;

    public static bool operator <=(VkBool32 left, VkBool32 right) => left.Handle <= right.Handle;

    public static bool operator >(VkBool32 left, VkBool32 right) => left.Handle > right.Handle;

    public static bool operator >=(VkBool32 left, VkBool32 right) => left.Handle >= right.Handle;

    public static implicit operator bool(VkBool32 value) => value.Handle != 0;

    public static implicit operator VkBool32(bool value) => new VkBool32(value ? 1u : 0u);

    public static bool operator false(VkBool32 value) => value.Handle == 0;

    public static bool operator true(VkBool32 value) => value.Handle != 0;

    public static implicit operator VkBool32(byte value) => new VkBool32(value);

    public static explicit operator byte(VkBool32 value) => (byte)(value.Handle);

    public static explicit operator VkBool32(short value) => new VkBool32(unchecked((uint)(value)));

    public static explicit operator short(VkBool32 value) => (short)(value.Handle);

    public static implicit operator VkBool32(int value) => new VkBool32(unchecked((uint)(value)));

    public static implicit operator int(VkBool32 value) => (int)(value.Handle);

    public static explicit operator VkBool32(long value) => new VkBool32(unchecked((uint)(value)));

    public static implicit operator long(VkBool32 value) => value.Handle;

    public static explicit operator VkBool32(nint value) => new VkBool32(unchecked((uint)(value)));

    public static explicit operator nint(VkBool32 value) => (nint)(value.Handle);

    public static explicit operator VkBool32(sbyte value) => new VkBool32(unchecked((uint)(value)));

    public static explicit operator sbyte(VkBool32 value) => (sbyte)(value.Handle);

    public static implicit operator VkBool32(ushort value) => new VkBool32(value);

    public static explicit operator ushort(VkBool32 value) => (ushort)(value.Handle);

    public static explicit operator VkBool32(uint value) => new VkBool32(value);

    public static explicit operator uint(VkBool32 value) => value.Handle;

    public static explicit operator VkBool32(ulong value) => new VkBool32(unchecked((uint)(value)));

    public static implicit operator ulong(VkBool32 value) => value.Handle;

    public static explicit operator VkBool32(nuint value) => new VkBool32(unchecked((uint)(value)));

    public static implicit operator nuint(VkBool32 value) => value.Handle;

    public int CompareTo(object? obj)
    {
        if (obj is VkBool32 other)
        {
            return CompareTo(other);
        }

        return (obj is null) ? 1 : throw new ArgumentException("obj is not an instance of VkBool32.");
    }

    public int CompareTo(VkBool32 other) => Handle.CompareTo(other.Handle);

    public override bool Equals(object? obj) => (obj is VkBool32 other) && Equals(other);

    public bool Equals(VkBool32 other) => Handle.Equals(other.Handle);

    public override int GetHashCode() => Handle.GetHashCode();

    public override string ToString() => Handle.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider) => Handle.ToString(format, formatProvider);
}