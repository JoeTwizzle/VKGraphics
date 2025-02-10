//using BrickEngine.Core.Mathematics;
//using BrickEngine.Example.DataStructures.SparseOctree;
//using BrickEngine.Example.VolumeRenderer.Data;
//using BrickEngine.Mathematics;
//using Microsoft.CodeAnalysis.Operations;
//using System.Diagnostics;
//using System.IO.MemoryMappedFiles;
//using System.Numerics;
//using System.Runtime.CompilerServices;
//using System.Runtime.InteropServices;

//namespace BrickEngine.Example.DataStructures.Octree
//{
//    internal static class SparseVoxelOctreeBuilder
//    {
//        const uint MaxBufferSize = 2147483648;
//        const uint SubVolumes = 256;
//        const uint MaxChunkSize = MaxBufferSize / SubVolumes; //8388608 -> 128^3 float
//        const int BitsPerVoxel = sizeof(byte) * 8;

//        public static unsafe SparseVoxelOctree Build(FilteredVolumeInfo filteredVolumeInfo, MemoryMappedFile rawFile, bool allowCompression, bool allowCompleteBranch)
//        {
//            uint size = (uint)Max(Pow2(filteredVolumeInfo.GlobalVolumeInfo.Dimensions.Max));
//            int maxDepth = BitOperations.Log2(size);
//            //what counts as empty voxels
//            float threshold = 0.001f;
//            //float threshold = 0;
//            if (allowCompleteBranch && !allowCompression)
//            {
//                throw new ArgumentException("Must have compression if cuboid extraction is enabled");
//            }
//            using var view = rawFile.CreateViewAccessor(GlobalVolumeInfo.HeaderDataLength, 0, MemoryMappedFileAccess.Read);
//            byte* srcPtr = null;

//            view.SafeMemoryMappedViewHandle.AcquirePointer(ref srcPtr);

//            SparseOctreeNode root = SparseOctreeNode.Create();
//            var (voxelCount, nodesCount) = CreateTree(&root, (float*)srcPtr, filteredVolumeInfo, threshold, maxDepth);
//            if (allowCompleteBranch)
//            {
//                MarkCompleteBranches(&root);
//                ExtractCompleteBranches(&root, size);
//            }
//            view.SafeMemoryMappedViewHandle.ReleasePointer();

//            var info = FlattenSparseTree(filteredVolumeInfo.GlobalVolumeInfo, root, nodesCount, voxelCount, size, allowCompression);
//            root.Dispose();
//            //var treeData = new SparseVoxelOctree(info, nodesCount, voxelCount);
//            return info;
//        }

//        static unsafe void FillChunk(SparseOctreeNode* current, float* data, int index)
//        {
//            index <<= 3;
//            //Debug.Assert(current->OctreeNodeFlags.HasFlag(OctreeNodeFlags.HasFullSubchunk));
//            if (current->OctreeNodeFlags.HasFlag(OctreeNodeFlags.HasChildNodes))
//            {
//                for (int i = 0; i < 8; i++)
//                {
//                    FillChunk(&current->ChildNodes[i], data, index | i);
//                }
//            }
//            else if (current->OctreeNodeFlags.HasFlag(OctreeNodeFlags.HasChildVoxels))
//            {
//                //index *= 8;
//                for (int i = 0; i < 8; i++)
//                {
//                    data[index | i] = current->ChildValues->Values[i];
//                }
//            }
//        }

//        static unsafe void ExtractCompleteBranches(SparseOctreeNode* current, uint gridSize)
//        {
//            //Mark all non leaf voxels
//            if (current->OctreeNodeFlags.HasFlag(OctreeNodeFlags.HasFullSubtree) &&
//                current->OctreeNodeFlags.HasFlag(OctreeNodeFlags.HasChildNodes))
//            {
//                Debug.Assert(gridSize != 2);
//                Debug.Assert(gridSize != 1);
//                uint voxelCount = gridSize * gridSize * gridSize;
//                float* data = (float*)NativeMemory.AllocZeroed(voxelCount, sizeof(float));
//                //int index = 0;
//                FillChunk(current, data, 0);
//                //Debug.Assert(index == voxelCount);
//                SparseOctreeNode node = SparseOctreeNode.CreateFilled(data);
//                *current = node;
//            }
//            else if (!current->OctreeNodeFlags.HasFlag(OctreeNodeFlags.HasChildVoxels))
//            {
//                for (int i = 0; i < 8; i++)
//                {
//                    if (current->HasChildAt(i))
//                    {
//                        ExtractCompleteBranches(&current->ChildNodes[i], gridSize / 2);
//                    }
//                }
//            }
//        }

