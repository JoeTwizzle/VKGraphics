//using BrickEngine.Mathematics;
//using Microsoft.CodeAnalysis.CSharp.Syntax;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Runtime.InteropServices;
//using System.Text;
//using System.Threading.Tasks;

//namespace BrickEngine.Example.RayTracing.Octree
//{
//    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 8 * sizeof(float))]
//    struct LinearOctreeNode
//    {
//        public Box3 BoundingBox;
//        public int FirstChildIndex;
//        public short SourceRegionIndex;
//        public short SourceRegionCount;

//        public LinearOctreeNode(Box3 boundingBox, int firstChildIndex, short sourceRegionIndex, short sourceRegionCount)
//        {
//            BoundingBox = boundingBox;
//            FirstChildIndex = firstChildIndex;
//            SourceRegionIndex = sourceRegionIndex;
//            SourceRegionCount = sourceRegionCount;
//        }
//    }
//}
