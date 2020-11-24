using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class reflectionProbe : MonoBehaviour
{
    // Start is called before the first frame update
    private CameraController _camera;
    
    void Start()
    {
        _camera = GameObject.Find("Main Camera").GetComponent<CameraController>();
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
