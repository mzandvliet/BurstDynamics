using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet.Math.FixedPoint;
using Ramjet.Math.LinearAlgebra;

/*
    First simple test where we write some physics stuff
    with Fixed Point. Observations to be made along the way.

    ** Physical State Representation **

    Using the same qantizations for positions and velocity no longer make sense.

    vec2_q23_8 might make sense for position data, but using the
    same type for modest velocities in this space means you'll have
    low precision and wasted bits.

    After all, a large velocity in vec2_q23_8 would mean that the particle
    traverses the space from top to bottom in a single tick.

    ** State Update **

    Delta-time is a weird notion, computationally. Specifically the
    practise where you take a physical value, some energy, and you
    multiply it by a fractional number to scale it down to a smaller 
    window of time. 
    
    With infinite-precision numbers, or something that approaches that,
    you can pretend that your choice of delta time doesn't matter. But
    with these limited-precision fixed point numbers we find that choice
    of such deltaTime makes a world of difference.

    Having said that, when integrating you might not want to instantly
    add all your velocity to position, only a portion.

    So how do we deal with this? There must be multiple strategies.

    1. Don't have a energy * deltaTime step
    
    If you want to scale by time in a way that doesn't wreck numerical
    havok, you really have to pre-integrate this factor into the energies
    you calculate, I think.

    2. ??

    This leads to:

    ** Mixed precision arithmetic **

    The easiest way to support some mixed-precision arithmetic is
    to allow types with the same scale to interact, regardless
    of wordlength.

    E.g. you can freely add the following to a vec2_q24_7:
    - vec2_q0_7
    - vec2_q8_7
    - vec2_q24_7

    Unrelated: heh, we have a q24_7 type, nice.

    Edit: We've implemented a first round of this for scalars,
    and while it needs extra work and tooling, it works quite
    well, even for non-equal fractional types.

    ** Initialization **

    Make a Random Number Generator that works for all Fixed types.

    rng.NextQ24_7(0q, 14q);

    We want better ways to initialize numbers in meaningful ways
    without first expressing a number as an integer or float.

    One way is to take a known yardstick and say: gimme 1/10 of
    that.

    q24_7.one is a useful yardstick, for sure.

    So make that real easy.

    ** Literals **

    Is it at all possible to build support for custom literals into
    this? Kind of sucks to have to type "+ new q24_7(1)" when you
    mean to add 1 to something.

    ** Type Conversion **

    Implement explicit casts.

    ** Range Tracking **

    Want textual and visual indication of min/max, and precision.
    Are we safe? Where would space actually wrap? How
    much precision is left after this division?

    ** Syntax Sugar **

    TypeDeffing your favorite fixed point types per document
    makes it easier to work with them.

    ---

    UNDERFLOW: Figure out conditions for when it will happen,
    devise some strategies for avoiding it.

    ---

    Beyond the Basics

    I want types that actually go way beyond the qn_m types
    I have now.

    When doing highly quantized physics, I want to track
    velocity with 8 bits of mostly fraction, but for
    an 8 bit position I want only 3 fraction bits or
    less.

    I might often now want the result of a multiplication
    to be downshifted.
 */

using pscalar = Ramjet.Math.FixedPoint.qs3_4;
using position = Ramjet.Math.LinearAlgebra.vec2_qs3_4;
using vscalar = Ramjet.Math.FixedPoint.qs1_6;
using velocity = Ramjet.Math.LinearAlgebra.vec2_qs1_6;

public class FixedPointBrownianParticles : MonoBehaviour {
    private NativeList<Particle> _particles;
    private const int NumParticles = 1024;
    private Rng _rng;
    
