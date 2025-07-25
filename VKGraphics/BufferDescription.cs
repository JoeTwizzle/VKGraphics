namespace VKGraphics;

/// <summary>
/// Describes a <see cref="DeviceBuffer"/>, used in the creation of <see cref="DeviceBuffer"/> objects by a
/// <see cref="ResourceFactory"/>.
/// </summary>
public readonly struct BufferDescription : IEquatable<BufferDescription>
{
    /// <summary>
    /// The desired capacity, in bytes, of the <see cref="DeviceBuffer"/>.
    /// </summary>
    public readonly uint SizeInBytes;

    /// <summary>
    /// Indicates how the <see cref="DeviceBuffer"/> will be used.
    /// </summary>
    public readonly BufferUsage Usage;

    /// <summary>
    /// For structured buffers, this value indicates the size in bytes of a single structure element, and must be non-zero.
    /// For all other buffer types, this value must be zero.
    /// </summary>
    public readonly uint StructureByteStride;

    /// <summary>
    /// Indicates that this is a raw buffer. This should be combined with
    /// <see cref="BufferUsage.StructuredBufferReadWrite"/>.
    /// </summary>
    /// <remarks>
    /// This affects how the buffer is bound in the D3D11 backend.
    /// </remarks>
    public readonly bool RawBuffer;

    /// <summary>
    /// Optional source of data to fill the buffer with.
    /// </summary>
    public readonly IntPtr InitialData;

    /// <summary>
    /// Constructs a new <see cref="BufferDescription"/> describing a non-dynamic <see cref="DeviceBuffer"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="RawBuffer"/> is set to <see langword="true"/>.
    /// </remarks>
    /// <param name="sizeInBytes">The desired capacity, in bytes.</param>
    /// <param name="usage">Indicates how the <see cref="DeviceBuffer"/> will be used.</param>
    public BufferDescription(uint sizeInBytes, BufferUsage usage)
    {
        SizeInBytes = sizeInBytes;
        Usage = usage;
        StructureByteStride = 0;
        RawBuffer = true;
        InitialData = IntPtr.Zero;
    }

    /// <summary>
    /// Constructs a new <see cref="BufferDescription"/>.
    /// </summary>
    /// <param name="sizeInBytes">The desired capacity, in bytes.</param>
    /// <param name="usage">Indicates how the <see cref="DeviceBuffer"/> will be used.</param>
    /// <param name="structureByteStride">For structured buffers, this value indicates the size in bytes of a single
    /// structure element, and must be non-zero. For all other buffer types, this value must be zero.</param>
    /// <remarks>
    /// <see cref="RawBuffer"/> is set to <see langword="false"/>.
    /// </remarks>
    public BufferDescription(uint sizeInBytes, BufferUsage usage, uint structureByteStride)
    {
        SizeInBytes = sizeInBytes;
        Usage = usage;
        StructureByteStride = structureByteStride;
        RawBuffer = false;
        InitialData = IntPtr.Zero;
    }

    /// <summary>
    /// Constructs a new <see cref="BufferDescription"/>.
    /// </summary>
    /// <param name="sizeInBytes">The desired capacity, in bytes.</param>
    /// <param name="usage">Indicates how the <see cref="DeviceBuffer"/> will be used.</param>
    /// <param name="structureByteStride">For structured buffers, this value indicates the size in bytes of a single
    /// structure element, and must be non-zero. For all other buffer types, this value must be zero.</param>
    /// <param name="rawBuffer">Indicates that this is a raw buffer. This should be combined with
    /// <see cref="BufferUsage.StructuredBufferReadWrite"/>. This affects how the buffer is bound in the D3D11 backend.
    /// </param>
    public BufferDescription(uint sizeInBytes, BufferUsage usage, uint structureByteStride, bool rawBuffer)
    {
        SizeInBytes = sizeInBytes;
        Usage = usage;
        StructureByteStride = structureByteStride;
        RawBuffer = rawBuffer;
        InitialData = IntPtr.Zero;
    }

    /// <summary>
    /// Element-wise equality.
    /// </summary>
    /// <param name="other">The instance to compare to.</param>
    /// <returns>True if all elements are equal; false otherswise.</returns>
    public bool Equals(BufferDescription other)
    {
        return SizeInBytes.Equals(other.SizeInBytes)
            && Usage == other.Usage
            && StructureByteStride.Equals(other.StructureByteStride)
            && RawBuffer.Equals(other.RawBuffer)
            && InitialData == other.InitialData;
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer that is the hash code for this instance.</returns>
    public override int GetHashCode()
    {
        return HashHelper.Combine(
            SizeInBytes.GetHashCode(),
            (int)Usage,
            StructureByteStride.GetHashCode(),
            RawBuffer.GetHashCode(),
            InitialData.GetHashCode());
    }
}
