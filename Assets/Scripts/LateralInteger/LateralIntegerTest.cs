using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;


using word_u64 = System.UInt64;
using word_u32 = System.UInt32;
using word_u16 = System.UInt16;
using word_u8 = System.Byte;
using System.Runtime.CompilerServices;

/*
    Observations while porting this:

    - Initial straight port yielded horrible performance

    Integer add ticks: 6989
    LInteger<32> add ticks: 25145
    
    Nothing would vectorize. Generated job code for both
    int and lint addition yielded assembly with tons of
    ugly cruft.

    - Changing test loop count from 999999 to 65536 made
    a big difference in generated code, seems faster for
    Lint

    Integer add ticks: 11483
    LInteger<32> add ticks: 15821

    - Changing inner loop counts from a.Length to known
    constant 32 caused speedups for regular integers adds

    ... And at this point:

    UInteger32 add ticks: 10536
    LInteger<32> add ticks: 15802

    Byte add ticks: 1446
    LInteger<8> add ticks: 4096

    Ah, it is able to vectorize this case of byte adds,
    so it wins significantly over Lint.

    --

    Implementented a modified test for 8 bit adding, where
    now we're adding arrays of ints, and comparing it to
    adding arrays of packed Lints.

    Both now fluctuate between 1x to 3x duration of the
    reference implementation.

    I'm guessing that for some specific use cases, types
    like these might compete.

    --

    Added a version where we store 64 32-bit integers
    in a single Lint. We get:

    uint add ticks: 1857
    LInt add ticks: 2071

    This is a negligable performance difference now, and
    this makes it the best-performing Lint on my own
    computer (for this style of addition anyway).

    === Todo ===

    Types!

    Make a type for Lint, such that we no longer confuse a
    single Lint with an array of Lints

    Implement more operations, like multiplication, and
    some fixed-point arithmetic. See how that compares.
 */

namespace LateralIntegers {
    public class LateralIntegerTest : MonoBehaviour {

        private void Awake() {
            /* Running each test twice, because sometimes Burst
            doesn't compile it in time for the first run and
            we get the managed implementation */

            Tests.AddInt32();
            Tests.AddInt32();

            Tests.AddInt32WithInt64();
            Tests.AddInt32WithInt64();

            Tests.AddInt8();
            Tests.AddInt8();
        }
    }

    public static class Tests {

        /*
        Add 32 32-bit numbers to each other
         */
        public static void AddInt32() {
            var rand = new Rng(1234);

            const int NumNumbers = 16384 * 16;

            // Generate some random integer inputs

            var aInt = new NativeArray<word_u32>(NumNumbers, Allocator.TempJob);
            var bInt = new NativeArray<word_u32>(NumNumbers, Allocator.TempJob);
            var rInt = new NativeArray<word_u32>(NumNumbers, Allocator.TempJob);
            for (int i = 0; i < NumNumbers; i++) {
                aInt[i] = rand.NextUInt(0, (word_u32)(word_u32.MaxValue / 4));
                bInt[i] = rand.NextUInt(0, (word_u32)(word_u32.MaxValue / 4));
            }

            // Perform regular integer adds, measure time

            var addIntJob = new AddInt32Job()
            {
                Count = NumNumbers,
                a = aInt,
                b = bInt,
                r = rInt
            };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            addIntJob.Schedule().Complete();
            watch.Stop();
            Debug.Log("uint add ticks: " + watch.ElapsedTicks);

            // Convert to LInt format

            const int NumLints = NumNumbers / 32;
            var aLInt = new NativeArray<word_u32>(NumLints * 32, Allocator.TempJob);
            var bLInt = new NativeArray<word_u32>(NumLints * 32, Allocator.TempJob);
            var rLInt = new NativeArray<word_u32>(NumLints * 32, Allocator.TempJob);
            LUInt.ToLIntArray(aInt, aLInt);
            LUInt.ToLIntArray(bInt, bLInt);

            // Perform linteger adds, measure time

            var addLIntJob = new AddLUInt32Job()
            {
                Count = NumLints,
                a = aLInt,
                b = bLInt,
                r = rLInt
            };
            watch = System.Diagnostics.Stopwatch.StartNew();
            addLIntJob.Schedule().Complete();
            watch.Stop();
            Debug.Log("LInteger<32> add ticks: " + watch.ElapsedTicks);

            // Print linteger addition results as integers

            var rAsInt = new NativeArray<word_u32>(NumNumbers, Allocator.Temp);
            LUInt.ToIntArray(rLInt, rAsInt);

            // UInt.Print(aInt);
            // Debug.Log("++++++++++++++++++++++++++++++++");
            // UInt.Print(bInt);
            // Debug.Log("================================");
            // UInt.Print(rAsInt);
            // Debug.Log("===== should be equal to: ======");
            // UInt.Print(rInt);

            bool correct = true;
            for (int i = 0; i < rInt.Length; i++) {
                if (rAsInt[i] != rInt[i]) {
                    correct = false;
                    break;
                }
            }
            if (correct) {
                Debug.Log("All calculations verified and correct!");
            } else {
                Debug.LogError("Some error occured, calculation results are not correct.");
            }

            aInt.Dispose();
            bInt.Dispose();
            rInt.Dispose();
            aLInt.Dispose();
            bLInt.Dispose();
            rLInt.Dispose();
            rAsInt.Dispose();
        }

