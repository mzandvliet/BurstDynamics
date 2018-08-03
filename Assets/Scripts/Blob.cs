using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Jobs;
using Unity.Burst;

/* Todo:
 - Make time a factor
 - Interface for acceleration
 - 
 */

public struct Point {
	public float2 Last;
    public float2 Now;
}

public struct Line {
	public int A;
	public int B;
}

[BurstCompile]
public struct UpdatePointsJob : IJob {
    public NativeArray<Point> Points;
	
	public void Execute() {
		const float box = 10f;
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
public struct ConstrainPointsJob : IJob {
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

public class Blob : MonoBehaviour {
	private NativeArray<Point> _points;

	private void Awake() {
		const int numPoints = 1024;
		_points = new NativeArray<Point>(numPoints, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

		for (int i = 0; i < numPoints; i++) {
			_points[i] = new Point {
				Last = new float2(Random.Range(-0.1f, 0.1f),Random.Range(-0.1f, 0.1f)),
                Now = new float2(Random.Range(-0.1f, 0.1f), Random.Range(-0.1f, 0.1f))
			};
		}
	}

	private void OnDestroy() {
		_points.Dispose();
	}

	private void Update() {
		var h = new JobHandle();

		h = new UpdatePointsJob() {
			Points = _points
		}.Schedule();
		h = new ConstrainPointsJob() {
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
    }
}