//        static unsafe bool MarkCompleteBranches(SparseOctreeNode* current)
//        {
//            if (current->OctreeNodeFlags.HasFlag(OctreeNodeFlags.HasChildVoxels))
//            {
//                //Full
//                bool res = current->ChildValues->Children == 0xFF;
//                return res;
//            }

//            bool potential = true;
//            for (int i = 0; i < 8; i++)
//            {
//                if (!current->HasChildAt(i) || !MarkCompleteBranches(&current->ChildNodes[i]))
//                {
//                    potential = false;
//                }
//            }
//            if (potential)
//            {
//                current->OctreeNodeFlags |= OctreeNodeFlags.HasFullSubtree;
//            }

//            return potential;
//        }

//        static unsafe SparseVoxelOctree FlattenSparseTree(GlobalVolumeInfo globalVolumeInfo, SparseOctreeNode root, int nodeCount, int voxelCount, uint size, bool allowCompression)
//        {
//            LinearizedSparseNode* destPtr =
//                (LinearizedSparseNode*)NativeMemory.AllocZeroed((nuint)(nodeCount * sizeof(int))); // one bit per voxel

//            float* voxelDataPtr =
//                (float*)NativeMemory.AllocZeroed((nuint)(voxelCount * sizeof(float)));

//            int nodeStorageOffset = 1;
//            int voxelStorageOffset = 0;

//            //Accellerated DFS
//            uint rootNodeInfo = Flatten(globalVolumeInfo,
//                                      destPtr,
//                                      voxelDataPtr,
//                                      root,
//                                      ref nodeStorageOffset,
//                                      ref voxelStorageOffset,
//                                      size,
//                                      allowCompression);

//            destPtr[0] = new LinearizedSparseNode(rootNodeInfo);
//            if (allowCompression)
//            {
//                voxelDataPtr = (float*)NativeMemory.Realloc(voxelDataPtr, (nuint)(voxelStorageOffset * sizeof(float)));
//            }
//            return new SparseVoxelOctree(new SparseTreeInfo(destPtr, voxelDataPtr),
//                                         nodeStorageOffset,
//                                         voxelStorageOffset);
//        }

//        static unsafe uint Flatten(GlobalVolumeInfo globalVolumeInfo,
//                                 LinearizedSparseNode* nodeDataPtr,
//                                 float* voxelDataPtr,
//                                 SparseOctreeNode currentNode,
//                                 ref int nodeStorageOffset,
//                                 ref int voxelStorageOffset,
//                                 uint size,
//                                 bool allowCompression)
//        {
//            if (currentNode.OctreeNodeFlags.HasFlag(OctreeNodeFlags.HasChildNodes))
//            {
//                return EncodeNode(globalVolumeInfo, nodeDataPtr, voxelDataPtr, currentNode, ref nodeStorageOffset, ref voxelStorageOffset, size, allowCompression);
//            }
//            else
//            {
//                Debug.Assert(currentNode.OctreeNodeFlags.HasFlag(OctreeNodeFlags.HasChildVoxels));
//                if (currentNode.OctreeNodeFlags.HasFlag(OctreeNodeFlags.HasFullSubtree))
//                {
//                    return EncodeFullNode(globalVolumeInfo, voxelDataPtr, currentNode, ref voxelStorageOffset, size);
//                }
//                else
//                {
//                    return EncodeLeafNode(globalVolumeInfo, voxelDataPtr, currentNode, ref voxelStorageOffset, allowCompression);
//                }
//            }
//        }

