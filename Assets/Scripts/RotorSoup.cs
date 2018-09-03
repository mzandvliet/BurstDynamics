using UnityEngine;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;

public struct Rotor {
    public float2 Position;
    public float2 Momentum;
    public float Mass;
}

//[BurstCompile]
public struct UpdateRotorsJob : IJob {
    public NativeArray<Rotor> Rotors;

    public void Execute() {
        // var pos = Rotors[4].Position;
        // Rotors[127] = new Rotor();
        for (int i = 0; i < Rotors.Length; i++) {
            Rotor a = Rotors[i];
            float2 momentum = float2.zero;
            // for (int j = 0; j < Rotors.Length; j++) {
            //     if (j == i) {
            //         continue;
            //     }

            //     Rotor b = Rotors[j];
            //     float2 localPos = a.Position - b.Position;
            //     float affect = 1f / math.length(localPos);
            //     float2 displaced = ComplexF.Mul(GetRotor(affect), localPos);
            //     accum += displaced - localPos;
            // }
            a.Momentum = momentum;
        }

        // for (int i = 0; i < Rotors.Length; i++) {
        //     Rotor r = Rotors[i];
        //     r.Position += RotorsAccum[i];
        //     Rotors[i] = r; 
        // }
    }

    private static Vector2 GetRotor(float momentum) {
        float2 result;
        math.sincos(momentum, out result.y, out result.x);
        return result;
    }
}

public class RotorSoup : MonoBehaviour {
    private NativeArray<Rotor> _rotors;

    private const int NumRotors = 128;

    private JobHandle _handle;
    
    private void Awake() {
        _rotors = new NativeArray<Rotor>(NumRotors, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        System.Random rand = new System.Random();
        const float extents = 10f;

        for (int i = 0; i < _rotors.Length; i++) {
            _rotors[i] = new Rotor {
                Position = new float2((float)rand.NextDouble() * extents, (float)rand.NextDouble() * extents),
                Mass = 1f
            };
        }

        var j = new UpdateRotorsJob();
        j.Rotors = _rotors;
        _handle = j.Schedule();
    }

    private void OnDestroy() {
        _rotors.Dispose();
    }

    // private void Update() {
    //     _handle.Complete();
        
    //     var j = new UpdateRotorsJob();
    //     j.Rotors = _rotors;
    //     j.RotorsAccum = _rotorsAccum;
    //     _handle = j.Schedule();
    // }

    private void OnDrawGizmos() {
        if (!Application.isPlaying) {
            return;
        }

        _handle.Complete();

        Gizmos.color = Color.red;
        for (int i = 0; i < _rotors.Length; i++) {
            Rotor r = _rotors[i];
            var pos = new Vector3(r.Position.x, r.Position.y, 0f);
            var mom = new Vector3(r.Momentum.x, r.Momentum.y, 0f);
            Gizmos.DrawSphere(pos, 0.5f);
            Gizmos.DrawRay(pos, mom);
        }
    }
}

