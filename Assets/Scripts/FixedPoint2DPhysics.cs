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

Please Add:
- Casting operators
- >=, <=
- Full interoperability with int, uint, short, byte
- Only generate rings for types marked as in use through
config file

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

If I try to naively convert from fixed point to projective int
using:

return new vec2_proj(
    qs19_12.One.v, // 4096
    v.x.v,
    v.y.v
);

Then coefficients get LARGE quick once you start arithmetic,
because taht value of C gets multiplied into everything. This
quickly overflows, sometimes even after a single Join or Meet.

Instead I've opted to use fixed point values in the code
below for [c,x,y]

Still, we might be able to find a better fusion of projective
and fixed point arithmetic. Gotta keep experimenting.

There's a kind of rolling normalization you can do. Since
and projective vector is unchanged if multiplied by a scalar,
if you quickly find good scalars that keep numbers in check...

====

Todo:
- Compare approaches for collision functions
    - Notably: first inverse-transforming B to the space of A
- Try finite fields, polynumbers

*/

namespace FixedPointPhysics {
   
    public class FixedPoint2DPhysics : MonoBehaviour {
        private NativeArray<Rigidbody> _bodies;
        private NativeArray<Segment> _staticSegments;

        private const int StepsPerSecond = 64;
        private const int ShiftToFrameDelta = 6; // log2(StepsPerSecond)

        private vec2_qs6_9 _gravity = new vec2_qs6_9(0f, -9.81f / StepsPerSecond);

        private void Awake() {
            _bodies = new NativeArray<Rigidbody>(1, Allocator.Persistent);
            _staticSegments = new NativeArray<Segment>(1, Allocator.Persistent);

            _bodies[0] = new Rigidbody() {
                position = new vec2_qs19_12(-8, 10),
                velocity = new vec2_qs6_9(3f, 1f)
            };

            _staticSegments[0] = new Segment() {
                a = new vec2_qs19_12(-10, 0),
                b = new vec2_qs19_12(10, 0)
            };
        }

        private void OnDestroy() {
            _bodies.Dispose();
            _staticSegments.Dispose();
        }

        private void Update() {
            for (int i = 0; i < _bodies.Length; i++) {
                var body = _bodies[i];

                body.velocity += _gravity;
                body.velocity *= qs6_9.One - qs6_9.Epsilon * 2;

                AddAssign(ref body.position, Shr(body.velocity, ShiftToFrameDelta));

                var bodySegment = new Segment {
                    a = body.position + new vec2_qs19_12(0f, 0.25f),
                    b = body.position - new vec2_qs19_12(0f, 0.25f),
                };

                for (int s = 0; s < _staticSegments.Length; s++) {
                    var seg = _staticSegments[s];
                    
                    if (Segment.Intersect(bodySegment, seg)) {
                        /* 
                        * Note that the normal can be any length we want, so we might as well
                        * ensure that its magnitude is something helpful
                        * 
                        * todo: correct position, so we don't slide through eventually
                         */

                        var segDelta = seg.b - seg.a;
                        var segNormal = new vec2_qs19_12(segDelta.y * -1, segDelta.x);

                        var velocity = Cast(body.velocity);
                        velocity = velocity - segNormal * (vec2_qs19_12.dot(velocity * 2, segNormal) / vec2_qs19_12.dot(segNormal, segNormal));

                        body.velocity = Cast(velocity);
                    }
                }
                
                _bodies[i] = body;
            }
        }

        private void OnDrawGizmos() {
            DrawPhysicsState();
        }

        private void DrawPhysicsState() {
            Gizmos.matrix = Matrix4x4.TRS(
                Vector3.zero,
                Quaternion.identity,
                Vector3.one
            );
            Gizmos.color = Color.white;

            for (int i = 0; i < _staticSegments.Length; i++) {
                var segment = _staticSegments[i];

                var posA = new float3(
                    segment.a.x,
                    segment.a.y,
                    0f);

                var posB = new float3(
                    segment.b.x,
                    segment.b.y,
                    0f);

                Gizmos.DrawLine(posA, posB);
            }

            for (int i = 0; i < _bodies.Length; i++) {
                var body = _bodies[i];

                var pos = new float3(
                    body.position.x,
                    body.position.y,
                    0f);

                var boxTransform = Matrix4x4.TRS(
                    pos,
                    Quaternion.identity,
                    new Vector3(1f, 0.5f, 1f)
                );
                Gizmos.matrix = boxTransform;

                Gizmos.DrawCube(Vector3.zero, Vector3.one);
            }
        }

        // Todo: the below should come from the generator
        private static vec2_qs19_12 Cast(vec2_qs6_9 v) {
            return new vec2_qs19_12(
                qs19_12.Raw(v.x.v >> 3),
                qs19_12.Raw(v.y.v >> 3)
            );
        }

        private static vec2_qs6_9 Cast(vec2_qs19_12 v) {
            return new vec2_qs6_9(
                qs6_9.Raw((short)(v.x.v << 3)),
                qs6_9.Raw((short)(v.y.v << 3))
            );
        }

        private static vec2_qs3_12 Shr(vec2_qs6_9 v, int shift) {
            // Combines a shift right by six and a reinterpret to +3 fractional bits
            return new vec2_qs3_12(
                qs3_12.Raw((short)(v.x.v >> (shift - 3))),
                qs3_12.Raw((short)(v.y.v >> (shift - 3)))
            );
        }

        private static void AddAssign(ref vec2_qs19_12 a, vec2_qs3_12 b) {
            a.x.v = (a.x.v + b.x.v);
            a.y.v = (a.y.v + b.y.v);
        }

        private static vec3_qs19_12 JoinMeet(vec3_qs19_12 a, vec3_qs19_12 b) {
            return new vec3_qs19_12(
                a.y * b.z - a.z * b.y,
                a.z * b.x - a.x * b.z,
                a.x * b.y - a.y * b.x
            );
        }

        private static qs19_12 Incidence(vec3_qs19_12 a, vec3_qs19_12 b) {
            return a.z * b.z + a.x * b.x + a.y * b.y;
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
                  This uses some projective geometry to find
                  intersection. But I'm newbie.
                 */

                var a_a_proj = new vec3_qs19_12(a.a.x, a.a.y, qs19_12.One);
                var a_b_proj = new vec3_qs19_12(a.b.x, a.b.y, qs19_12.One);
                var b_a_proj = new vec3_qs19_12(b.a.x, b.a.y, qs19_12.One);
                var b_b_proj = new vec3_qs19_12(b.b.x, b.b.y, qs19_12.One);

                var joinA_proj = JoinMeet(a_a_proj, a_b_proj);
                var joinB_proj = JoinMeet(b_a_proj, b_b_proj);
                var meet_proj = JoinMeet(joinA_proj, joinB_proj);

                /*
                Now we know the intersection of the lines, but still need to find
                whether the point is bounded by the ends of the two segments.

                For this we use two dot products and four comparisons, back in
                Cartesian space. Could be better?
                */

                var deltaA = a.b - a.a;
                var deltaB = b.b - b.a;
                var meet = new vec2_qs19_12(meet_proj.x / meet_proj.z, meet_proj.y / meet_proj.z);
                var dotA = vec2_qs19_12.dot(deltaA, meet - a.a);
                var dotB = vec2_qs19_12.dot(deltaB, meet - b.a);

                return 
                    dotA > qs19_12.Zero && dotA < vec2_qs19_12.lengthsq(deltaA) &&
                    dotB > qs19_12.Zero && dotB < vec2_qs19_12.lengthsq(deltaB);
            }
        }
    }

    
}
