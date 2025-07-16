

using OpenTK.Platform;
using System.Collections.Generic;
using System.Diagnostics;

namespace Example;

/// <summary>
/// This is a demonstration of the Vulkan backend of Veldrid using OpenTK 5
/// The vulkan backend used was created by nike4613 https://github.com/nike4613/veldrid
/// </summary>
internal static class Program
{
    static void Main(string[] args)
    {
        FirewallConfig.EnsureRuleIsSet();

        using var game = new Game();
    }
}