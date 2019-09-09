// Add context menu named "Do Something" to context menu
using UnityEngine;
using UnityEditor;
using Unity.Mathematics;
using Ramjet.Mathematics.FixedPoint;
// using Ramjet.Math.Complex;
using Ramjet.Mathematics.LinearAlgebra;

public class CodeGeneratorWindow : EditorWindow {
    // Add menu item
    [MenuItem("CodeGen/Run Test")]
    static void Generate(MenuCommand command) {
        Debug.Log("Mixed mode addition, subtraction");
        Debug.Log(qs8_7.FromInt(112) + qs3_4.FromInt(1));
        Debug.Log(qs16_15.FromInt(112) - qs4_3.FromInt(2));

        Debug.Log("Mixed mode multiplication");
        Debug.Log(qu8_8.FromInt(2) * qs9_6.FromInt(2));
        Debug.Log(qs7_8.FromInt(2) * qs9_6.FromInt(-2));
        Debug.Log(qu8_8.FromInt(2) * qs9_6.FromInt(-2)); // Note: result is negative, but returned as positive

        Debug.Log("Same type division");
        Debug.Log("[" + qu4_4.RangeMinDouble + ", " + qu4_4.RangeMinDouble + "], " + qu4_4.EpsilonDouble);
        Debug.Log(qu4_4.FromInt(4) * qu4_4.FromInt(2));
        Debug.Log(qu4_4.FromInt(4) / qu4_4.FromInt(2));
        Debug.Log(qu4_4.FromInt(4) / qu4_4.FromInt(8));

        Debug.Log("Mixed type division");
        Debug.Log(new qu4_4((byte)((qu4_4.FromInt(4) / qu6_2.FromInt(3)).v + 1)));
        Debug.Log(new qu10_6((ushort)((qu10_6.FromInt(4) / qu6_2.FromInt(3)).v + 1)));

        // The following should throw ArgumentExceptions
        Debug.Log("Argument errors");
        Debug.Log(qu4_4.FromFloat(-31f));
        Debug.Log(qu4_4.FromInt(17));

        var a = qs15_16.FromInt(10);
        var b = qs14_17.FromInt(10);
        var c = qs12_19.FromFloat(11.5f);
        Debug.Log(c);

        var d = qs12_19.FromFloat(0.01f);
        Debug.Log(d);

        Debug.Log(c + d);
        Debug.Log(c * d);

        Debug.Log(qs12_19.Frac(qs12_19.FromFloat( 2.3456f)));
        Debug.Log(qs17_14.FromFloat(-1.2345f));
        Debug.Log(qs17_14.Frac(qs17_14.FromFloat(-1.2345f)));

        // float step = math.PI * 0.1f;
        // var phase = complex_qs7_24.FromFloat(1f, 0f);
        // var rotator = complex_qs7_24.FromFloat(math.cos(step), math.sin(step));
        // for (int i = 0; i < 8; i++) {
        //     phase = rotator * phase;
        //     Debug.Log(phase + ", " + (phase.r * phase.r + phase.i * phase.i));
        // }

        var vec = vec3_qs15_16.FromFloat(1f, 1f, 1f) - vec3_qs15_16.FromFloat(2f, 2f, 2f);
        Debug.Log("vec: " + vec + ", " + vec3_qs15_16.lengthsq(vec));
    }
}