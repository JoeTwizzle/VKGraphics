using System.Runtime.InteropServices;

namespace BrickEngine.Example
{
    unsafe struct UnmanagedMemoryRange : IDisposable
    {
        public void* First;
        public ulong Length;

        public UnmanagedMemoryRange(void* first, ulong length)
        {
            First = first;
            Length = length;
        }

        public void Dispose()
        {
            NativeMemory.Free(First);
        }
    }
}
