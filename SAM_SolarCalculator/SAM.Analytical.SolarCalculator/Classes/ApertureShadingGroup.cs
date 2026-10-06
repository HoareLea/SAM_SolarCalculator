// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// Several apertures that may share ONE physical shading device: a deterministic, serialisable
    /// statement of which apertures, on which host panel, in which left-to-right order, on which
    /// common facade frame, over which combined extent.
    ///
    /// Why this type exists. The single-aperture contract (ShadingDevice, Optimise.ShadingTypology)
    /// is deliberately one aperture at a time, and it must stay that way: its identity checks exist
    /// to stop a device sized for one window being reported against another. A grouped awning is a
    /// DIFFERENT thing — one physical unit measured against several apertures at once — so it gets
    /// its own type instead of a synthetic "group GUID" smuggled into a field documented as an
    /// aperture GUID.
    ///
    /// The group GUID is deterministic: identical membership and geometry give identical identity,
    /// regardless of the order the apertures were supplied in, so a saved group can be compared and
    /// a recalculation can recognise the same group.
    ///
    /// Members are ordered left-to-right across the facade. The group's local frame is the
    /// leftmost member's aperture frame, so X still runs across the facade, Y up-slope and Z
    /// outward; MinX/MaxX/MaxY are the combined envelope in that frame.
    /// </summary>
    public class ApertureShadingGroup : IJSAMObject, ISolarObject
    {
        private Guid groupGuid;
        private Guid panelGuid;
        private List<Guid> apertureGuids = new List<Guid>();
        private List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>();
        private Plane plane;
        private double minX = double.NaN;
        private double maxX = double.NaN;
        private double maxY = double.NaN;
        private double totalGrossArea = double.NaN;
        private List<int> cellIndexOffsets = new List<int>();

        public ApertureShadingGroup(Guid groupGuid, Guid panelGuid, IEnumerable<ApertureSolarTarget> targets, Plane plane, double minX, double maxX, double maxY, IEnumerable<int> cellIndexOffsets = null)
        {
            this.groupGuid = groupGuid;
            this.panelGuid = panelGuid;

            if (targets != null)
            {
                foreach (ApertureSolarTarget target in targets)
                {
                    if (target == null)
                    {
                        continue;
                    }

                    apertureGuids.Add(target.ApertureGuid);
                    this.targets.Add(new ApertureSolarTarget(target));
                }
            }

            this.plane = plane == null ? null : new Plane(plane);
            this.minX = minX;
            this.maxX = maxX;
            this.maxY = maxY;

            if (cellIndexOffsets != null)
            {
                this.cellIndexOffsets.AddRange(cellIndexOffsets);
            }
        }

        public ApertureShadingGroup(ApertureShadingGroup apertureShadingGroup)
        {
            if (apertureShadingGroup == null)
            {
                return;
            }

            groupGuid = apertureShadingGroup.groupGuid;
            panelGuid = apertureShadingGroup.panelGuid;
            apertureGuids = new List<Guid>(apertureShadingGroup.apertureGuids);
            targets = apertureShadingGroup.targets.ConvertAll(x => x == null ? null : new ApertureSolarTarget(x));
            plane = apertureShadingGroup.plane == null ? null : new Plane(apertureShadingGroup.plane);
            minX = apertureShadingGroup.minX;
            maxX = apertureShadingGroup.maxX;
            maxY = apertureShadingGroup.maxY;
            totalGrossArea = apertureShadingGroup.totalGrossArea;
            cellIndexOffsets = new List<int>(apertureShadingGroup.cellIndexOffsets);
        }

        public ApertureShadingGroup(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>Deterministic identity of this group: membership and geometry, not input order.</summary>
        public Guid GroupGuid { get { return groupGuid; } }

        /// <summary>The host panel every member aperture belongs to.</summary>
        public Guid PanelGuid { get { return panelGuid; } }

        /// <summary>Member aperture GUIDs, ordered left-to-right across the facade.</summary>
        public List<Guid> ApertureGuids { get { return new List<Guid>(apertureGuids); } }

        /// <summary>Member targets, in the same left-to-right order as <see cref="ApertureGuids"/>.</summary>
        public List<ApertureSolarTarget> Targets { get { return targets.ConvertAll(x => x == null ? null : new ApertureSolarTarget(x)); } }

        public ApertureSolarTarget Target(Guid apertureGuid)
        {
            return targets.Find(x => x != null && x.ApertureGuid == apertureGuid);
        }

        /// <summary>The group's facade-local frame (X across, Y up-slope, Z outward).</summary>
        public Plane Plane { get { return plane == null ? null : new Plane(plane); } }

        /// <summary>Combined aperture envelope across the facade, m — left edge in the group frame.</summary>
        public double MinX { get { return minX; } }

        /// <summary>Combined aperture envelope across the facade, m — right edge in the group frame.</summary>
        public double MaxX { get { return maxX; } }

        /// <summary>The highest head of any member in the group frame, m — the mounting line the canopy hangs from.</summary>
        public double MaxY { get { return maxY; } }

        /// <summary>Combined aperture envelope width across the facade, m.</summary>
        public double Width { get { return maxX - minX; } }

        /// <summary>Sum of member gross aperture areas, m2 — the denominator of the group material fraction.</summary>
        public double TotalGrossArea
        {
            get
            {
                if (!double.IsNaN(totalGrossArea))
                {
                    return totalGrossArea;
                }

                double result = 0;
                foreach (ApertureSolarTarget target in targets)
                {
                    double area = target?.GrossArea ?? 0;
                    if (!double.IsNaN(area))
                    {
                        result += area;
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Each member's first cell index in the shared visibility cache, in member order; -1 where
        /// the offset has not been resolved (no shared cell space attached).
        /// </summary>
        public List<int> CellIndexOffsets { get { return new List<int>(cellIndexOffsets); } }

        public int CellIndexOffset(Guid apertureGuid)
        {
            int index = apertureGuids.IndexOf(apertureGuid);
            return index < 0 || index >= cellIndexOffsets.Count ? -1 : cellIndexOffsets[index];
        }

        internal void SetCellIndexOffsets(IEnumerable<int> offsets)
        {
            cellIndexOffsets = offsets == null ? new List<int>() : new List<int>(offsets);
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("GroupGuid")) { Guid.TryParse(jObject["GroupGuid"]?.GetValue<string>(), out groupGuid); }
            if (jObject.ContainsKey("PanelGuid")) { Guid.TryParse(jObject["PanelGuid"]?.GetValue<string>(), out panelGuid); }

            apertureGuids = new List<Guid>();
            if (jObject.ContainsKey("ApertureGuids") && jObject["ApertureGuids"] is JsonArray guidsArray)
            {
                foreach (JsonNode node in guidsArray)
                {
                    if (Guid.TryParse(node?.GetValue<string>(), out Guid guid))
                    {
                        apertureGuids.Add(guid);
                    }
                }
            }

            targets = new List<ApertureSolarTarget>();
            if (jObject.ContainsKey("Targets") && jObject["Targets"] is JsonArray targetsArray)
            {
                foreach (JsonNode node in targetsArray)
                {
                    if (node is JsonObject targetObject)
                    {
                        targets.Add(Core.Create.IJSAMObject<ApertureSolarTarget>(targetObject));
                    }
                }
            }

            plane = jObject.ContainsKey("Plane") ? new Plane(jObject["Plane"] as JsonObject) : null;
            minX = Read(jObject, "MinX");
            maxX = Read(jObject, "MaxX");
            maxY = Read(jObject, "MaxY");
            totalGrossArea = Read(jObject, "TotalGrossArea");

            cellIndexOffsets = new List<int>();
            if (jObject.ContainsKey("CellIndexOffsets") && jObject["CellIndexOffsets"] is JsonArray offsetsArray)
            {
                foreach (JsonNode node in offsetsArray)
                {
                    cellIndexOffsets.Add(node?.GetValue<int>() ?? -1);
                }
            }

            return true;
        }

        private static double Read(JsonObject jObject, string name)
        {
            return jObject.ContainsKey(name) ? (jObject[name]?.GetValue<double>() ?? double.NaN) : double.NaN;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            jObject.Add("GroupGuid", groupGuid.ToString());
            jObject.Add("PanelGuid", panelGuid.ToString());

            JsonArray guidsArray = new JsonArray();
            foreach (Guid guid in apertureGuids)
            {
                guidsArray.Add(guid.ToString());
            }
            jObject.Add("ApertureGuids", guidsArray);

            JsonArray targetsArray = new JsonArray();
            foreach (ApertureSolarTarget target in targets)
            {
                targetsArray.Add(target?.ToJsonObject());
            }
            jObject.Add("Targets", targetsArray);

            if (plane != null)
            {
                jObject.Add("Plane", plane.ToJsonObject());
            }

            jObject.Add("MinX", minX);
            jObject.Add("MaxX", maxX);
            jObject.Add("MaxY", maxY);
            jObject.Add("TotalGrossArea", TotalGrossArea);

            JsonArray offsetsArray = new JsonArray();
            foreach (int offset in cellIndexOffsets)
            {
                offsetsArray.Add(offset);
            }
            jObject.Add("CellIndexOffsets", offsetsArray);

            return jObject;
        }
    }
}
