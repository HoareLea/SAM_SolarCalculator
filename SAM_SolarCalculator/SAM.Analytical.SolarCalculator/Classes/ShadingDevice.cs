// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// A shading device WITH the identity of the aperture it was designed for.
    ///
    /// Why this type exists. An IShadingTypology is deliberately aperture-agnostic: it is a rule
    /// ("an overhang 0.57 m deep") that is turned into geometry against whatever target it is given.
    /// That is the right design for the optimiser — but it means a bare typology handed from one
    /// node to another carries NOTHING that says which window it belongs to. On a single-window
    /// script that is harmless. On ten windows it means the verification node has no way to tell
    /// that the device on its wire was sized for a different facade: it would build that device on
    /// the target it was given, measure it correctly, and report a number that is arithmetically
    /// right and about the wrong design. A wrong answer that looks exactly like a right one is the
    /// worst failure mode an engineering tool can have, so device identity is carried explicitly and
    /// checked, rather than inferred from the order things happen to sit in a list.
    ///
    /// The typology itself is stored, not the geometry: geometry is regenerated from the target, so
    /// a device is always built against the aperture it is being measured on.
    /// </summary>
    public class ShadingDevice : IJSAMObject, ISolarObject
    {
        private Guid apertureGuid;
        private IShadingTypology typology;

        public ShadingDevice(Guid apertureGuid, IShadingTypology typology)
        {
            this.apertureGuid = apertureGuid;
            this.typology = typology;
        }

        public ShadingDevice(ShadingDevice shadingDevice)
        {
            if (shadingDevice != null)
            {
                apertureGuid = shadingDevice.apertureGuid;
                typology = shadingDevice.typology;
            }
        }

        public ShadingDevice(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>The aperture this device was designed for.</summary>
        public Guid ApertureGuid { get { return apertureGuid; } }

        /// <summary>The device itself. Null is not expected; a "build nothing" answer is a NoShading typology.</summary>
        public IShadingTypology Typology { get { return typology; } }

        /// <summary>True when this is the null device — the answer being "leave the window alone".</summary>
        public bool IsNoShading { get { return typology is NoShading; } }

        public string TypologyName { get { return typology?.Name; } }

        public override string ToString()
        {
            return typology == null ? "ShadingDevice (none)" : typology.Name + " @ " + apertureGuid;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("ApertureGuid"))
            {
                Guid.TryParse(jObject["ApertureGuid"]?.GetValue<string>(), out apertureGuid);
            }

            typology = jObject.ContainsKey("Typology")
                ? Core.Create.IJSAMObject<IShadingTypology>(jObject["Typology"] as JsonObject)
                : null;

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            jObject.Add("ApertureGuid", apertureGuid.ToString());

            if (typology != null)
            {
                jObject.Add("Typology", typology.ToJsonObject());
            }

            return jObject;
        }
    }
}
