namespace BrickEngine.Example.DataStructures.Bvh
{
    public struct Bin
    {
        public BoundingBox BoundingBox;
        public int PrimCount;

        public static Bin Create()
        {
            return new Bin { BoundingBox = BoundingBox.Empty, PrimCount = 0 };
        }

        public void Extend(in Bin other)
        {
            BoundingBox.Extend(other.BoundingBox);
            PrimCount += other.PrimCount;
        }

        public float Cost => BoundingBox.HalfArea * PrimCount;
    }
}
