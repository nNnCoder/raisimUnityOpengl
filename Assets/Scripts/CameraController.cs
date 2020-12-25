/*
 * MIT License
 * 
 * Copyright (c) 2019, Dongho Kang, Robotics Systems Lab, ETH Zurich
 * 
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 *
 * Note. This code is inspired by https://gist.github.com/DashW/74d726293c0d3aeb53f4
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using raisimUnity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    // camera pose control
    private float speed = 0.1f;
    private float sensitivity = 0.5f;
 
    private Camera cam;
    private Vector3 _anchorPoint;
    private Quaternion _anchorRot;
    private Vector3 _relativePositionB;
    private Vector3 _cursorPosition;
    private int _cursorStaticCount = 0;

    // object selection
    private GameObject _selected; 

    // video recording
    private bool _isRecording = false;

    public bool IsRecording
    {
        get => _isRecording;
    }

    public int selectedNumber_ = 0;

    // Public Properties
    public int maxFrames; // maximum number of frames you want to record in one video
    private int frameRate = 60; // number of frames to capture per second
    public bool videoAvailable = false;
    
    // The Encoder Thread
    private Thread _saverThread;

    // Texture Readback Objects
    private Texture2D _tempTexture2D24;

    // Timing Data
    private float captureFrameTime;
    private float lastFrameTime;
    private int frameNumber;
    private int frameNumberSent = 0;

    // Encoder Thread Shared Resources
    private Queue<byte[]> _frameQueue;
    private int _screenWidth;
    private int _screenHeight;
    private bool terminateThreadWhenDone;
    private bool threadIsProcessing;
    
    // Screenshot related
    private string _dirPath = "";
    private string _videoName = "Recording.mp4";    // updated to Recording-<TIME>.mp4
    private float recordingStartTime_;
    private int _moreFrames=1;
    
    // Error modal view
    private const string _ErrorModalViewName = "_CanvasModalViewError";
    private ErrorViewController _errorModalView;
    private GameObject _sidebar;
    private GameObject _helpUI;

    private Vector3 targetPos;
    private Vector3 currentPos;

    // UI
    private Shader _standardShader;

    // object to follow
    private string _toFollow="";
    
    // easy access
    private RsUnityRemote _remote;
    public bool ThreadIsProcessing
    {
        get => threadIsProcessing;
    }

    public void Follow(string obj, Vector3 pos)
    {
        _toFollow = obj;
        _relativePositionB = pos;
    }
    
    public void Follow(string obj)
    {
        _toFollow = obj;
    }

    IEnumerator Start () 
    {
        cam = GetComponent<Camera>();
        if (GraphicsSettings.renderPipelineAsset is HDRenderPipelineAsset)
        {
            _standardShader = Shader.Find("HDRP/Lit");
        }
        else
        {
            _standardShader = Shader.Find("Standard");
        }
        
        // Error modal view
        _errorModalView = GameObject.Find("_CanvasModalViewError").GetComponent<ErrorViewController>();
        
        // Check if FFMPEG available
        int ffmpegExitCode = FFMPEGTest();
        if (ffmpegExitCode == 0)
            videoAvailable = true;
        
        // Check if video directory is created
        _dirPath = Path.Combine(Application.dataPath, "../Screenshot");
        if (!File.Exists(_dirPath))
            Directory.CreateDirectory(_dirPath);
        
        _sidebar = GameObject.Find("_CanvasSidebar");
        _helpUI = GameObject.Find("_CanvasHelpUI");
        GameObject.Find("_CanvasSidebar").GetComponent<Canvas>().enabled = true;
        GameObject.Find("_CanvasHelpUI").GetComponent<Canvas>().enabled = false;
        _remote = GameObject.Find("RaiSimUnity").GetComponent<RsUnityRemote>();
        
        // Set target frame rate (optional)
        _relativePositionB = new Vector3(1.0f,1.0f,-1.0f);

        // Prepare textures and initial values
        _screenWidth = cam.pixelWidth;
        _screenHeight = cam.pixelHeight;

        _frameQueue = new Queue<byte[]> ();
        
        frameNumber = 0;

        captureFrameTime = 1.0f / (float)frameRate;
        lastFrameTime = Time.time;
        
        // Kill the encoder thread if running from a previous execution
        if (_saverThread != null && (threadIsProcessing || _saverThread.IsAlive)) {
            threadIsProcessing = false;
            _saverThread.Join();
        }
        
        while (true)
        {
            yield return new WaitForEndOfFrame();
            
            if (_isRecording)
            {
                // Check if render target size has changed, if so, terminate
                if(Screen.width != _screenWidth || Screen.height != _screenHeight)
                {
                    FinishRecording();
                    _screenWidth = Screen.width;
                    _screenHeight = Screen.height;
            
                    // Show error modal view
                    _errorModalView.Show(true);
                    _errorModalView.SetMessage("You cannot change screen size during a recording. Terminated recording. (video is saved)");
                }

                // Calculate number of video frames to produce from this game frame
                // Generate 'padding' frames if desired framerate is higher than actual framerate
                float thisFrameTime = Time.time;
                // _tempTexture2D = ScreenCapture.CaptureScreenshotAsTexture();
                // Add the required number of copies to the queue

                var _rt = RenderTexture.GetTemporary(Screen.width, Screen.height, 0);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(_rt);
                AsyncGPUReadback.Request(_rt, 0, TextureFormat.RGB24, OnCompleteReadback);
                RenderTexture.ReleaseTemporary(_rt);
                GameObject.Find("_CanvasSidebar").GetComponent<UIController>().setState((1.0 / Time.smoothDeltaTime).ToString() + "  " + frameNumberSent);
                var currentTime = Time.time;
                _moreFrames = (int)Math.Max((currentTime - recordingStartTime_) * frameRate - frameNumberSent, 1.0);

                lastFrameTime = thisFrameTime;
            }
        }
    }

    private void OnApplicationQuit()
    {
        FinishRecording();
    }

    void OnCompleteReadback(AsyncGPUReadbackRequest request)
    {
        _frameQueue.Enqueue(request.GetData<Byte>().ToArray());
        frameNumber++;
    }
    
    private static void FlipTextureVertically(Texture2D original)
    {
        // on some platforms texture is flipped by unity
        
        var originalPixels = original.GetPixels();

        Color[] newPixels = new Color[originalPixels.Length];

        int width = original.width;
        int rows = original.height;

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                newPixels[x + y * width] = originalPixels[x + (rows - y -1) * width];
            }
        }

        original.SetPixels(newPixels);
        original.Apply();
    }

    void Update() {
        if (!string.IsNullOrEmpty(_toFollow))
        {
            _selected = GameObject.Find(_remote.GetSubName(_toFollow));
        }
        
        // move by keyboard
        if (_selected == null)
        {
            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W))
                move += Vector3.forward * speed;
            if (Input.GetKey(KeyCode.S))
                move -= Vector3.forward * speed;
            if (Input.GetKey(KeyCode.D))
                move += Vector3.right * speed;
            if (Input.GetKey(KeyCode.A))
                move -= Vector3.right * speed;
            if (Input.GetKey(KeyCode.E))
                move += Vector3.up * speed;
            if (Input.GetKey(KeyCode.Q))
                move -= Vector3.up * speed;
            transform.Translate(move);
        }

        var view = cam.ScreenToViewportPoint(Input.mousePosition);
        if (view[0] < 0 || view[0] > 0.2 || view[1] > 1 || view[1] < 0)
        {
            _cursorStaticCount++;
        }
        else
        {
            _cursorStaticCount = 0;
        }
        
        _cursorPosition = view;
        
        if (_cursorStaticCount > 80)
        {
            _sidebar.GetComponent<Canvas>().enabled = false;
            _helpUI.GetComponent<Canvas>().enabled = false;
        }
        else
        {
            UIController prevUi = GameObject.Find("_CanvasSidebar").GetComponent<UIController>();
            prevUi.gameObject.GetComponent<Canvas>().enabled = true;
        }

        if (!EventSystem.current.IsPointerOverGameObject ()) 
        {
            // Only do this if mouse pointer is not on the GUI
            
            // Select object by left click
            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;
                if (Physics.Raycast(ray, out hit))
                {
                    // Set selected object
                    _selected = hit.transform.parent.gameObject;
                    _toFollow = "";
                }
            }

            // Change camera orientation by right drag 
            if (Input.GetMouseButtonDown(1))
            {
                _anchorPoint = new Vector3(Input.mousePosition.y, -Input.mousePosition.x);
                _anchorRot = transform.rotation;

                // deselect object by right click
                if (_selected != null)
                {
                    foreach (var ren in _selected.GetComponentsInChildren<Renderer>())
                    {
                        ren.material.shader = _standardShader;
                    }
                }
            
                _selected = null;
                _toFollow = "";
            }
            
            if (Input.GetMouseButton(1))
            {
                Quaternion rot = _anchorRot;
                Vector3 dif = _anchorPoint - new Vector3(Input.mousePosition.y, -Input.mousePosition.x);
                rot.eulerAngles += dif * sensitivity;
                transform.rotation = rot;
            }   
            
            // Set anchor for orbiting around selected object  
            if (Input.GetMouseButtonDown(0) && _selected != null)
            {
                _anchorPoint = new Vector3(Input.mousePosition.y, -Input.mousePosition.x);
                _anchorRot = transform.rotation;
            }
            
            if (Input.GetMouseButton(0) && _selected != null)
            {
                Vector3 dif = _anchorPoint - new Vector3(Input.mousePosition.y, -Input.mousePosition.x);
                Vector3 normRelPos = _relativePositionB.normalized;
                Vector3 crossed = new Vector3(normRelPos[2], 0, -normRelPos[0]);
                
                Quaternion rot1 = Quaternion.AngleAxis(sensitivity * dif[1], new Vector3(0, 1, 0));
                Quaternion rot2 = Quaternion.AngleAxis(sensitivity * -dif[0], crossed.normalized);
                
                var newPos = rot2 * rot1 * _relativePositionB;
                _relativePositionB = newPos;
                
                _anchorPoint = new Vector3(Input.mousePosition.y, -Input.mousePosition.x);
                _anchorRot = transform.rotation;
            }
            
            // zoom in and out
            if (Input.GetAxis("Mouse ScrollWheel") > 0f  && _selected != null)
            {
                _relativePositionB *= 1.15f;
            }
            
            if (Input.GetAxis("Mouse ScrollWheel") < 0f  && _selected != null)
            {
                _relativePositionB /= 1.15f;
            }
        }
        
        // Follow and orbiting around selected object  
        if (_selected != null)
        {
            targetPos = _selected.transform.position + _relativePositionB;
            currentPos = 0.3f * targetPos + 0.7f * transform.position;
            
            transform.position = currentPos;
            transform.transform.LookAt(_selected.transform.position);
        }
        var halfspace = GameObject.Find("halfspace_viz");
        if (halfspace != null)
        {
            GameObject.Find("Planar Reflection").GetComponent<PlanarReflectionProbe>().enabled = true;
            var position = transform.position;
            position.y = halfspace.transform.position.y + 0.001f;
            var planarReflection = GameObject.Find("Planar Reflection");

            GameObject.Find("Planar Reflection").GetComponent<PlanarReflectionProbe>().RequestRenderNextUpdate();
            float increment = 16.6666666f;
            halfspace.transform.position = new Vector3( Convert.ToInt32(position.x/increment)*increment, halfspace.transform.position.y, Convert.ToInt32(position.z/increment)*increment);
            planarReflection.transform.position = halfspace.transform.position;
        }
        else
        {
            GameObject.Find("Planar Reflection").GetComponent<PlanarReflectionProbe>().enabled = false;
        }
    }
    
    public void TakeScreenShot()
    {
        if (!File.Exists(_dirPath))
            Directory.CreateDirectory(_dirPath);
        var filename = Path.Combine(
            _dirPath,
            "Screenshot-" + DateTime.Now.ToString("yyyy-MM-dd-hh-mm-ss") + ".png");
        ScreenCapture.CaptureScreenshot(filename);
    }
    
    public void StartRecording(string videoName="")
    {

        if (FFMPEGTest() == -1) return;
        
        Application.targetFrameRate = 60;
        // Kill thread if it's still alive
        if (_saverThread != null && (threadIsProcessing || _saverThread.IsAlive)) {
            threadIsProcessing = false;
            _saverThread.Join();
        }
        _tempTexture2D24 = new Texture2D(_screenWidth, _screenHeight, TextureFormat.RGB24, false);

        // Set recording screend width and height
        _screenWidth = cam.pixelWidth;
        _screenHeight = cam.pixelHeight;

        // Start recording
        if (threadIsProcessing)
        {
            // TODO error... something wrong...
            print("oops...");
        }
        else
        {
            _isRecording = true;
            frameNumber = 0;
            frameNumberSent = 0;
        
            // Start a new encoder thread
            if (videoName == "")
                _videoName = "Recording-" + DateTime.Now.ToString("yyyy-MM-dd-hh-mm-ss") + ".mp4";
            else
                _videoName = videoName;
            
            lastFrameTime = Time.time - lastFrameTime;

            terminateThreadWhenDone = false;
            threadIsProcessing = true;
            recordingStartTime_ = Time.time;
            _saverThread = new Thread(SaveVideo);
            _saverThread.Start();
        }
    }

    public void FinishRecording()
    {
        // Done queueing
        _isRecording = false;
        
        // Terminate thread after it saves
        terminateThreadWhenDone = true;
        if(_saverThread != null && _saverThread.IsAlive) _saverThread.Join();
        _frameQueue.Clear();
        Application.targetFrameRate = 150;
    }

    private int FFMPEGTest()
    {
        // to check ffmpeg works 
        using (var ffmpegProc = new Process())
        {
            if ((Application.platform == RuntimePlatform.LinuxEditor ||
                Application.platform == RuntimePlatform.LinuxPlayer) &&
                GraphicsSettings.renderPipelineAsset is HDRenderPipelineAsset)
            {
                // Linux
                ffmpegProc.StartInfo.FileName = "/bin/sh";
            }
            else if (Application.platform == RuntimePlatform.OSXEditor ||
                     Application.platform == RuntimePlatform.OSXPlayer)
            {
                // Mac
                return -1;
            }
            else
            {
                // Else...
                return -1;
            }

            ffmpegProc.StartInfo.UseShellExecute = false;
            ffmpegProc.StartInfo.CreateNoWindow = true;
            ffmpegProc.StartInfo.RedirectStandardInput = true;
            ffmpegProc.StartInfo.RedirectStandardOutput = true;
            ffmpegProc.StartInfo.RedirectStandardError = true;
            ffmpegProc.StartInfo.Arguments =
                "-c \"" +
                "ffmpeg -version\"";

            ffmpegProc.OutputDataReceived += new DataReceivedEventHandler((s, e) =>
            {
                // print(e.Data);    // this is for debugging 
            });
            ffmpegProc.ErrorDataReceived += new DataReceivedEventHandler((s, e) =>
            {
                // print(e.Data);     // this is for debugging
            });

            // Start ffmpeg
            ffmpegProc.Start();
            ffmpegProc.BeginErrorReadLine();
            ffmpegProc.BeginOutputReadLine();

            while (!ffmpegProc.HasExited) {}
            
            // check exit code
            return ffmpegProc.ExitCode;
        }

    }

    private void SaveVideo()
    {
        print ("SCREENRECORDER IO THREAD STARTED");

        // Generate file path
        string path = Path.Combine(_dirPath, _videoName);

        using (var ffmpegProc = new Process())
        {
            string argumentPrefix = "";
            
            if (Application.platform == RuntimePlatform.LinuxEditor ||
                Application.platform == RuntimePlatform.LinuxPlayer)
            {
                // Linux
                ffmpegProc.StartInfo.FileName = "/bin/sh";
                argumentPrefix = "-c \"" + "ffmpeg ";
            }
            else if (Application.platform == RuntimePlatform.OSXEditor || 
                     Application.platform == RuntimePlatform.OSXPlayer)
            {
                // Mac
                ffmpegProc.StartInfo.FileName = "/bin/sh";
                argumentPrefix = "-c \"" + "ffmpeg ";
            }
            else
            {
                // Else...
                ffmpegProc.StartInfo.FileName = "ffmpeg.exe";
            }
            
            ffmpegProc.StartInfo.UseShellExecute = false;
            ffmpegProc.StartInfo.CreateNoWindow = true;
            ffmpegProc.StartInfo.RedirectStandardInput = true;
            ffmpegProc.StartInfo.RedirectStandardOutput = true;
            ffmpegProc.StartInfo.RedirectStandardError = true;
            ffmpegProc.StartInfo.Arguments =
                argumentPrefix + "-r " + frameRate.ToString() + " -f rawvideo -pix_fmt rgb24 -s " + _screenWidth.ToString() + "x" + _screenHeight.ToString() +
                " -i - -threads 0 -preset fast -y -c:v libx264 " +
                "-crf 21 " + path + "\"";
        
            ffmpegProc.OutputDataReceived += new DataReceivedEventHandler((s, e) => 
            { 
                // print(e.Data);    // this is for debugging 
            });
            ffmpegProc.ErrorDataReceived += new DataReceivedEventHandler((s, e) =>
            {
                // print(e.Data);     // this is for debugging
            });

            // Start ffmpeg
            ffmpegProc.Start();
            ffmpegProc.BeginErrorReadLine();
            ffmpegProc.BeginOutputReadLine();

            if (ffmpegProc.HasExited)
            {
                // check exit code
                int result = ffmpegProc.ExitCode;
                if (result == 127)
                {
                    new RsuException("ffmpeg command not found. Something is wrong with the installation");
                }
            }

            while (threadIsProcessing) 
            {
                // Dequeue the frame, encode it as a bitmap, and write it to the file
                if(_frameQueue.Count > 0)
                {
                    var ffmpegStream = ffmpegProc.StandardInput.BaseStream;
                
                    byte[] data = _frameQueue.Dequeue();
                    
                    for (int i = 0; i < Math.Min(_moreFrames,2); i++)
                    {
                        ffmpegStream.Write(data, 0, data.Length);
                        ffmpegStream.Flush();
                        frameNumberSent++;
                    }
                }
                else
                {
                    if(terminateThreadWhenDone)
                    {
                        break;
                    }
                }
            }
        
            // Close ffmpeg
            ffmpegProc.StandardInput.BaseStream.Flush();
            ffmpegProc.StandardInput.BaseStream.Close();
            ffmpegProc.WaitForExit();
            
            if (ffmpegProc.HasExited)
            {
                // check exit code
                int result = ffmpegProc.ExitCode;
                if (result != 0)
                {
                    // TODO error
                }
            }
            
            ffmpegProc.Dispose();
        }
        
        
        terminateThreadWhenDone = false;
        threadIsProcessing = false;
        
        print ("SCREENRECORDER IO THREAD FINISHED");
    }
}