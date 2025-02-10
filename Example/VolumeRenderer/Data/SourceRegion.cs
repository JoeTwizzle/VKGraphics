//using BrickEngine.Mathematics;
//using System.Runtime.InteropServices;

//namespace BrickEngine.Example.VolumeRenderer.Data
//{
//    // AABB with offset into buffer which contains data
//    [StructLayout(LayoutKind.Sequential, Size = sizeof(int) * 8)]
//    struct SourceRegion
//    {
//        public Box3i SourceDimensions;
//        /// <summary>
//        /// Buffer Index offset for floats
//        /// </summary>
//        public long BufferOffset;

//        public SourceRegion(Box3i sourceBox, long bufferOffset)
//        {
//            SourceDimensions = sourceBox;
//            BufferOffset = bufferOffset;
//        }
//    }
//}
