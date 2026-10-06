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
    /// Stage 8 performance of one shading proposal against one aperture. All quantities are
    /// ENERGY-weighted (kWh, from Stage 5's kWh/m2 times analysis-cell area). Ray counts are never
    /// used as a percentage: a ray at grazing incidence in December and a ray at normal incidence
    /// in June are not interchangeable, and counting them would flatter any device that blocks a
    /// lot of weak sun.
    ///
    /// The baseline is the UNSHADED ADMITTED state: the direct beam that reaches the aperture with
    /// existing context in place but without the candidate. Only rays admitted in that state can be
    /// credited to the candidate, so a device gets nothing for "blocking" sun an adjacent building
    /// already blocked.
    ///
    /// Every percentage has an explicit unavailable result. When a denominator is zero — no wanted
    /// solar in the weighting, no admitted beam at all — the metric is NaN, never a misleading 0 %
    /// or 100 %.
    /// </summary>
    public class ShadingPerformance : IJSAMObject, ISolarObject
    {
        private Guid apertureGuid;
        private string typologyName;
        private double admittedDirectEnergy = double.NaN;
        private double admittedUnwantedEnergy = double.NaN;
        private double admittedWantedEnergy = double.NaN;
        private double directSolarIntercepted = double.NaN;
        private double unwantedSolarIntercepted = double.NaN;
        private double wantedSolarBlocked = double.NaN;
        private double unattributedInterceptedEnergy = double.NaN;
        private double materialFraction = double.NaN;
        private Dictionary<Guid, double> energyPerElement;
        private Dictionary<Guid, string> namePerElement;

        public ShadingPerformance(
            Guid apertureGuid,
            string typologyName,
            double admittedDirectEnergy,
            double admittedUnwantedEnergy,
            double admittedWantedEnergy,
            double directSolarIntercepted,
            double unwantedSolarIntercepted,
            double wantedSolarBlocked,
            double unattributedInterceptedEnergy,
            double materialFraction,
            Dictionary<Guid, double> energyPerElement,
            Dictionary<Guid, string> namePerElement)
        {
            this.apertureGuid = apertureGuid;
            this.typologyName = typologyName;
            this.admittedDirectEnergy = admittedDirectEnergy;
            this.admittedUnwantedEnergy = admittedUnwantedEnergy;
            this.admittedWantedEnergy = admittedWantedEnergy;
            this.directSolarIntercepted = directSolarIntercepted;
            this.unwantedSolarIntercepted = unwantedSolarIntercepted;
            this.wantedSolarBlocked = wantedSolarBlocked;
            this.unattributedInterceptedEnergy = unattributedInterceptedEnergy;
            this.materialFraction = materialFraction;
            this.energyPerElement = energyPerElement == null ? new Dictionary<Guid, double>() : new Dictionary<Guid, double>(energyPerElement);
            this.namePerElement = namePerElement == null ? new Dictionary<Guid, string>() : new Dictionary<Guid, string>(namePerElement);
        }

        public ShadingPerformance(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public Guid ApertureGuid { get { return apertureGuid; } }

        public string TypologyName { get { return typologyName; } }

        /// <summary>Unshaded admitted direct beam, kWh — the denominator of the efficiency.</summary>
        public double AdmittedDirectEnergy { get { return admittedDirectEnergy; } }

        /// <summary>Unshaded admitted UNWANTED direct beam, kWh.</summary>
        public double AdmittedUnwantedEnergy { get { return admittedUnwantedEnergy; } }

        /// <summary>Unshaded admitted WANTED direct beam, kWh.</summary>
        public double AdmittedWantedEnergy { get { return admittedWantedEnergy; } }

        /// <summary>Direct Solar Intercepted [kWh]: admitted beam the candidate now stops.</summary>
        public double DirectSolarIntercepted { get { return directSolarIntercepted; } }

        /// <summary>Unwanted portion of the intercepted beam, kWh.</summary>
        public double UnwantedSolarIntercepted { get { return unwantedSolarIntercepted; } }

        /// <summary>
        /// Wanted beam the candidate destroys, kWh. Named for what it IS — a penalty quantity, a
        /// loss — so that a fit score subtracting it reads correctly and cannot be added by mistake.
        /// </summary>
        public double WantedSolarBlocked { get { return wantedSolarBlocked; } }

        /// <summary>
        /// Intercepted energy on base-admitted rays whose first hit was NOT one of the candidate's
        /// elements, kWh. It should be zero: a ray admitted with context in place cannot be stopped
        /// by that same context. Anything here means the attribution and the baseline disagree, so
        /// it is reported rather than folded silently into the totals.
        /// </summary>
        public double UnattributedInterceptedEnergy { get { return unattributedInterceptedEnergy; } }

        /// <summary>
        /// Admitted direct beam the brief claimed NEITHER way, kWh: AdmittedDirect - AdmittedUnwanted
        /// - AdmittedWanted.
        ///
        /// This is the number that makes the headline figures reconcile, and its absence is the
        /// single most confusing thing on the canvas. A north window admitting 61.3 kWh of which
        /// 46.0 kWh is unwanted and 0.0 kWh wanted looks like 15.3 kWh has gone missing; it has not.
        /// Under the default seasonal brief — summer unwanted, winter wanted — the spring and autumn
        /// beam belongs to neither period, and this is it.
        ///
        ///   AdmittedDirectEnergy = AdmittedUnwantedEnergy + AdmittedWantedEnergy + AdmittedNeutralEnergy
        ///
        /// GENERAL CASE, and why this is a subtraction rather than a fourth accumulator. The unwanted
        /// and wanted energies are DESIRABILITY-WEIGHTED sums (|w| x energy over the hours of each
        /// sign), not slices of a partition. Written as a residual, the identity above holds by
        /// construction for every strategy, including continuous and custom ones; what varies is
        /// whether the residual is a physical energy. It is — and is non-negative — exactly when
        /// every applied weight lay within [-1, 1], which
        /// <see cref="ApertureDesirability.WeightsWithinUnitMagnitude"/> reports from the weights
        /// actually used. Beyond unit magnitude the brief claims more beam than physically arrives
        /// and this residual goes negative; that is a true statement about the weighting and is
        /// surfaced rather than clamped to zero.
        /// </summary>
        public double AdmittedNeutralEnergy { get { return admittedDirectEnergy - admittedUnwantedEnergy - admittedWantedEnergy; } }

        /// <summary>
        /// The neutral part of what the device stopped, kWh: DirectSolarIntercepted -
        /// UnwantedSolarIntercepted - WantedSolarBlocked. The interception-side counterpart of
        /// <see cref="AdmittedNeutralEnergy"/>, with the same general treatment:
        ///
        ///   DirectSolarIntercepted = UnwantedSolarIntercepted + WantedSolarBlocked + NeutralSolarIntercepted
        ///
        /// It is neither a benefit nor a harm under the brief as stated, which is precisely why it
        /// does not appear in the objective — and precisely why it has to appear in the reporting,
        /// or the two energies the engineer can see do not add up to the one they started from.
        /// </summary>
        public double NeutralSolarIntercepted { get { return directSolarIntercepted - unwantedSolarIntercepted - wantedSolarBlocked; } }

        /// <summary>Device area as a fraction of the aperture's gross area.</summary>
        public double MaterialFraction { get { return materialFraction; } }

        /// <summary>Per-element intercepted direct energy, kWh, credited to the FIRST element hit.</summary>
        public Dictionary<Guid, double> EnergyPerElement { get { return new Dictionary<Guid, double>(energyPerElement); } }

        public string ElementName(Guid guid) { return namePerElement.TryGetValue(guid, out string name) ? name : null; }

        /// <summary>Direct Shading Efficiency [%]: intercepted / unshaded admitted direct. NaN when nothing is admitted.</summary>
        public double DirectShadingEfficiency { get { return Ratio(directSolarIntercepted, admittedDirectEnergy); } }

        /// <summary>Unwanted Solar Blocked [%]: intercepted unwanted / unshaded admitted unwanted. NaN when there is no unwanted solar.</summary>
        public double UnwantedSolarBlocked { get { return Ratio(unwantedSolarIntercepted, admittedUnwantedEnergy); } }

        /// <summary>Wanted Solar Retained [%]: still-admitted wanted / unshaded admitted wanted. NaN when there is no wanted solar.</summary>
        public double WantedSolarRetained { get { return Ratio(admittedWantedEnergy - wantedSolarBlocked, admittedWantedEnergy); } }

        /// <summary>
        /// Sum of the per-element contributions plus the unattributed residual. Must reconcile with
        /// DirectSolarIntercepted; the difference is the conservation check.
        /// </summary>
        public double ReconciledInterceptedEnergy
        {
            get
            {
                double total = unattributedInterceptedEnergy;
                foreach (KeyValuePair<Guid, double> pair in energyPerElement)
                {
                    total += pair.Value;
                }

                return total;
            }
        }

        private static double Ratio(double numerator, double denominator)
        {
            // An explicit unavailable result, never a flattering 0 % or 100 %.
            return double.IsNaN(denominator) || denominator <= 0 ? double.NaN : numerator / denominator;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("ApertureGuid")) { Guid.TryParse(jObject["ApertureGuid"]?.GetValue<string>(), out apertureGuid); }
            if (jObject.ContainsKey("TypologyName")) { typologyName = jObject["TypologyName"]?.GetValue<string>(); }
            admittedDirectEnergy = Read(jObject, "AdmittedDirectEnergy");
            admittedUnwantedEnergy = Read(jObject, "AdmittedUnwantedEnergy");
            admittedWantedEnergy = Read(jObject, "AdmittedWantedEnergy");
            directSolarIntercepted = Read(jObject, "DirectSolarIntercepted");
            unwantedSolarIntercepted = Read(jObject, "UnwantedSolarIntercepted");
            wantedSolarBlocked = Read(jObject, "WantedSolarBlocked");
            unattributedInterceptedEnergy = Read(jObject, "UnattributedInterceptedEnergy");
            materialFraction = Read(jObject, "MaterialFraction");

            energyPerElement = new Dictionary<Guid, double>();
            namePerElement = new Dictionary<Guid, string>();
            if (jObject.ContainsKey("Elements") && jObject["Elements"] is JsonArray jsonArray)
            {
                foreach (JsonNode node in jsonArray)
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
            jObject.Add("ApertureGuid", apertureGuid.ToString());
            if (typologyName != null) { jObject.Add("TypologyName", typologyName); }

            jObject.Add("AdmittedDirectEnergy", admittedDirectEnergy);
            jObject.Add("AdmittedUnwantedEnergy", admittedUnwantedEnergy);
            jObject.Add("AdmittedWantedEnergy", admittedWantedEnergy);
            jObject.Add("DirectSolarIntercepted", directSolarIntercepted);
            jObject.Add("UnwantedSolarIntercepted", unwantedSolarIntercepted);
            jObject.Add("WantedSolarBlocked", wantedSolarBlocked);
            jObject.Add("UnattributedInterceptedEnergy", unattributedInterceptedEnergy);
            jObject.Add("MaterialFraction", materialFraction);

            JsonArray jsonArray = new JsonArray();
            foreach (KeyValuePair<Guid, double> pair in energyPerElement)
            {
                JsonObject element = new JsonObject();
                element.Add("Guid", pair.Key.ToString());
                element.Add("Energy", pair.Value);
                if (namePerElement.TryGetValue(pair.Key, out string name) && name != null)
                {
                    element.Add("Name", name);
                }

                jsonArray.Add(element);
            }

            jObject.Add("Elements", jsonArray);
            return jObject;
        }
    }
}
