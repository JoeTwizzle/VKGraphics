using System.Runtime.InteropServices;

namespace BrickEngine.Example.DataStructures.SparseOctree
{
    [Flags]
    public enum OctreeNodeFlags
    {
        None = 0,
        HasFullSubtree = 1,
        HasChildNodes = 2,
        HasChildVoxels = 4,
    }
    unsafe struct SparseOctreeNode : IDisposable
    {
        public OctreeNodeFlags OctreeNodeFlags;
        private void* children;

        public SparseOctreeNode* ChildNodes => (SparseOctreeNode*)children;
        public LeafNode* ChildValues => (LeafNode*)children;
        public float* ChunkValues => (float*)children;

        public static SparseOctreeNode Create()
        {
            return new SparseOctreeNode()
            {
                OctreeNodeFlags = OctreeNodeFlags.HasChildNodes,
                children = NativeMemory.AllocZeroed((nuint)(sizeof(SparseOctreeNode) * 8))
            };
        }

        public static SparseOctreeNode CreateLeaf()
        {
            return new SparseOctreeNode()
            {
                OctreeNodeFlags = OctreeNodeFlags.HasChildVoxels,
                children = NativeMemory.AllocZeroed((nuint)(sizeof(LeafNode)))
            };
        }

        public static SparseOctreeNode CreateFilled(float* data)
        {
            return new SparseOctreeNode()
            {
                OctreeNodeFlags = OctreeNodeFlags.HasFullSubtree | OctreeNodeFlags.HasChildVoxels,
                children = data
            };
        }

        public bool HasChildAt(int i)
        {
            if (OctreeNodeFlags.HasFlag(OctreeNodeFlags.HasChildNodes))
            {
                return ChildNodes[i].OctreeNodeFlags != OctreeNodeFlags.None;
            }
            else if (OctreeNodeFlags == OctreeNodeFlags.HasChildVoxels)
            {
                return ChildValues->HasChildAt(i);
            }
            return false;
        }

        public void Dispose()
        {
            if (OctreeNodeFlags.HasFlag(OctreeNodeFlags.HasChildNodes))
            {
                for (int i = 0; i < 8; i++)
                {
                    if (HasChildAt(i))
                    {
                        ChildNodes[i].Dispose();
                    }
                }
            }
            NativeMemory.Free(children);
        }
    }

    unsafe struct LeafNode
    {
        public uint Children;
        public fixed float Values[8];

        public bool HasChildAt(int index)
        {
            return (Children & (1u << index)) != 0;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = sizeof(int))]
    struct LinearizedSparseNode
    {
        public uint Items; //8b child mask 24b childNodesOffset or 2x2x2 block of voxel values

        public LinearizedSparseNode(uint items)
        {
            Items = items;
        }
    }
}
