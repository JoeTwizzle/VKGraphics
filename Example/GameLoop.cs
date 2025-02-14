using OpenTK.Platform;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Example;
internal sealed class GameLoop
{
    public bool ShouldRun;

    const double TicksToSeconds = 1e-7;
    public void Run(ref Metrics metrics, Action updateCallback)
    {
        long prev = Stopwatch.GetTimestamp();
        while (ShouldRun)
        {
            long current = Stopwatch.GetTimestamp();
            metrics.DeltaTimeFull = (current - prev) * TicksToSeconds; //Ticks to seconds constant
            metrics.DeltaTime = (float)metrics.DeltaTimeFull;
            prev = current;
            updateCallback();
        }
    }
}
