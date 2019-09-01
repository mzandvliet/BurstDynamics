using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;

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
    both add methods

    Integer add ticks: 11483
    LInteger<32> add ticks: 15821

    - Changing inner loop counts from a.Length to known
    constant 32 caused speedups for regular integers adds

    ... And at this point:

    UInteger32 add ticks: 10536
    LInteger<32> add ticks: 15802

    Byte add ticks: 1446
    LInteger<8> add ticks: 15569

    Ah, it is able to vectorize this case of byte adds,
    so it wins significantly over Lint.

    But why is the Lint<8> case not faster than Lint<32>???
 */

namespace LateralIntegers {
    public class LateralIntegerTest : MonoBehaviour {

        private void Awake() {
            /* Running each test twice, because sometimes Burst
            doesn't compile it in time for the first run and
            we get the managed implementation */

            Tests.AddInt32();
            Tests.AddInt32();

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

            // Generate some random integer inputs

            var aInt = new NativeArray<word_u32>(32, Allocator.TempJob);
            var bInt = new NativeArray<word_u32>(32, Allocator.TempJob);
            var rInt = new NativeArray<word_u32>(32, Allocator.TempJob);
            for (int i = 0; i < aInt.Length; i++) {
                aInt[i] = rand.NextUInt(0, (uint)(word_u32.MaxValue / 4));
                bInt[i] = rand.NextUInt(0, (uint)(word_u32.MaxValue / 4));
            }

            // Perform regular integer adds, measure time
            
            var addIntJob = new AddInt32Job()
            {
                a = aInt,
                b = bInt,
                r = rInt
            };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            addIntJob.Schedule().Complete();
            watch.Stop();
            Debug.Log("Integer add ticks: " + watch.ElapsedTicks);

            // Perform linteger adds, measure time

            // Convert to LInt format

            var aLInt = new NativeArray<word_u32>(32, Allocator.TempJob);
            var bLInt = new NativeArray<word_u32>(32, Allocator.TempJob);
            var rLInt = new NativeArray<word_u32>(32, Allocator.TempJob);
            LUInt32.ToLInt(aInt, aLInt);
            LUInt32.ToLInt(bInt, bLInt);

            var addLIntJob = new AddLInt32Job()
            {
                a = aLInt,
                b = bLInt,
                r = rLInt
            };
            watch = System.Diagnostics.Stopwatch.StartNew();
            addLIntJob.Schedule().Complete();
            watch.Stop();
            Debug.Log("LInteger<32> add ticks: " + watch.ElapsedTicks);

            // Print linteger addition results as integers

            // var rAsInt = new NativeArray<word_u32>(32, Allocator.Temp);
            // LUInt32.ToInt(rLInt, rAsInt);

            // UInt.Print(aInt);
            // Debug.Log("++++++++++++++++++++++++++++++++");
            // UInt.Print(bInt);
            // Debug.Log("================================");
            // UInt.Print(rAsInt);
            // Debug.Log("===== should be equal to: ======");
            // UInt.Print(rInt);

            aInt.Dispose();
            bInt.Dispose();
            rInt.Dispose();
            aLInt.Dispose();
            bLInt.Dispose();
            rLInt.Dispose();
        }

