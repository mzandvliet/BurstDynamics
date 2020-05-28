using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using System.Collections;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;

/*
    Todo:
    - Open boundary so we can see how stuff behaves without getting reflections
    - Run some audio through a space
 */

public class WavePropagation : MonoBehaviour
{
    [SerializeField] private Texture2D _spaceTexture;

    private NativeArray<float> _space;

    private NativeArray<float> _bufferA;
    private NativeArray<float> _bufferB;
    private NativeArray<float> _bufferC;

    // private NativeArray<float> _audioOut;

    private const int RES = 1024;
    private const float MAX_IMPULSE = 1f;

    private Texture2D _screen;
    private NativeArray<Color> _colorsNative;
    private Color[] _colorsManaged;

    private JobHandle _handle;

    void Start()
    {
        _space = new NativeArray<float>(RES * RES, Allocator.Persistent);

        _bufferA = new NativeArray<float>(RES * RES, Allocator.Persistent);
        _bufferB = new NativeArray<float>(RES * RES, Allocator.Persistent);
        _bufferC = new NativeArray<float>(RES * RES, Allocator.Persistent);

        _colorsNative = new NativeArray<Color>(RES * RES, Allocator.Persistent);
        _colorsManaged = new Color[RES * RES];
        _screen = new Texture2D(RES, RES, TextureFormat.ARGB32, false, true);
        _screen.filterMode = FilterMode.Point;

        ConstructSpace(_spaceTexture, _space);

        StartCoroutine(Loop());
    }

    void OnDestroy() {
        _handle.Complete();

        _space.Dispose();

        _bufferA.Dispose();
        _bufferB.Dispose();
        _bufferC.Dispose();

        _colorsNative.Dispose();
    }

    private static void ConstructSpace(Texture2D source, NativeArray<float> space) {
        // Value of 1 represents open air. Value of 0 represents solid boundary.

        for (int x = 0; x < RES; x++) {
            for (int y = 0; y < RES; y++) {
                space[Idx(x, y)] = source.GetPixel(x, y).r;
            }
        }
    }

    private IEnumerator Loop() {
        Rng rng = new Rng(1234);
        ulong tick = 0;
        while (true) {
            // Render

            _handle = new JobHandle();

            const int ticksPerFrame = 8;

            for (int i = 0; i < ticksPerFrame; i++) {
                var rj = new RenderJobParallel()
                {
                    space = _space,
                    buf = _bufferC,
                    colors = _colorsNative
                };
                _handle = rj.Schedule(_colorsNative.Length, 64, _handle);
                
                // swap
                var temp = _bufferA;
                _bufferA = _bufferB;
                _bufferB = _bufferC;
                _bufferC = temp;

                // input

                var ij = new AddImpulseJob() {
                    curr = _bufferB,
                    tick = tick+(ulong)i,
                };
                _handle = ij.Schedule(_handle);

                // Simulate

                var j = new PropagateJobParallel()
                {
                    space = _space,
                    prev = _bufferA,
                    curr = _bufferB,
                    next = _bufferC
                };
                _handle = j.Schedule(_bufferA.Length, 64, _handle);
            }

            tick += ticksPerFrame;

            _handle.Complete();
            ToTexture2D(_colorsNative, _colorsManaged, _screen, new int2(RES, RES));
            

            yield return new WaitForSeconds(0.025f);
        }
    }

    void OnGUI() {
        // GUI.DrawTexture(new Rect(0f, 0f, Screen.height, Screen.height), _screen, ScaleMode.ScaleToFit);
        GUI.DrawTexture(new Rect(0f, 0f, RES+2, RES+2), _screen, ScaleMode.ScaleToFit);
    }

    [BurstCompile]
    public struct AddImpulseJob : IJob {
        public NativeArray<float> curr;
        public ulong tick;

        public void Execute() {
            float osc = math.sin(tick * Mathf.PI * 0.08f) * (1f / (1f + tick * 6f)) * MAX_IMPULSE;
            curr[Idx(100, 100)] = osc;
        }
    }


    [BurstCompile]
    public struct PropagateJobParallel : IJobParallelFor {
        [ReadOnly] public NativeArray<float> space;

        [ReadOnly] public NativeArray<float> prev;
        [ReadOnly] public NativeArray<float> curr;
        [WriteOnly] public NativeArray<float> next;

        public void Execute(int i) {
            const float C = 0.5f;

            var coord = Coord(i);
            var x = coord.x;
            var y = coord.y;

            float bL = space[Idx(x - 1, y)];
            float bR = space[Idx(x + 1, y)];
            float bT = space[Idx(x, y + 1)];
            float bB = space[Idx(x, y - 1)];

            float boundsSum = bL + bR + bT + bB;

            float spatial =
                curr[Idx(x - 1, y)] * bL +
                curr[Idx(x + 1, y)] * bR +
                curr[Idx(x, y + 1)] * bT +
                curr[Idx(x, y - 1)] * bB -
                boundsSum * curr[Idx(x, y)];
            float temporal = -prev[Idx(x, y)] + 2f * curr[Idx(x, y)];
            float v =
                C * spatial + temporal;

            next[Idx(x, y)] = v;
        }
    }

    [BurstCompile]
    public struct RenderJobParallel : IJobParallelFor {
        [ReadOnly] public NativeArray<float> space;
        [ReadOnly] public NativeArray<float> buf;

        [WriteOnly] public NativeArray<Color> colors;

        public void Execute(int i) {
            float bounds = 1f - space[i];
            var c = new Color(bounds, bounds, bounds, 1f);

            float scaled = buf[i] / (MAX_IMPULSE) * 16f;
            var pos = math.max(0f, scaled);
            var neg = math.max(0f, -scaled);

            var cSound = new Color(neg, 0f, pos, 1f);

            colors[i] = Color.Lerp(c, cSound, space[i]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Idx(int x, int y) {
        return Mod(y,RES) * RES + Mod(x,RES);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int2 Coord(int i) {
        return new int2(i % RES, i / RES);
    }

    // Naive-but-correct mod that handles negative numbers the way I want
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Mod(int x, int m) {
        int r = x % m;
        return r < 0 ? r + m : r;
    }

    private static void ToTexture2D(NativeArray<Color> colorNative, Color[] colorManaged, Texture2D tex, int2 resolution) {
        Copy(colorManaged, colorNative);
        tex.SetPixels(0, 0, (int)resolution.x, (int)resolution.y, colorManaged, 0);
        tex.Apply();
    }

    private static unsafe void Copy(Color[] destination, NativeArray<Color> source) {
        fixed (void* destinationPointer = destination) {
            long itemSize = (long)UnsafeUtility.SizeOf<Color>();
            void* sourcePointer = NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(source);
            UnsafeUtility.MemCpy(destinationPointer, sourcePointer, destination.Length * itemSize);
        }
    }
}