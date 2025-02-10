namespace BrickEngine.Example.DataStructures.Bvh
{
    public sealed class BoundingVolumeHierarchy
    {
        public Node[] Nodes;
        public int[] PrimitiveIndices;

        public int MaxDepth;

        public BoundingVolumeHierarchy(Node[] nodes, int[] primitiveIndices)
        {
            Nodes = nodes;
            PrimitiveIndices = primitiveIndices;
            MaxDepth = GetDepth();
        }

        public void Refresh()
        {
            MaxDepth = GetDepth();
        }

        public int GetDepth(uint node_index = 0)
        {
            var node = Nodes[node_index];
            return node.IsLeaf ? 1 : 1 + Math.Max(GetDepth(node.FirstIndex), GetDepth(node.FirstIndex + 1));
        }
    }
}
