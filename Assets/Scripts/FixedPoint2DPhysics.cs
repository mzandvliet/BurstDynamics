using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet.Mathematics.FixedPoint;
using Ramjet.Mathematics.LinearAlgebra;

/*
Todo:
Is a bootstrapper even needed anymore?

Convert to ECS once you have something you like

--

More findings:

- Mixing fp types is powerful, but syntax is hellish

The Shr() function here shows how you can anticipate
a possible drop in precision by catching the result of
a shift in a type with more fractional bits. The
act of reinterpreting the underlying int already does
an effective shift of 3 bits, so you only need to shift
3 more.

Crucially, the function itself needs to do this, as
if you were to try to cast the result of a shift
on qs6_9 into another type, you'd already be too late.

C# operators and methods have signatures that depend
only on the LHS and RHS arguments, not on the variable
that is set to capture the return value. Most languages
work like this, right?

The only way that I see the current scheme working with
that kind of flexibility is by adding non-operator
versions, with massive permutation lists for all
possible variants... Oh my god.

Rust could handle if you write your code wrapped in a
macro that performs analysis. I guess you could do the
same with a Roslyn preprocessing step.

=== Projective Geometry ===

We're trying some projective geometry for working with
points and lines, and finding interesting tangents
between fixed point and projective arithmetic.

In a way, they are dual. Fixed point has a power-of-two
scale value that acts the same a projective coefficients,
absorbing fractional values from the other coefficients
to make coordinates into integers.

There are differences though!

The projective coordinate is more accurate in canceling
out fractions than a power-of-two shift can be, finding
the best match. This requires finding least common multiple.

Projective arithmetic requires more calculations per
operation, due to the extra coordinate being involved.

Projective arithmetic can freely mix coordinates that
exist across different scales. So while on the face of
it it may look like in the projective sense we perform
more calculations, you have to keep in mind that you
no longer have to be so fussy in shifting, casting and
reinterpreting scale ranges so much.

Note though, that you definitely still need a system
for managing scale if you want to mix multi-world-length
arithmetic.

Anyway, this gives us additional options. :)

*/

namespace FixedPointPhysics {
   
    public class FixedPoint2DPhysics : MonoBehaviour {
        private NativeArray<Rigidbody> _bodies;
        private NativeArray<Segment> _staticSegments;

        private const int StepsPerSecond = 64;
        private const int ShiftToFrameDelta = 6; // log2(StepsPerSecond)

        private vec2_qs6_9 _gravity = vec2_qs6_9.FromFloat(0f, -9.81f / StepsPerSecond);

        private void Awake() {
            _bodies = new NativeArray<Rigidbody>(1, Allocator.Persistent);
            _staticSegments = new NativeArray<Segment>(1, Allocator.Persistent);

            _bodies[0] = new Rigidbody() {
                position = vec2_qs19_12.FromInt(1, 10),
                velocity = vec2_qs6_9.FromFloat(0f, 1f)
            };

            _staticSegments[0] = new Segment() {
                a = vec2_qs19_12.FromInt(-10, 0),
                b = vec2_qs19_12.FromInt(10, 0)
            };

            TestProjectiveGeometry();
        }

        private void TestProjectiveGeometry() {
            var segmentA = new Segment()
            {
                a = vec2_qs19_12.FromInt(10, 0),
                b = vec2_qs19_12.FromInt(-10, 0)
            };

            var segmentB = new Segment()
            {
                a = vec2_qs19_12.FromInt(1, -10),
                b = vec2_qs19_12.FromInt(1, 10)
            };

            // Should meet at [1, 0]

            // var a_a = ToProjective(segmentA.a);
            // var a_b = ToProjective(segmentA.b);
            // var b_a = ToProjective(segmentB.a);
            // var b_b = ToProjective(segmentB.b);

            var a_a = new vec2_proj(1, 10, 0);
            var a_b = new vec2_proj(1, -10, 0);
            var b_a = new vec2_proj(1, 1, -10);
            var b_b = new vec2_proj(1, 1, 10);

            var joinA = JoinOfPoints(a_a, a_b);
            var joinB = JoinOfPoints(b_a, b_b);

            var meetAB = MeetOfLines(joinA, joinB);

            var result = ToFixed(meetAB);

            Debug.Log(meetAB.x / (float)meetAB.c + ", " + meetAB.y / (float)meetAB.c);

            // The above trivial version with small integers works
        }