//        private static unsafe uint EncodeNode(GlobalVolumeInfo globalVolumeInfo, LinearizedSparseNode* nodeDataPtr, float* voxelDataPtr, SparseOctreeNode currentNode, ref int nodeStorageOffset, ref int voxelStorageOffset, uint size, bool allowCompression)
//        {
//            SparseOctreeNode* childPtr = currentNode.ChildNodes;
//            uint ownStorageOffset = (uint)nodeStorageOffset;
//            uint childBits = 0;
//            for (int i = 0; i < 8; i++)
//            {
//                if (currentNode.HasChildAt(i)) //child exists
//                {
//                    childBits |= (1u << i);
//                    nodeStorageOffset++; //Reserve space for stored children
//                }
//            }
//            int children = 0;
//            for (int i = 0; i < 8; i++)
//            {
//                if (currentNode.HasChildAt(i)) //child exists
//                {
//                    uint info = Flatten(globalVolumeInfo,
//                                      nodeDataPtr,
//                                      voxelDataPtr,
//                                      childPtr[i],
//                                      ref nodeStorageOffset,
//                                      ref voxelStorageOffset,
//                                      size / 2,
//                                      allowCompression);

//                    uint a = info & 0x00FFFFFF;
//                    uint c = info & 0xFF000000;
//                    if (!currentNode.ChildNodes[i].OctreeNodeFlags.HasFlag(OctreeNodeFlags.HasChildVoxels))
//                    {
//                        a -= ownStorageOffset;
//                    }
//                    nodeDataPtr[ownStorageOffset + children] = new LinearizedSparseNode(c | a);
//                    children++;
//                }
//            }
//            CheckRange(ownStorageOffset);
//            return (childBits << 24) | ownStorageOffset;
//        }

//        private static unsafe uint EncodeLeafNode(GlobalVolumeInfo globalVolumeInfo, float* voxelDataPtr, SparseOctreeNode currentNode, ref int voxelStorageOffset, bool allowCompression)
//        {
//            LeafNode* voxelValuesPtr = currentNode.ChildValues;
//            uint childBits = voxelValuesPtr->Children;
//            uint childCount = (uint)BitOperations.PopCount(childBits);
//            uint voxelStart = (uint)voxelStorageOffset;
//            if (!allowCompression || childCount <= 3)
//            {
//                for (int i = 0; i < 8; i++)
//                {
//                    if (voxelValuesPtr->HasChildAt(i)) //has child
//                    {
//                        voxelDataPtr[voxelStorageOffset++] = voxelValuesPtr->Values[i];
//                    }
//                }
//                return (childBits << 24) | voxelStart;
//            }

//            Span<float> blockData = stackalloc float[8];
//            int count = 0;
//            float minBlock = float.MaxValue;
//            float maxBlock = -float.MaxValue;
//            for (int i = 0; i < 8; i++)
//            {
//                if (voxelValuesPtr->HasChildAt(i)) //has child
//                {
//                    float val = voxelValuesPtr->Values[i];
//                    blockData[count++] = val;
//                    maxBlock = Math.Max(val, maxBlock);
//                    minBlock = Math.Min(val, minBlock);
//                    //voxelDataPtr[voxelStorageOffset++] = voxelValuesPtr[1 + i];
//                }
//            }

//            double minData = minBlock - globalVolumeInfo.MinValue;
//            double maxData = maxBlock - globalVolumeInfo.MinValue;
//            float globalScale = (globalVolumeInfo.MaxValue - globalVolumeInfo.MinValue);
//            double scaledMin = minData / globalScale;
//            double scaledMax = maxData / globalScale;
//            ushort packedMin = (ushort)Math.Round(scaledMin * 65535.0);
//            ushort packedMax = (ushort)Math.Round(scaledMax * 65535.0);
//            int pack = packedMin | (packedMax << 16);
//            voxelDataPtr[voxelStorageOffset++] = Unsafe.BitCast<int, float>(pack);
//            int bits = 0;
//            for (int i = 0; i < count; i++)
//            {
//                double data = blockData[i] - minBlock;
//                double scale = maxBlock - minBlock;

