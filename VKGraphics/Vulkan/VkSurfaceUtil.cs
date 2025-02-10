//using static VKGraphics.Vulkan.VulkanUtil;

//namespace VKGraphics.Vulkan;

//internal static unsafe class VkSurfaceUtil
//{
//    //internal static VkSurfaceKHR CreateSurface(VkGraphicsDevice gd, VkInstance instance, SwapchainSource swapchainSource)
//    //{
//    //    // TODO a null GD is passed from VkSurfaceSource.CreateSurface for compatibility
//    //    //      when VkSurfaceInfo is removed we do not have to handle gd == null anymore
//    //    bool doCheck = gd != null;

//    //    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VkKhrSurfaceExtensionName))
//    //    {
//    //        throw new VeldridException($"The required instance extension was not available: {CommonStrings.VkKhrSurfaceExtensionName}");
//    //    }

//    //    switch (swapchainSource)
//    //    {
//    //        case XlibSwapchainSource xlibSource:
//    //            if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VkKhrXlibSurfaceExtensionName))
//    //            {
//    //                throw new VeldridException($"The required instance extension was not available: {CommonStrings.VkKhrXlibSurfaceExtensionName}");
//    //            }

//    //            return createXlib(instance, xlibSource);

//    //        case WaylandSwapchainSource waylandSource:
//    //            if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VkKhrWaylandSurfaceExtensionName))
//    //            {
//    //                throw new VeldridException($"The required instance extension was not available: {CommonStrings.VkKhrWaylandSurfaceExtensionName}");
//    //            }

//    //            return createWayland(instance, waylandSource);

//    //        case Win32SwapchainSource win32Source:
//    //            if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VkKhrWin32SurfaceExtensionName))
//    //            {
//    //                throw new VeldridException($"The required instance extension was not available: {CommonStrings.VkKhrWin32SurfaceExtensionName}");
//    //            }

//    //            return createWin32(instance, win32Source);

//    //        //case AndroidSurfaceSwapchainSource androidSource:
//    //        //    if (doCheck && !gd.HasSurfaceExtension(CommonStrings.VkKhrAndroidSurfaceExtensionName))
//    //        //    {
//    //        //        throw new VeldridException($"The required instance extension was not available: {CommonStrings.VkKhrAndroidSurfaceExtensionName}");
//    //        //    }

//    //        //    return createAndroidSurface(instance, androidSource);

//    //        default:
//    //            throw new VeldridException("The provided SwapchainSource cannot be used to create a Vulkan surface.");
//    //    }
//    //}

//    //private static VkSurfaceKHR createWin32(VkInstance instance, Win32SwapchainSource win32Source)
//    //{
//    //    var surfaceCi = new VkWin32SurfaceCreateInfoKHR
//    //    {
//    //        hwnd = win32Source.Hwnd,
//    //        hinstance = win32Source.Hinstance
//    //    };
//    //    VkSurfaceKHR surface;

//    //    var result = Vk.CreateWin32SurfaceKHR(instance, &surfaceCi, null, &surface);
//    //    CheckResult(result);
//    //    return surface;
//    //}

//    //private static VkSurfaceKHR createXlib(VkInstance instance, XlibSwapchainSource xlibSource)
//    //{
//    //    var xsci = new VkXlibSurfaceCreateInfoKHR
//    //    {
//    //        dpy = xlibSource.Display,
//    //        window = (nuint)xlibSource.Window
//    //    };
//    //    VkSurfaceKHR surface;
//    //    var result = Vk.CreateXlibSurfaceKHR(instance, &xsci, null, &surface);
//    //    CheckResult(result);
//    //    return surface;
//    //}

//    //private static VkSurfaceKHR createWayland(VkInstance instance, WaylandSwapchainSource waylandSource)
//    //{
//    //    var wsci = new VkWaylandSurfaceCreateInfoKHR();
//    //    wsci.display = waylandSource.Display;
//    //    wsci.surface = waylandSource.Surface;
//    //    VkSurfaceKHR surface;
//    //    var result = Vk.CreateWaylandSurfaceKHR(instance, &wsci, null, &surface);
//    //    CheckResult(result);
//    //    return surface;
//    //}

//    //private static VkSurfaceKHR createAndroidSurface(VkInstance instance, AndroidSurfaceSwapchainSource androidSource)
//    //{
//    //    IntPtr aNativeWindow = AndroidRuntime.ANativeWindow_fromSurface(androidSource.JniEnv, androidSource.Surface);

//    //    var androidSurfaceCi = new VkAndroidSurfaceCreateInfoKHR();
//    //    androidSurfaceCi.window = aNativeWindow;
//    //    VkSurfaceKHR surface;
//    //    var result = Vk.CreateAndroidSurfaceKHR(instance, &androidSurfaceCi, null, &surface);
//    //    CheckResult(result);
//    //    return surface;
//    //}
//}
