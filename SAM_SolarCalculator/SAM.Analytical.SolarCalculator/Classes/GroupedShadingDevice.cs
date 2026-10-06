// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// ONE physical awning designed for a GROUP of apertures, with the identity of that group.
    ///
    /// The single-aperture ShadingDevice deliberately carries one aperture and is refused against
    /// another; that contract is unchanged. A grouped awning is a different engineering object — one
    /// unit spanning several windows — so it gets its own type instead of a synthetic group Guid
    /// written into a field documented as an aperture Guid.
    ///
    /// The typology is stored, not the geometry: the canopy is regenerated from the group's frame
    /// and combined bounds, so a grouped device is always rebuilt against the group it was designed
    /// for. The null device (NoShading) means "build nothing over this group" and is the honest
    /// NO SHADE answer, exactly as in the single-aperture path.
    ///
    /// THE GROUPING CRITERIA ARE CARRIED. MaximumGap and HeadTolerance record the criteria the
    /// group was formed under, so a downstream node (SAMAnalytical.ShadingOperation) can
    /// re-establish the EXACT original physical group instead of re-grouping under the algorithm
    /// defaults. A device without recorded criteria — an older file, or the five-parameter
    /// constructor — claims the defaults, which is exactly how such a device's group was
    /// re-established before the criteria were carried.
    /// </summary>
    public class GroupedShadingDevice : IJSAMObject, ISolarObject
    {
        // The ONE definition of the grouping-criteria defaults: the Create.ApertureShadingGroups
        // and Create.AwningGroupResults optional parameters reference these constants, so the
        // algorithm and the device can never drift into disagreeing about what a criteria-less
        // call claims. A device that does not record its criteria is treated as formed under them.
        internal const double DefaultMaximumGap = 0.20;
        internal const double DefaultHeadTolerance = 0.02;

        private Guid groupGuid;
        private Guid panelGuid;
        private List<Guid> apertureGuids = new List<Guid>();
        private IShadingTypology typology;
        private AwningSpecification specification;
        private double maximumGap = DefaultMaximumGap;
        private double headTolerance = DefaultHeadTolerance;

        /// <summary>
        /// The original five-parameter constructor, retained exactly so callers compiled against
        /// the pre-grouping-criteria signature keep resolving. It forwards with the algorithm
        /// defaults — the only honest claim a criteria-less call can make.
        /// </summary>
        public GroupedShadingDevice(Guid groupGuid, Guid panelGuid, IEnumerable<Guid> apertureGuids, IShadingTypology typology, AwningSpecification specification)
            : this(groupGuid, panelGuid, apertureGuids, typology, specification, DefaultMaximumGap, DefaultHeadTolerance)
        {
        }

        public GroupedShadingDevice(Guid groupGuid, Guid panelGuid, IEnumerable<Guid> apertureGuids, IShadingTypology typology, AwningSpecification specification, double maximumGap, double headTolerance)
        {
            this.groupGuid = groupGuid;
            this.panelGuid = panelGuid;
            this.apertureGuids = new List<Guid>(apertureGuids ?? new List<Guid>());
            this.typology = typology;
            this.specification = specification;

            // NaN is not a criterion: it would make every grouping comparison false, so the
            // device falls back to the default rather than carry a criterion that means nothing.
            this.maximumGap = double.IsNaN(maximumGap) ? DefaultMaximumGap : maximumGap;
            this.headTolerance = double.IsNaN(headTolerance) ? DefaultHeadTolerance : headTolerance;
        }

        public GroupedShadingDevice(GroupedShadingDevice groupedShadingDevice)
        {
            if (groupedShadingDevice != null)
            {
                groupGuid = groupedShadingDevice.groupGuid;
                panelGuid = groupedShadingDevice.panelGuid;
                apertureGuids = new List<Guid>(groupedShadingDevice.apertureGuids);
                typology = groupedShadingDevice.typology;
                specification = groupedShadingDevice.specification;
                maximumGap = groupedShadingDevice.maximumGap;
                headTolerance = groupedShadingDevice.headTolerance;
            }
        }

        public GroupedShadingDevice(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>The deterministic identity of the aperture group this device belongs to.</summary>
        public Guid GroupGuid { get { return groupGuid; } }

        /// <summary>The host panel every member aperture belongs to.</summary>
        public Guid PanelGuid { get { return panelGuid; } }

        /// <summary>Member aperture GUIDs, ordered left-to-right across the facade.</summary>
        public List<Guid> ApertureGuids { get { return new List<Guid>(apertureGuids); } }

        /// <summary>The device itself. NoShading is the null device: "build nothing over this group".</summary>
        public IShadingTypology Typology { get { return typology; } }

        /// <summary>The product preset the device was constrained by. Null for the generic single-aperture family.</summary>
        public AwningSpecification Specification { get { return specification; } }

        /// <summary>
        /// The largest horizontal gap between adjacent apertures that still shares one awning [m],
        /// as used when this group was formed. Carried so the group can be re-established under
        /// exactly the original criterion, never re-grouped under a default.
        /// </summary>
        public double MaximumGap { get { return maximumGap; } }

        /// <summary>
        /// The largest head-level spread within this group [m], as used when it was formed.
        /// Carried so the group can be re-established under exactly the original criterion.
        /// </summary>
        public double HeadTolerance { get { return headTolerance; } }

        public string SpecificationName { get { return specification?.Name; } }

        public string TypologyName { get { return typology?.Name; } }

        public bool IsNoShading { get { return typology is NoShading; } }

        /// <summary>The awning width this device spans [m]: group envelope width plus both side extensions. NaN for the null device.</summary>
        public double Width(ApertureShadingGroup group)
        {
            if (group == null || !(typology is RetractableAwning awning))
            {
                return double.NaN;
            }

            return group.Width + 2.0 * awning.GetParameter("ExtensionBeyondJambs");
        }

        /// <summary>The shared element set of this device over the group: built ONCE in world
        /// coordinates from the group frame, never one copy per member aperture.</summary>
        public List<ShadingElement> ShadingElements(ApertureShadingGroup group)
        {
            if (group == null || typology == null)
            {
                return null;
            }

            if (typology is NoShading)
            {
                return new List<ShadingElement>();
            }

            if (!(typology is RetractableAwning awning))
            {
                return null;
            }

            return awning.ShadingElements(group.Plane, group.MinX, group.MaxX, group.MaxY);
        }

        public override string ToString()
        {
            return typology == null ? "GroupedShadingDevice (none)" : typology.Name + " @ " + groupGuid;
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

            typology = jObject.ContainsKey("Typology")
                ? Core.Create.IJSAMObject<IShadingTypology>(jObject["Typology"] as JsonObject)
                : null;

            specification = jObject.ContainsKey("Specification")
                ? new AwningSpecification(jObject["Specification"] as JsonObject)
                : null;

            // Absent keys mean a file written before the criteria were carried: the defaults are
            // the honest reading, exactly the behaviour such a device always re-established with.
            maximumGap = Criterion(jObject, "MaximumGap", DefaultMaximumGap);
            headTolerance = Criterion(jObject, "HeadTolerance", DefaultHeadTolerance);

            return true;
        }

        /// <summary>A grouping criterion from JSON: the stored value, or the default when the key
        /// is absent (a pre-criteria file) or carries no usable number.</summary>
        private static double Criterion(JsonObject jObject, string name, double defaultValue)
        {
            double value = jObject.ContainsKey(name) ? (jObject[name]?.GetValue<double>() ?? double.NaN) : double.NaN;
            return double.IsNaN(value) ? defaultValue : value;
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

            if (typology != null)
            {
                jObject.Add("Typology", typology.ToJsonObject());
            }

            if (specification != null)
            {
                jObject.Add("Specification", specification.ToJsonObject());
            }

            jObject.Add("MaximumGap", maximumGap);
            jObject.Add("HeadTolerance", headTolerance);

            return jObject;
        }
    }
}
