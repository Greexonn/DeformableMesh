using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimpleMobileCameraController : MonoBehaviour
{
    void Start()
    {
        Input.gyro.enabled = true;
    }

    void Update()
    {
        if (Input.touchCount > 0)
        {
            transform.Translate(transform.forward * -2 * Time.deltaTime);
        }

        transform.Rotate(-Input.gyro.rotationRateUnbiased.x, -Input.gyro.rotationRateUnbiased.y, 0);
    }
}
