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
using System.Linq;
using System.Xml;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;

namespace raisimUnity
{
    enum ClientStatus : int
    {
        Idle = 0,    // waiting for connection or server is hibernating
        InitializingObjects,
        UpdateObjectPosition,
    }

    public enum RsObejctType : int
    {
        RsSphereObject = 0, 
        RsBoxObject,
        RsCylinderObject,
        RsConeObject, 
        RsCapsuleObject,
        RsMeshObject,
        RsHalfSpaceObject, 
        RsCompoundObject,
        RsHeightMapObject,
        RsArticulatedSystemObject,
    }

    public enum RsShapeType : int
    {
        RsBoxShape = 0, 
        RsCylinderShape,
        RsSphereShape,
        RsMeshShape,
        RsCapsuleShape, 
        RsConeShape,
    }

    public enum RsVisualType : int
    {
        RsVisualSphere = 0,
        RsVisualBox,
        RsVisualCylinder,
        RsVisualCapsule,
        RsVisualMesh,
        RsVisualArrow
    }

    static class VisualTag
    {
        public const string Visual = "visual";
        public const string Collision = "collision";
        public const string ArticulatedSystemCollision = "articulated_system_collision";
        public const string Frame = "frame";
        public const string Both = "both";
    }

    public class RsUnityRemote : MonoBehaviour
    {
        // Prevent repeated instances
        private static RsUnityRemote instance;
        
        private XmlReader _xmlReader;
        private ResourceLoader _loader;
        private TcpHelper _tcpHelper;
        public Dictionary<string, string> _objName;
        
        private RsUnityRemote()
        {
            _tcpHelper = new TcpHelper();
            _xmlReader = new XmlReader();
            _loader = new ResourceLoader();
            _objName = new Dictionary<string, string>();
        }
        
        // Status
        private ClientStatus _clientStatus;

        // Visualization
        private bool _showVisualBody = true;
        private bool _showCollisionBody = false;
        private bool _showContactPoints = false;
        private bool _showContactForces = false;
        private bool _showBodyFrames = false;
        private float _contactPointMarkerScale = 1;
        private float _contactForceMarkerScale = 1;
        private float _bodyFrameMarkerScale = 1;
        private GameObject _arrowMesh;

        // Root objects
        private GameObject _objectsRoot;
        private GameObject _visualsRoot;
        private GameObject _contactPointsRoot;
        private GameObject _polylineRoot;
        private GameObject _contactForcesRoot;
        private GameObject _objectCache;
        
        // Object controller 
        private ObjectController _objectController;
        private ulong _numInitializedObjects;
        private ulong _numWorldObjects; 
        private ulong _numInitializedVisuals;
        private ulong _numWorldVisuals;
        private ulong _wireN=0;
        
        // Shaders
        private Shader _transparentShader;
        private Shader _standardShader;
        
        // Default materials
        private Material _planeMaterial;
        private Material _whiteMaterial;
        private Material _wireMaterial;
        private Material _defaultMaterialR;
        private Material _defaultMaterialG;
        private Material _defaultMaterialB;

        // Modal view
        private ErrorViewController _errorModalView;
        private LoadingViewController _loadingModalView;
        
        // Configuration number (should be always matched with server)
        private ulong _objectConfiguration = 0; 
        private CameraController _camera = null;
        private string _defaultShader;
        private string _colorString;
        
        // objects reinitialize
        private bool _initialization = true;
        
        // visualization arrows
        private ulong _nCreatedArrowsForContactForce = 0;
        private ulong _nCreatedArrowsForExternalForce = 0;
        private ulong _nCreatedArrowsForExternalTorque = 0;
        private ulong _nCreatedPolyLineBox = 0;

        void Start()
        {
            // object roots
            _objectsRoot = new GameObject("_RsObjects");
            _objectsRoot.transform.SetParent(transform);
            _objectCache = new GameObject("_ObjectCache");
            _objectCache.transform.SetParent(transform);
            _visualsRoot = new GameObject("_VisualObjects");
            _visualsRoot.transform.SetParent(transform);
            _contactPointsRoot = new GameObject("_ContactPoints");
            _contactPointsRoot.transform.SetParent(transform);
            _polylineRoot = new GameObject("_polylineRoot");
            _polylineRoot.transform.SetParent(transform);
            _contactForcesRoot = new GameObject("_ContactForces");
            _contactForcesRoot.transform.SetParent(transform);
            _camera = GameObject.Find("Main Camera").GetComponent<CameraController>();
            _arrowMesh = Resources.Load("others/arrow") as GameObject;

            if (GraphicsSettings.renderPipelineAsset is HDRenderPipelineAsset)
            {
                _defaultShader = "HDRP/Lit";
                _colorString = "_BaseColor";
            }
            else
            {
                _defaultShader = "Standard";
                _colorString = "_Color";
            }
            
            // object controller 
            _objectController = new ObjectController(_objectCache);

            // shaders 
            _standardShader = Shader.Find(_defaultShader);
            _transparentShader = Shader.Find("RaiSim/Transparent");

            // materials
            _planeMaterial = Resources.Load<Material>("Tiles1");
            _whiteMaterial = Resources.Load<Material>("white");
            _wireMaterial = Resources.Load<Material>("wire");
            _defaultMaterialR = Resources.Load<Material>("Plastic1");
            _defaultMaterialG = Resources.Load<Material>("Plastic2");
            _defaultMaterialB = Resources.Load<Material>("Plastic3");
            
            // ui controller 
            _errorModalView = GameObject.Find("_CanvasModalViewError").GetComponent<ErrorViewController>();
            _loadingModalView = GameObject.Find("_CanvasModalViewLoading").GetComponent<LoadingViewController>();
            _clientStatus = ClientStatus.Idle;
        }

        public void EstablishConnection(int waitTime=1000)
        {
            _tcpHelper.EstablishConnection(waitTime);
            _clientStatus = ClientStatus.InitializingObjects;
        }

        public void CloseConnection()
        {
            ClearScene();
            
            _tcpHelper.CloseConnection();
            _clientStatus = ClientStatus.Idle;
        }

