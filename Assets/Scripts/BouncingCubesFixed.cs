using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;

using Ramjet.Math.FixedPoint;
using Ramjet.Math.LinearAlgebra;

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

    private const int NumCubes = 128;
    private const int PointsPerCube = 4;
    private const int SticksPerCube = 6;

    private void Awake() {
        _points = new NativeArray<Point>(NumCubes * PointsPerCube, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _sticks = new NativeArray<Stick>(NumCubes * SticksPerCube, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        for (int i = 0; i < NumCubes; i++) {
            var pos = vec2_qs15_16.FromFloat(
                UnityEngine.Random.Range(-20, 20f),
                UnityEngine.Random.Range(1f, 9f));
            AddCube(_points, _sticks, i, pos);
        }
    }

    private static void AddCube(NativeArray<Point> points, NativeArray<Stick> sticks, int idx, vec2_qs15_16 pos) {
        // Create a box with 2 diagonal supports

        int pointIdx = idx * PointsPerCube;
        int stickIdx = idx * SticksPerCube;

        points[pointIdx + 0] = new Point
        {
            Last = pos + vec2_qs15_16.FromInt(0, 0),
            Now = pos + vec2_qs15_16.FromInt(0, 0)
        };
        points[pointIdx + 1] = new Point
        {
            Last = pos + vec2_qs15_16.FromInt(0, 1),
            Now = pos + vec2_qs15_16.FromInt(0, 1)
        };
        points[pointIdx + 2] = new Point
        {
            Last = pos + vec2_qs15_16.FromInt(1, 1),
            Now = pos + vec2_qs15_16.FromInt(1, 1)
        };
        points[pointIdx + 3] = new Point
        {
            Last = pos + vec2_qs15_16.FromInt(1, 0),
            Now = pos + vec2_qs15_16.FromInt(1, 0)
        };



        sticks[stickIdx++] = new Stick
        {
            A = pointIdx + 0,
            B = pointIdx + 1,
            Length = qs15_16.FromInt(1)
        };
        sticks[stickIdx++] = new Stick
        {
            A = pointIdx + 1,
            B = pointIdx + 2,
            Length = qs15_16.FromInt(1)
        };
        sticks[stickIdx++] = new Stick
        {
            A = pointIdx + 2,
            B = pointIdx + 3,
            Length = qs15_16.FromInt(1)
        };
        sticks[stickIdx++] = new Stick
        {
            A = pointIdx + 3,
            B = pointIdx + 0,
            Length = qs15_16.FromInt(1)
        };
        sticks[stickIdx++] = new Stick
        {
            A = pointIdx + 0,
            B = pointIdx + 2,
            Length = qs15_16.FromFloat(math.sqrt(2.0f))
        };
        sticks[stickIdx++] = new Stick
        {
            A = pointIdx + 1,
            B = pointIdx + 3,
            Length = qs15_16.FromFloat(math.sqrt(2.0f))
        };
    }

    private void OnDestroy() {
        _points.Dispose();
        _sticks.Dispose();
    }

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
    }

    private void OnDrawGizmos() {
        Gizmos.color = Color.red;
        for (int i = 0; i < _points.Length; i++) {
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
        return new float3(qs15_16.ToFloat(v.x), qs15_16.ToFloat(v.y), 0f);
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
            qs15_16 friction = qs15_16.One - qs15_16.Epsilon;
            vec2_qs15_16 g = vec2_qs15_16.FromFloat(0f, -0.01f);

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
            vec2_qs15_16 box = vec2_qs15_16.FromFloat(20f, 10f);

            qs15_16 restitution = qs15_16.FromFloat(0.75f);

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
                qs15_16 squish = (s.Length * s.Length - sqrLength) >> 3;
                vec2_qs15_16 offset = delta * squish;

                a.Now += offset;
                b.Now -= offset;

                Points[s.A] = a;
                Points[s.B] = b;
            }
        }
    }
}