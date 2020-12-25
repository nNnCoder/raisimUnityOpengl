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
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;
using UnityEngine.UIElements;
using Button = UnityEngine.UI.Button;
using Slider = UnityEngine.UI.Slider;
using Toggle = UnityEngine.UI.Toggle;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.Rendering;

namespace raisimUnity
{
    public class UIController : MonoBehaviour
    {
        private RsUnityRemote _remote = null;
        private CameraController _camera = null;
        private ErrorViewController _error = null;
        private bool isAutoConnecting = false;
        
        static GUIStyle _style = null;
        private string _state = null;
        
        // UI element names
        // Buttons
        private const string _ButtonConnectName = "_ButtonConnect";
        private const string _ButtonScreenshotName = "_ButtonScreenshot";
        private const string _ButtonRecordName = "_ButtonRecord";
        private const string _ButtonAddResourceName = "_ButtonAddResource";
        private const string _ButtonDeleteResourceName = "_ButtonDeleteResource";
        
        // Scroll View
        private const string _ScrollViewResourceDirs = "ScrollResources";
        
        // Input field
        private const string _InputFieldTcpName = "_InputFieldTcp"; 
        private const string _InputFieldPortName = "_InputFieldPort"; 
        
        // Toggle
        private const string _ToggleVisualBodiesName = "_ToggleVisualBodies";
        private const string _ToggleCollisionBodiesName = "_ToggleCollisionBodies";
        private const string _ToggleContactPointsName = "_ToggleContactPoints";
        private const string _ToggleContactForcesName = "_ToggleContactForces";
        private const string _ToggleBodyFramesName = "_ToggleBodyFrames";
        private int connectionTryCounter = 0;
        
        // Sliders
        private const string _SliderBodyFramesName = "_SliderBodyFrames";
        private const string _SliderContactPointsName = "_SliderContactPoints";
        private const string _SliderContactForcesName = "_SliderContactForces";
        
        // Modal view
        private const string _ErrorModalViewName = "_CanvasModalViewError";
        private const string _ErrorModalViewMessageName = "_TextErrorMessage";

        private Thread _autoConnectThread;

        private List<string> _lookAtOptions;
        private List<string> _SkyOptions;

