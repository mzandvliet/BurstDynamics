using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet.Mathematics.FixedPoint;
using Ramjet.Mathematics.LinearAlgebra;

using fix = Ramjet.Mathematics.FixedPoint.qs15_16;
using fix4 = Ramjet.Mathematics.LinearAlgebra.vec4_qs15_16;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

public class BurstVectorizationTest : MonoBehaviour
{
    const int count = 128 * 128 * 32;

    private NativeArray<float4> float4InputA;
    private NativeArray<float4> float4InputB;
    private NativeArray<float> floatOutput;

    private NativeArray<fix4> fixed4InputA;
    private NativeArray<fix4> fixed4InputB;
    private NativeArray<fix> fixedOutput;

    private void Awake() {
        float4InputA = new NativeArray<float4>(count, Allocator.Persistent);
        float4InputB = new NativeArray<float4>(count, Allocator.Persistent);
        floatOutput = new NativeArray<float>(count, Allocator.Persistent);

        fixed4InputA = new NativeArray<fix4>(count, Allocator.Persistent);
        fixed4InputB = new NativeArray<fix4>(count, Allocator.Persistent);
        fixedOutput = new NativeArray<fix>(count, Allocator.Persistent);
    }

    private void OnDestroy() {
        float4InputA.Dispose();
        float4InputB.Dispose();
        floatOutput.Dispose();

        fixed4InputA.Dispose();
        fixed4InputB.Dispose();
        fixedOutput.Dispose();
    }

    private void Update() {
        var job_float4 = new float4Job()
        {
            _inputA = float4InputA,
            _inputB = float4InputB,
            _output = floatOutput,
        };

        var job_float4_parallel = new float4JobParallel()
        {
            _inputA = float4InputA,
            _inputB = float4InputB,
            _output = floatOutput,
        };

        var job_fixed4 = new fix4Job()
        {
            _inputA = fixed4InputA,
            _inputB = fixed4InputB,
            _output = fixedOutput,
        };

        var job_fixed4_parallel = new fix4JobParallel()
        {
            _inputA = fixed4InputA,
            _inputB = fixed4InputB,
            _output = fixedOutput,
        };

        var watch = System.Diagnostics.Stopwatch.StartNew();
        job_float4.Schedule().Complete();
        watch.Stop();
        Debug.Log("float4Single: " + watch.ElapsedTicks);
        watch = System.Diagnostics.Stopwatch.StartNew();
        job_fixed4.Schedule().Complete();
        watch.Stop();
        Debug.Log("fix4Single: " + watch.ElapsedTicks);
        watch = System.Diagnostics.Stopwatch.StartNew();
        job_float4_parallel.Schedule(float4InputA.Length, 32).Complete();
        watch.Stop();
        Debug.Log("float4Multi: " + watch.ElapsedTicks);
        watch = System.Diagnostics.Stopwatch.StartNew();
        job_fixed4_parallel.Schedule(fixed4InputA.Length, 32).Complete();
        watch.Stop();
        Debug.Log("fix4Multi: " + watch.ElapsedTicks);
    }

    // float4

    [BurstCompile]
    public struct float4Job : IJob {
        [ReadOnly] public NativeArray<float4> _inputA;
        [ReadOnly] public NativeArray<float4> _inputB;
        [WriteOnly] public NativeArray<float> _output;

        public void Execute() {
            for (int i = 0; i < _inputA.Length; i++) {
                _output[i] = math.dot(_inputA[i], _inputB[i]);
            }
        }
    }

    [BurstCompile]
    public struct float4JobParallel : IJobParallelFor {
        [ReadOnly] public NativeArray<float4> _inputA;
        [ReadOnly] public NativeArray<float4> _inputB;
        [WriteOnly] public NativeArray<float> _output;

        public void Execute(int i) {
            _output[i] = math.dot(_inputA[i], _inputB[i]);
        }
    }

    // fixed4

    [BurstCompile]
    public struct fix4Job : IJob {
        [ReadOnly] public NativeArray<fix4> _inputA;
        [ReadOnly] public NativeArray<fix4> _inputB;
        [WriteOnly] public NativeArray<fix> _output;

        public void Execute() {
            for (int i = 0; i < _inputA.Length; i++) {
                _output[i] = fix4.dot(_inputA[i], _inputB[i]);
            }
        }
    }

    [BurstCompile]
    public struct fix4JobParallel : IJobParallelFor {
        [ReadOnly] public NativeArray<fix4> _inputA;
        [ReadOnly] public NativeArray<fix4> _inputB;
        [WriteOnly] public NativeArray<fix> _output;

        public void Execute(int i) {
            _output[i] = fix4.dot(_inputA[i], _inputB[i]);
        }
    }
}

// ------

/*
This one vectorizes
 */
[BurstCompile]
public struct byteAddJob : IJob {
    [ReadOnly] public NativeArray<byte> _inputA;
    [ReadOnly] public NativeArray<byte> _inputB;
    [WriteOnly] public NativeArray<byte> _output;

    public void Execute() {
        for (int i = 0; i < _inputA.Length; i++) {
            _output[i] = (byte)(_inputA[i] + _inputB[i]);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct byte4 {
    public byte x;
    public byte y;
    public byte z;
    public byte w;

    public byte4(byte x, byte y, byte z, byte w) {
        this.x = x;
        this.y = y;
        this.z = z;
        this.w = w;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte4 operator +(byte4 lhs, byte4 rhs) {
        return new byte4(
            (byte)(lhs.x + rhs.x),
            (byte)(lhs.y + rhs.y),
            (byte)(lhs.y + rhs.z),
            (byte)(lhs.w + rhs.w));
    }
}

/*
This one doesn't vectorize.
(It does manage to optimize it every other way)

It seems Burst is not able to vectorize operations on structs
by default, or it needs very specific conditions to be met
for it to happen.

Agressive Inlining makes no difference.

Wait, AVX2 does vectorize it, but anything below it does not
ARM instructions look awful.
 */
[BurstCompile]
public struct byte4AddJob : IJob {
    [ReadOnly] public NativeArray<byte4> _inputA;
    [ReadOnly] public NativeArray<byte4> _inputB;
    [WriteOnly] public NativeArray<byte4> _output;

    public void Execute() {
        for (int i = 0; i < _inputA.Length; i++) {
            _output[i] = _inputA[i] + _inputB[i];
        }
    }
}