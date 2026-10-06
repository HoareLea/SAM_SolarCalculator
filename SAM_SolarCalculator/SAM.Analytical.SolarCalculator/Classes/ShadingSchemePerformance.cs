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
    /// The measured performance of a complete shading SCHEME against its whole aperture set.
    ///
    /// ACCOUNTING (modelled on GroupedShadingPerformance, whose head comment is the specification
    /// for this class). Every member aperture is measured against the SAME complete element set and
    /// keeps its own ShadingPerformance with its real aperture Guid. Scheme energies are the sums of
    /// the members; scheme percentages are computed from the summed numerators and denominators,
    /// never from averaged percentages.
    ///
    /// MATERIAL IS CHARGED ONCE PER PHYSICAL ELEMENT. The scheme material fraction is the sum of the
    /// DISTINCT scheme element areas divided by the sum of the member gross areas. Because every
    /// element carries a scheme-scoped Guid (ShadingScheme.SchemeElementGuid), a shared canopy over
    /// three apertures is counted once while three separate overhangs are counted three times, and
    /// measuring one element against several apertures never multiplies its area. Two overlapping
    /// separate devices both count in full — material is what is bought and hung, never a geometric
    /// union of shadows.
    ///
    /// THE PER-MEMBER MaterialFraction VALUES ARE DELIBERATELY DISCARDED. The shared-elements
    /// overload computes them as (whole scheme area / one window area), which is roughly N times too
    /// large and is meaningless at scheme level; the scheme material is computed once, here.
    /// </summary>
    public class ShadingSchemePerformance : IJSAMObject, ISolarObject
    {
        private List<ShadingPerformance> perAperture = new List<ShadingPerformance>();
        private double admittedDirectEnergy = double.NaN;
        private double admittedUnwantedEnergy = double.NaN;
        private double admittedWantedEnergy = double.NaN;
        private double directSolarIntercepted = double.NaN;
        private double unwantedSolarIntercepted = double.NaN;
        private double wantedSolarBlocked = double.NaN;
        private double unattributedInterceptedEnergy = double.NaN;
        private double physicalDeviceArea = double.NaN;
        private double materialFraction = double.NaN;
        private bool materialAvailable;
        private Dictionary<Guid, double> energyPerElement = new Dictionary<Guid, double>();
        private Dictionary<Guid, string> namePerElement = new Dictionary<Guid, string>();
        private Dictionary<Guid, double> energyPerPlacement = new Dictionary<Guid, double>();

        public ShadingSchemePerformance(IEnumerable<ShadingPerformance> perAperture, double physicalDeviceArea, bool materialAvailable, double totalGrossArea, Dictionary<Guid, Guid> elementOwners)
        {
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

            this.physicalDeviceArea = physicalDeviceArea;
            this.materialAvailable = materialAvailable;

            foreach (KeyValuePair<Guid, Guid> pair in elementOwners ?? new Dictionary<Guid, Guid>())
            {
                // Every placement gets a row, including a zero-energy one, so the roll-up table is
                // complete rather than populated only by the elements that happened to intercept.
                energyPerPlacement[pair.Value] = 0.0;
            }

            foreach (KeyValuePair<Guid, double> pair in energyPerElement)
            {
                if (elementOwners != null && elementOwners.TryGetValue(pair.Key, out Guid placementKey))
                {
                    energyPerPlacement.TryGetValue(placementKey, out double placementEnergy);
                    energyPerPlacement[placementKey] = placementEnergy + pair.Value;
                }
            }

            materialFraction = !materialAvailable || double.IsNaN(totalGrossArea) || totalGrossArea <= 0
                ? double.NaN
                : physicalDeviceArea / totalGrossArea;
        }

        public ShadingSchemePerformance(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>Per-member performances, one per scope aperture, each carrying its real aperture Guid.</summary>
        public List<ShadingPerformance> PerAperture { get { return new List<ShadingPerformance>(perAperture); } }

        public ShadingPerformance Performance(Guid apertureGuid)
        {
            return perAperture.Find(x => x != null && x.ApertureGuid == apertureGuid);
        }

        public List<Guid> ApertureGuids
        {
            get
            {
                List<Guid> result = new List<Guid>();
                foreach (ShadingPerformance performance in perAperture)
                {
                    result.Add(performance.ApertureGuid);
                }
                return result;
            }
        }

        /// <summary>Sum of the members' unshaded admitted direct beam, kWh.</summary>
        public double AdmittedDirectEnergy { get { return admittedDirectEnergy; } }

        /// <summary>Sum of the members' unshaded admitted UNWANTED direct beam, kWh.</summary>
        public double AdmittedUnwantedEnergy { get { return admittedUnwantedEnergy; } }

        /// <summary>Sum of the members' unshaded admitted WANTED direct beam, kWh.</summary>
        public double AdmittedWantedEnergy { get { return admittedWantedEnergy; } }

        /// <summary>Sum of the members' intercepted direct beam, kWh.</summary>
        public double DirectSolarIntercepted { get { return directSolarIntercepted; } }

        /// <summary>Sum of the members' intercepted unwanted beam, kWh — the benefit.</summary>
        public double UnwantedSolarIntercepted { get { return unwantedSolarIntercepted; } }

        /// <summary>Sum of the members' blocked wanted beam, kWh — the harm.</summary>
        public double WantedSolarBlocked { get { return wantedSolarBlocked; } }

        /// <summary>Sum of the members' first-hit residuals, kWh. Non-zero means attribution and the baseline disagree.</summary>
        public double UnattributedInterceptedEnergy { get { return unattributedInterceptedEnergy; } }

        /// <summary>Admitted direct beam the brief claimed neither way, kWh (computed residual — I4).</summary>
        public double AdmittedNeutralEnergy { get { return admittedDirectEnergy - admittedUnwantedEnergy - admittedWantedEnergy; } }

        /// <summary>Neutral part of what the scheme stopped, kWh (computed residual — I5).</summary>
        public double NeutralSolarIntercepted { get { return directSolarIntercepted - unwantedSolarIntercepted - wantedSolarBlocked; } }

        /// <summary>Total shading material: the sum of the DISTINCT scheme element areas, m2. Each physical element counted exactly once.</summary>
        public double PhysicalDeviceArea { get { return physicalDeviceArea; } }

        /// <summary>PhysicalDeviceArea / sum of member gross areas. NaN when the quantity cannot be measured.</summary>
        public double MaterialFraction { get { return materialFraction; } }

        /// <summary>True when every element area is a finite number and the denominator is usable.</summary>
        public bool MaterialAvailable { get { return materialAvailable; } }

        /// <summary>Per-element intercepted energy aggregated over every member, kWh, keyed by scheme-scoped element Guid.</summary>
        public Dictionary<Guid, double> EnergyPerElement { get { return new Dictionary<Guid, double>(energyPerElement); } }

        public string ElementName(Guid guid) { return namePerElement.TryGetValue(guid, out string name) ? name : null; }

        /// <summary>Per-placement intercepted energy, kWh, rolled up through the scheme's element owners.</summary>
        public Dictionary<Guid, double> EnergyPerPlacement { get { return new Dictionary<Guid, double>(energyPerPlacement); } }

        /// <summary>Direct Shading Efficiency [%]: scheme intercepted / scheme unshaded admitted direct. NaN when nothing is admitted.</summary>
        public double DirectShadingEfficiency { get { return Ratio(directSolarIntercepted, admittedDirectEnergy); } }

        /// <summary>Unwanted Solar Blocked [%] over the scheme, from the summed energies. NaN when there is no unwanted solar.</summary>
        public double UnwantedSolarBlocked { get { return Ratio(unwantedSolarIntercepted, admittedUnwantedEnergy); } }

        /// <summary>Wanted Solar Retained [%] over the scheme, from the summed energies. NaN when there is no wanted solar.</summary>
        public double WantedSolarRetained { get { return Ratio(admittedWantedEnergy - wantedSolarBlocked, admittedWantedEnergy); } }

        private static double Ratio(double numerator, double denominator)
        {
            // The ShadingPerformance rule, copied rather than reinvented.
            return double.IsNaN(denominator) || denominator <= 0 ? double.NaN : numerator / denominator;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

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
            physicalDeviceArea = Read(jObject, "PhysicalDeviceArea");
            materialFraction = Read(jObject, "MaterialFraction");
            if (jObject.ContainsKey("MaterialAvailable")) { materialAvailable = jObject["MaterialAvailable"]?.GetValue<bool>() ?? false; }

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

            energyPerPlacement = new Dictionary<Guid, double>();
            if (jObject.ContainsKey("Placements") && jObject["Placements"] is JsonArray placementsArray)
            {
                foreach (JsonNode node in placementsArray)
                {
                    JsonObject placement = node as JsonObject;
                    if (placement == null || !Guid.TryParse(placement["Guid"]?.GetValue<string>(), out Guid guid))
                    {
                        continue;
                    }

                    energyPerPlacement[guid] = placement["Energy"]?.GetValue<double>() ?? 0;
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

            JsonArray perApertureArray = new JsonArray();
            foreach (ShadingPerformance performance in perAperture)
            {
                perApertureArray.Add(performance?.ToJsonObject());
            }
            jObject.Add("PerAperture", perApertureArray);

            AddFinite(jObject, "AdmittedDirectEnergy", admittedDirectEnergy);
            AddFinite(jObject, "AdmittedUnwantedEnergy", admittedUnwantedEnergy);
            AddFinite(jObject, "AdmittedWantedEnergy", admittedWantedEnergy);
            AddFinite(jObject, "DirectSolarIntercepted", directSolarIntercepted);
            AddFinite(jObject, "UnwantedSolarIntercepted", unwantedSolarIntercepted);
            AddFinite(jObject, "WantedSolarBlocked", wantedSolarBlocked);
            AddFinite(jObject, "UnattributedInterceptedEnergy", unattributedInterceptedEnergy);
            AddFinite(jObject, "PhysicalDeviceArea", physicalDeviceArea);
            jObject.Add("MaterialAvailable", materialAvailable);
            AddFinite(jObject, "MaterialFraction", materialFraction);

            JsonArray elementsArray = new JsonArray();
            foreach (KeyValuePair<Guid, double> pair in energyPerElement)
            {
                JsonObject element = new JsonObject();
                element.Add("Guid", pair.Key.ToString());
                if (!double.IsNaN(pair.Value) && !double.IsInfinity(pair.Value)) { element.Add("Energy", pair.Value); }
                if (namePerElement.TryGetValue(pair.Key, out string name) && name != null)
                {
                    element.Add("Name", name);
                }

                elementsArray.Add(element);
            }
            jObject.Add("Elements", elementsArray);

            JsonArray placementsArray = new JsonArray();
            foreach (KeyValuePair<Guid, double> pair in energyPerPlacement)
            {
                JsonObject placement = new JsonObject();
                placement.Add("Guid", pair.Key.ToString());
                if (!double.IsNaN(pair.Value) && !double.IsInfinity(pair.Value)) { placement.Add("Energy", pair.Value); }
                placementsArray.Add(placement);
            }
            jObject.Add("Placements", placementsArray);

            return jObject;
        }

        /// <summary>Non-finite doubles are OMITTED from the JSON (absent = NaN on read), so the serialised form is always writable and byte-stable.</summary>
        private static void AddFinite(JsonObject jObject, string name, double value)
        {
            if (!double.IsNaN(value) && !double.IsInfinity(value))
            {
                jObject.Add(name, value);
            }
        }
    }
}