        void Update()
        {
            // Broken connection: clear
            if( !_tcpHelper.CheckConnection() )
            {
                CloseConnection();
            }
            else
            {
                _errorModalView.Show(false);
            }
     
            // Data available: handle communication
            if (_tcpHelper.DataAvailable)
            {
                try
                {
                    switch (_clientStatus)
                    {
                        //**********************************************************************************************
                        // Step 0
                        //**********************************************************************************************
                        case ClientStatus.Idle:
                        {
                            try
                            {
                                // Server hibernating
                                ClearScene();
                                _tcpHelper.CloseConnection();
                                _clientStatus = ClientStatus.InitializingObjects;
                                ReadAndCheckServer(ClientMessageType.RequestServerStatus);
                            }
                            catch (Exception e)
                            {
                                new RsuException(e, "RsUnityRemote: ClientStatus.Idle");
                            }
                            
                            break;
                        }
                        //**********************************************************************************************
                        // Step 1
                        //**********************************************************************************************
                        case ClientStatus.InitializingObjects:
                        {
                            try
                            {
                                if (_initialization)
                                {
                                    // If server side has been changed, initialize objects clear objects first
                                    foreach (Transform objT in _objectsRoot.transform)
                                        Destroy(objT.gameObject);
                                    
                                    foreach (Transform objT in _visualsRoot.transform)
                                        Destroy(objT.gameObject);
                                    
                                    // Read XML string
                                    // ReadXmlString();
                                    if (ReadAndCheckServer(ClientMessageType.RequestInitializeObjects) != ServerMessageType.Initialization)
                                        return;
                                    _objectConfiguration = _tcpHelper.GetDataUlong();
                                    _numWorldObjects = _tcpHelper.GetDataUlong();
                                    _numInitializedObjects = 0;
                                    _numInitializedVisuals = 0;
                                    _initialization = false;
                                    // _loadingModalView.Show(true);
                                    // _loadingModalView.SetTitle("Initializing RaiSim Objects Starts");
                                    // _loadingModalView.SetMessage("Loading resources...");
                                    // _loadingModalView.SetProgress((float) 0 / _numWorldObjects);
                                }
                                
                                if (_numInitializedObjects < _numWorldObjects)
                                {
                                    // Initialize objects from data
                                    // If the function call time is > 0.1 sec, rest of objects are initialized in next Update iteration
                                    PartiallyInitializeObjects();
                                    // _loadingModalView.SetProgress((float) _numInitializedObjects / _numWorldObjects);   

                                    if (_numInitializedObjects == _numWorldObjects)
                                    {
                                        _wireN = _tcpHelper.GetDataUlong();
                                        for (ulong i = 0; i < _wireN; i++)
                                        {
                                            var objFrame = _objectController.CreateRootObject(_objectsRoot, "wire" + i);
                                            var cylinder = _objectController.CreateCylinder(objFrame, 1, 1);
                                            cylinder.GetComponentInChildren<MeshRenderer>().material.shader =
                                                _standardShader;
                                            cylinder.GetComponentInChildren<MeshRenderer>().material = _wireMaterial;
                                            cylinder.tag = VisualTag.Both;
                                        }
                                    }
                                }
                                
                                if (_numInitializedObjects == _numWorldObjects)
                                {
                                    if(_numInitializedVisuals == 0)
                                        _numWorldVisuals = _tcpHelper.GetDataUlong();
                                    
                                    if (_numInitializedVisuals < _numWorldVisuals)
                                    {
                                        // Initialize visuals from data
                                        // If the function call time is > 0.1 sec, rest of objects are initialized in next Update iteration
                                        PartiallyInitializeVisuals();
                                        // _loadingModalView.SetProgress((float) _numInitializedVisuals / _numWorldVisuals);   
                                    }
                                    
                                    if (_numInitializedVisuals == _numWorldVisuals)
                                    {
                                        // Disable other cameras than main camera
                                        foreach (var cam in Camera.allCameras)
                                        {
                                            if (cam == Camera.main) continue;
                                            cam.enabled = false;
                                        }
                                        UpdateObjectsPosition();

                                        // Initialization done 
                                        _clientStatus = ClientStatus.UpdateObjectPosition;
                                        _initialization = true;
                                        ShowOrHideObjects();
                                        // _loadingModalView.Show(false);
                                        GameObject.Find("_CanvasSidebar").GetComponent<UIController>().ConstructLookAt();
                                    }
                                }
                            } catch (Exception e)
                            {
                                new RsuException(e, "RsUnityRemote: InitializeObjects");
                            }
                            
                            break;
                        }
                        //**********************************************************************************************
                        // Step 2
                        //**********************************************************************************************
                        case ClientStatus.UpdateObjectPosition:
                        {
                            try
                            {
                                if(ReadAndCheckServer(ClientMessageType.RequestObjectPosition) != ServerMessageType.ObjectPositionUpdate)
                                    return;
                                
                                if (!UpdateObjectsPosition())
                                    return;
                                
                                if(_showContactPoints || _showContactForces)
                                    UpdateContacts();
                                
                                if (!_showContactForces)
                                {
                                    for (ulong i = 0; i < _nCreatedArrowsForContactForce; i++)
                                    {
                                        var forceMaker = _contactForcesRoot.transform.Find("contactForce" + i.ToString()).gameObject;
                                        forceMaker.SetActive(false);
                                    }
                                }
            
                                if (!_showContactPoints)
                                {
                                    for (ulong i = 0; i < _nCreatedArrowsForContactForce; i++)
                                    {
                                        var forceMaker = _contactPointsRoot.transform.Find("contactPosition" + i.ToString()).gameObject;
                                        forceMaker.SetActive(false);
                                    }
                                }
                                
                                // If configuration number for visuals doesn't match, _clientStatus is updated to ReinitializeObjectsStart  
                                // Else clientStatus is updated to UpdateVisualPosition
                            } catch (Exception e)
                            {
                                new RsuException(e, "RsUnityRemote: UpdateObjectPosition");
                            }
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    // Modal view
                    // _errorModalView.Show(true);
                    // _errorModalView.SetMessage(e.Message);
                    GameObject.Find("_CanvasSidebar").GetComponent<UIController>().setState(e.Message);

                    _clientStatus = ClientStatus.Idle;
                    ClearScene();
                    
                    // Close connection
                    _tcpHelper.CloseConnection();
                }
            }
        }

        private void processServerRequest()
        {
            ulong requestN = _tcpHelper.GetDataUlong();
            for (ulong i = 0; i < requestN; i++)
            {
                var requestType = _tcpHelper.GetServerRequest();
                switch (requestType)
                {
                    case TcpHelper.ServerRequestType.NoRequest:
                        break;
                
                    case TcpHelper.ServerRequestType.SetCameraTo:
                        float px = (float)_tcpHelper.GetDataDouble();
                        float py = (float)_tcpHelper.GetDataDouble();
                        float pz = (float)_tcpHelper.GetDataDouble();
                        float lx = (float)_tcpHelper.GetDataDouble();
                        float ly = (float)_tcpHelper.GetDataDouble();
                        float lz = (float)_tcpHelper.GetDataDouble();
                        _camera.transform.LookAt(new Vector3(px, pz, py), new Vector3(lx, lz, ly));
                        break;
                
                    case TcpHelper.ServerRequestType.FocusOnSpecificObject:
                        var obj = _tcpHelper.GetDataString();
                        if (obj != "")
                        { 
                            _camera.Follow(obj);
                        }
                        break;
                
                    case TcpHelper.ServerRequestType.StartRecordVideo:
                        var videoName = _tcpHelper.GetDataString();
                        _camera.StartRecording(videoName);
                        break;
                
                    case TcpHelper.ServerRequestType.StopRecordVideo:
                        _camera.FinishRecording();
                        break;
                }
            }
        }
        
        private void ClearScene()
        {
            // Objects
            foreach (Transform objT in _objectsRoot.transform)
            {
                Destroy(objT.gameObject);
            }
            
            // contact points
            foreach (Transform objT in _contactPointsRoot.transform)
            {
                Destroy(objT.gameObject);
            }
            
            // polylines
            foreach (Transform objT in _polylineRoot.transform)
            {
                Destroy(objT.gameObject);
            }
            
            // contact forces
            foreach (Transform child in _contactForcesRoot.transform)
            {
                Destroy(child.gameObject);
            }
            
            // visuals
            foreach (Transform child in _visualsRoot.transform)
            {
                Destroy(child.gameObject);
            }

            _nCreatedArrowsForContactForce = 0;
            _nCreatedArrowsForExternalForce = 0;
            _nCreatedArrowsForExternalTorque = 0;
            _nCreatedPolyLineBox = 0;
            
            // clear appearances
            if(_xmlReader != null)
                _xmlReader.ClearAppearanceMap();
            
            // clear modal view
            _loadingModalView.Show(false);
            
            // clear object cache
            _objName.Clear();

            // _objectController.ClearCache();
            
            _tcpHelper.Flush();
        }
        
        private void PartiallyInitializeObjects()
        {
            while (_numInitializedObjects < _numWorldObjects)
            {
                ulong objectIndex = _tcpHelper.GetDataUlong();
                RsObejctType objectType = _tcpHelper.GetDataRsObejctType();
                
                // get name and find corresponding appearance from XML
                string name = _tcpHelper.GetDataString();
                if (name != "" && !_objName.ContainsKey(name))
                {
                    _objName.Add(name, objectIndex.ToString() + "/0/0");    
                }
                
                Appearances? appearances = _xmlReader.FindApperancesFromObjectName(name);
                
                if (objectType == RsObejctType.RsArticulatedSystemObject)
                {
                    string urdfDirPathInServer = _tcpHelper.GetDataString(); 

                    // visItem = 0 (visuals)
                    // visItem = 1 (collisions)
                    for (int visItem = 0; visItem < 2; visItem++)
                    {
                        ulong numberOfVisObjects = _tcpHelper.GetDataUlong();

                        for (ulong j = 0; j < numberOfVisObjects; j++)
                        {
                            RsShapeType shapeType = _tcpHelper.GetDataRsShapeType();
                                
                            ulong group = _tcpHelper.GetDataUlong();

                            string subName = objectIndex.ToString() + "/" + visItem.ToString() + "/" + j.ToString();
                            var objFrame = _objectController.CreateRootObject(_objectsRoot, subName);

                            string tag = "";
                            if (visItem == 0)
                                tag = VisualTag.Visual;
                            else if (visItem == 1)
                                tag = VisualTag.ArticulatedSystemCollision;

                            if (shapeType == RsShapeType.RsMeshShape)
                            {
                                string meshFile = _tcpHelper.GetDataString();
                                string meshFileExtension = Path.GetExtension(meshFile);

                                double sx = _tcpHelper.GetDataDouble();
                                double sy = _tcpHelper.GetDataDouble();
                                double sz = _tcpHelper.GetDataDouble();

                                string meshFilePathInResourceDir = _loader.RetrieveMeshPath(urdfDirPathInServer, meshFile);
                                if (meshFilePathInResourceDir == null)
                                {
                                    new RsuException("Cannot find mesh from resource directories = " + meshFile);
                                }

                                try
                                {
                                    var mesh = _objectController.CreateMesh(objFrame, meshFilePathInResourceDir, (float)sx, (float)sy, (float)sz);
                                    mesh.tag = tag;
                                }
                                catch (Exception e)
                                {
                                    new RsuException("Cannot create mesh: " + e.Message);
                                    throw;
                                }
                            }
                            else
                            {
                                ulong size = _tcpHelper.GetDataUlong();
                                    
                                var visParam = new List<double>();
                                for (ulong k = 0; k < size; k++)
                                {
                                    double visSize = _tcpHelper.GetDataDouble();
                                    visParam.Add(visSize);
                                }
                                switch (shapeType)
                                {
                                    case RsShapeType.RsBoxShape:
                                    {
                                        if (visParam.Count != 3) new RsuException("Box Mesh error");
                                        var box = _objectController.CreateBox(objFrame, (float) visParam[0], (float) visParam[1], (float) visParam[2]);
                                        box.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;
                                        box.GetComponentInChildren<MeshRenderer>().material = _whiteMaterial;
                                        box.tag = tag;
                                    }
                                        break;
                                    case RsShapeType.RsCapsuleShape:
                                    {
                                        if (visParam.Count != 2) new RsuException("Capsule Mesh error");
                                        var capsule = _objectController.CreateCapsule(objFrame, (float)visParam[0], (float)visParam[1]);
                                        capsule.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;
                                        capsule.GetComponentInChildren<MeshRenderer>().material = _whiteMaterial;
                                        capsule.tag = tag;
                                    }
                                        break;
                                    case RsShapeType.RsConeShape:
                                    {
                                        // TODO URDF does not support cone shape
                                    }
                                        break;
                                    case RsShapeType.RsCylinderShape:
                                    {
                                        if (visParam.Count != 2) new RsuException("Cylinder Mesh error");
                                        var cylinder = _objectController.CreateCylinder(objFrame, (float)visParam[0], (float)visParam[1]);
                                        cylinder.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;
                                        cylinder.GetComponentInChildren<MeshRenderer>().material = _whiteMaterial;
                                        cylinder.tag = tag;
                                    }
                                        break;
                                    case RsShapeType.RsSphereShape:
                                    {
                                        if (visParam.Count != 1) new RsuException("Sphere Mesh error");
                                        var sphere = _objectController.CreateSphere(objFrame, (float)visParam[0]);
                                        sphere.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;
                                        sphere.GetComponentInChildren<MeshRenderer>().material = _whiteMaterial;
                                        sphere.tag = tag;
                                    }
                                        break;
                                }
                            }
                        }
                    }
                }
                else if (objectType == RsObejctType.RsHalfSpaceObject)
                {
                    // get material
                    Material material;
                    if (appearances != null && !string.IsNullOrEmpty(appearances.As<Appearances>().materialName))
                    {
                        material = Resources.Load<Material>(appearances.As<Appearances>().materialName);
                    }
                    else
                    {
                        // default material
                        material = _planeMaterial;
                    }
                    
                    float height = _tcpHelper.GetDataFloat();
                    var objFrame = _objectController.CreateRootObject(_objectsRoot, objectIndex.ToString());
                    var plane = _objectController.CreateHalfSpace(objFrame, height);
                    plane.GetComponentInChildren<Renderer>().material = _whiteMaterial;
                    plane.tag = VisualTag.Collision;

                    // default visual object
                    if (appearances == null || !appearances.As<Appearances>().subAppearances.Any())
                    {
                        var planeVis = _objectController.CreateHalfSpace(objFrame, height);
                        planeVis.GetComponentInChildren<Renderer>().material = material;
                        planeVis.GetComponentInChildren<Renderer>().material.mainTextureScale = new Vector2(15, 15);
                        planeVis.tag = VisualTag.Visual;
                        planeVis.name = "halfspace_viz";
                    }
                }
                else if (objectType == RsObejctType.RsHeightMapObject)
                {
                    // center
                    float centerX = _tcpHelper.GetDataFloat();
                    float centerY = _tcpHelper.GetDataFloat();
                    // size
                    float sizeX = _tcpHelper.GetDataFloat();
                    float sizeY = _tcpHelper.GetDataFloat();
                    // num samples
                    ulong numSampleX = _tcpHelper.GetDataUlong();
                    ulong numSampleY = _tcpHelper.GetDataUlong();
                    ulong numSample = _tcpHelper.GetDataUlong();
                        
                    // height values 
                    float[,] heights = new float[numSampleY, numSampleX];
                    for (ulong j = 0; j < numSampleY; j++)
                    {
                        for (ulong k = 0; k < numSampleX; k++)
                        {
                            float height = _tcpHelper.GetDataFloat();
                            heights[j, k] = height;
                        }
                    }

                    var objFrame = _objectController.CreateRootObject(_objectsRoot, objectIndex.ToString());
                    var terrain = _objectController.CreateTerrain(objFrame, numSampleX, sizeX, centerX, numSampleY, sizeY, centerY, heights, true);
                    terrain.tag = VisualTag.Both;
                    terrain.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;
                    terrain.GetComponentInChildren<MeshRenderer>().material = _whiteMaterial;
                }
                else if (objectType == RsObejctType.RsCompoundObject)
                {
                    // default material
                    Material material;
                    switch (_numInitializedObjects % 3)
                    {
                        case 0:
                            material = _defaultMaterialR;
                            break;
                        case 1:
                            material = _defaultMaterialG;
                            break;
                        case 2:
                            material = _defaultMaterialB;
                            break;
                        default:
                            material = _defaultMaterialR;
                            break;
                    }
                    
                    for (int visItem = 0; visItem < 2; visItem++)
                    {
                        if (visItem == 1)
                            material = _whiteMaterial;
                        
                        ulong numberOfVisObjects = _tcpHelper.GetDataUlong();

                        for (ulong j = 0; j < numberOfVisObjects; j++)
                        {
                            RsObejctType obType = _tcpHelper.GetDataRsObejctType();
                            string subName = objectIndex.ToString() + "/" + visItem.ToString() + "/" + j.ToString();
                            var objFrame = _objectController.CreateRootObject(_objectsRoot, subName);

                            string tag = "";
                            if (visItem == 0)
                                tag = VisualTag.Visual;
                            else if (visItem == 1)
                                tag = VisualTag.Collision;
                            
                            switch (obType)
                            {
                                case RsObejctType.RsBoxObject:
                                {
                                    double x = _tcpHelper.GetDataDouble();
                                    double y = _tcpHelper.GetDataDouble();
                                    double z = _tcpHelper.GetDataDouble();
                                    var box = _objectController.CreateBox(objFrame, (float) x, (float) y, (float) z);
                                    box.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;
                                    box.GetComponentInChildren<MeshRenderer>().material = material;
                                    box.tag = tag;
                                }
                                    break;
                                case RsObejctType.RsCapsuleObject:
                                {
                                    double radius = _tcpHelper.GetDataDouble();
                                    double height = _tcpHelper.GetDataDouble();

                                    var capsule = _objectController.CreateCapsule(objFrame, (float)radius, (float)height);
                                    capsule.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;
                                    capsule.GetComponentInChildren<MeshRenderer>().material = material;
                                    capsule.tag = tag;
                                }
                                    break;
                                case RsObejctType.RsConeObject:
                                {
                                    // TODO URDF does not support cone shape
                                }
                                    break;
                                case RsObejctType.RsCylinderObject:
                                {
                                    double radius = _tcpHelper.GetDataDouble();
                                    double height = _tcpHelper.GetDataDouble();
                                    var cylinder = _objectController.CreateCylinder(objFrame, (float)radius, (float)height);
                                    cylinder.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;
                                    cylinder.GetComponentInChildren<MeshRenderer>().material = material;
                                    cylinder.tag = tag;
                                }
                                    break;
                                case RsObejctType.RsSphereObject:
                                {
                                    double radius = _tcpHelper.GetDataDouble();
                                    var sphere = _objectController.CreateSphere(objFrame, (float)radius);
                                    sphere.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;
                                    sphere.GetComponentInChildren<MeshRenderer>().material = material;
                                    sphere.tag = tag;
                                }
                                    break;
                                
                            }
                        }
                    }
                }
                else
                {
                    // single body object
                    
                    // create base frame of object
                    var objFrame = _objectController.CreateRootObject(_objectsRoot, objectIndex.ToString());
                    
                    // get material
                    Material material;
                    if (appearances != null && !string.IsNullOrEmpty(appearances.As<Appearances>().materialName))
                        material = Resources.Load<Material>(appearances.As<Appearances>().materialName);
                    else
                    {
                        // default material
                        switch (_numInitializedObjects % 3)
                        {
                            case 0:
                                material = _defaultMaterialR;
                                break;
                            case 1:
                                material = _defaultMaterialG;
                                break;
                            case 2:
                                material = _defaultMaterialB;
                                break;
                            default:
                                material = _defaultMaterialR;
                                break;
                        }
                    }
                    
                    // collision body 
                    GameObject collisionObject = null;
                    
                    switch (objectType) 
                    {
                        case RsObejctType.RsSphereObject :
                        {
                            float radius = _tcpHelper.GetDataFloat();
                            collisionObject =  _objectController.CreateSphere(objFrame, radius);
                            collisionObject.tag = VisualTag.Collision;
                        }
                            break;

                        case RsObejctType.RsBoxObject :
                        {
                            float sx = _tcpHelper.GetDataFloat();
                            float sy = _tcpHelper.GetDataFloat();
                            float sz = _tcpHelper.GetDataFloat();
                            collisionObject = _objectController.CreateBox(objFrame, sx, sy, sz);
                            collisionObject.tag = VisualTag.Collision;
                        }
                            break;
                        case RsObejctType.RsCylinderObject:
                        {
                            float radius = _tcpHelper.GetDataFloat();
                            float height = _tcpHelper.GetDataFloat();
                            collisionObject = _objectController.CreateCylinder(objFrame, radius, height);
                            collisionObject.tag = VisualTag.Collision;
                        }
                            break;
                        case RsObejctType.RsCapsuleObject:
                        {
                            float radius = _tcpHelper.GetDataFloat();
                            float height = _tcpHelper.GetDataFloat();
                            collisionObject = _objectController.CreateCapsule(objFrame, radius, height);
                            collisionObject.tag = VisualTag.Collision;
                        }
                            break;
                        case RsObejctType.RsMeshObject:
                        {
                            string meshFile = _tcpHelper.GetDataString();
                            float scale = _tcpHelper.GetDataFloat();
                            
                            string meshFileName = Path.GetFileName(meshFile);       
                            string meshFileExtension = Path.GetExtension(meshFile);
                            
                            string meshFilePathInResourceDir = _loader.RetrieveMeshPath(Path.GetDirectoryName(meshFile), meshFileName);
                            
                            collisionObject = _objectController.CreateMesh(objFrame, meshFilePathInResourceDir, 
                                scale, scale, scale);
                            collisionObject.tag = VisualTag.Collision;
                        }
                            break;
                    }
                    collisionObject.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;
                    collisionObject.GetComponentInChildren<MeshRenderer>().material = _whiteMaterial;
                    
                    // visual body
                    GameObject visualObject = null;

                    if (appearances != null)
                    {
                        foreach (var subapp in appearances.As<Appearances>().subAppearances)
                        {
                            // subapp material 
                            if(!String.IsNullOrEmpty(subapp.materialName))
                                material = Resources.Load<Material>(subapp.materialName);
                            
                            switch (subapp.shapes)
                            {
                                case AppearanceShapes.Sphere:
                                {
                                    float radius = subapp.dimension.x;
                                    visualObject =  _objectController.CreateSphere(objFrame, radius);
                                    visualObject.GetComponentInChildren<Renderer>().material = material;
                                    visualObject.tag = VisualTag.Visual;
                                }
                                    break;
                                case AppearanceShapes.Box:
                                {
                                    visualObject = _objectController.CreateBox(objFrame, subapp.dimension.x, subapp.dimension.y, subapp.dimension.z);
                                    visualObject.GetComponentInChildren<Renderer>().material = material;
                                    visualObject.tag = VisualTag.Visual;
                                }
                                    break;
                                case AppearanceShapes.Cylinder:
                                {
                                    visualObject = _objectController.CreateCylinder(objFrame, subapp.dimension.x, subapp.dimension.y);
                                    visualObject.GetComponentInChildren<Renderer>().material = material;
                                    visualObject.tag = VisualTag.Visual;
                                }
                                    break;
                                case AppearanceShapes.Capsule:
                                {
                                    visualObject = _objectController.CreateCapsule(objFrame, subapp.dimension.x, subapp.dimension.y);
                                    visualObject.GetComponentInChildren<Renderer>().material = material;
                                    visualObject.tag = VisualTag.Visual;
                                }
                                    break;
                                case AppearanceShapes.Mesh:
                                {
                                    string meshFileName = Path.GetFileName(subapp.fileName);       
                                    string meshFileExtension = Path.GetExtension(subapp.fileName);
                                    string meshFilePathInResourceDir = _loader.RetrieveMeshPath(Path.GetDirectoryName(subapp.fileName), meshFileName);
                            
                                    visualObject = _objectController.CreateMesh(objFrame, meshFilePathInResourceDir, 
                                        subapp.dimension.x, subapp.dimension.y, subapp.dimension.z);
                                    visualObject.GetComponentInChildren<Renderer>().material = material;
                                    visualObject.tag = VisualTag.Visual;
                                }
                                    break;
                                default:
                                    new RsuException("Not Implemented Appearance Shape");
                                    break;
                            }
                        }
                    }
                    else
                    {
                        // default visual object (same shape with collision)
                        visualObject = GameObject.Instantiate(collisionObject, objFrame.transform);
                        visualObject.GetComponentInChildren<Renderer>().material = material;
                        visualObject.tag = VisualTag.Visual;
                    }
                }

                _numInitializedObjects++;
                if (Time.deltaTime > 0.1f)
                    // If initialization takes too much time, do the rest in next iteration (to prevent freezing GUI(
                    break;
            }
        }

        private void PartiallyInitializeVisuals()
        {
            while (_numInitializedVisuals < _numWorldVisuals)
            {
                RsVisualType objectType = _tcpHelper.GetDataRsVisualType();
                
                // get name and find corresponding appearance from XML
                string objectName = _tcpHelper.GetDataString();
                
                float colorR = _tcpHelper.GetDataFloat();
                float colorG = _tcpHelper.GetDataFloat();
                float colorB = _tcpHelper.GetDataFloat();
                float colorA = _tcpHelper.GetDataFloat();
                string materialName = _tcpHelper.GetDataString();
                bool glow = _tcpHelper.GetDataBool();
                bool shadow = _tcpHelper.GetDataBool();

                var visFrame = _objectController.CreateRootObject(_visualsRoot, objectName);
                
                GameObject visual = null;
                    
                switch (objectType)
                {
                    case RsVisualType.RsVisualSphere :
                    {
                        float radius = _tcpHelper.GetDataFloat();
                        visual =  _objectController.CreateSphere(visFrame, radius);
                        visual.tag = VisualTag.Visual;
                    }
                        break;
                    case RsVisualType.RsVisualBox:
                    {
                        float sx = _tcpHelper.GetDataFloat();
                        float sy = _tcpHelper.GetDataFloat();
                        float sz = _tcpHelper.GetDataFloat();
                        visual = _objectController.CreateBox(visFrame, sx, sy, sz);
                        visual.tag = VisualTag.Visual;
                    }
                        break;
                    case RsVisualType.RsVisualCylinder:
                    {
                        float radius = _tcpHelper.GetDataFloat();
                        float height = _tcpHelper.GetDataFloat();
                        visual = _objectController.CreateCylinder(visFrame, radius, height);
                        visual.tag = VisualTag.Visual;
                    }
                        break;
                    case RsVisualType.RsVisualCapsule:
                    {
                        float radius = _tcpHelper.GetDataFloat();
                        float height = _tcpHelper.GetDataFloat();
                        visual = _objectController.CreateCapsule(visFrame, radius, height);
                        visual.tag = VisualTag.Visual;
                    }
                        break;
                    case RsVisualType.RsVisualArrow:
                    {
                        float radius = _tcpHelper.GetDataFloat();
                        float height = _tcpHelper.GetDataFloat();
                        visual = _objectController.CreateArrow(visFrame, radius, height);
                        visual.tag = VisualTag.Visual;
                    }
                        break;
                }
                
                // set material or color
                if (string.IsNullOrEmpty(materialName) && visual != null)
                {
                    // set material by rgb 
                    visual.GetComponentInChildren<Renderer>().material.SetColor(_colorString, new Color(colorR, colorG, colorB, colorA));
                    if(glow)
                    {
                        visual.GetComponentInChildren<Renderer>().material.EnableKeyword("_EMISSION");
                        visual.GetComponentInChildren<Renderer>().material.SetColor(
                            "_EmissionColor", new Color(colorR, colorG, colorB, colorA));
                    }
                }
                else
                {
                    // set material from
                    Material material = Resources.Load<Material>(materialName);
                    visual.GetComponentInChildren<Renderer>().material = material;
                }
                
                // set shadow 
                if (shadow)
                {
                    visual.GetComponentInChildren<Renderer>().shadowCastingMode = ShadowCastingMode.On;
                }
                else
                {
                    visual.GetComponentInChildren<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }

                _numInitializedVisuals++;
                if (Time.deltaTime > 0.03f)
                    // If initialization takes too much time, do the rest in next iteration (to prevent freezing GUI(
                    break;
            }
        }
        
        private bool UpdateObjectsPosition() 
        {
            ulong configurationNumber = _tcpHelper.GetDataUlong();

            if (configurationNumber != _objectConfiguration)
            {
                _numInitializedObjects = 0;
                _clientStatus = ClientStatus.InitializingObjects;
                return false;
            }
            
            ulong numObjects = _tcpHelper.GetDataUlong();

            for (ulong i = 0; i < numObjects; i++)
            {
                ulong localIndexSize = _tcpHelper.GetDataUlong();

                for (ulong j = 0; j < localIndexSize; j++)
                {
                    string objectName = _tcpHelper.GetDataString();
                    
                    double posX = _tcpHelper.GetDataDouble();
                    double posY = _tcpHelper.GetDataDouble();
                    double posZ = _tcpHelper.GetDataDouble();
                    
                    double quatW = _tcpHelper.GetDataDouble();
                    double quatX = _tcpHelper.GetDataDouble();
                    double quatY = _tcpHelper.GetDataDouble();
                    double quatZ = _tcpHelper.GetDataDouble();

                    GameObject localObject = GameObject.Find(objectName);

                    if (localObject != null)
                    {
                        ObjectController.SetTransform(
                            localObject, 
                            new Vector3((float)posX, (float)posY, (float)posZ), 
                            new Quaternion((float)quatX, (float)quatY, (float)quatZ, (float)quatW)
                        );
                    }
                    else
                    {
                        new RsuException("Cannot find unity game object: " + objectName);
                    }
                }
            }

            // visual objects
            numObjects = _tcpHelper.GetDataUlong();

            for (ulong i = 0; i < numObjects; i++)
            {
                string visualName = _tcpHelper.GetDataString();
                
                double posX = _tcpHelper.GetDataDouble();
                double posY = _tcpHelper.GetDataDouble();
                double posZ = _tcpHelper.GetDataDouble();
                    
                double quatW = _tcpHelper.GetDataDouble();
                double quatX = _tcpHelper.GetDataDouble();
                double quatY = _tcpHelper.GetDataDouble();
                double quatZ = _tcpHelper.GetDataDouble();
                
                RsVisualType objectType = _tcpHelper.GetDataRsVisualType();

                double colorR = _tcpHelper.GetDataDouble();
                double colorG = _tcpHelper.GetDataDouble();
                double colorB = _tcpHelper.GetDataDouble();
                double colorA = _tcpHelper.GetDataDouble();
                
                double sizeA = _tcpHelper.GetDataDouble();
                double sizeB = _tcpHelper.GetDataDouble();
                double sizeC = _tcpHelper.GetDataDouble();

                GameObject localObject = GameObject.Find(visualName);

                if (localObject != null)
                {
                    ObjectController.SetTransform(
                        localObject, 
                        new Vector3((float)posX, (float)posY, (float)posZ), 
                        new Quaternion((float)quatX, (float)quatY, (float)quatZ, (float)quatW)
                    );
                    
                    // set material by rgb 
                    localObject.GetComponentInChildren<Renderer>().material.SetColor(_colorString, new Color((float)colorR, (float)colorG, (float)colorB, (float)colorA));
                    
                    switch (objectType)
                    {
                        case RsVisualType.RsVisualSphere :
                        {
                            localObject.transform.localScale = new Vector3((float)sizeA, (float)sizeA, (float)sizeA);
                        }
                            break;
                        case RsVisualType.RsVisualBox:
                        {
                            localObject.transform.localScale = new Vector3((float)sizeA, (float)sizeB, (float)sizeC);
                        }
                            break;
                        case RsVisualType.RsVisualCylinder:
                        {
                            localObject.transform.localScale = new Vector3((float)sizeA, (float)sizeB, (float)sizeA);
                        }
                            break;
                        case RsVisualType.RsVisualCapsule:
                        {
                            localObject.transform.localScale = new Vector3((float)sizeA, (float)sizeB*0.5f+(float)sizeA*0.5f, (float)sizeA);
                        }
                            break;
                        case RsVisualType.RsVisualArrow:
                        {
                            localObject.transform.localScale = new Vector3((float)sizeA, (float)sizeA, (float)sizeB);
                        }
                            break;
                    }
                }
                else
                {
                    new RsuException("Cannot find unity game object: " + visualName);
                }
            }
            
            // polylines objects
            numObjects = _tcpHelper.GetDataUlong();
            List<List<Vector3>> lineList = new List<List<Vector3>>();
            List<Color> colorList = new List<Color>();
            List<double> widthList = new List<double>();

            ulong polyLineSegN = 0;

            for (ulong i = 0; i < numObjects; i++)
            {
                string visualName = _tcpHelper.GetDataString();
                double colorR = _tcpHelper.GetDataDouble();
                double colorG = _tcpHelper.GetDataDouble();
                double colorB = _tcpHelper.GetDataDouble();
                double colorA = _tcpHelper.GetDataDouble();
                double width = _tcpHelper.GetDataDouble();
                widthList.Add(width);
                colorList.Add(new Color((float)colorR, (float)colorG, (float)colorB, (float)colorA));
                
                var npoints = _tcpHelper.GetDataUlong();
                if (npoints != 0)
                    polyLineSegN += npoints - 1;
                lineList.Add(new List<Vector3>());
                for (ulong j = 0; j < npoints; j++)
                    lineList.Last().Add(new Vector3((float)_tcpHelper.GetDataDouble(), (float)_tcpHelper.GetDataDouble(), (float)_tcpHelper.GetDataDouble()));
            }
            
            for (ulong markerID = _nCreatedPolyLineBox; markerID < polyLineSegN; markerID++)
            {
                var segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = "polylines" + markerID.ToString();
                segment.tag = VisualTag.Both;
                segment.transform.SetParent(_polylineRoot.transform, true);
                segment.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;
                _nCreatedPolyLineBox = markerID+1;
            }

            ulong boxyId = 0;
            for (int i = 0; i < lineList.Count; i++)
            {
                var line = lineList[i];
                for (int j = 0; j < line.Count-1; j++)
                {
                    var box = _polylineRoot.transform.Find("polylines" + boxyId.ToString()).gameObject;
                    var pos1 = line[j];
                    var pos2 = line[j + 1];
                    boxyId++;
                    
                    Quaternion q = new Quaternion(); 
                    q.SetLookRotation(new Vector3((float)(pos1[0]-pos2[0]), (float)(pos1[1]-pos2[1]), (float)(pos1[2]-pos2[2])), new Vector3(1,0,0));
                
                    ObjectController.SetTransform(box,
                        new Vector3((float)(pos1[0]+pos2[0])/2.0f, (float)(pos1[1]+pos2[1])/2.0f, (float)(pos1[2]+pos2[2])/2.0f),
                        q);

                    double length = Math.Sqrt((pos1[0] - pos2[0]) * (pos1[0] - pos2[0]) + (pos1[1] - pos2[1]) * (pos1[1] - pos2[1]) +
                                              (pos1[2] - pos2[2]) * (pos1[2] - pos2[2]));
                    box.GetComponent<Renderer>().material.SetColor(_colorString, colorList[(int)i]);
                    
                    box.transform.localScale = new Vector3((float)widthList[i], (float)length, (float)widthList[i]);
                }
            }
            
            for (ulong i = polyLineSegN; i < _nCreatedPolyLineBox; i++)
            {
                var forceMaker = _contactForcesRoot.transform.Find("polylines" + i.ToString()).gameObject;
                forceMaker.SetActive(false);
            }

            // constraints
            _wireN = _tcpHelper.GetDataUlong();
            for (ulong i = 0; i < _wireN; i++)
            {
                double posX1 = _tcpHelper.GetDataDouble();
                double posY1 = _tcpHelper.GetDataDouble();
                double posZ1 = _tcpHelper.GetDataDouble();
                
                double posX2 = _tcpHelper.GetDataDouble();
                double posY2 = _tcpHelper.GetDataDouble();
                double posZ2 = _tcpHelper.GetDataDouble();
                
                GameObject localObject = GameObject.Find("wire"+i);
                
                Quaternion q = new Quaternion(); 
                q.SetLookRotation(new Vector3((float)(posX1-posX2), (float)(posY1-posY2), (float)(posZ1-posZ2)), new Vector3(1,0,0));
                
                ObjectController.SetTransform(localObject,
                    new Vector3((float)(posX1+posX2)/2.0f, (float)(posY1+posY2)/2.0f, (float)(posZ1+posZ2)/2.0f),
                    q);

                double length = Math.Sqrt((posX1 - posX2) * (posX1 - posX2) + (posY1 - posY2) * (posY1 - posY2) +
                                          (posZ1 - posZ2) * (posZ1 - posZ2));
                localObject.transform.localScale = new Vector3((float)0.005, (float)length, (float)0.005);
            }
            
            // external force
            ulong ExternalForceN = _tcpHelper.GetDataUlong();

            // create contact marker
            List<Tuple<Vector3, Vector3>> externalForceList = new List<Tuple<Vector3, Vector3>>();
            float forceMaxNorm = 0;
            
            for (ulong markerID = _nCreatedArrowsForExternalForce; markerID < ExternalForceN; markerID++)
            {
                var forceMaker = GameObject.Instantiate(_arrowMesh);

                forceMaker.name = "externalForce" + markerID.ToString();
                forceMaker.tag = VisualTag.Both;
                forceMaker.transform.SetParent(_contactForcesRoot.transform, true);
                forceMaker.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;

                _objectController.SetContactForceMarker(
                    forceMaker, new Vector3(0,0,0), new Vector3(1,1,1), Color.green,
                    _contactForceMarkerScale);

                _nCreatedArrowsForExternalForce = markerID+1;
            }

            for (ulong i = 0; i < ExternalForceN; i++)
            {
                double posX = _tcpHelper.GetDataDouble();
                double posY = _tcpHelper.GetDataDouble();
                double posZ = _tcpHelper.GetDataDouble();

                double forceX = _tcpHelper.GetDataDouble();
                double forceY = _tcpHelper.GetDataDouble();
                double forceZ = _tcpHelper.GetDataDouble();
                var force = new Vector3((float) forceX, (float) forceY, (float) forceZ);
                
                externalForceList.Add(new Tuple<Vector3, Vector3>(
                    new Vector3((float) posX, (float) posY, (float) posZ), force
                ));
                
                forceMaxNorm = Math.Max(forceMaxNorm, force.magnitude);
            }

            for (ulong i = ExternalForceN; i < _nCreatedArrowsForExternalForce; i++)
            {
                var forceMaker = _contactForcesRoot.transform.Find("externalForce" + i.ToString()).gameObject;
                forceMaker.SetActive(false);
            }

            for (ulong i = 0; i < ExternalForceN; i++)
            {
                var forceMaker = _contactForcesRoot.transform.Find("externalForce" + i.ToString()).gameObject;
                var contact = externalForceList[(int) i];
                _objectController.SetContactForceMarker(
                    forceMaker, contact.Item1, contact.Item2 / forceMaxNorm, Color.green,
                    _contactForceMarkerScale);
                forceMaker.SetActive(true);
            }

            forceMaxNorm = 0;
            // external torque
            var ExternalTorqueN = _tcpHelper.GetDataUlong();
            List<Tuple<Vector3, Vector3>> externalTorqueList = new List<Tuple<Vector3, Vector3>>();
            
            for (ulong markerID = _nCreatedArrowsForExternalTorque; markerID < ExternalTorqueN; markerID++)
            {
                var forceMaker = GameObject.Instantiate(_arrowMesh);

                forceMaker.name = "externalTorque" + markerID.ToString();
                forceMaker.tag = VisualTag.Both;
                forceMaker.transform.SetParent(_contactForcesRoot.transform, true);
                forceMaker.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;

                _objectController.SetContactForceMarker(
                    forceMaker, new Vector3(0,0,0), new Vector3(1,1,1), Color.yellow,
                    _contactForceMarkerScale);

                _nCreatedArrowsForExternalTorque = markerID+1;
            }
            
            for (ulong i = ExternalTorqueN; i < _nCreatedArrowsForExternalTorque; i++)
            {
                var forceMaker = _contactForcesRoot.transform.Find("externalTorque" + i.ToString()).gameObject;
                forceMaker.SetActive(false);
            }

            for (ulong i = 0; i < ExternalTorqueN; i++)
            {
                double posX = _tcpHelper.GetDataDouble();
                double posY = _tcpHelper.GetDataDouble();
                double posZ = _tcpHelper.GetDataDouble();

                double forceX = _tcpHelper.GetDataDouble();
                double forceY = _tcpHelper.GetDataDouble();
                double forceZ = _tcpHelper.GetDataDouble();
                var force = new Vector3((float) forceX, (float) forceY, (float) forceZ);
                
                externalTorqueList.Add(new Tuple<Vector3, Vector3>(
                    new Vector3((float) posX, (float) posY, (float) posZ), force
                ));
                
                forceMaxNorm = Math.Max(forceMaxNorm, force.magnitude);
            }
            
            for (ulong i = 0; i < ExternalTorqueN; i++)
            {
                var forceMaker = _contactForcesRoot.transform.Find("externalTorque" + i.ToString()).gameObject;
                var contact = externalTorqueList[(int) i];
                _objectController.SetContactForceMarker(
                    forceMaker, contact.Item1, contact.Item2 / forceMaxNorm, Color.yellow,
                    _contactForceMarkerScale);
                forceMaker.SetActive(true);
            }

            // Update object position done.
            // Go to visual object position update
            _clientStatus = ClientStatus.UpdateObjectPosition;

            return true;
        }

        private ServerMessageType ReadAndCheckServer(ClientMessageType type)
        {
            int counter = 0;
            int receivedData = 0;
            while (counter++ < 1 && receivedData == 0)
            {
                _tcpHelper.WriteData(BitConverter.GetBytes((int) type));
                receivedData = _tcpHelper.ReadData();
            }

            if (receivedData == 0)
            {
                new RsuException("cannot connect");
            }

            ServerStatus state = _tcpHelper.GetDataServerStatus();
            processServerRequest();

            if (state == ServerStatus.StatusTerminating)
            {
                new RsuException("Server is terminating");
                return ServerMessageType.Reset;
            }
            else if (state == ServerStatus.StatusHibernating)
            {
                _clientStatus = ClientStatus.Idle;
                return ServerMessageType.Reset;
            }
            else
            {
                return _tcpHelper.GetDataServerMessageType();
            }
        }

        private bool UpdateContacts()
        {
            if (ReadAndCheckServer(ClientMessageType.RequestContactInfos) != ServerMessageType.ContactInfoUpdate)
                return false;
            
            ulong numContacts = _tcpHelper.GetDataUlong();

            // create contact marker
            List<Tuple<Vector3, Vector3>> contactList = new List<Tuple<Vector3, Vector3>>();
            float forceMaxNorm = 0;
            
            for (ulong markerID = _nCreatedArrowsForContactForce; markerID < numContacts; markerID++)
            {
                var forceMaker = GameObject.Instantiate(_arrowMesh);
                var posMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);

                forceMaker.name = "contactForce" + markerID.ToString();
                posMarker.name = "contactPosition" + markerID.ToString();
                forceMaker.tag = VisualTag.Both;
                posMarker.tag = VisualTag.Both;
                forceMaker.transform.SetParent(_contactForcesRoot.transform, true);
                posMarker.transform.SetParent(_contactPointsRoot.transform, true);
                forceMaker.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;
                posMarker.GetComponentInChildren<MeshRenderer>().material.shader = _standardShader;
                
                _objectController.SetContactMarker(
                    posMarker, new Vector3(0,0,0), Color.red, _contactPointMarkerScale);
                _objectController.SetContactForceMarker(
                    forceMaker, new Vector3(0,0,0), new Vector3(1,1,1), Color.blue,
                    _contactForceMarkerScale);

                _nCreatedArrowsForContactForce = markerID+1;
            }

            for (ulong i = 0; i < numContacts; i++)
            {
                double posX = _tcpHelper.GetDataDouble();
                double posY = _tcpHelper.GetDataDouble();
                double posZ = _tcpHelper.GetDataDouble();

                double forceX = _tcpHelper.GetDataDouble();
                double forceY = _tcpHelper.GetDataDouble();
                double forceZ = _tcpHelper.GetDataDouble();
                var force = new Vector3((float) forceX, (float) forceY, (float) forceZ);
                
                contactList.Add(new Tuple<Vector3, Vector3>(
                    new Vector3((float) posX, (float) posY, (float) posZ), force
                ));
                
                forceMaxNorm = Math.Max(forceMaxNorm, force.magnitude);
            }
            
            for (ulong i = 0; i < numContacts; i++)
            {
                var forceMaker = _contactForcesRoot.transform.Find("contactForce" + i.ToString()).gameObject;
                var posMarker = _contactPointsRoot.transform.Find("contactPosition" + i.ToString()).gameObject;
                
                var contact = contactList[(int) i];

                if (contact.Item2.magnitude > 0)
                {
                    if (_showContactPoints)
                    {
                        _objectController.SetContactMarker(
                            posMarker, contact.Item1, Color.red, _contactPointMarkerScale);
                        posMarker.SetActive(true);
                    }

                    if (_showContactForces)
                    {
                        _objectController.SetContactForceMarker(
                            forceMaker, contact.Item1, contact.Item2 / forceMaxNorm, Color.blue,
                            _contactForceMarkerScale);
                        forceMaker.SetActive(true);
                    }
                }
                else
                {
                    forceMaker.SetActive(false);
                    posMarker.SetActive(false);
                }
            }
            
            for (ulong i = numContacts; i < _nCreatedArrowsForContactForce; i++)
            {
                var forceMaker = _contactForcesRoot.transform.Find("contactForce" + i.ToString()).gameObject;
                forceMaker.SetActive(false);
                var posMarker = _contactPointsRoot.transform.Find("contactPosition" + i.ToString()).gameObject;
                posMarker.SetActive(false);
            }
            
            return true;
        }
        
        private void ReadXmlString()
        {
            _tcpHelper.WriteData(BitConverter.GetBytes((int) ClientMessageType.RequestConfigXML));
            if (_tcpHelper.ReadData() <= 0)
                new RsuException("Cannot read data from TCP");
            
            ServerStatus state = _tcpHelper.GetDataServerStatus();
            
            if (state == ServerStatus.StatusTerminating)
                new RsuException("The server is terminating");
            else if (state == ServerStatus.StatusHibernating)
            {
                _clientStatus = ClientStatus.Idle;
                return;
            }
            processServerRequest();

            ServerMessageType messageType = _tcpHelper.GetDataServerMessageType();
            
            //TODO: properly use xml file here
            if (messageType == ServerMessageType.NoMessage) return; // No XML
                
            if (messageType != ServerMessageType.ConfigXml)
            {
                new RsuException("The server sends a wrong message");
            }

            string xmlString = _tcpHelper.GetDataString();

            XmlDocument xmlDoc = new XmlDocument();
            if (xmlDoc != null)
            {
                xmlDoc.LoadXml(xmlString);
                _xmlReader.CreateApperanceMap(xmlDoc);

                XmlNode cameraNode = xmlDoc.DocumentElement.SelectSingleNode("/raisim/camera");
                
                if(cameraNode != null)
                    _camera.Follow(cameraNode.Attributes["follow"].Value, XmlReader.GetXyzVector3(cameraNode));
            }
        }

        public string GetSubName(string name)
        {
            if (_objName.ContainsKey(name))
            {
                return _objName[name];
            } 
            
            if (_objName.ContainsKey(name+"/0"))
            {
                return _objName[name+"/0"];
            }
            
            if (_objName.ContainsKey(name+"/0/0"))
            {
                return _objName[name+"/0/0"];
            }

            return "";
        }

        void OnApplicationQuit()
        {
            // close tcp client
            _tcpHelper.CloseConnection();
            
            // save preference
            _loader.SaveToPref();
        }

        public void ShowOrHideObjects()
        {
            // Visual body
            foreach (var obj in GameObject.FindGameObjectsWithTag(VisualTag.Visual))
            {
                foreach (var collider in obj.GetComponentsInChildren<Collider>())
                    collider.enabled = _showVisualBody;
                
                foreach (var renderer in obj.GetComponentsInChildren<Renderer>())
                {
                    renderer.enabled = _showVisualBody;
                }
            }

            // Collision body
            foreach (var obj in GameObject.FindGameObjectsWithTag(VisualTag.Collision))
            {
                foreach (var col in obj.GetComponentsInChildren<Collider>())
                    col.enabled = _showCollisionBody;
                
                foreach (var ren in obj.GetComponentsInChildren<Renderer>())
                {
                    ren.enabled = _showCollisionBody;
                }
            }
            
            // Articulated System Collision body
            foreach (var obj in GameObject.FindGameObjectsWithTag(VisualTag.ArticulatedSystemCollision))
            {
                foreach (var col in obj.GetComponentsInChildren<Collider>())
                    col.enabled = _showCollisionBody || _showVisualBody;
                
                foreach (var ren in obj.GetComponentsInChildren<Renderer>())
                {
                    ren.enabled = _showCollisionBody;
                }
            }
            
            // Body frames
            foreach (var obj in GameObject.FindGameObjectsWithTag(VisualTag.Frame))
            {
                foreach (var renderer in obj.GetComponentsInChildren<Renderer>())
                {
                    renderer.enabled = _showBodyFrames;
                }
            }
        }

        public void requestConnection ()
        {
            if(_clientStatus == ClientStatus.Idle)
            {
                try
                {
                    if(_tcpHelper.TryConnection())
                    {
                        _clientStatus = ClientStatus.InitializingObjects;
                    }
                }
                catch
                {

                }
            }
        }

        //**************************************************************************************************************
        //  Getter and Setters 
        //**************************************************************************************************************
        
        public bool ShowVisualBody
        {
            get => _showVisualBody;
            set => _showVisualBody = value;
        }

        public bool ShowCollisionBody
        {
            get => _showCollisionBody;
            set => _showCollisionBody = value;
        }

        public bool ShowContactPoints
        {
            get => _showContactPoints;
            set => _showContactPoints = value;
        }

        public bool ShowContactForces
        {
            get => _showContactForces;
            set => _showContactForces = value;
        }

        public bool ShowBodyFrames
        {
            get => _showBodyFrames;
            set => _showBodyFrames = value;
        }

        public float ContactPointMarkerScale
        {
            get => _contactPointMarkerScale;
            set => _contactPointMarkerScale = value;
        }

        public float ContactForceMarkerScale
        {
            get => _contactForceMarkerScale;
            set => _contactForceMarkerScale = value;
        }

        public float BodyFrameMarkerScale
        {
            get => _bodyFrameMarkerScale;
            set
            {
                _bodyFrameMarkerScale = value;
                foreach (var obj in GameObject.FindGameObjectsWithTag(VisualTag.Frame))
                {
                    obj.transform.localScale = new Vector3(0.03f * value, 0.03f * value, 0.1f * value);
                }
            }
        }

        public string TcpAddress
        {
            get => _tcpHelper.TcpAddress;
            set => _tcpHelper.TcpAddress = value;
        }

        public int TcpPort
        {
            get => _tcpHelper.TcpPort;
            set => _tcpHelper.TcpPort = value;
        }


        public bool TcpConnected
        {
            get => _tcpHelper.Connected;
        }

        public bool IsServerHibernating
        {
            get
            {
                return _clientStatus == ClientStatus.Idle && _tcpHelper.DataAvailable;
            }
        }

        public ResourceLoader ResourceLoader
        {
            get { return _loader; }
        }
    }
}