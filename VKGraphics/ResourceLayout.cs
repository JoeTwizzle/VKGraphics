﻿namespace VKGraphics;

/// <summary>
///     A device resource which describes the layout and kind of <see cref="IBindableResource" /> objects available
///     to a shader set.
///     See <see cref="ResourceLayoutDescription" />.
/// </summary>
public abstract class ResourceLayout : IDeviceResource, IDisposable
{
    /// <summary>
    ///     A bool indicating whether this instance has been disposed.
    /// </summary>
    public abstract bool IsDisposed { get; }

    /// <summary>
    ///     A string identifying this instance. Can be used to differentiate between objects in graphics debuggers and other
    ///     tools.
    /// </summary>
    public abstract string? Name { get; set; }

    internal ResourceLayout(ref ResourceLayoutDescription description)
    {
        Description = description;

        foreach (var element in description.Elements)
        {
            if ((element.Options & ResourceLayoutElementOptions.DynamicBinding) != 0)
            {
                DynamicBufferCount += 1;
            }
        }
    }

    #region Disposal

    /// <summary>
    ///     Frees unmanaged device resources controlled by this instance.
    /// </summary>
    public abstract void Dispose();

    #endregion


    internal readonly ResourceLayoutDescription Description;
    internal readonly uint DynamicBufferCount;
}
