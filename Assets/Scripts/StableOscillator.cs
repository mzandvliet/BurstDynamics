using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Ramjet.Mathematics.FixedPoint;
using Unity.Mathematics;
using Unity.Collections;

/*
Was alerted to this through @mmalex's tweet, which spawned an interesting thread

Todo:

- Types

c, s can anything away from 0 that you want. Unitary, fractional,
integer, doesn't matter. Energy will be preserved.

Setting a to 1 means we loop the square, and we are definitely no
longer a circle or ellipse. Still stable though
 */


public class StableOscillator : MonoBehaviour
{
    private struct Osc {
        public qs1_14 c;
        public qs1_14 s;
    }
    
    private qu1_7 a;

    private NativeArray<Osc> _oscStates;
    private int _head;

    void Start()
    {
        _oscStates = new NativeArray<Osc>(128, Allocator.Persistent);

        _head = 0;
        _oscStates[_head] = new Osc {
            c = qs1_14.FromInt(1),
            s = qs1_14.FromInt(0)
        };

        a = qu1_7.FromFloat(0.01f);
        // a = new qs15_16(256 * 31 + 128 + 5); // Allllmost hitting the same phase every time
    }

    private void OnDestroy() {
        _oscStates.Dispose();
    }

    // Update is called once per frame
    void Update()
    {
        var headOsc = _oscStates[_head];
        _head = (_head+1)%_oscStates.Length;
        var c = headOsc.c - headOsc.s * a;
        var s = headOsc.s + c * a;
        _oscStates[_head] = new Osc {
            c = c,
            s = s
        };

    }

    void OnDrawGizmos() {
        for (int i = 0; i < _oscStates.Length; i++) {
            var idx = (_head + 1 + i) % _oscStates.Length;
            var pos = new float3(
                qs1_14.ToFloat(_oscStates[idx].c),
                qs1_14.ToFloat(_oscStates[idx].s),
                0f
            );

            var color = i == _oscStates.Length - 1 ?
                Color.magenta :
                new Color(1,1,1, i / (float)_oscStates.Length);

            Gizmos.color = color;
            Gizmos.DrawLine(Vector3.zero, pos);
            Gizmos.DrawSphere(pos, 0.05f);
        }
    }
}
