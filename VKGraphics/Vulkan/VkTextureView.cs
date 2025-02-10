


namespace VKGraphics.Vulkan;

internal unsafe class VkTextureView : TextureView
{
    public VkImageView ImageView => imageView;

    public new VkTexture Target => (VkTexture)base.Target;

    public ResourceRefCount RefCount { get; }

    public override bool IsDisposed => destroyed;

    public override string Name
    {
        get => name;
        set
        {
            name = value;
            gd.SetResourceName(this, value);
        }
    }

    private readonly VkGraphicsDevice gd;
    private readonly VkImageView imageView;
    private bool destroyed;
    private string name;

    public VkTextureView(VkGraphicsDevice gd, ref TextureViewDescription description)
        : base(ref description)
    {
        this.gd = gd;
        var imageViewCi = new VkImageViewCreateInfo();
        var tex = Util.AssertSubtype<Texture, VkTexture>(description.Target);
        imageViewCi.image = tex.OptimalDeviceImage;
        imageViewCi.format = VkFormats.VdToVkPixelFormat(Format, (Target.Usage & TextureUsage.DepthStencil) != 0);

        var aspectFlags = (description.Target.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil
            ? VkImageAspectFlagBits.ImageAspectDepthBit
            : VkImageAspectFlagBits.ImageAspectColorBit;

        imageViewCi.subresourceRange = new VkImageSubresourceRange(
            aspectFlags,
            description.BaseMipLevel,
            description.MipLevels,
            description.BaseArrayLayer,
            description.ArrayLayers);

        if ((tex.Usage & TextureUsage.Cubemap) == TextureUsage.Cubemap)
        {
            imageViewCi.viewType = description.ArrayLayers == 1 ? VkImageViewType.ImageViewTypeCube : VkImageViewType.ImageViewTypeCubeArray;
            imageViewCi.subresourceRange.layerCount *= 6;
        }
        else
        {
            switch (tex.Type)
            {
                case TextureType.Texture1D:
                    imageViewCi.viewType = description.ArrayLayers == 1
                        ? VkImageViewType.ImageViewType1d
                        : VkImageViewType.ImageViewType1dArray;
                    break;

                case TextureType.Texture2D:
                    imageViewCi.viewType = description.ArrayLayers == 1
                        ? VkImageViewType.ImageViewType2d
                        : VkImageViewType.ImageViewType2dArray;
                    break;

                case TextureType.Texture3D:
                    imageViewCi.viewType = VkImageViewType.ImageViewType3d;
                    break;
            }
        }
        VkImageView vkimageView;
        Vk.CreateImageView(this.gd.Device, &imageViewCi, null, &vkimageView);
        imageView = vkimageView;
        RefCount = new ResourceRefCount(disposeCore);
    }

    #region Disposal

    public override void Dispose()
    {
        RefCount.Decrement();
    }

    #endregion

    private void disposeCore()
    {
        if (!destroyed)
        {
            destroyed = true;
            Vk.DestroyImageView(gd.Device, ImageView, null);
        }
    }
}