//                //Scale to range 0 - 1
//                double scaledValue = data / scale;
//                byte encodedValue = (byte)Math.Round(scaledValue * 255.0);
//                double decodedValue = encodedValue / 255.0;
//                double error = Math.Abs(scaledValue - decodedValue);
//                if (error > 0.01)
//                {
//                    Console.WriteLine("More than 1% error");
//                }
//                //Console.WriteLine(encodedValue);
//                bits |= (encodedValue << ((i % 4) * 8));
//                if ((i + 1) % 4 == 0)
//                {
//                    voxelDataPtr[voxelStorageOffset++] = Unsafe.BitCast<int, float>(bits);
//                    bits = 0;
//                }
//            }
//            CheckRange(voxelStart);
//            return (childBits << 24) | voxelStart;
//        }

//        private static unsafe uint EncodeFullNode(GlobalVolumeInfo globalVolumeInfo, float* voxelDataPtr, SparseOctreeNode currentNode, ref int voxelStorageOffset, uint size)
//        {
//            uint voxelStart = (uint)voxelStorageOffset;
//            uint count = size * size * size;
//            float minBlock = float.MaxValue;
//            float maxBlock = -float.MaxValue;
//            for (int i = 0; i < count; i++)
//            {
//                minBlock = Math.Min(minBlock, currentNode.ChunkValues[i]);
//                maxBlock = Math.Max(maxBlock, currentNode.ChunkValues[i]);
//            }
//            double minData = minBlock - globalVolumeInfo.MinValue;
//            double maxData = maxBlock - globalVolumeInfo.MinValue;
//            float globalScale = (globalVolumeInfo.MaxValue - globalVolumeInfo.MinValue);
//            double scaledMin = minData / globalScale;
//            double scaledMax = maxData / globalScale;
//            ushort packedMin = (ushort)Math.Round(scaledMin * 65535.0);
//            ushort packedMax = (ushort)Math.Round(scaledMax * 65535.0);
//            int pack = packedMin | (packedMax << 16);
//            voxelDataPtr[voxelStorageOffset++] = Unsafe.BitCast<int, float>(pack);
//            int bits = 0;
//            for (int i = 0; i < count; i++)
//            {
//                double data = currentNode.ChunkValues[i] - minBlock;
//                double scale = maxBlock - minBlock;
//                //Scale to range 0 - 1
//                double scaledValue = data / scale;
//                //Pack to 0 - 255 range
//                byte encodedValue = (byte)Math.Round(scaledValue * 255.0);
//                double decodedValue = encodedValue / 255.0;
//                double error = Math.Abs(scaledValue - decodedValue);
//                if (error > 0.01)
//                {
//                    Console.WriteLine("More than 1% error");
//                }
//                bits |= (encodedValue << ((i % 4) * 8));
//                if ((i + 1) % 4 == 0)
//                {
//                    voxelDataPtr[voxelStorageOffset++] = Unsafe.BitCast<int, float>(bits);
//                    bits = 0;
//                }
//            }
//            CheckRange(voxelStart);
//            return voxelStart;
//        }



//        static void CheckRange(uint index)
//        {
//            if (index >= 16777216 || index < 0)
//            {
//                throw new IndexOutOfRangeException("Next item too far from parent.");
//            }
//        }

//        static unsafe (int voxels, int nodes) CreateTree(SparseOctreeNode* root, float* src, FilteredVolumeInfo info, float threshold, int treeDepth)
//        {
//            int voxelCount = 0;
//            int nodesCount = 1;
//            var regions = info.SourceRegions;
//            var globalSize = info.GlobalVolumeInfo.Dimensions.Size;
//            for (int i = 0; i < regions.Length; i++)
//            {
//                var box = regions[i].SourceDimensions;
//                var size = box.Size;
//                var volumeData = src;
//                for (int z = 0; z < size.Z; z++)
//                {
//                    for (int y = 0; y < size.Y; y++)
//                    {
//                        for (int x = 0; x < size.X; x++)
//                        {
//                            Vector3i gridPos = box.Min + new Vector3i(x, y, z);
//                            float voxelValue = volumeData[((gridPos.Z) * globalSize.Y + gridPos.Y) * globalSize.X + gridPos.X];
//                            if (voxelValue > threshold)
//                            {
//                                uint treeIndex = PosToIndex(gridPos);
//                                nodesCount += CreateNodeAt(treeIndex, treeDepth, root, voxelValue);
//                                voxelCount++;
//                            }
//                        }
//                    }
//                }
//            }
//            return (voxelCount, nodesCount);
//        }

