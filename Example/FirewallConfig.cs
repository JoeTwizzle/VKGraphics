using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Example;

class FirewallConfig
{
    public static void EnsureRuleIsSet()
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!FirewallRuleExists("Allow RotationReceiver UDP 6000"))
                {
                    AddFirewallRule();
                    Console.WriteLine("Firewall rule added.");
                }
                else
                {
                    Console.WriteLine("Firewall rule already exists.");
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("bash", "-c \"sudo ufw allow 6000/udp\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Console.WriteLine("Please add an incoming port exception for udp 6000");
                //Process.Start("bash", "-c \"echo 'pass in proto udp from any to any port 6000' | sudo tee -a /etc/pf.conf && sudo pfctl -f /etc/pf.conf\"");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to configure firewall: {ex.Message}");
        }
    }

    private static bool FirewallRuleExists(string ruleName)
    {
        try
        {
            ProcessStartInfo psi = new()
            {
                FileName = "netsh",
                Arguments = "advfirewall firewall show rule name=\"" + ruleName + "\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using Process? process = Process.Start(psi);
            if (process == null)
                return false;

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return output.Contains(ruleName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool AddFirewallRule()
    {
        ProcessStartInfo psi = new()
        {
            FileName = "netsh",
            Arguments = $"advfirewall firewall add rule name=\"Allow RotationReceiver UDP 6000\" dir=in action=allow program=\"{Path.GetFullPath(Environment.ProcessPath ?? "")}\" protocol=UDP localport=6000",
            Verb = "runas",  // Run as Administrator
            UseShellExecute = true,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi);
        if (p == null)
        {
            return false;
        }

        p.WaitForExit();
        return p.ExitCode == 0;
    }
}
