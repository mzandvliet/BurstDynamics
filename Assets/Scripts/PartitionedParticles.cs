using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Rng = Unity.Mathematics.Random;
using Ramjet.Math.FixedPoint;
using Ramjet.Math.LinearAlgebra;

using rScalar = Ramjet.Math.FixedPoint.qu8_0;
using region = Ramjet.Math.LinearAlgebra.vec2_qu8_0;
using pscalar = Ramjet.Math.FixedPoint.qu2_6;
using position = Ramjet.Math.LinearAlgebra.vec2_qu2_6;
using vscalar = Ramjet.Math.FixedPoint.qs1_6;
using velocity = Ramjet.Math.LinearAlgebra.vec2_qs1_6;

/*
Experiment in which we store particles as 8-bit position values
relative to an index in a regular 8-bit worldspace grid.

This results in 8+8 bits = 16-bit position values.

Todo: actually perform region changes for particles crossing
the boundary.

 */

public class PartitionedParticles : MonoBehaviour {
    private NativeMultiHashMap<region, Particle> _partition;

    private NativeArray<float3> _positions;
    private NativeArray<float> _hues;

    private const int NumParticles = 1024;
    private Rng _rng;

    private void Awake() {
        _partition = new NativeMultiHashMap<region, Particle>(NumParticles, Allocator.Persistent);
        _positions = new NativeArray<float3>(1024, Allocator.Persistent);
        _hues = new NativeArray<float>(1024, Allocator.Persistent);

        _rng = new Rng(1234);

        for (int i = 0; i < NumParticles; i++) {
            // var region = vec2_qu8_0.FromInt((ushort)_rng.NextInt(256),(ushort)_rng.NextInt(256));
            var region = vec2_qu8_0.FromInt((ushort)_rng.NextInt(12), (ushort)_rng.NextInt(6));

            var particle = new Particle() {
                position = new position(new qu2_6((byte)_rng.NextInt(256)), new qu2_6((byte)_rng.NextInt(256))),
                velocity = velocity.FromInt(0, 0),
            };

            _partition.Add(region, particle);
        }
    }

    private void OnDestroy() {
        _partition.Dispose();
        _positions.Dispose();
        _hues.Dispose();
    }

    private double _movingAvg;

    private void Update() {
        var handle = new JobHandle();

        var updateParticlesJob = new UpdateParticlesJob() {
            rng = new Rng((uint)Time.frameCount)
        };
        handle = updateParticlesJob.Schedule(_partition, 16, handle);

        var atomicCounter = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        var convertParticlesJob = new ConvertParticlesJob() {
            positions = _positions,
            hues = _hues,
            counter = atomicCounter
        };
        handle = convertParticlesJob.Schedule(_partition, 16, handle);

        handle.Complete();
    }

    [BurstCompile]
    private struct UpdateParticlesJob : IJobNativeMultiHashMapVisitKeyMutableValue<region, Particle> {
        public Rng rng;

        public void ExecuteNext(region region, ref Particle particle) {
            var nudge = new velocity(
                new vscalar((sbyte)rng.NextInt(-1, 2)),
                new vscalar((sbyte)rng.NextInt(-1, 2)));

            particle.position.x += nudge.x;
            particle.position.y += nudge.y;
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
    private struct ConvertParticlesJob : IJobNativeMultiHashMapVisitKeyMutableValue<region, Particle> {
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float3> positions;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float> hues;
        [NativeDisableParallelForRestriction] public NativeArray<int> counter;

        public void ExecuteNext(region region, ref Particle p) {
            var rPos = new float3(
                    rScalar.ToFloat(region.x) * 4f,
                    rScalar.ToFloat(region.y) * 4f,
                    0f
                );

            int index = counter[0]++;

            hues[index] = ((((region.x.v * 17) % 13) + ((region.y.v * 19) % 11)) / (13f + 11f));
            positions[index] = (rPos + new float3(pscalar.ToFloat(p.position.x), pscalar.ToFloat(p.position.y), 0f));

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
            Gizmos.DrawSphere(_positions[i], 0.05f);
            // Gizmos.color = Color.magenta;
            // Gizmos.DrawRay(_positions[i], vel);
        }
    }

    public struct Particle {
        public position position;
        public velocity velocity;
    }
}