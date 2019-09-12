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

- Store Particles directly in hashmap?

- Find a way to flexibly express caching region size, irrespective
of position accuracy.


I mean, you can of course, but then we lose the ability to
efficiently store them by only their offset to region... Or
do you? Hierarchical numbers are great for compression, but
we shouldn't be using the exact same numbers for specifying
the  resolution of the spatial hash map.

- Ensure repulsion range of influence <= region size

- Realize that quickly moving particles with skip interactions along
their trajectory, and reconsider integrating with algebraic curves

- Type conversion

float <-> fixed
fixed_a <-> fixed_b

new fixed from raw value of other fixed, uint, int, byte, sbyte, etc.

-- 

Performance

As of 09-09-19, processing 65536 partitioned 8-bit particles
costs me around 6ms per frame. That's not too bad, but we can still
do much better. Remember, Unity Team claims 200k boids, using
floating point techniques.

- Tune region size to expected particle density

Having 1 particle per bucket is not very optimal use of the
spatial hashmap

- Find ways to coax vectorization for these smaller types, because
as it stands the smaller types don't vectorize **at all**, and are
holding performance back significantly.


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

** Performance, Arithmetic, Pipelining **

This way of writing fixed point logic has some advantages:

- Determinism (which is what we wanted)
- Lower memory footprint
- More control over precision
    - Too much precision starts to stick out

But the disadvantages:
- Code becomes more complex
- FP types are relatively unwieldy in C#
- Without toolchain you have to maintain a lot of mental state
- Performance is far lower than theoretical max. 8 bit
arithmetic, if it could be pipelined, would go superfast,
but Burst isn't able to transform the current code much.

 */

using rScalar = Ramjet.Mathematics.FixedPoint.qu8_0;
using region = Ramjet.Mathematics.LinearAlgebra.vec2_qu8_0;
using pscalar = Ramjet.Mathematics.FixedPoint.qu0_8;
using position = Ramjet.Mathematics.LinearAlgebra.vec2_qu0_8;
using vscalar = Ramjet.Mathematics.FixedPoint.qs0_7;
using velocity = Ramjet.Mathematics.LinearAlgebra.vec2_qs0_7;

public class PartitionedParticles : MonoBehaviour {
    [SerializeField] private Transform _cameraTransform;

    private NativeArray<Particle> _particles;
    private NativeArray<velocity> _interParticleForces;
    private NativeMultiHashMap<region, int> _partitionA;
    private NativeMultiHashMap<region, int> _partitionB;

    private NativeArray<float3> _positions;
    private NativeArray<float> _hues;

    private const int NumParticles = 2048 * 64;

    private SimulationConfig _simConfig;
    private int _numParticlesInView;

    private const float ScaleInMeters = 8f;

