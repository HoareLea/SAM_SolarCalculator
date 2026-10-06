// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.Geometry.SolarCalculator
{
    /// <summary>
    /// One sky-dome patch: a representative unit direction (pointing from the receiving surface
    /// TOWARD the patch; Z &gt;= 0 for sky patches, Z &lt; 0 for mirrored ground patches) and its
    /// solid angle in steradians. IsHorizonBand marks the lowest sky band, which carries the Perez
    /// horizon-brightening term.
    /// </summary>
    public class SkyPatch : IJSAMObject, ISolarObject
    {
        private int index;
        private Vector3D direction;
        private double solidAngle;
        private bool isHorizonBand;

        public SkyPatch(int index, Vector3D direction, double solidAngle, bool isHorizonBand)
        {
            this.index = index;
            this.direction = direction == null ? null : new Vector3D(direction);
            this.solidAngle = solidAngle;
            this.isHorizonBand = isHorizonBand;
        }

        public SkyPatch(SkyPatch skyPatch)
        {
            if (skyPatch != null)
            {
                index = skyPatch.index;
                direction = skyPatch.direction == null ? null : new Vector3D(skyPatch.direction);
                solidAngle = skyPatch.solidAngle;
                isHorizonBand = skyPatch.isHorizonBand;
            }
        }

        public SkyPatch(JsonObject jObject)
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

        /// <summary>Unit vector from the receiving surface toward the patch centre.</summary>
        public Vector3D Direction
        {
            get
            {
                return direction == null ? null : new Vector3D(direction);
            }
        }

        /// <summary>Solid angle of the patch, steradians.</summary>
        public double SolidAngle
        {
            get
            {
                return solidAngle;
            }
        }

        /// <summary>True for patches of the lowest sky band (the Perez horizon-brightening region).</summary>
        public bool IsHorizonBand
        {
            get
            {
                return isHorizonBand;
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

            if (jObject.ContainsKey("Direction"))
            {
                direction = new Vector3D(jObject["Direction"] as JsonObject);
            }

            if (jObject.ContainsKey("SolidAngle"))
            {
                solidAngle = jObject["SolidAngle"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("IsHorizonBand"))
            {
                isHorizonBand = jObject["IsHorizonBand"]?.GetValue<bool>() ?? default;
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            jObject.Add("Index", index);

            if (direction != null)
            {
                jObject.Add("Direction", direction.ToJsonObject());
            }

            jObject.Add("SolidAngle", solidAngle);
            jObject.Add("IsHorizonBand", isHorizonBand);

            return jObject;
        }
    }
}
