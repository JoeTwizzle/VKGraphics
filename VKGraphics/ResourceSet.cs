namespace VKGraphics;

/// <summary>
/// A device resource used to bind a particular set of <see cref="BindableResource"/> objects to a <see cref="CommandList"/>.
/// See <see cref="ResourceSetDescription"/>.
/// </summary>
public abstract class ResourceSet : DeviceResource, IDisposable
{
    internal ResourceSet(in ResourceSetDescription description)
    {
#if VALIDATE_USAGE
        Layout = description.Layout;
        Resources = description.BoundResources;
#endif
    }

    /// <inheritdoc/>
    public abstract string? Name { get; set; }

    /// <summary>
    /// A bool indicating whether this instance has been disposed.
    /// </summary>
    public abstract bool IsDisposed { get; }

    /// <summary>
    /// Frees unmanaged device resources controlled by this instance.
    /// </summary>
    public abstract void Dispose();

#if VALIDATE_USAGE
    internal ResourceLayout Layout { get; }
    internal BindableResource[] Resources { get; }
#endif
}
