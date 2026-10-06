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
    /// What ONE physical shading device did over the weather year across a GROUP of apertures.
    ///
    /// THE HEADLINE IS PER DEVICE, NOT PER APERTURE. A motor spanning three windows is reported by
    /// the hours it moved and the hours it stayed parked, never by the tallest window's request
    /// wearing the device's name. The device sets are UNIONS of the member schedules:
    ///
    ///   DeviceDemandHoursOfYear     union of member requested hours   (the motor could move)
    ///   DeviceDeployedHoursOfYear   union of member deployed hours    (the motor moved)
    ///   DeviceWindRetractedHoursOfYear  union of member refused hours (requested but parked)
    ///
    /// Because the wind speed is the weather file's own hourly value — the SAME hour for every
    /// aperture on the site — and the agreement gate below forces every member to share one wind
    /// limit, the three device sets keep the control profile's exact partition:
    ///
    ///   DeviceDemand = DeviceDeployed + DeviceWindRetracted, disjointly.
    ///
    /// PER-APERTURE DIAGNOSTICS ARE KEPT. Every member <see cref="ShadingOperationProfile"/> is
    /// retained, so the answer says both "how often does the motor move?" and "which window caused
    /// it?".
    ///
    /// THE AGREEMENT GATE, ENFORCED AT CREATION. Combining schedules built under different rules
    /// or timelines would produce a number nobody can interpret, so the grouped creation refuses
    /// members that disagree on year, sun-position shift or control settings (including the wind
    /// limit). Nothing is averaged, blended or silently merged.
    ///
    /// ENERGY AGGREGATION IS THE EXISTING GROUPED ACCOUNTING. <see cref="Performance"/> is a
    /// <see cref="GroupedShadingPerformance"/> built from the per-member controlled measurements —
    /// one shared material charge, per-element energy aggregated by element Guid. The operation
    /// profile adds only what that type does not carry: the device hour sets and the summed
    /// canopy/valance figures.
    /// </summary>
    public class GroupedShadingOperationProfile : IJSAMObject, ISolarObject
    {
        private Guid groupGuid;
        private Guid panelGuid;
        private string typologyName;
        private int year;
        private double timeShiftInMinutes;
        private SolarControlSettings settings;
        private List<ShadingOperationProfile> members = new List<ShadingOperationProfile>();
        private GroupedShadingPerformance performance;
        private List<int> deviceDemandHoursOfYear;
        private List<int> deviceDeployedHoursOfYear;
        private List<int> deviceWindRetractedHoursOfYear;
        private List<int> canopyEffectiveHoursOfYear;
        private List<int> valanceEffectiveHoursOfYear;
        private double canopyAttributedEnergy = double.NaN;
        private double valanceAttributedEnergy = double.NaN;

        /// <param name="groupGuid">The deterministic identity of the aperture group the device spans.</param>
        /// <param name="panelGuid">The host panel every member aperture belongs to.</param>
        /// <param name="typologyName">The device family name.</param>
        /// <param name="year">The weather year every hour of the year is counted in.</param>
        /// <param name="timeShiftInMinutes">Sun-position sampling offset of the timeline, minutes.</param>
        /// <param name="settings">The agreed control rule, including the wind limit.</param>
        /// <param name="members">Per-aperture diagnostics, one per member aperture.</param>
        /// <param name="performance">The existing grouped energy accounting over the members.</param>
        public GroupedShadingOperationProfile(Guid groupGuid, Guid panelGuid, string typologyName, int year, double timeShiftInMinutes, SolarControlSettings settings, IEnumerable<ShadingOperationProfile> members, GroupedShadingPerformance performance)
        {
            this.groupGuid = groupGuid;
            this.panelGuid = panelGuid;
            this.typologyName = typologyName;
            this.year = year;
            this.timeShiftInMinutes = timeShiftInMinutes;
            this.settings = settings == null ? null : new SolarControlSettings(settings);
            this.performance = performance;

            HashSet<int> demand = new HashSet<int>();
            HashSet<int> deployed = new HashSet<int>();
            HashSet<int> windRetracted = new HashSet<int>();
            HashSet<int> canopyEffective = new HashSet<int>();
            HashSet<int> valanceEffective = new HashSet<int>();
            double canopyEnergy = 0;
            double valanceEnergy = 0;
            bool anyCanopy = false;
            bool anyValance = false;

            foreach (ShadingOperationProfile member in members ?? new List<ShadingOperationProfile>())
            {
                if (member == null)
                {
                    continue;
                }

                this.members.Add(member);

                Union(demand, member.DeployedHoursOfYear);
                Union(demand, member.WindRetractedHoursOfYear); // demand = deployed + refused, exactly
                Union(deployed, member.DeployedHoursOfYear);
                Union(windRetracted, member.WindRetractedHoursOfYear);
                Union(canopyEffective, member.CanopyEffectiveHoursOfYear);
                Union(valanceEffective, member.ValanceEffectiveHoursOfYear);

                double memberCanopyEnergy = member.CanopyAttributedEnergy;
                if (!double.IsNaN(memberCanopyEnergy))
                {
                    canopyEnergy += memberCanopyEnergy;
                    anyCanopy = true;
                }

                double memberValanceEnergy = member.ValanceAttributedEnergy;
                if (!double.IsNaN(memberValanceEnergy))
                {
                    valanceEnergy += memberValanceEnergy;
                    anyValance = true;
                }
            }

            deviceDemandHoursOfYear = Sorted(demand);
            deviceDeployedHoursOfYear = Sorted(deployed);
            deviceWindRetractedHoursOfYear = Sorted(windRetracted);
            canopyEffectiveHoursOfYear = Sorted(canopyEffective);
            valanceEffectiveHoursOfYear = Sorted(valanceEffective);

            // NaN when no member carries the element at all: a device without a valance reports
            // "not applicable", never a fabricated 0.0 kWh.
            canopyAttributedEnergy = anyCanopy ? canopyEnergy : double.NaN;
            valanceAttributedEnergy = anyValance ? valanceEnergy : double.NaN;
        }

        public GroupedShadingOperationProfile(GroupedShadingOperationProfile groupedShadingOperationProfile)
        {
            if (groupedShadingOperationProfile != null)
            {
                groupGuid = groupedShadingOperationProfile.groupGuid;
                panelGuid = groupedShadingOperationProfile.panelGuid;
                typologyName = groupedShadingOperationProfile.typologyName;
                year = groupedShadingOperationProfile.year;
                timeShiftInMinutes = groupedShadingOperationProfile.timeShiftInMinutes;
                settings = groupedShadingOperationProfile.settings == null ? null : new SolarControlSettings(groupedShadingOperationProfile.settings);
                members = new List<ShadingOperationProfile>(groupedShadingOperationProfile.members);
                performance = groupedShadingOperationProfile.performance;
                deviceDemandHoursOfYear = groupedShadingOperationProfile.deviceDemandHoursOfYear == null ? null : new List<int>(groupedShadingOperationProfile.deviceDemandHoursOfYear);
                deviceDeployedHoursOfYear = groupedShadingOperationProfile.deviceDeployedHoursOfYear == null ? null : new List<int>(groupedShadingOperationProfile.deviceDeployedHoursOfYear);
                deviceWindRetractedHoursOfYear = groupedShadingOperationProfile.deviceWindRetractedHoursOfYear == null ? null : new List<int>(groupedShadingOperationProfile.deviceWindRetractedHoursOfYear);
                canopyEffectiveHoursOfYear = groupedShadingOperationProfile.canopyEffectiveHoursOfYear == null ? null : new List<int>(groupedShadingOperationProfile.canopyEffectiveHoursOfYear);
                valanceEffectiveHoursOfYear = groupedShadingOperationProfile.valanceEffectiveHoursOfYear == null ? null : new List<int>(groupedShadingOperationProfile.valanceEffectiveHoursOfYear);
                canopyAttributedEnergy = groupedShadingOperationProfile.canopyAttributedEnergy;
                valanceAttributedEnergy = groupedShadingOperationProfile.valanceAttributedEnergy;
            }
        }

        public GroupedShadingOperationProfile(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public Guid GroupGuid
        {
            get
            {
                return groupGuid;
            }
        }

        public Guid PanelGuid
        {
            get
            {
                return panelGuid;
            }
        }

        public string TypologyName
        {
            get
            {
                return typologyName;
            }
        }

        public int Year
        {
            get
            {
                return year;
            }
        }

        public double TimeShiftInMinutes
        {
            get
            {
                return timeShiftInMinutes;
            }
        }

        /// <summary>The agreed control rule, including the wind limit. Defensive copy.</summary>
        public SolarControlSettings Settings
        {
            get
            {
                return settings == null ? null : new SolarControlSettings(settings);
            }
        }

        /// <summary>Per-aperture diagnostics, one per member aperture, in member order.</summary>
        public List<ShadingOperationProfile> Members
        {
            get
            {
                return new List<ShadingOperationProfile>(members);
            }
        }

        public ShadingOperationProfile Member(Guid apertureGuid)
        {
            return members.Find(x => x != null && x.ApertureGuid == apertureGuid);
        }

        /// <summary>The existing grouped energy accounting: per-member controlled measurements aggregated with one shared material charge.</summary>
        public GroupedShadingPerformance Performance
        {
            get
            {
                return performance;
            }
        }

        /// <summary>Hours the DEVICE's shading was requested in — the union of the member requests. 0-based.</summary>
        public List<int> DeviceDemandHoursOfYear
        {
            get
            {
                return deviceDemandHoursOfYear == null ? null : new List<int>(deviceDemandHoursOfYear);
            }
        }

        public int DeviceDemandHours
        {
            get
            {
                return deviceDemandHoursOfYear?.Count ?? 0;
            }
        }

        /// <summary>Hours the DEVICE was actually deployed in — the union of the member deployments. 0-based.</summary>
        public List<int> DeviceDeployedHoursOfYear
        {
            get
            {
                return deviceDeployedHoursOfYear == null ? null : new List<int>(deviceDeployedHoursOfYear);
            }
        }

        public int DeviceDeployedHours
        {
            get
            {
                return deviceDeployedHoursOfYear?.Count ?? 0;
            }
        }

        /// <summary>Hours the DEVICE was requested and refused by wind — the union of the member refusals. 0-based.</summary>
        public List<int> DeviceWindRetractedHoursOfYear
        {
            get
            {
                return deviceWindRetractedHoursOfYear == null ? null : new List<int>(deviceWindRetractedHoursOfYear);
            }
        }

        public int DeviceWindRetractedHours
        {
            get
            {
                return deviceWindRetractedHoursOfYear?.Count ?? 0;
            }
        }

        /// <summary>Device deployed / device requested, 0-1. NaN when nothing was requested.</summary>
        public double DeviceShadeUseFraction
        {
            get
            {
                int demand = DeviceDemandHours;
                return demand == 0 ? double.NaN : (double)DeviceDeployedHours / demand;
            }
        }

        /// <summary>Deployed hours in which the SHARED canopy was effective for at least one member, 0-based. A subset of the device deployed hours.</summary>
        public List<int> CanopyEffectiveHoursOfYear
        {
            get
            {
                return canopyEffectiveHoursOfYear == null ? null : new List<int>(canopyEffectiveHoursOfYear);
            }
        }

        public int CanopyEffectiveHours
        {
            get
            {
                return canopyEffectiveHoursOfYear?.Count ?? 0;
            }
        }

        /// <summary>Deployed hours in which the SHARED valance was effective for at least one member, 0-based. Empty when there is no valance.</summary>
        public List<int> ValanceEffectiveHoursOfYear
        {
            get
            {
                return valanceEffectiveHoursOfYear == null ? null : new List<int>(valanceEffectiveHoursOfYear);
            }
        }

        public int ValanceEffectiveHours
        {
            get
            {
                return valanceEffectiveHoursOfYear?.Count ?? 0;
            }
        }

        /// <summary>Share of the DEVICE deployed hours the valance was effective in, 0-1. NaN when nothing was deployed or there is no valance.</summary>
        public double ValanceEffectiveFraction
        {
            get
            {
                int deployed = DeviceDeployedHours;
                return deployed == 0 || !HasValance ? double.NaN : (double)ValanceEffectiveHours / deployed;
            }
        }

        private bool HasValance
        {
            get
            {
                foreach (ShadingOperationProfile member in members)
                {
                    if (member != null && !double.IsNaN(member.ValanceAttributedEnergy))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Full-year direct-beam energy attributed to the shared canopy as the first element hit, summed over the members, kWh. Not limited to deployed hours.</summary>
        public double CanopyAttributedEnergy
        {
            get
            {
                return canopyAttributedEnergy;
            }
        }

        /// <summary>Full-year direct-beam energy attributed to the shared valance as the first element hit, summed over the members, kWh. Not limited to deployed hours. Not "marginal" — see ShadingOperationProfile.ValanceAttributedEnergy.</summary>
        public double ValanceAttributedEnergy
        {
            get
            {
                return valanceAttributedEnergy;
            }
        }

        /// <summary>Sum over members of the full-year direct-beam interception of the device geometry, kWh. Not weighted by the deployment schedule.</summary>
        public double ControlledDirectSolarIntercepted
        {
            get
            {
                return performance?.DirectSolarIntercepted ?? double.NaN;
            }
        }

        /// <summary>Sum over members of the unwanted part of the intercepted beam, weighted by the deployed hours, kWh — the actual operation.</summary>
        public double ControlledUnwantedSolarIntercepted
        {
            get
            {
                return performance?.UnwantedSolarIntercepted ?? double.NaN;
            }
        }

        /// <summary>Sum over members of what the always-deployed device would have intercepted, kWh.</summary>
        public double UncontrolledUnwantedSolarIntercepted
        {
            get
            {
                double result = 0;
                bool any = false;
                foreach (ShadingOperationProfile member in members)
                {
                    if (member == null)
                    {
                        continue;
                    }

                    double value = member.UncontrolledUnwantedSolarIntercepted;
                    if (!double.IsNaN(value))
                    {
                        result += value;
                        any = true;
                    }
                }

                return any ? result : double.NaN;
            }
        }

        /// <summary>Full-year first-hit direct-beam energy by element Guid, aggregated over every member, kWh — the geometric attribution, not weighted by the deployment schedule.</summary>
        public Dictionary<Guid, double> EnergyPerElement
        {
            get
            {
                return performance == null ? new Dictionary<Guid, double>() : performance.EnergyPerElement;
            }
        }

        public override string ToString()
        {
            string valance = double.IsNaN(ValanceAttributedEnergy)
                ? "no valance"
                : string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Valance: {0:0.##} kWh intercepted | {1} h effective | {2} % of deployed hours",
                    ValanceAttributedEnergy, ValanceEffectiveHours,
                    double.IsNaN(ValanceEffectiveFraction) ? 0.0 : 100.0 * ValanceEffectiveFraction);

            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} device @ {1}: {2} h requested | {3} h deployed{4} | {5} h wind-retracted | shade use {6} % | {7} | {8}",
                typologyName ?? "device", groupGuid,
                DeviceDemandHours,
                DeviceDeployedHours,
                CanopyEffectiveHours == 0 ? string.Empty : string.Format(System.Globalization.CultureInfo.InvariantCulture, " | canopy effective {0} h", CanopyEffectiveHours),
                DeviceWindRetractedHours,
                double.IsNaN(DeviceShadeUseFraction) ? "n/a" : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.#}", 100.0 * DeviceShadeUseFraction),
                string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "controlled unwanted intercepted {0:0.##} kWh (always-deployed {1:0.##})",
                    ControlledUnwantedSolarIntercepted, UncontrolledUnwantedSolarIntercepted),
                valance);
        }

        private static void Union(HashSet<int> target, IEnumerable<int> hoursOfYear)
        {
            if (hoursOfYear == null)
            {
                return;
            }

            foreach (int hourOfYear in hoursOfYear)
            {
                target.Add(hourOfYear);
            }
        }

        private static List<int> Sorted(HashSet<int> values)
        {
            List<int> result = new List<int>(values);
            result.Sort();
            return result;
        }

        private static double Read(JsonObject jObject, string name)
        {
            return jObject.ContainsKey(name) ? (jObject[name]?.GetValue<double>() ?? double.NaN) : double.NaN;
        }

        private static List<int> HoursOfYear(JsonObject jObject, string name)
        {
            if (!jObject.ContainsKey(name))
            {
                return null;
            }

            JsonArray jArray = jObject[name] as JsonArray;
            if (jArray == null)
            {
                return null;
            }

            List<int> result = new List<int>(jArray.Count);
            foreach (JsonNode jNode in jArray)
            {
                result.Add(jNode?.GetValue<int>() ?? default);
            }

            return result;
        }

        private static void Add(JsonObject jObject, string name, List<int> hoursOfYear)
        {
            if (hoursOfYear == null)
            {
                return;
            }

            JsonArray jArray = new JsonArray();
            foreach (int hourOfYear in hoursOfYear)
            {
                jArray.Add(hourOfYear);
            }

            jObject.Add(name, jArray);
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("GroupGuid")) { Guid.TryParse(jObject["GroupGuid"]?.GetValue<string>(), out groupGuid); }
            if (jObject.ContainsKey("PanelGuid")) { Guid.TryParse(jObject["PanelGuid"]?.GetValue<string>(), out panelGuid); }
            if (jObject.ContainsKey("TypologyName")) { typologyName = jObject["TypologyName"]?.GetValue<string>(); }
            if (jObject.ContainsKey("Year")) { year = jObject["Year"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("TimeShiftInMinutes")) { timeShiftInMinutes = jObject["TimeShiftInMinutes"]?.GetValue<double>() ?? default; }

            settings = jObject.ContainsKey("Settings") ? new SolarControlSettings(jObject["Settings"] as JsonObject) : null;

            members = new List<ShadingOperationProfile>();
            if (jObject.ContainsKey("Members") && jObject["Members"] is JsonArray membersArray)
            {
                foreach (JsonNode node in membersArray)
                {
                    if (node is JsonObject memberObject)
                    {
                        members.Add(new ShadingOperationProfile(memberObject));
                    }
                }
            }

            performance = jObject.ContainsKey("Performance") ? new GroupedShadingPerformance(jObject["Performance"] as JsonObject) : null;

            deviceDemandHoursOfYear = HoursOfYear(jObject, "DeviceDemandHoursOfYear");
            deviceDeployedHoursOfYear = HoursOfYear(jObject, "DeviceDeployedHoursOfYear");
            deviceWindRetractedHoursOfYear = HoursOfYear(jObject, "DeviceWindRetractedHoursOfYear");
            canopyEffectiveHoursOfYear = HoursOfYear(jObject, "CanopyEffectiveHoursOfYear");
            valanceEffectiveHoursOfYear = HoursOfYear(jObject, "ValanceEffectiveHoursOfYear");

            canopyAttributedEnergy = Read(jObject, "CanopyAttributedEnergy");
            valanceAttributedEnergy = Read(jObject, "ValanceAttributedEnergy");

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            jObject.Add("GroupGuid", groupGuid.ToString());
            jObject.Add("PanelGuid", panelGuid.ToString());
            if (typologyName != null) { jObject.Add("TypologyName", typologyName); }
            jObject.Add("Year", year);
            jObject.Add("TimeShiftInMinutes", timeShiftInMinutes);

            if (settings != null) { jObject.Add("Settings", settings.ToJsonObject()); }

            JsonArray membersArray = new JsonArray();
            foreach (ShadingOperationProfile member in members)
            {
                membersArray.Add(member?.ToJsonObject());
            }
            jObject.Add("Members", membersArray);

            if (performance != null) { jObject.Add("Performance", performance.ToJsonObject()); }

            Add(jObject, "DeviceDemandHoursOfYear", deviceDemandHoursOfYear);
            Add(jObject, "DeviceDeployedHoursOfYear", deviceDeployedHoursOfYear);
            Add(jObject, "DeviceWindRetractedHoursOfYear", deviceWindRetractedHoursOfYear);
            Add(jObject, "CanopyEffectiveHoursOfYear", canopyEffectiveHoursOfYear);
            Add(jObject, "ValanceEffectiveHoursOfYear", valanceEffectiveHoursOfYear);

            if (!double.IsNaN(canopyAttributedEnergy)) { jObject.Add("CanopyAttributedEnergy", canopyAttributedEnergy); }
            if (!double.IsNaN(valanceAttributedEnergy)) { jObject.Add("ValanceAttributedEnergy", valanceAttributedEnergy); }

            return jObject;
        }
    }
}
