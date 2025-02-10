//using BrickEngine.Core;
//using BrickEngine.Core.Mathematics;
//using BrickEngine.Example.VolumeRenderer.Data;
//using BrickEngine.Mathematics;
//using System.Diagnostics;
//using System.IO.MemoryMappedFiles;
//using System.Numerics;
//using System.Reflection.Metadata.Ecma335;
//using System.Runtime.InteropServices;

//namespace BrickEngine.Example.DataStructures.Octree
//{
//    internal struct Octree
//    {
//        public uint Nodes;
//        public uint Voxels;
//        public UnmanagedMemoryRange OctreeNodes;

//        public Octree(uint nodes, uint voxels, UnmanagedMemoryRange octreeNodes)
//        {
//            Nodes = nodes;
//            Voxels = voxels;
//            OctreeNodes = octreeNodes;
//        }
//    }

//    internal static class OctreeBuilder
//    {
//        public static unsafe Octree Build(FilteredVolumeInfo filteredVolumeInfo, MemoryMappedFile filteredFile)
//        {
//            var size = Max(Pow2(filteredVolumeInfo.GlobalVolumeInfo.Dimensions.Max));

//            int maxDepth = BitOperations.Log2((uint)size);
//            ulong treeIndex = PosToIndex(filteredVolumeInfo.GlobalVolumeInfo.Dimensions.Max);
//            uint layerStartOffset = 0;
//            uint neighbourCount = GetOctalDigit(treeIndex, 0);
//            int i = 1;
//            do
//            {
//                if (i >= maxDepth)
//                {
//                    break;
//                }

//                neighbourCount = neighbourCount * 8 + GetOctalDigit(treeIndex, i);
//                layerStartOffset += LayerSize(i);
//                i++;
//            } while (true);

//            uint nodeCount = layerStartOffset + neighbourCount;
//            if (nodeCount > int.MaxValue) //larger than max gpu buffer size
//            {
//                throw new Exception();
//            }

//            byte* destPtr = (byte*)NativeMemory.AllocZeroed((nuint)nodeCount);
//            //float threshold = 0; //what counts as empty voxels
//            float threshold = 0.001f; //what counts as empty voxels
//            using var view = filteredFile.CreateViewAccessor(filteredVolumeInfo.HeaderSize, 0, MemoryMappedFileAccess.Read);
//            byte* srcPtr = null;
//            view.SafeMemoryMappedViewHandle.AcquirePointer(ref srcPtr);
//            srcPtr += view.PointerOffset;
//            uint voxelCount = Write(filteredVolumeInfo, maxDepth, (float*)srcPtr, threshold, destPtr);
//            view.SafeMemoryMappedViewHandle.ReleasePointer();
//            return new Octree(nodeCount, voxelCount, new UnmanagedMemoryRange(destPtr, nodeCount));
//        }

//        private static unsafe uint Write(FilteredVolumeInfo info, int treeSize, float* filteredData, float threshold, byte* dest)
//        {
//            var regions = info.SourceRegions;
//            uint count = 0;
//            for (int i = 0; i < regions.Length; i++)
//            {
//                count += WriteValuesGreaterThanThreshold(regions[i].SourceDimensions, treeSize, filteredData + regions[i].BufferOffset, dest, threshold);
//            }
//            return count;
//        }

//        static uint LayerSize(int layer)
//        {
//            return 1u << (layer * 3); //-> 1, 8, 64, 512, 4096
//        }

//        static byte GetOctalDigit(ulong treeIndex, int layer)
//        {
//            return (byte)((treeIndex >>> (layer * 3)) & 0b111);
//        }

