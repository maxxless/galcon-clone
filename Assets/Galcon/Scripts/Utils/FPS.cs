using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FPS : MonoBehaviour
{
    [SerializeField] private int maxFpsCount = 144;
    private void Awake()
    {
        Application.targetFrameRate = maxFpsCount;
    }
}