    private void Awake() {
        _particles = new NativeArray<Particle>(NumParticles, Allocator.Persistent);
        _interParticleForces = new NativeArray<velocity>(NumParticles, Allocator.Persistent);
        _partitionA = new NativeMultiHashMap<region, int>(NumParticles, Allocator.Persistent);
        _partitionB = new NativeMultiHashMap<region, int>(NumParticles, Allocator.Persistent);

        _positions = new NativeArray<float3>(NumParticles, Allocator.Persistent);
        _hues = new NativeArray<float>(NumParticles, Allocator.Persistent);

        Rng rng = new Rng(1234);

        _simConfig = new SimulationConfig {
            frictionMul = vscalar.One - vscalar.Epsilon * 7,
        };

        for (int i = 0; i < NumParticles; i++) {
            var region = vec2_qu8_0.Raw((byte)rng.NextInt(0, 255), (byte)rng.NextInt(0, 64));

            var particle = new Particle() {
                position = position.Raw((byte)rng.NextInt(256), (byte)rng.NextInt(256)),
                velocity = new velocity(0, 0),
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

    private void Update() {
        var handle = new JobHandle();

        _partitionB.Clear();
        var findParticleForcesJob = new FindParticleForcesJob()
        {
            particles = _particles,
            partition = _partitionA,
            forces = _interParticleForces,
        };
        handle = findParticleForcesJob.Schedule(_partitionA, 2, handle);

        var updateParticlesJob = new UpdateParticlesJob() {
            frameCount = (uint)Time.frameCount,
            config = _simConfig,
            particles = _particles,
            forces = _interParticleForces,
            partitionNext = _partitionB.AsParallelWriter()
        };
        handle = updateParticlesJob.Schedule(_partitionA, 2, handle);

        
        var cameraCenterRegion = new region(
            _cameraTransform.position.x / ScaleInMeters,
            _cameraTransform.position.y / ScaleInMeters
        );
        const int regionsVisibleX = 14;
        const int regionsVisibleY = 10;
        var cameraMinRegion = new region(
            math.max(0, cameraCenterRegion.x.v - regionsVisibleX),
            math.max(0, cameraCenterRegion.y.v - regionsVisibleY)
        );
        var cameraMaxRegion = new region(
            math.min(byte.MaxValue, cameraCenterRegion.x.v + regionsVisibleX),
            math.min(byte.MaxValue, cameraCenterRegion.y.v + regionsVisibleY)
        );

        var atomicCounter = new JacksonDunstan.NativeCollections.NativeIntPtr(Allocator.Temp);
        var convertParticlesJob = new ConvertParticlesJob() {
            regionMin = cameraMinRegion,
            regionMax = cameraMaxRegion,
            particles = _particles,
            partition = _partitionB,
            positions = _positions,
            hues = _hues,
            counter = atomicCounter.GetParallel()
        };
        handle = convertParticlesJob.Schedule(handle);
        handle.Complete();

        _numParticlesInView = atomicCounter.Value;

        var temp = _partitionA;
        _partitionA = _partitionB;
        _partitionB = temp;
    }

    private struct SimulationConfig {
        public vscalar frictionMul;
    }

    private static vec2_qu8_8 ToWorld(region region, position pos) {
        return new vec2_qu8_8(
            qu8_8.Raw((ushort)(((uint)(region.x.v << 8)) | pos.x.v)),
            qu8_8.Raw((ushort)(((uint)(region.y.v << 8)) | pos.y.v))
        );
    }

    [BurstCompile]
    private struct FindParticleForcesJob : IJobNativeMultiHashMapVisitKeyValue<region, int> {
        public NativeArray<Particle> particles;
        [ReadOnly] public NativeMultiHashMap<region, int> partition;
        [WriteOnly] public NativeArray<velocity> forces;

        public void ExecuteNext(region region, int pIndex) {
            var particle = particles[pIndex];
            var posLarge = ToWorld(region, particle.position);

            IterateRegion(this, region, pIndex, posLarge);
        }

        void IterateRegion(FindParticleForcesJob data, region region, int pIndex, vec2_qu8_8 posLarge) {
            velocity Repulse(vec2_qu8_8 pos, vec2_qu8_8 posOther) {
                var delta = pos - posOther;
                var deltaQuadrance = vec2_qs7_8.lengthsq(delta);
                deltaQuadrance = deltaQuadrance << 4;
                if (deltaQuadrance != qs7_8.Zero) {
                    var nudge = (delta / deltaQuadrance) * new qs7_8(0.1f);
                    return new vec2_qs0_7(qs0_7.Raw((sbyte)nudge.x.v), qs0_7.Raw((sbyte)nudge.y.v));
                }
                return new velocity(0, 0);
            }

            var repulsion = new velocity(0, 0);
            
            /*
            Note: to be able to get the relative vectors between positions in
            neighboring regions, we only really need 1 extra bit of integer precision,
            but here we're tacking on 8 to construct a full world position.
             */
            
            NativeMultiHashMapIterator<region> iter;
            int pIndexOther;
            if (data.partition.TryGetFirstValue(region, out pIndexOther, out iter)) {
                if (pIndexOther != pIndex) {
                    repulsion += Repulse(posLarge, ToWorld(region, data.particles[pIndexOther].position));
                }

                while (data.partition.TryGetNextValue(out pIndexOther, ref iter)) {
                    if (pIndexOther != pIndex) {
                        repulsion += Repulse(posLarge, ToWorld(region, data.particles[pIndexOther].position));
                    }
                }
            }

            forces[pIndex] = repulsion;
        }
    }

    [BurstCompile]
    private struct UpdateParticlesJob : IJobNativeMultiHashMapVisitKeyValue<region, int> {
        public uint frameCount;
        public NativeArray<Particle> particles;
        [ReadOnly] public SimulationConfig config;
        [WriteOnly] public NativeMultiHashMap<region, int>.ParallelWriter partitionNext;
        [ReadOnly] public NativeArray<velocity> forces;

        public void ExecuteNext(region region, int pIndex) {
            Rng rng = new Rng(frameCount * region.x.v * region.y.v);

            var particle = particles[pIndex];

            var nudge = vec2_qs0_7.Raw(
                (sbyte)rng.NextInt(-1, 2),
                (sbyte)rng.NextInt(-1, 2));

            // var nudge = new vec2_qs0_7(
            //     rng.NextInt(-1, 2),
            //     rng.NextInt(-1, 2));

            nudge += forces[pIndex];

            var posLarge = ToWorld(region, particle.position);

            particle.velocity += nudge;
            particle.velocity *= config.frictionMul;

            posLarge.x += particle.velocity.x;
            posLarge.y += particle.velocity.y;

            region = new vec2_qu8_0(
                qu8_0.Raw((byte)(posLarge.x.v >> 8)),
                qu8_0.Raw((byte)(posLarge.y.v >> 8))
            );

            particle.position.x = qu0_8.Raw((byte)posLarge.x.v);
            particle.position.y = qu0_8.Raw((byte)posLarge.y.v);

            particles[pIndex] = particle;
            partitionNext.Add(region, pIndex);
        }
    }

    [BurstCompile]
    private struct ConvertParticlesJob : IJob {
        [ReadOnly] public region regionMin;
        [ReadOnly] public region regionMax;
        [ReadOnly] public NativeArray<Particle> particles;
        [ReadOnly] public NativeMultiHashMap<region, int> partition;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float3> positions;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> hues;
        public JacksonDunstan.NativeCollections.NativeIntPtr.Parallel counter;

        public void Execute() {
            for (qu8_0 x = regionMin.x; x < regionMax.x; x++) {
                for (qu8_0 y = regionMin.y; y < regionMax.y; y++) {
                    region region = new region(x, y);

                    var rPos = new float3(
                        (float)region.x * ScaleInMeters,
                        (float)region.y * ScaleInMeters,
                        0f
                    );

                    void WriteRenderData(ConvertParticlesJob data, int pIndex) {
                        var p = data.particles[pIndex];
                        int writeIndex = data.counter.Increment() - 1;
                        data.hues[writeIndex] = (((region.x.v * 0x747A9D7Bu + region.y.v * 0x4942CA39u) + 0xAF836EE1u) % 257) / 256f;
                        data.positions[writeIndex] = rPos + new float3(
                            (float)p.position.x * ScaleInMeters,
                            (float)p.position.y * ScaleInMeters,
                            0f);
                    }

                    NativeMultiHashMapIterator<region> iter;
                    int particleIndex;
                    if (partition.TryGetFirstValue(region, out particleIndex, out iter)) {
                        WriteRenderData(this, particleIndex);

                        while (partition.TryGetNextValue(out particleIndex, ref iter)) {
                            WriteRenderData(this, particleIndex);
                        }
                    }
                }
            }
        }
    }


    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }

        for (int i = 0; i < _numParticlesInView; i++) {
            var regionColor = Color.HSVToRGB(_hues[i], 0.8f, .9f);
            Gizmos.color = regionColor;
            // Gizmos.color = Color.magenta;

            Gizmos.DrawSphere(_positions[i], 0.2f);
            // Gizmos.DrawRay(_positions[i], vel);
        }
    }

    public struct Particle {
        public position position;
        public velocity velocity;
    }
}