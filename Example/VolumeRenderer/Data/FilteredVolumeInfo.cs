//using BrickEngine.Mathematics;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace BrickEngine.Example.VolumeRenderer.Data
//{
//    sealed class FilteredVolumeInfo
//    {
//        public readonly GlobalVolumeInfo GlobalVolumeInfo;
//        public readonly Box3i Dimensions;
//        public readonly long VoxelCount;
//        public readonly float MinValue;
//        public readonly float MaxValue;
//        public readonly SourceRegion[] SourceRegions;
//        public unsafe long HeaderSize => GlobalVolumeInfo.HeaderDataLength + sizeof(int) + (sizeof(Box3i) + sizeof(long)) * SourceRegions.Length;

//        public FilteredVolumeInfo(GlobalVolumeInfo globalVolumeInfo, Box3i dimensions, long voxelCount, float minValue, float maxValue, SourceRegion[] sourceRegions)
//        {
//            GlobalVolumeInfo = globalVolumeInfo;
//            Dimensions = dimensions;
//            VoxelCount = voxelCount;
//            MinValue = minValue;
//            MaxValue = maxValue;
//            SourceRegions = sourceRegions;
//        }
//    }
//}