//        private static unsafe uint WriteValuesGreaterThanThreshold(Box3i box, int layerCount, float* localVolumeData, byte* octreeNodes, float threshold)
//        {
//            uint count = 0;
//            var size = box.Size;
//            for (int z = 0; z < size.Z; z++)
//            {
//                for (int y = 0; y < size.Y; y++)
//                {
//                    for (int x = 0; x < size.X; x++)
//                    {
//                        Vector3i localGridPos = new Vector3i(x, y, z);
//                        float data = localVolumeData[((localGridPos.Z) * size.Y + localGridPos.Y) * size.X + localGridPos.X];
//                        if (data > threshold)
//                        {
//                            count++;
//                            checked
//                            {
//                                var pos = localGridPos + box.Min;
//                                ulong treeIndex = PosToIndex(pos);
//                                uint layerStartOffset = 0;
//                                int currentLayer = 1;
//                                uint neighbourCount = GetOctalDigit(treeIndex, layerCount - 1);
//                                do
//                                {
//                                    uint bitIndex = layerStartOffset + neighbourCount;
//                                    SetBit(bitIndex, octreeNodes);
//                                    if (currentLayer >= layerCount)
//                                    {
//                                        break;
//                                    }
//                                    neighbourCount = neighbourCount * 8 + GetOctalDigit(treeIndex, layerCount - currentLayer - 1);
//                                    layerStartOffset += LayerSize(currentLayer);
//                                    currentLayer++;
//                                } while (true);
//                            }
//                        }
//                    }
//                }
//            }
//            return count;
//        }

//        static unsafe void SetBit(uint bitIndex, byte* octreeNodes)
//        {
//            uint index = bitIndex >>> 3;
//            int remainder = (int)(bitIndex & 0b111);
//            octreeNodes[index] |= (byte)(1 << remainder);
//        }

//        //Converts 3d pos to index in octree
//        // (x,y,z) -> bin(x1y1z1x0y0z0) etc
//        static ulong PosToIndex(Vector3i pos)
//        {
//            static ulong PadZeros(uint val)
//            {
//                ulong result = 0;

//                for (int i = 0; i < 21; i++)
//                {
//                    // Extract the i-th bit from the input
//                    uint bit = (val >> i) & 1;

//                    // Set the corresponding bits in the result with two zeros in between
//                    result |= bit << (3 * i);
//                }
//                return result;
//            }
//            return (PadZeros((uint)pos.X) << 2) | (PadZeros((uint)pos.Y) << 1) | (PadZeros((uint)pos.Z) << 0);
//        }

//        static Box3i GetOctant(int index, Box3i parent)
//        {
//            var offset = GetOffset(index) * parent.HalfSize;
//            return new Box3i(parent.Min + offset, parent.Min + offset + parent.HalfSize);
//        }

//        static Vector3i GetOffset(int index)
//        {
//            //BFL BFR TFL TFR BBL BBR TBL TBR
//            int offsetX = (index >>> 0 & 0b1);
//            int offsetY = (index >>> 1 & 0b1);
//            int offsetZ = (index >>> 2 & 0b1);

//            return new Vector3i(offsetX, offsetY, offsetZ);
//        }

//        static Box3i GrowToPow2(Box3i box)
//        {
//            var maxX = Pow2(box.Max.X);
//            var maxY = Pow2(box.Max.Y);
//            var maxZ = Pow2(box.Max.Z);

//            var max = Math.Max(maxX, Math.Max(maxY, maxZ));

//            var minX = Pow2(box.Min.X);
//            var minY = Pow2(box.Min.Y);
//            var minZ = Pow2(box.Min.Z);

//            var min = Math.Min(minX, Math.Min(minY, minZ));
//            //return new Box3i(minX, minY, minZ, maxX, maxY, maxZ);
//            return new Box3i(min, min, min, max, max, max);
//        }

//        static int Max(Vector3i vec)
//        {
//            return Math.Max(vec.X, Math.Max(vec.Y, vec.Z));
//        }

//        static int Min(Vector3i vec)
//        {
//            return Math.Min(vec.X, Math.Min(vec.Y, vec.Z));
//        }

//        public static Vector3i Pow2(Vector3i vec)
//        {
//            var maxX = Pow2(vec.X);
//            var maxY = Pow2(vec.Y);
//            var maxZ = Pow2(vec.Z);
//            return new Vector3i(maxX, maxY, maxZ);
//        }

//        public static int Pow2(int val)
//        {
//            bool isNegative = val < 0;
//            val = isNegative ? -val : val;
//            uint res = BitOperations.RoundUpToPowerOf2((uint)val);
//            return (isNegative ? -1 : 1) * (int)res;
//        }
//    }
//}