        private struct vec2_proj {
            public int c;
            public int x;
            public int y;

            public vec2_proj(int c, int x, int y) {
                this.c = c;
                this.x = x;
                this.y = y;
            }
        }

        private static vec2_proj ToProjective(vec2_qs19_12 v) {
            return new vec2_proj(
                qs19_12.One.v,
                v.x.v,
                v.y.v
            );
        }

        private static vec2_qs19_12 ToFixed(vec2_proj v) {
            var scale = qs19_12.One.v / v.c;
            return new vec2_qs19_12(
                new qs19_12(v.x * scale),
                new qs19_12(v.y * scale)
            );
        }

        private static vec2_proj JoinOfPoints(vec2_proj a, vec2_proj b) {
            return new vec2_proj(
                a.x * b.y - a.y * b.x,
                a.y * b.c - a.c * b.y,
                a.c * b.x - a.x * b.c
            );
        }

        private static vec2_proj MeetOfLines(vec2_proj a, vec2_proj b) {
            return new vec2_proj(
                a.x * b.y - a.y * b.x,
                a.y * b.c - a.c * b.y,
                a.c * b.x - a.x * b.c
            );
        }

        private void Update() {
            for (int i = 0; i < _bodies.Length; i++) {
                var body = _bodies[i];

                body.velocity += _gravity;
                body.velocity *= qs6_9.One - qs6_9.Epsilon * 2;

                AddAssign(ref body.position, Shr(body.velocity, ShiftToFrameDelta));

                for (int s = 0; s < _staticSegments.Length; s++) {
                    
                }
                
                if (body.position.y < new qs19_12(0)) {
                    body.velocity = body.velocity * qs6_9.FromInt(-1);
                    body.position.y = qs19_12.Zero;
                }

                _bodies[i] = body;
            }
        }

        private static vec2_qs3_12 Shr(vec2_qs6_9 v, int shift) {
            // Combines a shift right by six and a reinterpret to +3 fractional bits
            return new vec2_qs3_12(
                new qs3_12((short)(v.x.v >> (shift - 3))),
                new qs3_12((short)(v.y.v >> (shift - 3)))
            );
        }

        private static void AddAssign(ref vec2_qs19_12 a, vec2_qs3_12 b) {
            a.x.v = (a.x.v + b.x.v);
            a.y.v = (a.y.v + b.y.v);
        }

        private void OnDrawGizmos() {
            for (int i = 0; i < _bodies.Length; i++) {
                var body = _bodies[i];

                var pos = new float3(
                    qs19_12.ToFloat(body.position.x),
                    qs19_12.ToFloat(body.position.y),
                    0f);

                var boxTransform = Matrix4x4.TRS(
                    pos,
                    Quaternion.identity,
                    new Vector3(1f,0.5f,1f)
                );
                Gizmos.matrix = boxTransform;

                Gizmos.DrawCube(Vector3.zero, Vector3.one);
            }
        }

        private void OnDestroy() {
            _bodies.Dispose();
        }
    }

    public struct Rigidbody {
        public vec2_qs19_12 position;
        public vec2_qs6_9 velocity;
    }

    public struct Segment {
        public vec2_qs19_12 a;
        public vec2_qs19_12 b;

        public static bool Intersect(Segment a, Segment b) {
            /*
                We can try doing this with projective coordinates

                https://youtu.be/lfTX00pB1L8?t=1065

                Through Wildberger's lectures, we know we could take
                rational x and y coordinates, and find a projective
                value to multiply by, such that [c, x, y] can all
                be integers.

                We can then find the join between two points of a
                segment.

                And from two of those we can find their meet.

                Here's a potential trick, then: since we're working
                in fixed point, everything is already integers, and
                we could interpret the Scale value as the projective
                coordinate's value.

                Very interesting confluence of fixed point arithmetic
                and projective geometry if that works out. :)

                Let's try...
             */

            return false;
        }
    }

    public struct BoxCollider {
        vec2_qu0_16 size;
    }
}