        /*
        Add 64 32-bit numbers to each other
         */
        public static void AddInt32WithInt64() {
            var rand = new Rng(1234);

            const int NumNumbers = 16384 * 16;

            // Generate some random integer inputs

            var aInt = new NativeArray<word_u32>(NumNumbers, Allocator.TempJob);
            var bInt = new NativeArray<word_u32>(NumNumbers, Allocator.TempJob);
            var rInt = new NativeArray<word_u32>(NumNumbers, Allocator.TempJob);
            for (int i = 0; i < NumNumbers; i++) {
                aInt[i] = rand.NextUInt(0, (word_u32)(word_u32.MaxValue / 4));
                bInt[i] = rand.NextUInt(0, (word_u32)(word_u32.MaxValue / 4));
            }

            // Perform regular integer adds, measure time

            var addIntJob = new AddInt32Job()
            {
                Count = NumNumbers,
                a = aInt,
                b = bInt,
                r = rInt
            };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            addIntJob.Schedule().Complete();
            watch.Stop();
            Debug.Log("uint add ticks: " + watch.ElapsedTicks);

            // Convert to LInt format

            const int NumLints = (NumNumbers / 32 / 2);
            var aLInt = new NativeArray<word_u64>(NumLints * 32, Allocator.TempJob);
            var bLInt = new NativeArray<word_u64>(NumLints * 32, Allocator.TempJob);
            var rLInt = new NativeArray<word_u64>(NumLints * 32, Allocator.TempJob);
            LUInt.ToLIntArray(aInt, aLInt);
            LUInt.ToLIntArray(bInt, bLInt);

            // Perform linteger adds, measure time

            var addLIntJob = new Add32BitLUInt64Job()
            {
                Count = NumLints,
                a = aLInt,
                b = bLInt,
                r = rLInt
            };
            watch = System.Diagnostics.Stopwatch.StartNew();
            addLIntJob.Schedule().Complete();
            watch.Stop();
            Debug.Log("LInteger<64> add ticks: " + watch.ElapsedTicks);

            // Print linteger addition results as integers

            var rAsInt = new NativeArray<word_u32>(NumNumbers, Allocator.Temp);
            LUInt.ToIntArray(rLInt, rAsInt);

            // UInt.Print(aInt);
            // Debug.Log("++++++++++++++++++++++++++++++++");
            // UInt.Print(bInt);
            // Debug.Log("================================");
            // UInt.Print(rAsInt);
            // Debug.Log("===== should be equal to: ======");
            // UInt.Print(rInt);

            bool correct = true;
            for (int i = 0; i < rInt.Length; i++) {
                if (rAsInt[i] != rInt[i]) {
                    correct = false;
                    break;
                }
            }
            if (correct) {
                Debug.Log("All calculations verified and correct!");
            } else {
                Debug.LogError("Some error occured, calculation results are not correct.");
            }

            aInt.Dispose();
            bInt.Dispose();
            rInt.Dispose();
            aLInt.Dispose();
            bLInt.Dispose();
            rLInt.Dispose();
            rAsInt.Dispose();
        }