        /*
        Add 32 32-bit numbers to each other
         */
        public static void AddInt8() {
            var rand = new Rng(1234);

            // Generate some random integer inputs

            var aInt = new NativeArray<word_u8>(32, Allocator.TempJob);
            var bInt = new NativeArray<word_u8>(32, Allocator.TempJob);
            var rInt = new NativeArray<word_u8>(32, Allocator.TempJob);
            for (int i = 0; i < aInt.Length; i++) {
                aInt[i] = (word_u8)rand.NextUInt(0, (word_u8)(word_u8.MaxValue / 4));
                bInt[i] = (word_u8)rand.NextUInt(0, (word_u8)(word_u8.MaxValue / 4));
            }

            // Convert to LInt format

            var aLInt = new NativeArray<word_u32>(8, Allocator.TempJob);
            var bLInt = new NativeArray<word_u32>(8, Allocator.TempJob);
            var rLInt = new NativeArray<word_u32>(8, Allocator.TempJob);
            LUInt32.ToLInt(aInt, aLInt);
            LUInt32.ToLInt(bInt, bLInt);

            // Perform regular integer adds, measure time
            
            var addIntJob = new AddInt8Job()
            {
                a = aInt,
                b = bInt,
                r = rInt
            };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            addIntJob.Schedule().Complete();
            watch.Stop();
            Debug.Log("Byte add ticks: " + watch.ElapsedTicks);

            // Perform linteger adds, measure time
            
            var addLIntJob = new AddLInt32Job()
            {
                a = aLInt,
                b = bLInt,
                r = rLInt
            };
            watch = System.Diagnostics.Stopwatch.StartNew();
            addLIntJob.Schedule().Complete();
            watch.Stop();
            Debug.Log("LInteger<8> add ticks: " + watch.ElapsedTicks);

            // Print linteger addition results as integers

            // var rAsInt = new NativeArray<word_u32>(32, Allocator.Temp);
            // LUInt32.ToInt(rLInt, rAsInt);

            // UInt.Print(aInt);
            // Debug.Log("++++++++++++++++++++++++++++++++");
            // UInt.Print(bInt);
            // Debug.Log("================================");
            // UInt.Print(rAsInt);
            // Debug.Log("===== should be equal to: ======");
            // UInt.Print(rInt);

            aInt.Dispose();
            bInt.Dispose();
            rInt.Dispose();
            aLInt.Dispose();
            bLInt.Dispose();
            rLInt.Dispose();
        }
    }

    [BurstCompile]
    public struct AddInt32Job : IJob {
        [ReadOnly] public NativeSlice<word_u32> a;
        [ReadOnly] public NativeSlice<word_u32> b;
        [WriteOnly] public NativeSlice<word_u32> r;

        public void Execute() {
            for (int i = 0; i < 65536; i++) {
                UInt.Add(a, b, r);
            }
        }
    }

    [BurstCompile]
    public struct AddInt8Job : IJob {
        [ReadOnly] public NativeSlice<word_u8> a;
        [ReadOnly] public NativeSlice<word_u8> b;
        [WriteOnly] public NativeSlice<word_u8> r;

        public void Execute() {
            for (int i = 0; i < 65536; i++) {
                UInt.Add(a, b, r);
            }
        }
    }

    [BurstCompile]
    public struct AddLInt32Job : IJob {
        [ReadOnly] public NativeSlice<word_u32> a;
        [ReadOnly] public NativeSlice<word_u32> b;
        [WriteOnly] public NativeSlice<word_u32> r;

        public void Execute() {
            for (int i = 0; i < 65536; i++) {
                LUInt32.Add(a, b, r);
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

    public static class LUInt32 {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static word_u32 Add(in NativeSlice<word_u32> a, in NativeSlice<word_u32> b, NativeSlice<word_u32> r) {
            word_u32 carry = 0;
            for (int i = 0; i < 32; i++) {
                word_u32 a_plus_b = a[i] ^ b[i];
                r[i] = a_plus_b ^ carry;
                carry = (a[i] & b[i]) ^ (carry & a_plus_b);
            }
            return carry;
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
        public static void ToLInt(in NativeSlice<word_u8> ints, NativeSlice<word_u32> lints) {
            for (int b = 0; b < lints.Length; b++) {
                lints[b] = 0;
                for (int i = 0; i < ints.Length; i++) {
                    lints[b] |= (((uint)ints[i] >> b) & 0x0000_0001) << i;
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
        public static void ToInt(in NativeSlice<word_u32> lints, NativeSlice<word_u8> ints) {
            for (int i = 0; i < ints.Length; i++) {
                ints[i] = 0;
                for (int b = 0; b < lints.Length; b++) {
                    ints[i] |= (byte)(((lints[b] >> i) & 0x0000_0001) << b);
                }
            }
        }

        public static void Print(in NativeSlice<word_u32> lints) {
            for (int i = 0; i < lints.Length; i++) {
                Debug.Log(ToBitString(lints[i]));
            }
        }

        public static string ToBitString(in uint value) {
            string b = System.Convert.ToString(value, 2);
            b = b.PadLeft(8, '0');
            return b;
        }
    }
}