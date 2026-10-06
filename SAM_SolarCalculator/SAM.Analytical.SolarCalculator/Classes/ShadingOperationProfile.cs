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
    /// What ONE retractable device actually DID over the weather year at ONE aperture: the deployed
    /// schedule (passed through from <see cref="SolarControlProfile"/>, never recomputed), the hours
    /// each physical element was effective in, and the energy the device intercepted.
    ///
    /// THE RESULT CARRIES THE DESIGN IT WAS MEASURED FOR. The aperture Guid, the typology name, the
    /// device parameters and the <see cref="SolarControlSettings"/> are all stored, so a profile can
    /// never be read against the wrong design — the same discipline as a ShadingDevice carrying its
    /// aperture Guid.
    ///
    /// THE SCHEDULE AND THE ENERGY SIT ON ONE TIMELINE. Year and TimeShiftInMinutes are recorded and
    /// the creation path refuses a profile whose timeline disagrees with the visibility cache, so
    /// the deployed hours and the intercepted energy always describe the same sun.
    ///
    /// NAMING, DELIBERATELY:
    ///   DeployedHoursOfYear          the device is OUT (requested AND wind-safe)
    ///   WindRetractedHoursOfYear     requested and refused by wind
    ///   CanopyEffectiveHoursOfYear   the canopy first-hit at least one lit cell in those hours
    ///   ValanceEffectiveHoursOfYear  the valance first-hit at least one lit cell in those hours
    ///
    /// CANOPY- AND VALANCE-EFFECTIVE HOURS ARE NOT MUTUALLY EXCLUSIVE. Within one hour some cells
    /// may first-hit the canopy while others first-hit the valance; first-hit exclusivity holds per
    /// traced (bin, cell) ray, not per hour. Each set is a subset of the deployed hours.
    ///
    /// <see cref="ValanceAttributedEnergy"/> is what the first-hit accounting credits the valance
    /// in the CURRENT geometry. It is deliberately NOT called marginal: it proves nothing about the
    /// same device with the valance removed — that comparison is a later, separate analysis.
    ///
    /// THREE KINDS OF ENERGY, KEPT APART:
    ///
    ///   GEOMETRIC / FULL-YEAR — the device geometry intercepts this beam regardless of when it was
    ///   actually out; the deployed schedule does NOT gate these:
    ///     <see cref="ControlledDirectSolarIntercepted"/> — full-year direct-beam interception;
    ///     <see cref="CanopyAttributedEnergy"/> — full-year direct beam on the canopy first hit;
    ///     <see cref="ValanceAttributedEnergy"/> — full-year direct beam on the valance first hit;
    ///     <see cref="EnergyPerElement"/> — the same full-year direct beam, per element Guid.
    ///
    ///   OPERATION-WEIGHTED — weighted by the DEPLOYED hours (the actual annual operation):
    ///     <see cref="ControlledUnwantedSolarIntercepted"/>.
    ///
    ///   REFERENCE / DEMAND-WEIGHTED — weighted by the REQUESTED hours:
    ///     <see cref="UncontrolledUnwantedSolarIntercepted"/>.
    ///
    /// The direct channel is deliberately unweighted: it is the neutral-beam accounting the existing
    /// ShadingPerformance pipeline owns, and only the unwanted channel carries the deployment weight.
    /// To quantify what the wind retraction costs, compare
    /// <see cref="ControlledUnwantedSolarIntercepted"/> against
    /// <see cref="UncontrolledUnwantedSolarIntercepted"/> — never the direct/element figures.
    ///
    /// NaN MEANS "NOT RECORDED / NOT APPLICABLE": zero deployed hours make ValanceEffectiveFraction
    /// NaN, a device without a valance reports no valance Guid and no valance energy, and JSON omits
    /// the keys rather than writing NaN.
    /// </summary>
    public class ShadingOperationProfile : IJSAMObject, ISolarObject
    {
        private Guid apertureGuid;
        private string typologyName;
        private Dictionary<string, double> deviceParameters;
        private SolarControlSettings settings;
        private int year;
        private double timeShiftInMinutes;
        private List<int> deployedHoursOfYear;
        private List<int> windRetractedHoursOfYear;
        private double shadeUseFraction = double.NaN;
        private Guid canopyGuid;
        private Guid valanceGuid;
        private Dictionary<Guid, List<int>> effectiveHoursOfYearPerElement;
        private double canopyAttributedEnergy = double.NaN;
        private double valanceAttributedEnergy = double.NaN;
        private double controlledDirectSolarIntercepted = double.NaN;
        private double controlledUnwantedSolarIntercepted = double.NaN;
        private double uncontrolledUnwantedSolarIntercepted = double.NaN;
        private Dictionary<Guid, double> energyPerElement;

        /// <param name="apertureGuid">The aperture this operation was measured at.</param>
        /// <param name="typologyName">The device family name.</param>
        /// <param name="deviceParameters">The device parameters it was operated under.</param>
        /// <param name="settings">The control rule, including the wind limit.</param>
        /// <param name="year">The weather year every hour of the year is counted in.</param>
        /// <param name="timeShiftInMinutes">Sun-position sampling offset of the timeline, minutes.</param>
        /// <param name="deployedHoursOfYear">Hours the device was actually deployed, 0-based. Passed through from the control profile.</param>
        /// <param name="windRetractedHoursOfYear">Requested hours refused by wind, 0-based. Passed through.</param>
        /// <param name="shadeUseFraction">Deployed / requested. NaN when nothing was requested. Passed through.</param>
        /// <param name="canopyGuid">The canopy element Guid; Guid.Empty when the device has none.</param>
        /// <param name="valanceGuid">The valance element Guid; Guid.Empty when the device has none.</param>
        /// <param name="effectiveHoursOfYearPerElement">Deployed hours each element first-hit at least one lit cell in, by element Guid.</param>
        /// <param name="canopyAttributedEnergy">Full-year direct-beam energy attributed to the canopy as the first element hit, kWh. Not limited to deployed hours.</param>
        /// <param name="valanceAttributedEnergy">Full-year direct-beam energy attributed to the valance as the first element hit, kWh. NaN when there is no valance. Not limited to deployed hours.</param>
        /// <param name="controlledDirectSolarIntercepted">Full-year direct-beam interception of the device geometry, kWh. Not weighted by the deployment schedule.</param>
        /// <param name="controlledUnwantedSolarIntercepted">Unwanted part of the intercepted beam, weighted by the deployed hours, kWh — the actual operation.</param>
        /// <param name="uncontrolledUnwantedSolarIntercepted">What the SAME device would have intercepted if always deployed on demand, kWh — the reference the control is measured against.</param>
        /// <param name="energyPerElement">Full-year first-hit direct-beam energy by element Guid, kWh. Not weighted by the deployment schedule.</param>
        public ShadingOperationProfile(Guid apertureGuid, string typologyName, IDictionary<string, double> deviceParameters, SolarControlSettings settings, int year, double timeShiftInMinutes, IEnumerable<int> deployedHoursOfYear, IEnumerable<int> windRetractedHoursOfYear, double shadeUseFraction, Guid canopyGuid, Guid valanceGuid, IDictionary<Guid, List<int>> effectiveHoursOfYearPerElement, double canopyAttributedEnergy, double valanceAttributedEnergy, double controlledDirectSolarIntercepted, double controlledUnwantedSolarIntercepted, double uncontrolledUnwantedSolarIntercepted, IDictionary<Guid, double> energyPerElement)
        {
            this.apertureGuid = apertureGuid;
            this.typologyName = typologyName;
            this.deviceParameters = deviceParameters == null ? new Dictionary<string, double>() : new Dictionary<string, double>(deviceParameters);
            this.settings = settings == null ? null : new SolarControlSettings(settings);
            this.year = year;
            this.timeShiftInMinutes = timeShiftInMinutes;
            this.deployedHoursOfYear = deployedHoursOfYear == null ? null : new List<int>(deployedHoursOfYear);
            this.windRetractedHoursOfYear = windRetractedHoursOfYear == null ? null : new List<int>(windRetractedHoursOfYear);
            this.shadeUseFraction = shadeUseFraction;
            this.canopyGuid = canopyGuid;
            this.valanceGuid = valanceGuid;
            this.effectiveHoursOfYearPerElement = CopyHours(effectiveHoursOfYearPerElement);
            this.canopyAttributedEnergy = canopyAttributedEnergy;
            this.valanceAttributedEnergy = valanceAttributedEnergy;
            this.controlledDirectSolarIntercepted = controlledDirectSolarIntercepted;
            this.controlledUnwantedSolarIntercepted = controlledUnwantedSolarIntercepted;
            this.uncontrolledUnwantedSolarIntercepted = uncontrolledUnwantedSolarIntercepted;
            this.energyPerElement = energyPerElement == null ? new Dictionary<Guid, double>() : new Dictionary<Guid, double>(energyPerElement);
        }

        public ShadingOperationProfile(ShadingOperationProfile shadingOperationProfile)
        {
            if (shadingOperationProfile != null)
            {
                apertureGuid = shadingOperationProfile.apertureGuid;
                typologyName = shadingOperationProfile.typologyName;
                deviceParameters = shadingOperationProfile.deviceParameters == null ? null : new Dictionary<string, double>(shadingOperationProfile.deviceParameters);
                settings = shadingOperationProfile.settings == null ? null : new SolarControlSettings(shadingOperationProfile.settings);
                year = shadingOperationProfile.year;
                timeShiftInMinutes = shadingOperationProfile.timeShiftInMinutes;
                deployedHoursOfYear = shadingOperationProfile.deployedHoursOfYear == null ? null : new List<int>(shadingOperationProfile.deployedHoursOfYear);
                windRetractedHoursOfYear = shadingOperationProfile.windRetractedHoursOfYear == null ? null : new List<int>(shadingOperationProfile.windRetractedHoursOfYear);
                shadeUseFraction = shadingOperationProfile.shadeUseFraction;
                canopyGuid = shadingOperationProfile.canopyGuid;
                valanceGuid = shadingOperationProfile.valanceGuid;
                effectiveHoursOfYearPerElement = CopyHours(shadingOperationProfile.effectiveHoursOfYearPerElement);
                canopyAttributedEnergy = shadingOperationProfile.canopyAttributedEnergy;
                valanceAttributedEnergy = shadingOperationProfile.valanceAttributedEnergy;
                controlledDirectSolarIntercepted = shadingOperationProfile.controlledDirectSolarIntercepted;
                controlledUnwantedSolarIntercepted = shadingOperationProfile.controlledUnwantedSolarIntercepted;
                uncontrolledUnwantedSolarIntercepted = shadingOperationProfile.uncontrolledUnwantedSolarIntercepted;
                energyPerElement = shadingOperationProfile.energyPerElement == null ? null : new Dictionary<Guid, double>(shadingOperationProfile.energyPerElement);
            }
        }

        public ShadingOperationProfile(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>The aperture this operation was measured at.</summary>
        public Guid ApertureGuid
        {
            get
            {
                return apertureGuid;
            }
        }

        public string TypologyName
        {
            get
            {
                return typologyName;
            }
        }

        /// <summary>The device parameters it was operated under. Defensive copy.</summary>
        public Dictionary<string, double> DeviceParameters
        {
            get
            {
                return deviceParameters == null ? null : new Dictionary<string, double>(deviceParameters);
            }
        }

        /// <summary>The control rule, including the wind limit. Defensive copy.</summary>
        public SolarControlSettings Settings
        {
            get
            {
                return settings == null ? null : new SolarControlSettings(settings);
            }
        }

        public int Year
        {
            get
            {
                return year;
            }
        }

        /// <summary>Sun-position sampling offset of the timeline, minutes.</summary>
        public double TimeShiftInMinutes
        {
            get
            {
                return timeShiftInMinutes;
            }
        }

        /// <summary>Hours the device was ACTUALLY deployed, 0-based — the operating schedule, passed through from the control profile.</summary>
        public List<int> DeployedHoursOfYear
        {
            get
            {
                return deployedHoursOfYear == null ? null : new List<int>(deployedHoursOfYear);
            }
        }

        public int DeployedHours
        {
            get
            {
                return deployedHoursOfYear?.Count ?? 0;
            }
        }

        /// <summary>Requested hours the wind constraint refused, 0-based. Passed through from the control profile.</summary>
        public List<int> WindRetractedHoursOfYear
        {
            get
            {
                return windRetractedHoursOfYear == null ? null : new List<int>(windRetractedHoursOfYear);
            }
        }

        public int WindRetractedHours
        {
            get
            {
                return windRetractedHoursOfYear?.Count ?? 0;
            }
        }

        /// <summary>Deployed / requested, 0-1. NaN when nothing was requested. Passed through.</summary>
        public double ShadeUseFraction
        {
            get
            {
                return shadeUseFraction;
            }
        }

        /// <summary>Hours the canopy first-hit at least one lit cell in, 0-based. Empty when the device has no canopy. A subset of the deployed hours.</summary>
        public List<int> CanopyEffectiveHoursOfYear
        {
            get
            {
                return EffectiveHoursOfYear(canopyGuid) ?? new List<int>();
            }
        }

        public int CanopyEffectiveHours
        {
            get
            {
                return EffectiveHours(canopyGuid);
            }
        }

        /// <summary>Hours the valance first-hit at least one lit cell in, 0-based. Empty when there is no valance. A subset of the deployed hours.</summary>
        public List<int> ValanceEffectiveHoursOfYear
        {
            get
            {
                return EffectiveHoursOfYear(valanceGuid) ?? new List<int>();
            }
        }

        public int ValanceEffectiveHours
        {
            get
            {
                return EffectiveHours(valanceGuid);
            }
        }

        /// <summary>
        /// Share of the DEPLOYED hours the valance was effective in, 0-1. NaN when nothing was
        /// deployed or there is no valance — it must never read as 0 % in those cases.
        /// </summary>
        public double ValanceEffectiveFraction
        {
            get
            {
                int deployed = DeployedHours;
                return deployed == 0 || valanceGuid == Guid.Empty ? double.NaN : (double)ValanceEffectiveHours / deployed;
            }
        }

        /// <summary>The deployed hours the given element first-hit at least one lit cell in, 0-based. Null when unknown.</summary>
        public List<int> EffectiveHoursOfYear(Guid elementGuid)
        {
            return effectiveHoursOfYearPerElement == null || !effectiveHoursOfYearPerElement.TryGetValue(elementGuid, out List<int> hours) || hours == null
                ? null
                : new List<int>(hours);
        }

        private int EffectiveHours(Guid elementGuid)
        {
            return effectiveHoursOfYearPerElement == null || !effectiveHoursOfYearPerElement.TryGetValue(elementGuid, out List<int> hours) || hours == null
                ? 0
                : hours.Count;
        }

        /// <summary>
        /// Full-year direct-beam energy attributed to the CANOPY as the first shading element hit,
        /// kWh. A geometric attribution metric: it is not limited to deployed hours, so a wind
        /// retraction does not move it. See <see cref="ControlledUnwantedSolarIntercepted"/> for the
        /// deployed-hour operational effect.
        /// </summary>
        public double CanopyAttributedEnergy
        {
            get
            {
                return canopyAttributedEnergy;
            }
        }

        /// <summary>
        /// Full-year direct-beam energy attributed to the VALANCE as the first shading element hit,
        /// kWh. A geometric attribution metric: it is not limited to deployed hours. NaN when there
        /// is no valance.
        ///
        /// Deliberately NOT "marginal": FirstHitGuid == valance proves attribution in this geometry,
        /// not what happens to the ray with the valance physically removed.
        /// </summary>
        public double ValanceAttributedEnergy
        {
            get
            {
                return valanceAttributedEnergy;
            }
        }

        /// <summary>
        /// Full-year direct-beam interception for the device geometry, kWh. This direct channel is
        /// NOT weighted by the deployment schedule; use
        /// <see cref="ControlledUnwantedSolarIntercepted"/> to quantify the deployed-hour operational
        /// effect.
        /// </summary>
        public double ControlledDirectSolarIntercepted
        {
            get
            {
                return controlledDirectSolarIntercepted;
            }
        }

        /// <summary>Unwanted part of the intercepted beam, weighted by the DEPLOYED hours, kWh — the actual operation and the benefit of the control.</summary>
        public double ControlledUnwantedSolarIntercepted
        {
            get
            {
                return controlledUnwantedSolarIntercepted;
            }
        }

        /// <summary>
        /// What the SAME device would have intercepted if it were always deployed whenever shading
        /// was requested, kWh — the always-deployed reference. The difference against
        /// <see cref="ControlledUnwantedSolarIntercepted"/> is what the wind retraction costs.
        /// </summary>
        public double UncontrolledUnwantedSolarIntercepted
        {
            get
            {
                return uncontrolledUnwantedSolarIntercepted;
            }
        }

        /// <summary>Full-year first-hit direct-beam energy by element Guid, kWh — the geometric attribution, not weighted by the deployment schedule. Defensive copy.</summary>
        public Dictionary<Guid, double> EnergyPerElement
        {
            get
            {
                return energyPerElement == null ? null : new Dictionary<Guid, double>(energyPerElement);
            }
        }

        public override string ToString()
        {
            string valance = valanceGuid == Guid.Empty
                ? "no valance"
                : string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Valance: {0:0.##} kWh intercepted | {1} h effective | {2} % of deployed hours",
                    valanceAttributedEnergy, ValanceEffectiveHours,
                    double.IsNaN(ValanceEffectiveFraction) ? 0.0 : 100.0 * ValanceEffectiveFraction);

            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} @ {1}: {2} h deployed{3} | {4} h wind-retracted | shade use {5} % | {6} | {7}",
                typologyName ?? "device", apertureGuid,
                DeployedHours,
                WindRetractedHours == 0 ? string.Empty : string.Format(System.Globalization.CultureInfo.InvariantCulture, " of {0} requested", DeployedHours + WindRetractedHours),
                WindRetractedHours,
                double.IsNaN(shadeUseFraction) ? "n/a" : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.#}", 100.0 * shadeUseFraction),
                string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "controlled unwanted intercepted {0:0.##} kWh (always-deployed {1:0.##})",
                    controlledUnwantedSolarIntercepted, uncontrolledUnwantedSolarIntercepted),
                valance);
        }

        private static Dictionary<Guid, List<int>> CopyHours(IDictionary<Guid, List<int>> hoursPerElement)
        {
            if (hoursPerElement == null)
            {
                return null;
            }

            Dictionary<Guid, List<int>> result = new Dictionary<Guid, List<int>>();
            foreach (KeyValuePair<Guid, List<int>> pair in hoursPerElement)
            {
                result[pair.Key] = pair.Value == null ? null : new List<int>(pair.Value);
            }

            return result;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("ApertureGuid")) { Guid.TryParse(jObject["ApertureGuid"]?.GetValue<string>(), out apertureGuid); }
            if (jObject.ContainsKey("TypologyName")) { typologyName = jObject["TypologyName"]?.GetValue<string>(); }
            if (jObject.ContainsKey("Year")) { year = jObject["Year"]?.GetValue<int>() ?? default; }
            if (jObject.ContainsKey("TimeShiftInMinutes")) { timeShiftInMinutes = jObject["TimeShiftInMinutes"]?.GetValue<double>() ?? default; }

            settings = jObject.ContainsKey("Settings") ? new SolarControlSettings(jObject["Settings"] as JsonObject) : null;

            deviceParameters = new Dictionary<string, double>();
            if (jObject.ContainsKey("DeviceParameters") && jObject["DeviceParameters"] is JsonObject deviceParametersObject)
            {
                foreach (KeyValuePair<string, JsonNode> pair in deviceParametersObject)
                {
                    deviceParameters[pair.Key] = pair.Value?.GetValue<double>() ?? double.NaN;
                }
            }

            deployedHoursOfYear = HoursOfYear(jObject, "DeployedHoursOfYear");
            windRetractedHoursOfYear = HoursOfYear(jObject, "WindRetractedHoursOfYear");
            shadeUseFraction = Read(jObject, "ShadeUseFraction");

            if (jObject.ContainsKey("CanopyGuid")) { Guid.TryParse(jObject["CanopyGuid"]?.GetValue<string>(), out canopyGuid); }
            if (jObject.ContainsKey("ValanceGuid")) { Guid.TryParse(jObject["ValanceGuid"]?.GetValue<string>(), out valanceGuid); }

            effectiveHoursOfYearPerElement = new Dictionary<Guid, List<int>>();
            if (jObject.ContainsKey("ElementEffectiveHours") && jObject["ElementEffectiveHours"] is JsonArray effectiveHoursArray)
            {
                foreach (JsonNode node in effectiveHoursArray)
                {
                    JsonObject element = node as JsonObject;
                    if (element == null || !Guid.TryParse(element["Guid"]?.GetValue<string>(), out Guid guid))
                    {
                        continue;
                    }

                    effectiveHoursOfYearPerElement[guid] = HoursOfYear(element, "HoursOfYear");
                }
            }

            canopyAttributedEnergy = Read(jObject, "CanopyAttributedEnergy");
            valanceAttributedEnergy = Read(jObject, "ValanceAttributedEnergy");
            controlledDirectSolarIntercepted = Read(jObject, "ControlledDirectSolarIntercepted");
            controlledUnwantedSolarIntercepted = Read(jObject, "ControlledUnwantedSolarIntercepted");
            uncontrolledUnwantedSolarIntercepted = Read(jObject, "UncontrolledUnwantedSolarIntercepted");

            energyPerElement = new Dictionary<Guid, double>();
            if (jObject.ContainsKey("EnergyPerElement") && jObject["EnergyPerElement"] is JsonObject energyObject)
            {
                foreach (KeyValuePair<string, JsonNode> pair in energyObject)
                {
                    if (Guid.TryParse(pair.Key, out Guid guid))
                    {
                        energyPerElement[guid] = pair.Value?.GetValue<double>() ?? 0.0;
                    }
                }
            }

            return true;
        }

        private static double Read(JsonObject jObject, string name)
        {
            // Absent means "not recorded", exactly what NaN means in memory. NaN is never written.
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

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            jObject.Add("ApertureGuid", apertureGuid.ToString());
            if (typologyName != null) { jObject.Add("TypologyName", typologyName); }
            jObject.Add("Year", year);
            jObject.Add("TimeShiftInMinutes", timeShiftInMinutes);

            if (settings != null) { jObject.Add("Settings", settings.ToJsonObject()); }

            if (deviceParameters != null)
            {
                JsonObject deviceParametersObject = new JsonObject();
                foreach (KeyValuePair<string, double> pair in deviceParameters)
                {
                    if (!double.IsNaN(pair.Value))
                    {
                        deviceParametersObject.Add(pair.Key, pair.Value);
                    }
                }
                jObject.Add("DeviceParameters", deviceParametersObject);
            }

            Add(jObject, "DeployedHoursOfYear", deployedHoursOfYear);
            Add(jObject, "WindRetractedHoursOfYear", windRetractedHoursOfYear);

            // NaN is not written: it is not a JSON number, and its meaning — "not recorded / not
            // applicable" — is carried by the key being absent.
            if (!double.IsNaN(shadeUseFraction)) { jObject.Add("ShadeUseFraction", shadeUseFraction); }

            if (canopyGuid != Guid.Empty) { jObject.Add("CanopyGuid", canopyGuid.ToString()); }
            if (valanceGuid != Guid.Empty) { jObject.Add("ValanceGuid", valanceGuid.ToString()); }

            if (effectiveHoursOfYearPerElement != null)
            {
                JsonArray effectiveHoursArray = new JsonArray();
                foreach (KeyValuePair<Guid, List<int>> pair in effectiveHoursOfYearPerElement)
                {
                    JsonObject element = new JsonObject();
                    element.Add("Guid", pair.Key.ToString());
                    Add(element, "HoursOfYear", pair.Value);
                    effectiveHoursArray.Add(element);
                }
                jObject.Add("ElementEffectiveHours", effectiveHoursArray);
            }

            if (!double.IsNaN(canopyAttributedEnergy)) { jObject.Add("CanopyAttributedEnergy", canopyAttributedEnergy); }
            if (!double.IsNaN(valanceAttributedEnergy)) { jObject.Add("ValanceAttributedEnergy", valanceAttributedEnergy); }
            if (!double.IsNaN(controlledDirectSolarIntercepted)) { jObject.Add("ControlledDirectSolarIntercepted", controlledDirectSolarIntercepted); }
            if (!double.IsNaN(controlledUnwantedSolarIntercepted)) { jObject.Add("ControlledUnwantedSolarIntercepted", controlledUnwantedSolarIntercepted); }
            if (!double.IsNaN(uncontrolledUnwantedSolarIntercepted)) { jObject.Add("UncontrolledUnwantedSolarIntercepted", uncontrolledUnwantedSolarIntercepted); }

            if (energyPerElement != null)
            {
                JsonObject energyObject = new JsonObject();
                foreach (KeyValuePair<Guid, double> pair in energyPerElement)
                {
                    energyObject.Add(pair.Key.ToString(), pair.Value);
                }
                jObject.Add("EnergyPerElement", energyObject);
            }

            return jObject;
        }
    }
}
