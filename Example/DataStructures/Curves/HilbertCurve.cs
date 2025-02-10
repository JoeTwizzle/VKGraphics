//using OpenTK.Mathematics;

//namespace BrickEngine.Example.DataStructures.Curves
//{
//    //Taken from https://github.com/rawrunprotected/hilbert_curves
//    //Adaped to c# by Joe Twizzle
//    internal static class HilbertCurve
//    {
//        public static uint Deinterleave(uint x)
//        {
//            x &= 0x55555555;
//            x = (x | (x >> 1)) & 0x33333333;
//            x = (x | (x >> 2)) & 0x0F0F0F0F;
//            x = (x | (x >> 4)) & 0x00FF00FF;
//            x = (x | (x >> 8)) & 0x0000FFFF;
//            return x;
//        }

//        public static uint Interleave(uint x)
//        {
//            x = (x | (x << 8)) & 0x00FF00FF;
//            x = (x | (x << 4)) & 0x0F0F0F0F;
//            x = (x | (x << 2)) & 0x33333333;
//            x = (x | (x << 1)) & 0x55555555;
//            return x;
//        }

//        public static uint PrefixScan(uint x)
//        {
//            x = (x >> 8) ^ x;
//            x = (x >> 4) ^ x;
//            x = (x >> 2) ^ x;
//            x = (x >> 1) ^ x;
//            return x;
//        }

//        public static uint Descan(uint x)
//        {
//            return x ^ (x >> 1);
//        }

//        public static void HilbertIndexToXY(uint n, uint i, out uint x, out uint y)
//        {
//            i <<= (int)(32 - 2 * n);

//            uint i0 = Deinterleave(i);
//            uint i1 = Deinterleave(i >> 1);

//            uint t0 = (i0 | i1) ^ 0xFFFF;
//            uint t1 = i0 & i1;

//            uint prefixT0 = PrefixScan(t0);
//            uint prefixT1 = PrefixScan(t1);

//            uint a = (((i0 ^ 0xFFFF) & prefixT1) | (i0 & prefixT0));

//            x = (a ^ i1) >>> (int)(16 - n);
//            y = (a ^ i0 ^ i1) >>> (int)(16 - n);
//        }

//        public static uint HilbertXYToIndex(uint n, uint x, uint y)
//        {
//            x <<= (int)(16 - n);
//            y <<= (int)(16 - n);

//            uint A, B, C, D;

//            // Initial prefix scan round, prime with x and y
//            {
//                uint a = x ^ y;
//                uint b = 0xFFFF ^ a;
//                uint c = 0xFFFF ^ (x | y);
//                uint d = x & (y ^ 0xFFFF);

//                A = a | (b >> 1);
//                B = (a >> 1) ^ a;

//                C = ((c >> 1) ^ (b & (d >> 1))) ^ c;
//                D = ((a & (c >> 1)) ^ (d >> 1)) ^ d;
//            }

//            {
//                uint a = A;
//                uint b = B;
//                uint c = C;
//                uint d = D;

//                A = ((a & (a >> 2)) ^ (b & (b >> 2)));
//                B = ((a & (b >> 2)) ^ (b & ((a ^ b) >> 2)));

//                C ^= ((a & (c >> 2)) ^ (b & (d >> 2)));
//                D ^= ((b & (c >> 2)) ^ ((a ^ b) & (d >> 2)));
//            }

//            {
//                uint a = A;
//                uint b = B;
//                uint c = C;
//                uint d = D;

//                A = ((a & (a >> 4)) ^ (b & (b >> 4)));
//                B = ((a & (b >> 4)) ^ (b & ((a ^ b) >> 4)));

//                C ^= ((a & (c >> 4)) ^ (b & (d >> 4)));
//                D ^= ((b & (c >> 4)) ^ ((a ^ b) & (d >> 4)));
//            }

//            // Final round and projection
//            {
//                uint a = A;
//                uint b = B;
//                uint c = C;
//                uint d = D;

//                C ^= ((a & (c >> 8)) ^ (b & (d >> 8)));
//                D ^= ((b & (c >> 8)) ^ ((a ^ b) & (d >> 8)));
//            }