        private void Start()
        {
            _remote = GameObject.Find("RaiSimUnity").GetComponent<RsUnityRemote>();
            _camera = GameObject.Find("Main Camera").GetComponent<CameraController>();
            _error = GameObject.Find("_CanvasModalViewError").GetComponent<ErrorViewController>();

            if (_remote == null)
            {
                // TODO exception
            }

            if (_camera == null)
            {
                // TODO exception
            }

            // modal view
            {
                var modal = GameObject.Find(_ErrorModalViewName).GetComponent<Canvas>();
                var okButton = modal.GetComponentInChildren<Button>();
                okButton.onClick.AddListener(() => { modal.enabled = false;});
            }
            
            
            // visualize section
            {
                var toggleVisual = GameObject.Find(_ToggleVisualBodiesName).GetComponent<Toggle>();
                toggleVisual.onValueChanged.AddListener((isSelected) =>
                {
                    _remote.ShowVisualBody = isSelected;
                    _remote.ShowOrHideObjects();
                });
                var toggleCollision = GameObject.Find(_ToggleCollisionBodiesName).GetComponent<Toggle>();
                toggleCollision.onValueChanged.AddListener((isSelected) =>
                {
                    _remote.ShowCollisionBody = isSelected;
                    _remote.ShowOrHideObjects();
                });
                
                
                var sliderContactPoints = GameObject.Find(_SliderContactPointsName).GetComponent<Slider>();
                sliderContactPoints.onValueChanged.AddListener((value) => { _remote.ContactPointMarkerScale = value; });
                var toggleContactPoints = GameObject.Find(_ToggleContactPointsName).GetComponent<Toggle>();
                toggleContactPoints.onValueChanged.AddListener((isSelected) =>
                {
                    _remote.ShowContactPoints = isSelected;
                    _remote.ShowOrHideObjects();

                    if (isSelected) sliderContactPoints.interactable = true;
                    else sliderContactPoints.interactable = false;
                });

                var sliderContactForces = GameObject.Find(_SliderContactForcesName).GetComponent<Slider>();
                sliderContactForces.onValueChanged.AddListener((value) => { _remote.ContactForceMarkerScale = value; });
                var toggleContactForces = GameObject.Find(_ToggleContactForcesName).GetComponent<Toggle>();
                toggleContactForces.onValueChanged.AddListener((isSelected) =>
                {
                    _remote.ShowContactForces = isSelected;
                    _remote.ShowOrHideObjects();
                    
                    if (isSelected) sliderContactForces.interactable = true;
                    else sliderContactForces.interactable = false;
                });
                
                var sliderBodyFrames = GameObject.Find(_SliderBodyFramesName).GetComponent<Slider>();
                sliderBodyFrames.onValueChanged.AddListener((value) => { _remote.BodyFrameMarkerScale = value; });

                var sliderLight = GameObject.Find("_LightSlider").GetComponent<Slider>();
                sliderLight.onValueChanged.AddListener((value) => { GameObject.Find("Directional Light").transform.localRotation = Quaternion.Euler(50, value, 0); });

                var toggleBodyFrames = GameObject.Find(_ToggleBodyFramesName).GetComponent<Toggle>();
                toggleBodyFrames.onValueChanged.AddListener((isSelected) =>
                {
                    _remote.ShowBodyFrames = isSelected;
                    _remote.ShowOrHideObjects();
                    
                    if (isSelected) sliderBodyFrames.interactable = true;
                    else sliderBodyFrames.interactable = false;
                });
            }
            
            // connection section
            {
                var ipInputField = GameObject.Find(_InputFieldTcpName).GetComponent<InputField>();
                ipInputField.text = _remote.TcpAddress;
                var portInputField = GameObject.Find(_InputFieldPortName).GetComponent<InputField>();
                portInputField.text = _remote.TcpPort.ToString();
                var connectButton = GameObject.Find(_ButtonConnectName).GetComponent<Button>();
                connectButton.onClick.AddListener(() =>
                {
                    _remote.TcpAddress = ipInputField.text;
                    _remote.TcpPort = Int32.Parse(portInputField.text);
                
                    // connect / disconnect
                    if (!_remote.TcpConnected && !isAutoConnecting)
                    {
                        try
                        {
                            _remote.EstablishConnection();
                        }
                        catch (Exception e)
                        {
                            var modal = GameObject.Find(_ErrorModalViewName).GetComponent<ErrorViewController>();
                            modal.Show(true);
                            modal.SetMessage(e.Message);
                        }
                    }
                    else
                    {
                        try
                        {
                            _remote.CloseConnection();
                        }
                        catch (Exception)
                        {
                        
                        }
                    }
                });
            }
            
            // recording section 
            {
                var screenshotButton = GameObject.Find(_ButtonScreenshotName).GetComponent<Button>();
                screenshotButton.onClick.AddListener(() =>
                {
                    _camera.TakeScreenShot();
                });
                
                var recordButton = GameObject.Find(_ButtonRecordName).GetComponent<Button>();
                
                if (!_camera.videoAvailable) recordButton.interactable = false;
                else recordButton.interactable = true;
                
                recordButton.onClick.AddListener(() =>
                {
                    if (_camera.IsRecording)
                    {
                        _camera.FinishRecording();
                    }
                    else
                    {
                        _camera.StartRecording();
                    }
                });
            }
            
            // resource section 
            {
                _remote.ResourceLoader.LoadFromPref();
                RefereshScrollResources();

                var addButton = GameObject.Find(_ButtonAddResourceName).GetComponent<Button>();
                addButton.onClick.AddListener(() =>
                {
                    SimpleFileBrowser.FileBrowser.ShowLoadDialog((path) =>
                    {
                        _remote.ResourceLoader.AddResourceDirectory(path);
                        RefereshScrollResources();
                    }, null, true);
                });
                
                var removeButton = GameObject.Find(_ButtonDeleteResourceName).GetComponent<Button>();
                removeButton.onClick.AddListener(() =>
                {
                    _remote.ResourceLoader.RemoveResourceDirectory();
                    RefereshScrollResources();
                });
            }

            if (GraphicsSettings.renderPipelineAsset is HDRenderPipelineAsset)
                ConstructSkyHDRP();
        }

        private void TryConnect()
        {
            isAutoConnecting = true;
            _remote.EstablishConnection();
            isAutoConnecting = false;
        }

        private void RefereshScrollResources()
        {
            var scrollRect = GameObject.Find(_ScrollViewResourceDirs).GetComponent<ScrollRect>();
            
            // remove every text
            var uiContent = FindContent(scrollRect);
            foreach (Transform child in uiContent)
            {
                Destroy(child.gameObject);
            }

            // referesh text
            foreach (var dir in _remote.ResourceLoader.ResourceDirs)
            {
                DefaultControls.Resources tempResource = new DefaultControls.Resources();
                GameObject newText = DefaultControls.CreateText(tempResource);
                newText.AddComponent<LayoutElement>();
                newText.transform.SetParent(uiContent);
                newText.GetComponent<Text>().text = dir;
                newText.GetComponent<Text>().alignment = TextAnchor.MiddleLeft;
            }
        }

        private RectTransform FindContent (ScrollRect ScrollViewObject) {
            RectTransform RetVal = null;
            Transform[] Temp = ScrollViewObject.GetComponentsInChildren<Transform>();
            foreach (Transform Child in Temp) {
                if (Child.name == "Content") { RetVal = Child.gameObject.GetComponent<RectTransform>(); }
            }
            return RetVal;
        }
        
        private void ChangeLookAt(Dropdown dropdown)
        {
            if (dropdown.value != 0)
            {
                _camera.Follow(_lookAtOptions[dropdown.value]);
            }
        }

