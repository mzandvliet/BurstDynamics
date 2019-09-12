using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;
using Rng = Unity.Mathematics.Random;

using Ramjet.Mathematics.FixedPoint;
using Ramjet.Mathematics.LinearAlgebra;

/* Todo:
 Clarify connection between Newtonian dynamics, the math, and this code
 - Make time a factor
 - Interface for acceleration

 Computational:
 - Figure out how to interact points and sticks without cache trashing
 - Can parallelize UpdatePoints and CollidePoints easily. Sticks too,
 but only when parallely processed sticks don't share points.
        - Sticks forming a single box, hmm..
        - But sticks from separate non-interacting boxes are fine.
 */

public class BouncingCubesFixed : MonoBehaviour {
    private NativeArray<Point> _points;
    private NativeArray<Stick> _sticks;
    private Rng _rng;

    private const int NumCubes = 4;
    private const int PointsPerCube = 4;
    private const int SticksPerCube = 6;

    private void Awake() {
        _points = new NativeArray<Point>(NumCubes * PointsPerCube, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _sticks = new NativeArray<Stick>(NumCubes * SticksPerCube, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        _rng = new Rng(1234);

        for (int i = 0; i < NumCubes; i++) {
            var pos = new vec2_qs15_16(
                _rng.NextFloat(-20, 20f),
                _rng.NextFloat(1f, 9f));
            AddCube(_points, _sticks, i, pos, ref _rng);
        }
    }

    private static void AddCube(NativeArray<Point> points, NativeArray<Stick> sticks, int idx, vec2_qs15_16 pos, ref Rng rng) {
        // Create a box with 2 diagonal supports

        int pointIdx = idx * PointsPerCube;
        int stickIdx = idx * SticksPerCube;

        vec2_qs15_16 cornerPos;
        vec2_qs15_16 cornerPosOffset;

        const float _spawnImpulse = 0.3f;

        cornerPos = pos + new vec2_qs15_16(0, 0);
        cornerPosOffset = new vec2_qs15_16(
            rng.NextFloat(-_spawnImpulse, _spawnImpulse),
            rng.NextFloat(-_spawnImpulse, _spawnImpulse));
        points[pointIdx + 0] = new Point
        {
            Last = cornerPos,
            Now = cornerPos + cornerPosOffset
        };

        cornerPos = pos + new vec2_qs15_16(0, 1);
        cornerPosOffset = new vec2_qs15_16(
            rng.NextFloat(-_spawnImpulse, _spawnImpulse),
            rng.NextFloat(-_spawnImpulse, _spawnImpulse));
        points[pointIdx + 1] = new Point {
            Last = cornerPos,
            Now = cornerPos + cornerPosOffset
        };

        cornerPos = pos + new vec2_qs15_16(1, 1);
        cornerPosOffset = new vec2_qs15_16(
            rng.NextFloat(-_spawnImpulse, _spawnImpulse),
            rng.NextFloat(-_spawnImpulse, _spawnImpulse));
        points[pointIdx + 2] = new Point
        {
            Last = cornerPos,
            Now = cornerPos + cornerPosOffset
        };

        cornerPos = pos + new vec2_qs15_16(1, 0);
        cornerPosOffset = new vec2_qs15_16(
            rng.NextFloat(-_spawnImpulse, _spawnImpulse),
            rng.NextFloat(-_spawnImpulse, _spawnImpulse));
        points[pointIdx + 3] = new Point
        {
            Last = cornerPos,
            Now = cornerPos + cornerPosOffset
        };



        sticks[stickIdx++] = new Stick
        {
            A = pointIdx + 0,
            B = pointIdx + 1,
            Length = 1
        };
        sticks[stickIdx++] = new Stick
        {
            A = pointIdx + 1,
            B = pointIdx + 2,
            Length = 1
        };
        sticks[stickIdx++] = new Stick
        {
            A = pointIdx + 2,
            B = pointIdx + 3,
            Length = 1
        };
        sticks[stickIdx++] = new Stick
        {
            A = pointIdx + 3,
            B = pointIdx + 0,
            Length = 1
        };
        sticks[stickIdx++] = new Stick
        {
            A = pointIdx + 0,
            B = pointIdx + 2,
            Length = math.sqrt(2.0f)
        };
        sticks[stickIdx++] = new Stick
        {
            A = pointIdx + 1,
            B = pointIdx + 3,
            Length = math.sqrt(2.0f)
        };
    }

    private void OnDestroy() {
        _points.Dispose();
        _sticks.Dispose();
    }

    private ulong _tick;
    private JobHandle _updateHandle;

    private void Update() {
        _updateHandle = new JobHandle();

        _updateHandle = new UpdatePointsJob
        {
            Points = _points
        }.Schedule(_updateHandle);

        _updateHandle = new UpdateSticksJob
        {
            Points = _points,
            Sticks = _sticks
        }.Schedule(_updateHandle);

        _updateHandle = new CollidePointsJob
        {
            Points = _points
        }.Schedule(_updateHandle);
    }

    private void LateUpdate() {
        _updateHandle.Complete();

        _tick++;
        if (_tick == 120) {
            // Hash some game state, so we can see if we have bitwise equivalence to prior runs
            int hash = 0;
            for (int i = 0; i < _points.Length; i++) {
                hash ^= _points[i].Now.x.v;
            }
            Debug.Log(hash);
        }
    }

    private void OnDrawGizmos() {
        /*
            Can easily divide points into regular grid by bit-shifting

            In this case, shifting right by fType.Scale leaves the int
            part, which represents meters in this world.

            If we add one extra unit of shift, we get regions of 2 meters.

            --

            We could use this for easy spatial partioning, and even
            better: hierarchical expressions of position data.

            The idea would be that we have something like 32-bit full
            position data, but most arithmetic can happen with 8-bit
            local words. Each position would be epxressed relative
            to a region. Whenever a position crosses into a new region
            we change the association.

            This would also mean that data by default is already
            in a fast spatial partition, so finding nearest-neighbours
            for collision detection or pathfinding or whatever
            could piggyback on this data structure for... free.

         */
        const int pointRegionScale = qs15_16.Scale + 2;
        const int fixedPointWordSize = 32; // todo: store as const in generate types
        const int posToUintOffset = (1 << (fixedPointWordSize - pointRegionScale));

        for (int i = 0; i < _points.Length; i++) {
            uint regionX = (uint)(posToUintOffset + (_points[i].Now.x.v >> pointRegionScale));
            uint regionY = (uint)(posToUintOffset + (_points[i].Now.y.v >> pointRegionScale));
            var hue = (((regionX * 3) % 5) + ((regionY * 7) % 11)) / (5f + 11f); // some mod-prime tricks to prevent black from showing up around zero
            Gizmos.color = Color.HSVToRGB(hue, 0.8f, .9f);

            float3 pos = toFloat3(_points[i].Now);
            Gizmos.DrawSphere(new Vector3(pos.x, pos.y, 0f), 0.1f);
        }

        Gizmos.color = Color.yellow;
        for (int i = 0; i < _sticks.Length; i++) {
            float3 a = toFloat3(_points[_sticks[i].A].Now);
            float3 b = toFloat3(_points[_sticks[i].B].Now);
            Gizmos.DrawLine(new Vector3(a.x, a.y, 0f), new Vector3(b.x, b.y, 0f));
        }
    }

    private static float3 toFloat3(vec2_qs15_16 v) {
        return new float3(v.x, v.y, 0f);
    }

    public struct Point {
        public vec2_qs15_16 Last;
        public vec2_qs15_16 Now;
    }

    public struct Stick {
        public int A;
        public int B;
        public qs15_16 Length;
    }

    [BurstCompile]
    public struct UpdatePointsJob : IJob {
        public NativeArray<Point> Points;

        public void Execute() {
            qs15_16 friction = qs15_16.One - qs15_16.Epsilon * (512 * 2);
            vec2_qs15_16 g = new vec2_qs15_16(0f, -0.005f);

            for (int i = 0; i < Points.Length; i++) {
                Point p = Points[i];
                vec2_qs15_16 v = p.Now - p.Last;

                p.Last = p.Now;
                p.Now = p.Now + v * friction;
                p.Now += g;

                Points[i] = p;
            }
        }
    }

    [BurstCompile]
    public struct CollidePointsJob : IJob {
        public NativeArray<Point> Points;

        public void Execute() {
            vec2_qs15_16 box = new vec2_qs15_16(20f, 10f);

            qs15_16 restitution = 0.4f;

            for (int i = 0; i < Points.Length; i++) {
                Point p = Points[i];
                vec2_qs15_16 v = p.Now - p.Last;

                // Todo: -box.x syntax

                if (p.Now.x > box.x) {
                    p.Now.x = box.x;
                    p.Last.x = p.Now.x + v.x * restitution;
                } else if (p.Now.x < box.x * -1) {
                    p.Now.x = box.x * -1;
                    p.Last.x = p.Now.x + v.x * restitution;
                } else if (p.Now.y > box.y) {
                    p.Now.y = box.y;
                    p.Last.y = p.Now.y + v.y * restitution;
                } else if (p.Now.y < box.y * -1) {
                    p.Now.y = box.y * -1;
                    p.Last.y = p.Now.y + v.y * restitution;
                }

                Points[i] = p;
            }
        }
    }

    [BurstCompile]
    public struct UpdateSticksJob : IJob {
        public NativeArray<Point> Points;
        public NativeArray<Stick> Sticks;

        /* Todo
         - This kind of access to Points structure promotes cache trashing, maybe?
         */

        public void Execute() {

            for (int i = 0; i < Sticks.Length; i++) {
                Stick s = Sticks[i];

                Point a = Points[s.A];
                Point b = Points[s.B];

                /* Todo: 
                - Can we find a more physically meaningful derivation of forces along
                the edges? Also, either without sqrt, or with a fixed-point sqrt

                - Stability: the squish value is unstable, can easily get into some
                range where the sim explodes.

                - Next, can we choose our fixed point types through the computation
                such that every step of the way we make good tradeoffs between
                range and precision?

                - Finally, can we make some simple tools that help us figure those
                things out?
                */

                // vec2_qs15_16 delta = a.Now - b.Now;
                // float displacement = math.sqrt(qs15_16.ToFloat(delta.x) * qs15_16.ToFloat(delta.x) + qs15_16.ToFloat(delta.y) * qs15_16.ToFloat(delta.y));
                // float diff = qs15_16.ToFloat(s.Length) - displacement;
                // float factor = math.clamp(diff / displacement / 2.0f, qs15_16.RangeMinFloat, qs15_16.RangeMaxFloat);
                // vec2_qs15_16 offset = delta * qs15_16.FromFloat(factor);

                vec2_qs15_16 delta = a.Now - b.Now;
                qs15_16 sqrLength = vec2_qs15_16.dot(delta, delta);
                // Debug.Log(sqrLength);
                qs15_16 squish = (s.Length * s.Length - sqrLength) >> 2;
                vec2_qs15_16 offset = delta * (squish / 2);

                a.Now += offset;
                b.Now -= offset;

                Points[s.A] = a;
                Points[s.B] = b;
            }
        }
    }
}