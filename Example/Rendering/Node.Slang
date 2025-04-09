struct Node
{
    uint PackedData[3];

    property bool IsLeaf
    {
        get {
            return (PackedData[0] & 1) != 0;
        }
    }
    property uint ChildPtr
    {
        get {
            return PackedData[0] >> 1;
        }
    }
    property uint64_t ChildMask
    {
        get {
            return PackedData[1] | uint64_t(PackedData[2]) << 32;
        }
    }
}