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

Rust could do it if you write your code wrapped in a
macro that performs analysis. I guess you could do the
same with a Roslyn preprocessing step.

*/

namespace FixedPointPhysics {
   
    public class FixedPoint2DPhysics : MonoBehaviour {
        private NativeArray<Rigidbody> _bodies;

        private const int StepsPerSecond = 64;
        private const int ShiftToFrameDelta = 6; // log2(StepsPerSecond)

        private vec2_qs6_9 _gravity = vec2_qs6_9.FromFloat(0f, -9.81f / StepsPerSecond);

        private void Awake() {
            _bodies = new NativeArray<Rigidbody>(1, Allocator.Persistent);

            _bodies[0] = new Rigidbody() {
                position = vec2_qs19_12.FromInt(1, 10),
                velocity = vec2_qs6_9.FromFloat(0f, 1f)
            };
        }

        private void Update() {
            for (int i = 0; i < _bodies.Length; i++) {
                var body = _bodies[i];

                body.velocity += _gravity;
                body.velocity *= qs6_9.One - qs6_9.Epsilon * 2;

                AddAssign(ref body.position, Shr(body.velocity, ShiftToFrameDelta));
                
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

    public struct BoxCollider {
        vec2_qu0_16 size;
    }
}
