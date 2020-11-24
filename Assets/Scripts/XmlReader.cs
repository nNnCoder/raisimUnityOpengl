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

using System.Collections.Generic;
using System.Xml;
using UnityEngine;

namespace raisimUnity
{
    public enum AppearanceShapes : int
    {
        Sphere = 0,
        Box,
        Cylinder,
        Capsule,
        Mesh,
    }
    
    public struct Appearance
    {
        public AppearanceShapes shapes;
        public Vector3 dimension;
        public string fileName;
        public string materialName;
    }

    public struct Appearances
    {
        // each raisim object can have multiple appearances.
        public string materialName;
        public List<Appearance> subAppearances;
    }

    public class XmlReader
    {
        // (name and List<appearance>) 
        private Dictionary<string, Appearances> _table;

        public XmlReader()
        {
            _table = new Dictionary<string, Appearances>();
        }

        public static Vector3 GetXyzVector3(XmlNode node)
        {
            var xlength = node.Attributes["x"];
            if (xlength == null)
            {
                new RsuException("missing x position");
            }
            var ylength = node.Attributes["y"];
            if (ylength == null)
            {
                new RsuException("missing y position");
            }
            var zlength = node.Attributes["z"];
            if (zlength == null)
            {
                new RsuException("missing z position");
            }
                            
            return new Vector3(float.Parse(xlength.Value), float.Parse(ylength.Value), float.Parse(zlength.Value));
        }

        public void CreateApperanceMap(XmlDocument xmlDocument)
        {
            // find appearance element under object
            var objects = xmlDocument.DocumentElement.SelectSingleNode("/raisim/objects");

            foreach (XmlNode obj in objects.ChildNodes)
            {
                // xml <appearance> tag
                var appearanceNode = obj.SelectSingleNode("apperance");
                if (appearanceNode == null) continue;
                
                Appearances appearances = new Appearances();
                appearances.subAppearances = new List<Appearance>();
                var material = appearanceNode.Attributes["material"];
                if (material != null) appearances.materialName = material.Value;

                // each subappearance
                foreach (XmlNode app in appearanceNode.ChildNodes)
                {
                    Appearance appearance = new Appearance();
                    var shape = app.Name;
                    
                    var matPerAppearance = app.Attributes["material"];
                    if (matPerAppearance != null) appearance.materialName = matPerAppearance.Value;

                    switch (shape)
                    {    
                        case "sphere":
                        {
                            appearance.shapes = AppearanceShapes.Sphere;
                            var radius = app.Attributes["radius"];
                            if (radius == null)
                            {
                                new RsuException("sphere appearance is missing radius");
                            }
                            appearance.dimension = new Vector3(float.Parse(radius.Value), 0, 0);
                        }
                        break;
                        case "box":
                        {
                            appearance.shapes = AppearanceShapes.Box;
                            var xlength = app.Attributes["x"];
                            if (xlength == null)
                            {
                                new RsuException("box appearance is missing x");
                            }
                            var ylength = app.Attributes["y"];
                            if (ylength == null)
                            {
                                new RsuException("box appearance is missing y");
                            }
                            var zlength = app.Attributes["z"];
                            if (zlength == null)
                            {
                                new RsuException("box appearance is missing z");
                            }
                            
                            appearance.dimension = new Vector3(float.Parse(xlength.Value), float.Parse(ylength.Value), float.Parse(zlength.Value));
                        }
                        break;
                        case "cylinder":
                        {
                            appearance.shapes = AppearanceShapes.Cylinder;
                            var radius = app.Attributes["radius"];
                            if (radius == null)
                            {
                                new RsuException("cylinder appearance is missing radius");
                            }
                            var length = app.Attributes["length"];
                            if (length == null)
                            {
                                new RsuException("cylinder appearance is missing length");
                            }
                            appearance.dimension = new Vector3(float.Parse(radius.Value), float.Parse(length.Value), 0);
                        }
                        break;
                        case "capsule":
                        {
                            appearance.shapes = AppearanceShapes.Capsule;
                            var radius = app.Attributes["radius"];
                            if (radius == null)
                            {
                                new RsuException("capsule appearance is missing radius");
                            }
                            var length = app.Attributes["length"];
                            if (length == null)
                            {
                                new RsuException("capsule appearance is missing length");
                            }
                            appearance.dimension = new Vector3(float.Parse(radius.Value), float.Parse(length.Value), 0);
                        }
                        break;
                        case "mesh":
                        {
                            appearance.shapes = AppearanceShapes.Mesh;
                            var scale = app.Attributes["scale"];
                            float scaleVal = 1;
                            if (scale != null)
                            {
                                scaleVal = float.Parse(scale.Value);
                            }
                            appearance.dimension = new Vector3(float.Parse(scale.Value), float.Parse(scale.Value), float.Parse(scale.Value));
                            var fileName = app.Attributes["fileName"];
                            if (fileName == null)
                            {
                                new RsuException("mesh appearance is missing fileName");
                            }
                            appearance.fileName = fileName.Value;
                        }
                        break;
                      default:
                            break;
                    }
                    
                    appearances.subAppearances.Add(appearance);
                }
                
                var name = obj.Attributes["name"].Value;
                _table.Add(name, appearances);
            }
        }

        public void ClearAppearanceMap()
        {
            _table.Clear();
        }

        public Appearances? FindApperancesFromObjectName(string name)
        {
            if (!_table.ContainsKey(name)) return null;
            return _table[name];
        }
    }
}