using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;

/* Todo:
 Clarify connection between Newtonian dynamics, the math, and this code
 - Make time a factor
 - Interface for acceleration

 Computational:
 - Figure out how to interact points and sticks without cache trashing
 - Can parallelize UpdatePoints and CollidePoints easily. Sticks too,
 but only when parallely processed sticks don't share points.
        - Sticks forming a single box, hmm..
        - But sticks from separate non-interacting boxes are fine.
 */

public struct Point {
	public float2 Last;
    public float2 Now;
}

public struct Stick {
	public int A;
	public int B;
	public float Length;
}

[BurstCompile]
public struct UpdatePointsJob : IJob {
    public NativeArray<Point> Points;
	
	public void Execute() {
		const float friction = 0.999f;
		float2 g = new float2(0f, -0.01f);
		
		for (int i = 0; i < Points.Length; i++) {
			Point p = Points[i];
			float2 v = p.Now - p.Last;

			p.Last = p.Now;
            p.Now = p.Now + v * friction;
			p.Now += g;

			Points[i] = p;
		}
	}
}

[BurstCompile]
public struct CollidePointsJob : IJob {
    public NativeArray<Point> Points;

    public void Execute() {
        float2 box = new float2(20f, 10f);

        for (int i = 0; i < Points.Length; i++) {
            Point p = Points[i];
			float2 v = p.Now - p.Last;

            if (p.Now.x > box.x) {
                p.Now.x = box.x;
                p.Last.x = p.Now.x + v.x;
            } else if (p.Now.x < -box.x) {
                p.Now.x = -box.x;
                p.Last.x = p.Now.x + v.x;
            } else if (p.Now.y > box.x) {
                p.Now.y = box.y;
                p.Last.y = p.Now.y + v.y;
            } else if (p.Now.y < -box.y) {
                p.Now.y = -box.y;
                p.Last.y = p.Now.y + v.y;
            }

            Points[i] = p;
        }
    }
}

[BurstCompile]
public struct UpdateSticksJob : IJob {
    public NativeArray<Point> Points;
    public NativeArray<Stick> Sticks;

	/* Todo
	 - This kind of access to Points structure promotes cache trashing
	 */

    public void Execute() {

        for (int i = 0; i < Sticks.Length; i++) {
			Stick s = Sticks[i];

            Point a = Points[s.A];
            Point b = Points[s.B];

			float2 delta = a.Now - b.Now;
			float displacement = math.length(delta);
			float diff = s.Length - displacement;
			float factor = diff / displacement / 2.0f;
			float2 offset = delta * factor;

			a.Now += offset;
			b.Now -= offset;

			Points[s.A] = a;
            Points[s.B] = b;
        }
    }
}

public class Blob : MonoBehaviour {
	private NativeArray<Point> _points;
	private NativeArray<Stick> _sticks;

	private void Awake() {
        // const int numPoints = 1024;
        // _points = new NativeArray<Point>(numPoints, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

        // for (int i = 0; i < numPoints; i++) {
        // 	_points[i] = new Point {
        // 		Last = new float2(Random.Range(-0.1f, 0.1f),Random.Range(-0.1f, 0.1f)),
        //         Now = new float2(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f))
        // 	};
        // }

		// Create a box with 2 diagonal supports

        _points = new NativeArray<Point>(4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

		_points[0] = new Point {
			Last = new float2(0f, 0f),
			Now = new float2(0f, 1f)
		};
        _points[1] = new Point
        {
            Last = new float2(0f, 1f),
            Now = new float2(1f, 1f)
        };
        _points[2] = new Point
        {
            Last = new float2(1f, 1f),
            Now = new float2(1f, 0f)
        };
        _points[3] = new Point
        {
            Last = new float2(1f, 0f),
            Now = new float2(0f, 0f)
        };

		_sticks = new NativeArray<Stick>(6, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

		_sticks[0] = new Stick {
			A = 0,
			B = 1,
			Length = 1.0f
		};
        _sticks[1] = new Stick
        {
            A = 1,
            B = 2,
            Length = 1.0f
        };
        _sticks[2] = new Stick
        {
            A = 2,
            B = 3,
            Length = 1.0f
        };
        _sticks[3] = new Stick
        {
            A = 3,
            B = 0,
            Length = 1.0f
        };
        _sticks[4] = new Stick
        {
            A = 0,
            B = 2,
            Length = math.sqrt(2.0f)
        };
        _sticks[5] = new Stick
        {
            A = 1,
            B = 3,
            Length = math.sqrt(2.0f)
        };
	}

	private void OnDestroy() {
		_points.Dispose();
		_sticks.Dispose();
	}

	private void Update() {
		var h = new JobHandle();

		h = new UpdatePointsJob {
			Points = _points
		}.Schedule();
		h = new UpdateSticksJob {
			Points = _points,
			Sticks = _sticks
		}.Schedule(h);
		h = new CollidePointsJob {
			Points = _points
		}.Schedule(h);

		h.Complete();
	}

    private void OnDrawGizmos() {
		Gizmos.color = Color.red;
        for (int i = 0; i < _points.Length; i++) {
			float2 pos = _points[i].Now;
            Gizmos.DrawSphere(new Vector3(pos.x, pos.y, 0f), 0.1f);
        }

        Gizmos.color = Color.yellow;
        for (int i = 0; i < _sticks.Length; i++) {
            float2 a = _points[_sticks[i].A].Now;
            float2 b = _points[_sticks[i].B].Now;
            Gizmos.DrawLine(new Vector3(a.x, a.y, 0f), new Vector3(b.x, b.y, 0f));
        }
    }
}