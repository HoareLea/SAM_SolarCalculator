// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.Spatial;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// One physical piece of a shading device: a single opaque Face3D with a stable Guid and a
    /// human-readable name ("Overhang", "Louvre 3", "Fin 2 (east)").
    ///
    /// The Guid is what first-hit attribution reports, so it is how per-element energy is credited.
    /// It is stable for a given typology and parameter set — regenerating the same device produces
    /// the same Guids — which is what allows a rationalisation sweep to compare candidates and a
    /// cached attribution to be checked against the geometry that produced it.
    ///
    /// The face is OPAQUE. There is no transmittance, no porosity and no reflectance: the ray
    /// engine answers "blocked or not blocked". See the note on perforated screens in
    /// IShadingTypology.
    /// </summary>
    public class ShadingElement : IJSAMObject, ISolarObject
    {
        private Guid guid;
        private string name;
        private Face3D face3D;

        public ShadingElement(Guid guid, string name, Face3D face3D)
        {
            this.guid = guid;
            this.name = name;
            this.face3D = face3D == null ? null : Core.Query.Clone(face3D);
        }

        public ShadingElement(ShadingElement shadingElement)
        {
            if (shadingElement != null)
            {
                guid = shadingElement.guid;
                name = shadingElement.name;
                face3D = shadingElement.face3D == null ? null : Core.Query.Clone(shadingElement.face3D);
            }
        }

        public ShadingElement(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public Guid Guid { get { return guid; } }

        public string Name { get { return name; } }

        public Face3D Face3D { get { return face3D == null ? null : Core.Query.Clone(face3D); } }

        public double Area { get { return face3D == null ? double.NaN : face3D.GetArea(); } }

        /// <summary>The element as a ray-engine occluder, carrying its Guid for attribution.</summary>
        public LinkedFace3D LinkedFace3D { get { return face3D == null ? null : new LinkedFace3D(guid, Core.Query.Clone(face3D)); } }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("Guid")) { Guid.TryParse(jObject["Guid"]?.GetValue<string>(), out guid); }
            if (jObject.ContainsKey("Name")) { name = jObject["Name"]?.GetValue<string>(); }
            if (jObject.ContainsKey("Face3D")) { face3D = new Face3D(jObject["Face3D"] as JsonObject); }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            jObject.Add("Guid", guid.ToString());

            if (name != null) { jObject.Add("Name", name); }
            if (face3D != null) { jObject.Add("Face3D", face3D.ToJsonObject()); }

            return jObject;
        }
    }
}