        public void ConstructSkyHDRP()
        {
            var SkyDropdown = GameObject.Find("_SkyDropDown").GetComponent<Dropdown>();
            _SkyOptions = new List<string>();
            foreach (var option in _remote._skyNames)
            {
                _SkyOptions.Add(option); // Or whatever you want for a label
            }

            SkyDropdown.ClearOptions();
            SkyDropdown.AddOptions(_SkyOptions);
            SkyDropdown.onValueChanged.AddListener(delegate {
                ChangeSky(SkyDropdown);
                DynamicGI.UpdateEnvironment();
            });
        }

        private void ChangeSky(Dropdown dropdown)
        {            
            GameObject volume = GameObject.Find("Sky");
            HDRISky sky;
            volume.GetComponent<Volume>().profile.TryGet<HDRISky>(out sky);
            sky.hdriSky.value = _remote._skyCubemaps[dropdown.value];

            if(dropdown.value == 7)
            {
                _camera.GetComponent<Camera>().GetComponent<HDAdditionalCameraData>().clearColorMode = HDAdditionalCameraData.ClearColorMode.Color;
                _camera.GetComponent<Camera>().GetComponent<HDAdditionalCameraData>().backgroundColorHDR = Color.white;
                _camera.GetComponent<Camera>().GetComponent<HDAdditionalCameraData>().volumeLayerMask = 0;
            } else
            {
                _camera.GetComponent<Camera>().GetComponent<HDAdditionalCameraData>().clearColorMode = HDAdditionalCameraData.ClearColorMode.Sky;
                _camera.GetComponent<Camera>().GetComponent<HDAdditionalCameraData>().volumeLayerMask = 1;
            }
            
        }

        public void ConstructLookAt()
        {
            var LookAtDropdown = GameObject.Find("_LookAtDropDown").GetComponent<Dropdown>();
            _lookAtOptions = new List<string> ();
            _lookAtOptions.Add("Free Cam");
            foreach (var option in _remote._objName) {
                _lookAtOptions.Add(option.Key); // Or whatever you want for a label
            }
            
            LookAtDropdown.ClearOptions ();
            LookAtDropdown.AddOptions(_lookAtOptions);
            LookAtDropdown.onValueChanged.AddListener(delegate {
                ChangeLookAt(LookAtDropdown);
                DynamicGI.UpdateEnvironment();
            });
        }
        
        // GUI
        void OnGUI()
        {
            // Set style once
            if (_style == null)
            {
                // _style = GUI.skin.textField;
                _style = GUI.skin.textArea;
                _style.normal.textColor = Color.white;
                _style.alignment = TextAnchor.LowerRight;

                // scale font size with DPI
                if (Screen.dpi < 100)
                    _style.fontSize = 14;
                else if (Screen.dpi > 300)
                    _style.fontSize = 34;
                else
                    _style.fontSize = Mathf.RoundToInt(14 + (Screen.dpi - 100.0f) * 0.1f);
            }

            GUILayout.BeginArea(new Rect(Screen.width - _state.Length*_style.fontSize/2, Screen.height-_style.fontSize*2, _state.Length*_style.fontSize/2, _style.fontSize*2), _style);  
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label(_state);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            

            // Show recording status
            var recordButton = GameObject.Find(_ButtonRecordName);
            
            if (_camera.IsRecording)
            {
                recordButton.GetComponentInChildren<Text>().text = "Stop Recording";
            }
            else
            {
                recordButton.GetComponentInChildren<Text>().text = "Record Video";
            }

            if (_camera.videoAvailable)
            {
                if (!_camera.IsRecording && _camera.ThreadIsProcessing)
                {
                    recordButton.GetComponent<Button>().interactable = false;
                    recordButton.GetComponentInChildren<Text>().text = "Saving Video...";
                }
                else
                {
                    recordButton.GetComponent<Button>().interactable = true;
                }
            }
            
            var connectToggle = GameObject.Find("_AutoConnect").GetComponent<Toggle>();

            if (!_error.isActive() && connectToggle.isOn && !isAutoConnecting)
            {
                // connect / disconnect
                if (!_remote.TcpConnected)
                {
                    if (connectionTryCounter++ % 100 == 0)
                    {
                        try
                        {
                            _autoConnectThread = new Thread(new ThreadStart(TryConnect));
                            _autoConnectThread.Start();
                            //_remote.EstablishConnection();
                        }
                        catch (Exception e)
                        {
                            isAutoConnecting = false;
                            var modal = GameObject.Find(_ErrorModalViewName).GetComponent<ErrorViewController>();
                            modal.Show(true);
                            modal.SetMessage(e.Message);
                        }
                    }
                }
                isAutoConnecting = false;
            }

            // Escape
            if (Input.GetKey("escape"))
                Application.Quit();
        }
        
        public void setState(string state)
        {
            _state = state;
        }
    }
}