        /*
        Add 32 8-bit numbers to each other
         */
        public static void AddInt8() {
            var rand = new Rng(1234);
            
            const int NumNumbers = 16384 * 16;
            
            // Generate some random integer inputs

            var aInt = new NativeArray<word_u8>(NumNumbers, Allocator.TempJob);
            var bInt = new NativeArray<word_u8>(NumNumbers, Allocator.TempJob);
            var rInt = new NativeArray<word_u8>(NumNumbers, Allocator.TempJob);
            for (int i = 0; i < NumNumbers; i++) {
                aInt[i] = (word_u8)rand.NextUInt(0, (word_u8)(word_u8.MaxValue / 4));
                bInt[i] = (word_u8)rand.NextUInt(0, (word_u8)(word_u8.MaxValue / 4));
            }

            // Perform regular integer adds, measure time
            
            var addIntJob = new AddInt8Job()
            {
                Count = NumNumbers,
                a = aInt,
                b = bInt,
                r = rInt
            };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            addIntJob.Schedule().Complete();
            watch.Stop();
            Debug.Log("Byte add ticks: " + watch.ElapsedTicks);

            // Convert to LInt format

            const int NumLints = (NumNumbers / 32);
            var aLInt = new NativeArray<word_u32>(NumLints * 8, Allocator.TempJob);
            var bLInt = new NativeArray<word_u32>(NumLints * 8, Allocator.TempJob);
            var rLInt = new NativeArray<word_u32>(NumLints * 8, Allocator.TempJob);
            LUInt.ToLIntArray(aInt, aLInt);
            LUInt.ToLIntArray(bInt, bLInt);

            // Perform linteger adds, measure time
            
            var addLIntJob = new AddLUInt8Job()
            {
                Count = NumLints,
                a = aLInt,
                b = bLInt,
                r = rLInt
            };
            watch = System.Diagnostics.Stopwatch.StartNew();
            addLIntJob.Schedule().Complete();
            watch.Stop();
            Debug.Log("LInteger<8> add ticks: " + watch.ElapsedTicks);

            // Print linteger addition results as integers

            var rAsInt = new NativeArray<word_u8>(NumNumbers, Allocator.Temp);
            LUInt.ToIntArray(rLInt, rAsInt);

            // UInt.Print(aInt);
            // Debug.Log("++++++++++++++++++++++++++++++++");
            // UInt.Print(bInt);
            // Debug.Log("================================");
            // UInt.Print(rAsInt);
            // Debug.Log("===== should be equal to: ======");
            // UInt.Print(rInt);

            bool correct = true;
            for (int i = 0; i < rInt.Length; i++) {
                if (rAsInt[i] != rInt[i]) {
                    correct = false;
                    break;
                }
            }
            if (correct) {
                Debug.Log("All calculations verified and correct!");
            } else {
                Debug.LogError("Some error occured, calculation results are not correct.");
            }

            aInt.Dispose();
            bInt.Dispose();
            rInt.Dispose();
            aLInt.Dispose();
            bLInt.Dispose();
            rLInt.Dispose();
            rAsInt.Dispose();
        }
    }

    [BurstCompile]
    public struct AddInt32Job : IJob {
        [ReadOnly] public int Count;
        [ReadOnly] public NativeSlice<word_u32> a;
        [ReadOnly] public NativeSlice<word_u32> b;
        [WriteOnly] public NativeSlice<word_u32> r;

        public void Execute() {
            UInt.Add(a, b, r);
        }
    }

    [BurstCompile]
    public struct AddInt8Job : IJob {
        [ReadOnly] public int Count;
        [ReadOnly] public NativeSlice<word_u8> a;
        [ReadOnly] public NativeSlice<word_u8> b;
        [WriteOnly] public NativeSlice<word_u8> r;

        public void Execute() {
            UInt.Add(a, b, r);
        }
    }

    [BurstCompile]
    public struct Add32BitLUInt64Job : IJob {
        [ReadOnly] public int Count;
        [ReadOnly] public NativeSlice<word_u64> a;
        [ReadOnly] public NativeSlice<word_u64> b;
        [WriteOnly] public NativeSlice<word_u64> r;

        public void Execute() {
            for (int i = 0; i < Count; i++) {
                LUInt.Add32BitAs64Bit(a.Slice(i * 32, 32), b.Slice(i * 32, 32), r.Slice(i * 32, 32));
            }
        }
    }

    [BurstCompile]
    public struct AddLUInt32Job : IJob {
        [ReadOnly] public int Count;
        [ReadOnly] public NativeSlice<word_u32> a;
        [ReadOnly] public NativeSlice<word_u32> b;
        [WriteOnly] public NativeSlice<word_u32> r;

