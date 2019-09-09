using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet.Mathematics.FixedPoint;
using Ramjet.Mathematics.LinearAlgebra;

/*
Experiment in which we store particles as 8-bit position values
relative to an index in a regular 8-bit worldspace grid.

This results in 8+8 bits = 16-bit position values

Todo:

- Use better datastructures to express this

NativeMultiHashMap seemed like a decent fit, but it has several
shortcomings:

- Visitor function only gives us a single particle state, plus
its region. This is enough for making a particle brownian-walk
on its own, but we want to add forces between nearby particles.
This means we we want to read particles from 9 surrounding regions.

It might be worth using Morton codes or something like that to
ensure neighbouring regions lie close.

- For some reason we couldn't write what we wanted back to the
map, despite using a Mutating visitor. EDIT: Reading the ECS
sample code for Boids, pingponging multiple hash maps is
exactly what they do.




---

Findings:

- Need lots of extra bookkeeping tools

You want to occasionally combine lower and higher parts
into single numbers for large-scale arithmetic. For
example, to handle moving between regions.

Range and scale mapping currently takes a heck of a lot
of work and mental bookkeeping.

We can end up with nested scale situations that are
meaningful in a game-world sense, but are not captured
by the type system, such as as 16 bit word storing:

[8 unsigned region bits][2 unsigned subregion bits][6 unsigned fractional region bits]

We get this if we take:
[8.0 bit region] + [2.6 bit fractional region position]

The best the type system can make of that is:
[8.8 bit unsigned]

If you then want to perform arithmetic on the lower
part, you need to circument the scale confusion.

I'm worried that, because with DotNet operators on small
types yielding int, and not having direct control over
Burst vectorization, this kind of structure will not
win out over simpler structure. You could make this
work really well in Rust, since it offers much tighter
control.

... I had to go and make things difficult by saying:
these regions are 3x3 meters. Like a chump. :P 

- Hiding fp type specifics behind using statements
means intellisense no longer shows you the qn.m format.
So in exchange for easier syntax you lose insight into
what scales you are working at.

- Shifint a uint returns an int, require an extra cast
in order to continue working with it in terms of unsigneds,
and also meaning you lose the MSB for information storage.
Bah....

 */

using rScalar = Ramjet.Mathematics.FixedPoint.qu8_0;
using region = Ramjet.Mathematics.LinearAlgebra.vec2_qu8_0;
using pscalar = Ramjet.Mathematics.FixedPoint.qu2_6;
using position = Ramjet.Mathematics.LinearAlgebra.vec2_qu2_6;
using vscalar = Ramjet.Mathematics.FixedPoint.qs1_6;
using velocity = Ramjet.Mathematics.LinearAlgebra.vec2_qs1_6;

public class PartitionedParticles : MonoBehaviour {
    private NativeArray<Particle> _particles;
    private NativeArray<velocity> _interParticleForces;
    private NativeMultiHashMap<region, int> _partitionA;
    private NativeMultiHashMap<region, int> _partitionB;

    private NativeArray<float3> _positions;
    private NativeArray<float> _hues;

    private const int NumParticles = 64;
    private Rng _rng;

    private SimulationConfig _simConfig;

    private void Awake() {
        _particles = new NativeArray<Particle>(NumParticles, Allocator.Persistent);
        _interParticleForces = new NativeArray<velocity>(NumParticles, Allocator.Persistent);
        _partitionA = new NativeMultiHashMap<region, int>(NumParticles, Allocator.Persistent);
        _partitionB = new NativeMultiHashMap<region, int>(NumParticles, Allocator.Persistent);

        _positions = new NativeArray<float3>(NumParticles, Allocator.Persistent);
        _hues = new NativeArray<float>(NumParticles, Allocator.Persistent);

        _rng = new Rng(1234);

        _simConfig = new SimulationConfig {
            frictionMul = vscalar.One - vscalar.Epsilon * 7,
        };

        for (int i = 0; i < NumParticles; i++) {
            // var region = vec2_qu8_0.FromInt((ushort)_rng.NextInt(256),(ushort)_rng.NextInt(256));
            var region = vec2_qu8_0.FromInt((ushort)_rng.NextInt(3, 8), (ushort)_rng.NextInt(3, 8));

            var particle = new Particle() {
                position = new position(new qu2_6((byte)_rng.NextInt(256)), new qu2_6((byte)_rng.NextInt(256))),
                velocity = velocity.FromInt(0, 0),
            };

            _particles[i] = particle;
            _partitionA.Add(region, i);
        }
    }

    private void OnDestroy() {
        _particles.Dispose();
        _interParticleForces.Dispose();
        _partitionA.Dispose();
        _partitionB.Dispose();
        _positions.Dispose();
        _hues.Dispose();
    }

    private double _movingAvg;

    private void Update() {
        var handle = new JobHandle();

        _partitionB.Clear();
        var findParticleForcesJob = new FindParticleForcesJob()
        {
            particles = _particles,
            self = _partitionA,
            forces = _interParticleForces,
        };
        handle = findParticleForcesJob.Schedule(_partitionA, 16, handle);

        var updateParticlesJob = new UpdateParticlesJob() {
            rng = new Rng((uint)Time.frameCount),
            config = _simConfig,
            particles = _particles,
            forces = _interParticleForces,
            partitionNext = _partitionB.AsParallelWriter()
        };
        handle = updateParticlesJob.Schedule(_partitionA, 16, handle);

        var atomicCounter = new JacksonDunstan.NativeCollections.NativeIntPtr(Allocator.Temp);
        var convertParticlesJob = new ConvertParticlesJob() {
            particles = _particles,
            positions = _positions,
            hues = _hues,
            counter = atomicCounter.GetParallel()
        };
        handle = convertParticlesJob.Schedule(_partitionB, 16, handle);

        handle.Complete();

        var temp = _partitionA;
        _partitionA = _partitionB;
        _partitionB = temp;
    }

