// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.Geometry.SolarCalculator
{
    /// <summary>
    /// One analysis cell of a subdivided Face3D: the cell face (in the parent face's plane), its
    /// area, centroid, a guaranteed-interior point (used for ray-cast visibility tests) and a
    /// stable index (position within the subdivision that created it).
    /// </summary>
    public class AnalysisCell : IJSAMObject, ISolarObject
    {
        private int index;
        private Face3D face3D;
        private double area;
        private Point3D centroid;
        private Point3D internalPoint3D;

        public AnalysisCell(int index, Face3D face3D, Point3D internalPoint3D)
        {
            this.index = index;
            this.face3D = face3D == null ? null : Core.Query.Clone(face3D);
            area = face3D?.GetArea() ?? double.NaN;
            centroid = face3D?.GetCentroid();
            this.internalPoint3D = internalPoint3D == null ? null : Core.Query.Clone(internalPoint3D);
        }

        public AnalysisCell(AnalysisCell analysisCell)
        {
            if (analysisCell != null)
            {
                index = analysisCell.index;
                face3D = analysisCell.face3D == null ? null : Core.Query.Clone(analysisCell.face3D);
                area = analysisCell.area;
                centroid = analysisCell.centroid == null ? null : Core.Query.Clone(analysisCell.centroid);
                internalPoint3D = analysisCell.internalPoint3D == null ? null : Core.Query.Clone(analysisCell.internalPoint3D);
            }
        }

        public AnalysisCell(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public int Index
        {
            get
            {
                return index;
            }
        }

        public Face3D Face3D
        {
            get
            {
                return face3D == null ? null : Core.Query.Clone(face3D);
            }
        }

        public double Area
        {
            get
            {
                return area;
            }
        }

        public Point3D Centroid
        {
            get
            {
                return centroid == null ? null : Core.Query.Clone(centroid);
            }
        }

        /// <summary>A point guaranteed to lie inside the cell face (used for visibility ray casts).</summary>
        public Point3D InternalPoint3D
        {
            get
            {
                return internalPoint3D == null ? null : Core.Query.Clone(internalPoint3D);
            }
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("Index"))
            {
                index = jObject["Index"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("Face3D"))
            {
                face3D = new Face3D(jObject["Face3D"] as JsonObject);
            }

            if (jObject.ContainsKey("Area"))
            {
                area = jObject["Area"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("Centroid"))
            {
                centroid = new Point3D(jObject["Centroid"] as JsonObject);
            }

            if (jObject.ContainsKey("InternalPoint3D"))
            {
                internalPoint3D = new Point3D(jObject["InternalPoint3D"] as JsonObject);
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            jObject.Add("Index", index);

            if (face3D != null)
            {
                jObject.Add("Face3D", face3D.ToJsonObject());
            }

            if (!double.IsNaN(area))
            {
                jObject.Add("Area", area);
            }

            if (centroid != null)
            {
                jObject.Add("Centroid", centroid.ToJsonObject());
            }

            if (internalPoint3D != null)
            {
                jObject.Add("InternalPoint3D", internalPoint3D.ToJsonObject());
            }

            return jObject;
        }
    }
}