        public void Execute() {
            for (int i = 0; i < Count; i++) {
                LUInt.Add32Bit(a.Slice(i * 32, 32), b.Slice(i * 32, 32), r.Slice(i * 32, 32));
            }
        }
    }

    [BurstCompile]
    public struct AddLUInt8Job : IJob {
        [ReadOnly] public int Count;
        [ReadOnly] public NativeSlice<word_u32> a;
        [ReadOnly] public NativeSlice<word_u32> b;
        [WriteOnly] public NativeSlice<word_u32> r;

        public void Execute() {
            for (int i = 0; i < Count; i++) {
                LUInt.Add8Bit(a.Slice(i * 8, 8), b.Slice(i * 8, 8), r.Slice(i * 8, 8));
            }
        }
    }

    public static class UInt {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Add(in NativeSlice<word_u32> a, in NativeSlice<word_u32> b, NativeSlice<word_u32> r) {
            for (int i = 0; i < a.Length; i++) {
                r[i] = a[i] + b[i];
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Add(in NativeSlice<word_u8> a, in NativeSlice<word_u8> b, NativeSlice<word_u8> r) {
            for (int i = 0; i < a.Length; i++) {
                r[i] = (word_u8)(a[i] + b[i]);
            }
        }

        public static void Print(in NativeSlice<word_u32> ints) {
            for (int i = 0; i < ints.Length; i++) {
                Debug.Log($@"[{i}]: {ints[i]}");
            }
        }

        public static void Print(in NativeSlice<word_u16> ints) {
            for (int i = 0; i < ints.Length; i++) {
                Debug.Log($@"[{i}]: {ints[i]}");
            }
        }

        public static void Print(in NativeSlice<word_u8> ints) {
            for (int i = 0; i < ints.Length; i++) {
                Debug.Log($@"[{i}]: {ints[i]}");
            }
        }
    }

    public static class LUInt {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Add32BitAs64Bit(in NativeSlice<word_u64> a, in NativeSlice<word_u64> b, NativeSlice<word_u64> r) {
            word_u64 carry = 0;
            for (int i = 0; i < 32; i++) {
                word_u64 a_plus_b = a[i] ^ b[i];
                r[i] = a_plus_b ^ carry;
                carry = (a[i] & b[i]) ^ (carry & a_plus_b);
            }
            // return carry;
        }

