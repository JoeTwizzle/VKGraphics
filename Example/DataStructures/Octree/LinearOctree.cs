//using BrickEngine.Editor;
//using BrickEngine.Example.VolumeRenderer.Data;
//using BrickEngine.Mathematics;
//using SharpGLTF.Schema2;
//using System;
//using System.Collections;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using Veldrid.Utilities;
//using static System.Runtime.InteropServices.JavaScript.JSType;

//namespace BrickEngine.Example.RayTracing.Octree
//{
//    internal class LinearOctree
//    {
//        public readonly LinearOctreeNode[] Nodes;
//        public readonly int[] Indices;

//        public LinearOctree(LinearOctreeNode[] nodes, int[] indices)
//        {
//            Nodes = nodes;
//            Indices = indices;
//        }

//        sealed class BuildOctreeNode
//        {
//            public Box3i BoundingBox;
//            public List<int> SourceBoxIndices;
//            public BuildOctreeNode()
//            {
//                SourceBoxIndices = new List<int>();
//                FirstChildIndex = -1;
//            }

//            public int FirstChildIndex;
//        }

//        public static LinearOctree Create(SourceRegion[] sourceRegions)
//        {
//            var max = sourceRegions.Max(x => x.SourceDimensions.Max);
//            var min = sourceRegions.Min(x => x.SourceDimensions.Min);

//            // Create Veldrid octree containing sourceRegions buffer offset
//            Octree<int> octree = new Octree<int>(new BoundingBox(min, max), sourceRegions.Length);
//            for (int i = 0; i < sourceRegions.Length; i++)
//            {
//                var region = sourceRegions[i];

//                octree.AddItem(new BoundingBox(region.SourceDimensions.Min, region.SourceDimensions.Max), i);
//            }
//            octree.ApplyPendingMoves();

//            List<int> sourceRegionIndices = new List<int>();
//            List<LinearOctreeNode> linearNodes = new List<LinearOctreeNode>();

//            // BFS
//            Queue<OctreeNode<int>> nodeQueue = new Queue<OctreeNode<int>>();
//            nodeQueue.Enqueue(octree.CurrentRoot);
//            int index = 0;
//            while (nodeQueue.Count > 0)
//            {
//                OctreeNode<int> currentNode = nodeQueue.Dequeue();

//                var items = currentNode.GetItems();
//                int firstItemIndex = items.Count > 0 ? sourceRegionIndices.Count : -1;
//                for (int i = 0; i < items.Count; i++)
//                {
//                    sourceRegionIndices.Add(items[i].Item);
//                }

//                linearNodes.Add(new LinearOctreeNode(new Box3(currentNode.Bounds.Min, currentNode.Bounds.Max), index, (short)firstItemIndex, (short)items.Count));
//                index++;
//                if (currentNode.Children != null)
//                {
//                    foreach (var child in currentNode.Children)
//                    {
//                        if (child != null)
//                        {
//                            nodeQueue.Enqueue(child);
//                        }
//                    }
//                }
//            }

//            // Package
//            var nodes = linearNodes.ToArray();
//            var indices = sourceRegionIndices.ToArray();
//            return new LinearOctree(nodes, indices);
//        }
//    }
//}