    private struct SimulationConfig {
        public vscalar frictionMul;
    }

    [BurstCompile]
    private struct FindParticleForcesJob : IJobNativeMultiHashMapVisitKeyValue<region, int> {
        public NativeArray<Particle> particles;
        [ReadOnly] public NativeMultiHashMap<region, int> self;
        [WriteOnly] public NativeArray<velocity> forces;

        public void ExecuteNext(region region, int pIndex) {
            var particle = particles[pIndex];

            // var posLarge = new vec2_qu8_8(
            //     new qu8_8((ushort)(((uint)(region.x.v << 8)) | particle.position.x.v)),
            //     new qu8_8((ushort)(((uint)(region.y.v << 8)) | particle.position.y.v))
            // );

            var repulsion = velocity.FromInt(0, 0);
            velocity Repulse(Particle p, Particle pOther) {
                var delta = (p.position - pOther.position);
                var deltaQuadrance = vec2_qs1_6.lengthsq(delta);
                deltaQuadrance = deltaQuadrance << 2;
                if (deltaQuadrance < new qs1_6(2) && deltaQuadrance != qs1_6.Zero) {
                    return (delta / deltaQuadrance) * qs1_6.FromFloat(0.1f);
                }
                return velocity.FromInt(0,0);
            }

            NativeMultiHashMapIterator<region> iter;
            int pIndexOther;
            if (self.TryGetFirstValue(region, out pIndexOther, out iter)) {
                if (pIndexOther != pIndex) {
                    repulsion += Repulse(particle, particles[pIndexOther]);
                }
            }
            while (self.TryGetNextValue(out pIndexOther, ref iter)) {
                if (pIndexOther != pIndex) {
                    repulsion += Repulse(particle, particles[pIndexOther]);
                }
            }

            forces[pIndex] = repulsion;
        }
    }

    [BurstCompile]
    private struct UpdateParticlesJob : IJobNativeMultiHashMapVisitKeyValue<region, int> {
        public Rng rng;
        public NativeArray<Particle> particles;
        [ReadOnly] public SimulationConfig config;
        [WriteOnly] public NativeMultiHashMap<region, int>.ParallelWriter partitionNext;
        [ReadOnly] public NativeArray<velocity> forces;

        public void ExecuteNext(region region, int pIndex) {
            var particle = particles[pIndex];

            var nudge = new vec2_qs1_6(
                new qs1_6((sbyte)rng.NextInt(-1, 2)),
                new qs1_6((sbyte)rng.NextInt(-1, 2)));

            nudge += forces[pIndex];

            var posLarge = new vec2_qu8_8(
                new qu8_8((ushort)(((uint)(region.x.v << 8)) | particle.position.x.v)),
                new qu8_8((ushort)(((uint)(region.y.v << 8)) | particle.position.y.v))
            );

            particle.velocity += nudge;
            particle.velocity *= config.frictionMul;

            posLarge.x += particle.velocity.x;
            posLarge.y += particle.velocity.y;

            region = new vec2_qu8_0(
                new qu8_0((byte)(posLarge.x.v >> 8)),
                new qu8_0((byte)(posLarge.y.v >> 8))
            );

            particle.position.x = new qu2_6((byte)posLarge.x.v);
            particle.position.y = new qu2_6((byte)posLarge.y.v);

            particles[pIndex] = particle;
            partitionNext.Add(region, pIndex);
        }
    }

    

    /*
    Convert fixed point particle state to something the floating-point-based
    renderer can deal with.

    Note: uses a hacky implementation of an atomic counter, such that we can
    write to a global list of things from this multithreaded visit of the
    original multihashmap.
     */
    [BurstCompile]
    private struct ConvertParticlesJob : IJobNativeMultiHashMapVisitKeyValue<region, int> {
        [ReadOnly] public NativeArray<Particle> particles;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float3> positions;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> hues;
        public JacksonDunstan.NativeCollections.NativeIntPtr.Parallel counter;

        public void ExecuteNext(region region, int pIndex) {
            var rPos = new float3(
                    rScalar.ToFloat(region.x) * 4f,
                    rScalar.ToFloat(region.y) * 4f,
                    0f
                );

            int writeIndex = counter.Increment()-1;

            var p = particles[pIndex];

            hues[writeIndex] = (((region.x.v * 0x747A9D7Bu + region.y.v * 0x4942CA39u) + 0xAF836EE1u) % 257) / 256f;
            positions[writeIndex] = (rPos + new float3(pscalar.ToFloat(p.position.x), pscalar.ToFloat(p.position.y), 0f));

            // var vel = new float3(vscalar.ToFloat(p.velocity.x), vscalar.ToFloat(p.velocity.y), 0f);
        }
    }


    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }

        for (int i = 0; i < _positions.Length; i++) {
            var regionColor = Color.HSVToRGB(_hues[i], 0.8f, .9f);
            Gizmos.color = regionColor;

            // Debug.Log(_positions[i]);
            Gizmos.DrawSphere(_positions[i], 0.1f);
            // Gizmos.color = Color.magenta;
            // Gizmos.DrawRay(_positions[i], vel);
        }
    }

    public struct Particle {
        public position position;
        public velocity velocity;
    }
}