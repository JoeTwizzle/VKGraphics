//using System.Buffers;
//using System.Buffers.Binary;
//using System.IO.MemoryMappedFiles;
//using System.Runtime.CompilerServices;

//namespace BrickEngine.Example.VolumeRenderer.FitsFormat
//{
//    sealed class FitsFloatDocument
//    {
//        private Memory<int> AxisIndexFactors;

//        public UnmanagedMemoryRange? RawData { get; }

//        public Header Header { get; }

//        public List<Extension> Extensions { get; }

//        public FitsFloatDocument(Header header, UnmanagedMemoryRange? content, List<Extension>? extensions = null)
//        {
//            Header = header;
//            RawData = content;
//            Extensions = extensions ?? new List<Extension>();
//            InitHelperData();
//        }

//        private void InitHelperData()
//        {
//            if (RawData.HasValue)
//            {
//                AxisIndexFactors = new int[Header.NumberOfAxisInMainContent];
//                Span<int> span = AxisIndexFactors.Span;
//                span[0] = 1;
//                for (int i = 1; i < AxisIndexFactors.Length; i++)
//                {
//                    ref int reference = ref span[i];
//                    int num = span[i - 1];
//                    Header header = Header;
//                    DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(5, 1);
//                    defaultInterpolatedStringHandler.AppendLiteral("NAXIS");
//                    defaultInterpolatedStringHandler.AppendFormatted(i);
//                    reference = num * Convert.ToInt32(header[defaultInterpolatedStringHandler.ToStringAndClear()]);
//                }
//            }
//        }
//    }

//    sealed class FitsFloatContentDeserializer
//    {
//        private const int ChunkSize = 2880;
//        private const int numberOfBytesPerValue = sizeof(float);

//        public unsafe (float minValue, float maxValue) Deserialize(MemoryMappedFile dataStream, ulong start, Header header, UnmanagedMemoryRange dest)
//        {
//            float min = float.MaxValue;
//            float max = float.MinValue;
//            if (header.NumberOfAxisInMainContent < 1)
//            {
//                // Return endOfStreamReached false, since this method is only called if endOfStreamReached was false
//                // before calling this method, so since we did not read anything, it should still be false
//                return (min, max);
//            }

//            var numberOfAxis = header.NumberOfAxisInMainContent;

//            var axisSizes = Enumerable.Range(1, numberOfAxis).Select(axisIndex => Convert.ToUInt64(header[$"NAXIS{axisIndex}"])).ToArray();

//            var axisSizesSpan = new ReadOnlySpan<ulong>(axisSizes);

//            var totalNumberOfValues = axisSizes.Aggregate((ulong)1, (x, y) => x * y);

//            ulong contentSizeInBytes = numberOfBytesPerValue * totalNumberOfValues;

//            UnmanagedMemoryRange dataPointsMemory = dest;
//            var dataPointer = (float*)dataPointsMemory.First;

//            (var q, var r) = Math.DivRem(contentSizeInBytes, ChunkSize);
//            if (r > 0)
//            {
//                q++;
//            }

//            ulong totalContentSizeInBytes = q * ChunkSize;

//            if (header.DataContentType != DataContentType.FLOAT)
//            {
//                throw new ArgumentException("Content must be float");
//            }

//            ulong bytesRead = 0;
//            ulong currentValueIndex = 0;
//            Span<byte> currentValueBuffer = stackalloc byte[numberOfBytesPerValue];
//            for (ulong index = 0; index < q; index++)
//            {
//                var blockSize = Math.Min(ChunkSize, contentSizeInBytes - bytesRead);
//                bytesRead += blockSize;
//                using var chunk = dataStream.CreateViewAccessor((long)(start + ChunkSize * index), (long)blockSize, MemoryMappedFileAccess.Read);
//                byte* pointer = null;
//                try
//                {
//                    chunk.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
//                    pointer += chunk.PointerOffset;
//                    for (ulong i = 0; i < blockSize; i += numberOfBytesPerValue)
//                    {
//                        float val = BinaryPrimitives.ReadSingleBigEndian(new Span<byte>(pointer + i, numberOfBytesPerValue));
//                        min = Math.Min(min, val);
//                        max = Math.Max(max, val);
//                        dataPointer[currentValueIndex++] = val;
//                    }
//                }
//                finally
//                {
//                    if (pointer != null)
//                    {
//                        chunk.SafeMemoryMappedViewHandle.ReleasePointer();
//                    }
//                }
//            }

//            return (min, max);
//        }
//    }
//}

