using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Cutter : MonoBehaviour
{
    private DeformableMesh _deformable;
    private float _radius;

    void OnCollisionEnter(Collision other)
    {
        _deformable = other.gameObject.GetComponent<DeformableMesh>();
    }

    void OnCollisionExit(Collision other)
    {
        _deformable = null;
    }

    void Start()
    {
        _radius = GetComponent<SphereCollider>().radius * transform.localScale.x;
    }

    void Update()
    {
        if (_deformable != null)
        {
            _deformable.CutSphere(_deformable.transform.InverseTransformPoint(transform.position), _radius);
        }
    }
}
