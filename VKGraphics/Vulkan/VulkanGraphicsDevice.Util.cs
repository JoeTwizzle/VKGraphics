using OpenTK.Graphics.Vulkan;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace VKGraphics.Vulkan;

internal partial class VulkanGraphicsDevice
{
    private static unsafe nint GetInstanceProcAddr(VkInstance instance, ReadOnlySpan<byte> name)
    {
        fixed (byte* pName = name)
        {
            if (pName[name.Length] != 0)
            {
                return RetryWithPooledNullTerminator(instance, name);

                [MethodImpl(MethodImplOptions.NoInlining)]
                static nint RetryWithPooledNullTerminator(VkInstance instance, ReadOnlySpan<byte> name)
                {
                    var arr = ArrayPool<byte>.Shared.Rent(name.Length + 1);
                    name.CopyTo(arr);
                    arr[name.Length] = 0;
                    var result = GetInstanceProcAddr(instance, arr.AsSpan(0, name.Length));
                    ArrayPool<byte>.Shared.Return(arr);
                    return result;
                }
            }

            return Vk.GetInstanceProcAddr(instance, pName);
        }
    }
    private static unsafe nint GetInstanceProcAddr(VkInstance instance, ReadOnlySpan<byte> name1, ReadOnlySpan<byte> name2)
    {
        var result = GetInstanceProcAddr(instance, name1);
        if (result == 0)
        {
            result = GetInstanceProcAddr(instance, name2);
        }
        return result;
    }
    private unsafe nint GetInstanceProcAddr(ReadOnlySpan<byte> name)
        => GetInstanceProcAddr(_deviceCreateState.Instance, name);
    private unsafe nint GetInstanceProcAddr(ReadOnlySpan<byte> name1, ReadOnlySpan<byte> name2)
    {
        var result = GetInstanceProcAddr(name1);
        if (result == 0)
        {
            result = GetInstanceProcAddr(name2);
        }
        return result;
    }

    private static unsafe nint GetDeviceProcAddr(VkDevice device, ReadOnlySpan<byte> name)
    {
        fixed (byte* pName = name)
        {
            if (pName[name.Length] != 0)
            {
                return RetryWithPooledNullTerminator(device, name);

                [MethodImpl(MethodImplOptions.NoInlining)]
                static nint RetryWithPooledNullTerminator(VkDevice device, ReadOnlySpan<byte> name)
                {
                    var arr = ArrayPool<byte>.Shared.Rent(name.Length + 1);
                    name.CopyTo(arr);
                    arr[name.Length] = 0;
                    var result = GetDeviceProcAddr(device, arr.AsSpan(0, name.Length));
                    ArrayPool<byte>.Shared.Return(arr);
                    return result;
                }
            }

            return Vk.GetDeviceProcAddr(device, pName);
        }
    }
    private static unsafe nint GetDeviceProcAddr(VkDevice device, ReadOnlySpan<byte> name1, ReadOnlySpan<byte> name2)
    {
        var result = GetDeviceProcAddr(device, name1);
        if (result == 0)
        {
            result = GetDeviceProcAddr(device, name2);
        }
        return result;
    }
    //private unsafe nint GetDeviceProcAddr(ReadOnlySpan<byte> name)
    //    => GetDeviceProcAddr(_deviceCreateState.Device, name);
    //private unsafe nint GetDeviceProcAddr(ReadOnlySpan<byte> name1, ReadOnlySpan<byte> name2)
    //{
    //    var result = GetDeviceProcAddr(name1);
    //    if (result == 0)
    //    {
    //        result = GetDeviceProcAddr(name2);
    //    }
    //    return result;
    //}
    private unsafe nint GetDeviceProcAddr(ReadOnlySpan<byte> name)
        => GetInstanceProcAddr(_deviceCreateState.Instance, name);
    private unsafe nint GetDeviceProcAddr(ReadOnlySpan<byte> name1, ReadOnlySpan<byte> name2)
    {
        var result = GetDeviceProcAddr(name1);
        if (result == 0)
        {
            result = GetDeviceProcAddr(name2);
        }
        return result;
    }
}