        // [NoAlias]?
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Add32Bit(in NativeSlice<word_u32> a, in NativeSlice<word_u32> b, NativeSlice<word_u32> r) {
            word_u32 carry = 0;
            for (int i = 0; i < 32; i++) {
                word_u32 a_plus_b = a[i] ^ b[i];
                r[i] = a_plus_b ^ carry;
                carry = (a[i] & b[i]) ^ (carry & a_plus_b);
            }
            // return carry;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Add8Bit(in NativeSlice<word_u32> a, in NativeSlice<word_u32> b, NativeSlice<word_u32> r) {
            word_u32 carry = 0;
            for (int i = 0; i < 8; i++) {
                word_u32 a_plus_b = a[i] ^ b[i];
                r[i] = a_plus_b ^ carry;
                carry = (a[i] & b[i]) ^ (carry & a_plus_b);
            }
            // return carry;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToLIntArray(in NativeSlice<word_u32> ints, NativeSlice<word_u32> lints) {
            for (int i = 0; i < ints.Length / 32; i++) {
                ToLInt(ints.Slice(i * 32, 32), lints.Slice(i * 32, 32));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToLIntArray(in NativeSlice<word_u32> ints, NativeSlice<word_u64> lints) {
            if (ints.Length / 2 != lints.Length) {
                throw new System.ArgumentException(string.Format("Array sizes don't match: {0}/2 should equal {1}", ints.Length, lints.Length));
            }

            for (int i = 0; i < ints.Length / 64; i++) {
                ToLInt(ints.Slice(i * 64, 64), lints.Slice(i * 32, 32));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToLInt(in NativeSlice<word_u32> ints, NativeSlice<word_u64> lints) {
            for (int b = 0; b < lints.Length; b++) {
                lints[b] = 0;
                for (int i = 0; i < ints.Length; i++) {
                    lints[b] |= (word_u64)(((ints[(int)i] >> b) & 0x0000_0001)) << i;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToLInt(in NativeSlice<word_u32> ints, NativeSlice<word_u32> lints) {
            for (int b = 0; b < lints.Length; b++) {
                lints[b] = 0;
                for (int i = 0; i < ints.Length; i++) {
                    lints[b] |= ((ints[i] >> b) & 0x0000_0001) << i;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToLInt(in NativeSlice<word_u16> ints, NativeSlice<word_u32> lints) {
            for (int b = 0; b < lints.Length; b++) {
                lints[b] = 0;
                for (int i = 0; i < ints.Length; i++) {
                    lints[b] |= (((uint)ints[i] >> b) & 0x0000_0001) << i;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToLIntArray(in NativeSlice<word_u8> ints, NativeSlice<word_u32> lints) {
            for (int i = 0; i < ints.Length/32; i++) {
                ToLInt(ints.Slice(i*32, 32), lints.Slice(i * 8, 8));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToLInt(in NativeSlice<word_u8> ints, NativeSlice<word_u32> lints) {
            for (int b = 0; b < lints.Length; b++) {
                lints[b] = 0;
                for (int i = 0; i < ints.Length; i++) {
                    lints[b] |= (((uint)ints[i] >> b) & 0x0000_0001) << i;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToIntArray(in NativeSlice<word_u64> lints, NativeSlice<word_u32> ints) {
            for (int i = 0; i < lints.Length / 32; i++) {
                ToInt(lints.Slice(i * 32, 32), ints.Slice(i * 64, 64));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToIntArray(in NativeSlice<word_u32> lints, NativeSlice<word_u32> ints) {
            for (int i = 0; i < ints.Length / 32; i++) {
                ToInt(lints.Slice(i * 32, 32), ints.Slice(i * 32, 32));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToInt(in NativeSlice<word_u64> lints, NativeSlice<word_u32> ints) {
            for (int i = 0; i < ints.Length; i++) {
                ints[i] = 0;
                for (int b = 0; b < lints.Length; b++) {
                    ints[i] |= (word_u32)(((lints[b] >> i) & 0x0000_0000_0000_0001) << b);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToInt(in NativeSlice<word_u32> lints, NativeSlice<word_u32> ints) {
            for (int i = 0; i < ints.Length; i++) {
                ints[i] = 0;
                for (int b = 0; b < lints.Length; b++) {
                    ints[i] |= ((lints[b] >> i) & 0x0000_0001) << b;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToInt(in NativeSlice<word_u32> lints, NativeSlice<word_u16> ints) {
            for (int i = 0; i < ints.Length; i++) {
                ints[i] = 0;
                for (int b = 0; b < lints.Length; b++) {
                    ints[i] |= (ushort)(((lints[b] >> i) & 0x0000_0001) << b);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToIntArray(in NativeSlice<word_u32> lints, NativeSlice<word_u8> ints) {
            for (int i = 0; i < ints.Length / 32; i++) {
                ToInt(lints.Slice(i * 8, 8), ints.Slice(i * 32, 32));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ToInt(in NativeSlice<word_u32> lints, NativeSlice<word_u8> ints) {
            for (int i = 0; i < ints.Length; i++) {
                ints[i] = 0;
                for (int b = 0; b < lints.Length; b++) {
                    ints[i] |= (byte)(((lints[b] >> i) & 0x0000_0001) << b);
                }
            }
        }

        public static void Print(in NativeSlice<word_u64> lints) {
            for (int i = 0; i < lints.Length; i++) {
                Debug.Log(ToBitString(lints[i]));
            }
        }

        public static void Print(in NativeSlice<word_u32> lints) {
            for (int i = 0; i < lints.Length; i++) {
                Debug.Log(ToBitString(lints[i]));
            }
        }

        public static string ToBitString(in word_u64 value) {
            uint high = unchecked(((uint)(value >> 32)) << 32);
            uint low = unchecked(((uint)(value)));
            Debug.Log(value);
            string bLow = System.Convert.ToString(low, 2);
            bLow = bLow.PadLeft(32, '0');
            string bHigh = System.Convert.ToString(high, 2);
            bHigh = bHigh.PadLeft(32, '0');
            return bHigh + bLow;
        }

        public static string ToBitString(in word_u32 value) {
            string b = System.Convert.ToString(value, 2);
            b = b.PadLeft(32, '0');
            return b;
        }
    }
}