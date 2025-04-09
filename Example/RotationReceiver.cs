using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Numerics;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Numerics.Tensors;

namespace Example;

class RotationReceiver : IDisposable
{
    private const int ListenPort = 6000;
    private const int SendPort = 12356;
    object lockObj = new();
    Quaternion recievedRotation = Quaternion.Identity;
    bool shouldRun;
    public RotationReceiver()
    {
        shouldRun = true;
        var t1 = new Thread(Broadcast);
        t1.Start();
        var t2 = new Thread(Listen);
        t2.Start();
    }

    public Quaternion GetRotation()
    {
        lock (lockObj)
        {
            return recievedRotation;
        }
    }

    private async void Listen()
    {
        await ListenAsync();
    }
    private async Task ListenAsync()
    {
        IPEndPoint remoteEndPoint = new(IPAddress.Any, ListenPort);
        using UdpClient udpClient = new(remoteEndPoint);
        while (shouldRun)
        {
            try
            {
                var res = await udpClient.ReceiveAsync(CancellationTokenSource.Token);
                byte[] receivedBytes = res.Buffer;
                unsafe
                {
                    if (receivedBytes.Length == sizeof(Quaternion) + 1)
                    {
                        var data = receivedBytes.AsSpan(0, sizeof(Quaternion));
                        var checksum = TensorPrimitives.Sum(data);
                        if (checksum == receivedBytes[sizeof(Quaternion)])
                        {
                            Quaternion q;
                            data.CopyTo(new Span<byte>(&q, sizeof(Quaternion)));
                            lock (lockObj)
                            {
                                recievedRotation = q;
                            }
                            continue;
                        }
                    }
                }

                Console.WriteLine("Invalid data format.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error receiving data: {ex.Message}");
            }
        }
    }

    private async void Broadcast()
    {
        await BroadcastBody();
    }
    static readonly CancellationTokenSource CancellationTokenSource = new();
    static readonly IPAddress s_multicastAddress = IPAddress.Parse("224.0.2.60");
    private async ValueTask BroadcastBody()
    {
        using UdpClient udpClient = new();
        udpClient.EnableBroadcast = true;
        udpClient.JoinMulticastGroup(s_multicastAddress);
        var ep = new IPEndPoint(s_multicastAddress, SendPort);
        using PeriodicTimer p = new(TimeSpan.FromMilliseconds(5000));
        while (shouldRun)
        {
            try
            {
                udpClient.Send("RotationHostServer"u8, ep);
                await p.WaitForNextTickAsync(CancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
        }
    }

    public void Dispose()
    {
        CancellationTokenSource.Cancel();
        shouldRun = false;
    }
}
