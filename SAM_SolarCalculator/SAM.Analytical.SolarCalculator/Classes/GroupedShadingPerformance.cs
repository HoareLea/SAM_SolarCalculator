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
    /// The measured performance of ONE shared shading device against a GROUP of apertures.
    ///
    /// ACCOUNTING. Every member aperture is measured against the SAME element set (same Guids) and
    /// keeps its own ShadingPerformance with its real aperture Guid and its correct shared-cache
    /// offset. Group energies are the SUM of the members — group percentages are computed from the
    /// summed numerators and denominators, never from averaged percentages.
    ///
    /// MATERIAL IS CHARGED ONCE. The device is one physical awning, so the material quantity is one
    /// shared element area; the group material fraction is that area divided by the SUM of the
    /// member gross aperture areas. Summing independently calculated per-window material costs for
    /// the same shared plane would double- and triple-charge one piece of fabric.
    ///
    /// The null device (NoShading) measures the unshaded baseline for every member and scores zero,
    /// exactly as in the single-aperture path; a group that could not be measured at all is a
    /// failure and is reported separately — "not evaluated" is never "no shade".
    /// </summary>
    public class GroupedShadingPerformance : IJSAMObject, ISolarObject
    {
        private Guid groupGuid;
        private Guid panelGuid;
        private string typologyName;
        private List<ShadingPerformance> perAperture = new List<ShadingPerformance>();
        private double admittedDirectEnergy = double.NaN;
        private double admittedUnwantedEnergy = double.NaN;
        private double admittedWantedEnergy = double.NaN;
        private double directSolarIntercepted = double.NaN;
        private double unwantedSolarIntercepted = double.NaN;
        private double wantedSolarBlocked = double.NaN;
        private double unattributedInterceptedEnergy = double.NaN;
        private double sharedDeviceArea = double.NaN;
        private double materialFraction = double.NaN;
        private Dictionary<Guid, double> energyPerElement = new Dictionary<Guid, double>();
        private Dictionary<Guid, string> namePerElement = new Dictionary<Guid, string>();

        public GroupedShadingPerformance(Guid groupGuid, Guid panelGuid, string typologyName, IEnumerable<ShadingPerformance> perAperture, double sharedDeviceArea, double totalGrossArea)
        {
            this.groupGuid = groupGuid;
            this.panelGuid = panelGuid;
            this.typologyName = typologyName;

            double admittedDirect = 0, admittedUnwanted = 0, admittedWanted = 0;
            double interceptedDirect = 0, interceptedUnwanted = 0, blockedWanted = 0, unattributed = 0;

            foreach (ShadingPerformance performance in perAperture ?? new List<ShadingPerformance>())
            {
                if (performance == null)
                {
                    continue;
                }

                this.perAperture.Add(performance);

                admittedDirect += performance.AdmittedDirectEnergy;
                admittedUnwanted += performance.AdmittedUnwantedEnergy;
                admittedWanted += performance.AdmittedWantedEnergy;
                interceptedDirect += performance.DirectSolarIntercepted;
                interceptedUnwanted += performance.UnwantedSolarIntercepted;
                blockedWanted += performance.WantedSolarBlocked;
                unattributed += performance.UnattributedInterceptedEnergy;

                foreach (KeyValuePair<Guid, double> pair in performance.EnergyPerElement)
                {
                    energyPerElement.TryGetValue(pair.Key, out double energy);
                    energyPerElement[pair.Key] = energy + pair.Value;
                    if (!namePerElement.ContainsKey(pair.Key))
                    {
                        namePerElement[pair.Key] = performance.ElementName(pair.Key);
                    }
                }
            }

            admittedDirectEnergy = admittedDirect;
            admittedUnwantedEnergy = admittedUnwanted;
            admittedWantedEnergy = admittedWanted;
            directSolarIntercepted = interceptedDirect;
            unwantedSolarIntercepted = interceptedUnwanted;
            wantedSolarBlocked = blockedWanted;
            unattributedInterceptedEnergy = unattributed;

            this.sharedDeviceArea = sharedDeviceArea;
            materialFraction = double.IsNaN(totalGrossArea) || totalGrossArea <= 0 ? double.NaN : sharedDeviceArea / totalGrossArea;
        }

        public GroupedShadingPerformance(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public Guid GroupGuid { get { return groupGuid; } }

        public Guid PanelGuid { get { return panelGuid; } }

        public string TypologyName { get { return typologyName; } }

        /// <summary>Per-member performances in member order, each carrying its real aperture Guid.</summary>
        public List<ShadingPerformance> PerAperture { get { return new List<ShadingPerformance>(perAperture); } }

        public ShadingPerformance Performance(Guid apertureGuid)
        {
            return perAperture.Find(x => x != null && x.ApertureGuid == apertureGuid);
        }

        /// <summary>Sum of the members' unshaded admitted direct beam, kWh.</summary>
        public double AdmittedDirectEnergy { get { return admittedDirectEnergy; } }

        /// <summary>Sum of the members' unshaded admitted UNWANTED direct beam, kWh.</summary>
        public double AdmittedUnwantedEnergy { get { return admittedUnwantedEnergy; } }

        /// <summary>Sum of the members' unshaded admitted WANTED direct beam, kWh.</summary>
        public double AdmittedWantedEnergy { get { return admittedWantedEnergy; } }

        /// <summary>Sum of the members' intercepted direct beam, kWh.</summary>
        public double DirectSolarIntercepted { get { return directSolarIntercepted; } }

        /// <summary>Sum of the members' intercepted unwanted beam, kWh.</summary>
        public double UnwantedSolarIntercepted { get { return unwantedSolarIntercepted; } }

        /// <summary>Sum of the members' blocked wanted beam, kWh.</summary>
        public double WantedSolarBlocked { get { return wantedSolarBlocked; } }

        /// <summary>Sum of the members' first-hit residuals, kWh. Non-zero means attribution and the baseline disagree.</summary>
        public double UnattributedInterceptedEnergy { get { return unattributedInterceptedEnergy; } }

        /// <summary>Admitted direct beam the brief claimed neither way, kWh.</summary>
        public double AdmittedNeutralEnergy { get { return admittedDirectEnergy - admittedUnwantedEnergy - admittedWantedEnergy; } }

        /// <summary>Neutral part of what the device stopped, kWh.</summary>
        public double NeutralSolarIntercepted { get { return directSolarIntercepted - unwantedSolarIntercepted - wantedSolarBlocked; } }

        /// <summary>The ONE shared device area, m2 — charged once however many apertures it spans.</summary>
        public double SharedDeviceArea { get { return sharedDeviceArea; } }

        /// <summary>Shared device area / sum of member gross aperture areas. NaN when the denominator is not usable.</summary>
        public double MaterialFraction { get { return materialFraction; } }

        /// <summary>Per-element intercepted energy aggregated over every member, kWh — the same element Guid means the same piece of fabric.</summary>
        public Dictionary<Guid, double> EnergyPerElement { get { return new Dictionary<Guid, double>(energyPerElement); } }

        public string ElementName(Guid guid) { return namePerElement.TryGetValue(guid, out string name) ? name : null; }

        /// <summary>Direct Shading Efficiency [%]: group intercepted / group unshaded admitted direct. NaN when nothing is admitted.</summary>
        public double DirectShadingEfficiency { get { return Ratio(directSolarIntercepted, admittedDirectEnergy); } }

        /// <summary>Unwanted Solar Blocked [%] over the group, from the summed energies. NaN when there is no unwanted solar.</summary>
        public double UnwantedSolarBlocked { get { return Ratio(unwantedSolarIntercepted, admittedUnwantedEnergy); } }

        /// <summary>Wanted Solar Retained [%] over the group, from the summed energies. NaN when there is no wanted solar.</summary>
        public double WantedSolarRetained { get { return Ratio(admittedWantedEnergy - wantedSolarBlocked, admittedWantedEnergy); } }

        private static double Ratio(double numerator, double denominator)
        {
            return double.IsNaN(denominator) || denominator <= 0 ? double.NaN : numerator / denominator;
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

            perAperture = new List<ShadingPerformance>();
            if (jObject.ContainsKey("PerAperture") && jObject["PerAperture"] is JsonArray perApertureArray)
            {
                foreach (JsonNode node in perApertureArray)
                {
                    if (node is JsonObject performanceObject)
                    {
                        perAperture.Add(new ShadingPerformance(performanceObject));
                    }
                }
            }

            admittedDirectEnergy = Read(jObject, "AdmittedDirectEnergy");
            admittedUnwantedEnergy = Read(jObject, "AdmittedUnwantedEnergy");
            admittedWantedEnergy = Read(jObject, "AdmittedWantedEnergy");
            directSolarIntercepted = Read(jObject, "DirectSolarIntercepted");
            unwantedSolarIntercepted = Read(jObject, "UnwantedSolarIntercepted");
            wantedSolarBlocked = Read(jObject, "WantedSolarBlocked");
            unattributedInterceptedEnergy = Read(jObject, "UnattributedInterceptedEnergy");
            sharedDeviceArea = Read(jObject, "SharedDeviceArea");
            materialFraction = Read(jObject, "MaterialFraction");

            energyPerElement = new Dictionary<Guid, double>();
            namePerElement = new Dictionary<Guid, string>();
            if (jObject.ContainsKey("Elements") && jObject["Elements"] is JsonArray elementsArray)
            {
                foreach (JsonNode node in elementsArray)
                {
                    JsonObject element = node as JsonObject;
                    if (element == null || !Guid.TryParse(element["Guid"]?.GetValue<string>(), out Guid guid))
                    {
                        continue;
                    }

                    energyPerElement[guid] = element["Energy"]?.GetValue<double>() ?? 0;
                    namePerElement[guid] = element["Name"]?.GetValue<string>();
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
            if (typologyName != null) { jObject.Add("TypologyName", typologyName); }

            JsonArray perApertureArray = new JsonArray();
            foreach (ShadingPerformance performance in perAperture)
            {
                perApertureArray.Add(performance?.ToJsonObject());
            }
            jObject.Add("PerAperture", perApertureArray);

            jObject.Add("AdmittedDirectEnergy", admittedDirectEnergy);
            jObject.Add("AdmittedUnwantedEnergy", admittedUnwantedEnergy);
            jObject.Add("AdmittedWantedEnergy", admittedWantedEnergy);
            jObject.Add("DirectSolarIntercepted", directSolarIntercepted);
            jObject.Add("UnwantedSolarIntercepted", unwantedSolarIntercepted);
            jObject.Add("WantedSolarBlocked", wantedSolarBlocked);
            jObject.Add("UnattributedInterceptedEnergy", unattributedInterceptedEnergy);
            jObject.Add("SharedDeviceArea", sharedDeviceArea);
            jObject.Add("MaterialFraction", materialFraction);

            JsonArray elementsArray = new JsonArray();
            foreach (KeyValuePair<Guid, double> pair in energyPerElement)
            {
                JsonObject element = new JsonObject();
                element.Add("Guid", pair.Key.ToString());
                element.Add("Energy", pair.Value);
                if (namePerElement.TryGetValue(pair.Key, out string name) && name != null)
                {
                    element.Add("Name", name);
                }

                elementsArray.Add(element);
            }
            jObject.Add("Elements", elementsArray);

            return jObject;
        }
    }
}
