using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraPanner2D : MonoBehaviour
{
    [SerializeField] private float _speed = 1f;
  
    private void Update()
    {
        transform.Translate(new Vector3(
            Input.GetAxis("Horizontal"),
            Input.GetAxis("Vertical"),
            0f
        ));
    }
}
