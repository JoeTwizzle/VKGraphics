namespace VKGraphics;

/// <summary>
/// A bindable device resource which controls how texture values are sampled within a shader.
/// See <see cref="SamplerDescription"/>.
/// </summary>
/// <seealso cref="BindableResource"/>
public abstract class Sampler : DeviceResource, IDisposable
{
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
}
