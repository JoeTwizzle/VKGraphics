using System.Diagnostics;

namespace BrickEngine.Example
{
    enum ActiveVolumeRenderer
    {
        FastVoxelTraversal,
        OctreeTraversal,
        SvoTraversal,
        Dynamic
    }

    internal static class Program
    {
        const string bigVol = "E:\\Data\\SourceVolumes\\sky_dev\\sky_dev_v2.fits";
        const string bigCat = "E:\\Data\\SourceVolumes\\sky_dev\\catalogue.res_cat3.xml";
        const string smallVol = "E:\\Data\\SourceVolumes\\n4565\\n4565_lincube_big.fits";
        const string smallCat = "E:\\Data\\SourceVolumes\\n4565\\sofia_output\\outname_cat.xml";

        static void Main(string[] args)
        {
            var debug = args.Contains("-d");

            debug |= Debugger.IsAttached;
#if DEBUG
            debug = true;
#endif

            string vol = smallVol;
            string cat = smallCat;

            int volIdx = Array.IndexOf(args, "-vol");
            if (volIdx >= 0 && args.Length > volIdx + 1 && File.Exists(args[volIdx + 1]))
            {
                vol = args[volIdx + 1];
            }
            int catIdx = Array.IndexOf(args, "-cat");
            if (catIdx >= 0 && args.Length > catIdx + 1 && File.Exists(args[catIdx + 1]))
            {
                cat = args[catIdx + 1];
            }
            new MyGameWindow(/*new(debug, false),*/ vol, cat, ActiveVolumeRenderer.Dynamic).Run();
        }
    }
}