    private void Awake() {
        _particles = new NativeList<Particle>(NumParticles, Allocator.Persistent);

        _rng = new Rng(1234);

        for (int i = 0; i < NumParticles; i++) {
            _particles.Add(new Particle() {
                position = position.FromFloat(_rng.NextFloat(-1f, 1f), _rng.NextFloat(-1f, 1f)),
                velocity = velocity.FromInt(0, 0),
            });
        }

        // var friction = q2 4_7.One - q24_7.Epsilon;
        var friction = pscalar.FromFloat(0.9f);

        /*
        Problem: With friction values [0,1), and very small timesteps,
        we get that velocity never goes to zero.

        Note that close to zero, the ratio between successively smaller
        numbers in the sequences becomes linear.

        We get different results between these two approaches:
        velocity = velocity * (One-Epsilon)
        velocity = velocity / (One+Epsilon)

        The multiplicative version yields UNDERFLOW of a kind, such
        that velocity never goes to zero.
        */

        // Debug.Log("2.0 * 1.5 = " + (q24_7.FromFloat(2f) * q24_7.FromFloat(1.5f)));
        // Debug.Log("-0.5f * 0.99f = " + (q24_7.FromFloat(-0.5f) * q24_7.FromFloat(0.99f)));

        // var myVec = vec2_q24_7.FromFloat(9.5f, -1.2f);
        // for (int i = 0; i < 128; i++) {
        //     myVec *= friction;
        //     Debug.Log(myVec.x.v);
        // }

        // myVec = vec2_q24_7.FromFloat(-9.5f, -1.2f);
        // for (int i = 0; i < 128; i++) {
        //     myVec *= friction;
        //     Debug.Log(myVec.x.v);
        // }
    }

    private void OnDestroy() {
        _particles.Dispose();
    }

    // private static readonly vscalar deltaTime = new vscalar(24);

    private double _movingAvg;

    private void Update() {
        var frictionMul = vscalar.One - vscalar.Epsilon * 3;
        var frictionDiv = vscalar.One + vscalar.Epsilon * 3;
        // var frictionMul = scalar.FromFloat(0.9f);
        // var frictionDiv = scalar.FromFloat(1.1f);

        double avgNudgeX = 0;
        double avgVelStepX = 0;

        for (int i = 0; i < _particles.Length; i++) {
            var p = _particles[i];

            var nudge = new velocity(
                new vscalar((sbyte)_rng.NextInt(-1, 2)),
                new vscalar((sbyte)_rng.NextInt(-1, 2)));

            // avgNudgeX += nudge.x.v;

            p.velocity.x += nudge.x;
            p.velocity.y += nudge.y;

            // p.velocity.x *= frictionMul;
            // p.velocity.y *= frictionMul;

            // p.velocity.x /= frictionDiv;
            // p.velocity.y /= frictionDiv;

            // p.velocity.x.v += (sbyte)(_rng.NextInt(-1, 2));
            // p.velocity.y.v += (sbyte)(_rng.NextInt(-1, 2));

            var scaledVelocity = p.velocity;
            // var scaledVelocity = new velocity(p.velocity.x / 2, p.velocity.y / 2);
            // scaledVelocity.x.v += (sbyte)(_rng.NextInt(-1, 2));
            // scaledVelocity.y.v += (sbyte)(_rng.NextInt(-1, 2));

            avgVelStepX += vscalar.ToDouble(scaledVelocity.x);

            p.position.x += scaledVelocity.x;
            p.position.y += scaledVelocity.y;

            _particles[i] = p;
        }

        // _movingAvg = math.lerp(_movingAvg, avgNudgeX / (double)_particles.Length, 0.01f);
        _movingAvg = math.lerp(_movingAvg, avgVelStepX / (double)_particles.Length, 0.01f);
        Debug.Log(_movingAvg);
    }

    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }

        for (int i = 0; i < _particles.Length; i++) {
            var p = _particles[i];
            var pos = new float3(pscalar.ToFloat(p.position.x) * 2f, pscalar.ToFloat(p.position.y) * 2f, 0f);
            var vel = new float3(vscalar.ToFloat(p.velocity.x) * 2f, vscalar.ToFloat(p.velocity.y) * 2f, 0f);

            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(pos, 0.05f);
            Gizmos.color = Color.magenta;
            Gizmos.DrawRay(pos, vel);
        }
    }

    public struct Particle {
        public position position;
        public velocity velocity;
    }
}