//            {
//                // Undo transformation prefix scan
//                uint a = C ^ (C >> 1);
//                uint b = D ^ (D >> 1);

//                // Recover index bits
//                uint i0 = x ^ y;
//                uint i1 = b | (0xFFFF ^ (i0 | a));

//                return (((Interleave(i1) << 1) | Interleave(i0))) >>> (int)(32 - 2 * n);
//            }
//        }

//        // These are multiplication tables of the alternating group A4,
//        // preconvolved with the mapping between Morton and Hilbert curves.
//        static ReadOnlySpan<byte> MortonToHilbertTable => new byte[]{
//            48, 33, 27, 34, 47, 78, 28, 77,
//            66, 29, 51, 52, 65, 30, 72, 63,
//            76, 95, 75, 24, 53, 54, 82, 81,
//            18,  3, 17, 80, 61,  4, 62, 15,
//             0, 59, 71, 60, 49, 50, 86, 85,
//            84, 83,  5, 90, 79, 56,  6, 89,
//            32, 23,  1, 94, 11, 12,  2, 93,
//            42, 41, 13, 14, 35, 88, 36, 31,
//            92, 37, 87, 38, 91, 74,  8, 73,
//            46, 45,  9, 10,  7, 20, 64, 19,
//            70, 25, 39, 16, 69, 26, 44, 43,
//            22, 55, 21, 68, 57, 40, 58, 67,
//        };

//        static ReadOnlySpan<byte> HilbertToMortonTable => new byte[]
//        {
//            48, 33, 35, 26, 30, 79, 77, 44,
//            78, 68, 64, 50, 51, 25, 29, 63,
//            27, 87, 86, 74, 72, 52, 53, 89,
//            83, 18, 16,  1,  5, 60, 62, 15,
//            0, 52, 53, 57, 59, 87, 86, 66,
//            61, 95, 91, 81, 80,  2,  6, 76,
//            32,  2,  6, 12, 13, 95, 91, 17,
//            93, 41, 40, 36, 38, 10, 11, 31,
//            14, 79, 77, 92, 88, 33, 35, 82,
//            70, 10, 11, 23, 21, 41, 40,  4,
//            19, 25, 29, 47, 46, 68, 64, 34,
//            45, 60, 62, 71, 67, 18, 16, 49,
//        };

//        public static uint TransformCurve(uint input, uint bits, ReadOnlySpan<byte> lookupTable)
//        {

//            uint transform = 0;
//            uint output = 0;

//            for (int i = (int)(3 * (bits - 1)); i >= 0; i -= 3)
//            {
//                transform = lookupTable[(int)(transform | ((input >> i) & 7))];
//                output = (output << 3) | (transform & 7);
//                unchecked
//                {
//                    transform &= (uint)~7;
//                }
//            }

//            return output;
//        }

//        public static uint MortonToHilbert3D(uint mortonIndex, uint bits)
//        {
//            return TransformCurve(mortonIndex, bits, MortonToHilbertTable);
//        }

//        public static uint HilbertToMorton3D(uint hilbertIndex, uint bits)
//        {
//            return TransformCurve(hilbertIndex, bits, HilbertToMortonTable);
//        }

//        public static int PositionToHilbertIndex(Vector3i position, uint gridSize)
//        {
//            if (!uint.IsPow2(gridSize))
//            {
//                throw new ArgumentException(nameof(gridSize) + " must be a power of 2");
//            }

//            if (position.X < 0 || position.Y < 0 || position.Z < 0)
//            {
//                throw new ArgumentException(nameof(position) + " must be positive");
//            }

//            uint indexXY = HilbertXYToIndex(gridSize, (uint)position.X, (uint)position.Y);
//            uint indexYZ = HilbertXYToIndex(gridSize, (uint)position.Y, (uint)position.Z);
//            uint indexXZ = HilbertXYToIndex(gridSize, (uint)position.X, (uint)position.Z);

//            return (int)((indexXY << 2) | (indexYZ << 1) | indexXZ);
//        }
//    }
//}
