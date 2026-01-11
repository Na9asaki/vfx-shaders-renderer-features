using System;
using UnityEngine;
using UnityEngine.VFX;

public class VFXExplousion : MonoBehaviour
{
    [SerializeField] private VisualEffect _vfx;


    public void Boom()
    {
        _vfx.Play();
    }
}