//        static unsafe int CreateNodeAt(uint treeIndex, int maxDepth, SparseOctreeNode* root, float value)
//        {
//            int nodesCreated = 0;
//            SparseOctreeNode* current = root;
//            for (int i = maxDepth - 1; i >= 1; i--)
//            {
//                byte selectedOctant = GetOctalDigit(treeIndex, i);
//                SparseOctreeNode* next = &current->ChildNodes[selectedOctant];
//                if (next->OctreeNodeFlags == OctreeNodeFlags.None) //node empty
//                {
//                    nodesCreated++;
//                    if (i == 1)
//                    {
//                        *next = SparseOctreeNode.CreateLeaf();
//                    }
//                    else
//                    {
//                        *next = SparseOctreeNode.Create();
//                    }
//                }
//                current = next;
//            }
//            int leafOctant = GetOctalDigit(treeIndex, 0);
//            LeafNode* leaf = current->ChildValues;
//            leaf->Children |= 1u << leafOctant;
//            leaf->Values[leafOctant] = value;
//            return nodesCreated;
//        }

//        static byte GetOctalDigit(uint treeIndex, int layer)
//        {
//            return (byte)((treeIndex >>> (layer * 3)) & 0b111);
//        }

//        //static uint PadZeros(int a)
//        //{
//        //    uint n = unchecked((uint)a);
//        //    n &= 0x000003ff;
//        //    n = (n ^ (n << 16)) & 0xff0000ff;
//        //    n = (n ^ (n << 8)) & 0x0300f00f;
//        //    n = (n ^ (n << 4)) & 0x030c30c3;
//        //    n = (n ^ (n << 2)) & 0x09249249;
//        //    return n;
//        //}

//        static uint PadZeros(int val)
//        {
//            uint result = 0;

//            for (int i = 0; i < 21; i++)
//            {
//                // Extract the i-th bit from the input
//                uint bit = ((uint)(val >>> i) & 1);

//                // Set the corresponding bits in the result with two zeros in between
//                result |= (bit << (3 * i));
//            }
//            return result;
//        }

//        static uint PosToIndex(Vector3i pos)
//        {
//            return PadZeros(pos.Z) | (PadZeros(pos.Y) << 1) | (PadZeros(pos.X) << 2);
//        }

//        ////Converts 3d pos to index in octree
//        //// (x,y,z) -> bin(x1y1z1x0y0z0) etc
//        //static uint PosToIndex(Vector3i pos)
//        //{
//        //    static uint PadZeros(uint val)
//        //    {
//        //        uint result = 0;

//        //        for (int i = 0; i < 21; i++)
//        //        {
//        //            // Extract the i-th bit from the input
//        //            uint bit = (val >> i) & 1;

//        //            // Set the corresponding bits in the result with two zeros in between
//        //            result |= (bit << (3 * i));
//        //        }
//        //        return result;
//        //    }
//        //    return (PadZeros((uint)pos.X) << 2) | (PadZeros((uint)pos.Y) << 1) | (PadZeros((uint)pos.Z) << 0);
//        //}

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

//    internal unsafe struct SparseTreeInfo
//    {
//        public LinearizedSparseNode* Nodes;
//        public float* Voxels;

//        public SparseTreeInfo(LinearizedSparseNode* nodes, float* voxels)
//        {
//            Nodes = nodes;
//            Voxels = voxels;
//        }
//    }

//    struct SparseVoxelOctree
//    {
//        public SparseTreeInfo TreeInfo;
//        public int NodesCount;
//        public int VoxelCount;

//        public SparseVoxelOctree(SparseTreeInfo treeInfo, int nodesCount, int voxelCount)
//        {
//            TreeInfo = treeInfo;
//            NodesCount = nodesCount;
//            VoxelCount = voxelCount;
//        }

//        public override string? ToString()
//        {
//            return $"Nodes: {NodesCount} | Voxels: {VoxelCount} | Nodes mem: {NodesCount * 4} bytes, Voxel mem: {VoxelCount * 4} bytes, Total: {NodesCount * 4 + VoxelCount * 4} bytes";
//        }
//    }
//}






