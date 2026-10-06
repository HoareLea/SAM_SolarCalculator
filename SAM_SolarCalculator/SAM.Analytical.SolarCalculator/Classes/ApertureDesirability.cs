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
    /// Stage 5 result: the desirability-weighted direct-solar energy of one aperture, per sun
    /// group, aligned by index with the SolarVisibilityCache's bins. All energies are aperture-plane
    /// beam energy densities (kWh/m2): per member hour, DNI x cos(thetaI) x 1 h / 1000, where cos
    /// (thetaI) uses the aperture OUTWARD normal and the sun position at hour + cache shift. Hours
    /// whose beam misses the aperture (cos &lt;= 0) contribute exactly zero.
    ///
    /// Wanted and unwanted energy stay DISTINGUISHABLE by design:
    ///   UnwantedEnergyPerGroup[g] — energy that should be blocked (positive-weight hours), &gt;= 0
    ///   WantedEnergyPerGroup[g]   — energy that should be preserved (negative-weight hours), &gt;= 0
    ///   NetDesirabilityPerGroup[g] = Unwanted - Wanted (positive = net block-me)
    ///   DirectEnergyPerGroup[g]   — the group's total energy regardless of desirability
    /// A group whose wanted and unwanted hours share (nearly) the same direction nets them: the
    /// wanted/unwanted trade-off is resolved ACROSS directions, which is the physics of shading —
    /// one device cannot block a summer noon ray and pass an identical winter ray.
    ///
    /// Diffuse radiation is deliberately NOT carried here: shading desirability is about direct
    /// solar interception only.
    /// </summary>
    public class ApertureDesirability : IJSAMObject, ISolarObject
    {
        public const int CurrentSchemaVersion = 2;

        private int schemaVersion = CurrentSchemaVersion;
        private Guid apertureGuid;
        private string desirabilityStrategyName;
        private int year;
        private double timeShiftInMinutes;
        private double[] directEnergy;
        private double[] unwantedEnergy;
        private double[] wantedEnergy;
        private int evaluatedHours;
        private int missingWeatherHours;
        private double maximumWeightMagnitude = double.NaN;

        public ApertureDesirability(Guid apertureGuid, string desirabilityStrategyName, int year, double timeShiftInMinutes, double[] directEnergy, double[] unwantedEnergy, double[] wantedEnergy, int evaluatedHours, int missingWeatherHours, double maximumWeightMagnitude = double.NaN)
        {
            this.maximumWeightMagnitude = maximumWeightMagnitude;
            this.apertureGuid = apertureGuid;
            this.desirabilityStrategyName = desirabilityStrategyName;
            this.year = year;
            this.timeShiftInMinutes = timeShiftInMinutes;
            this.directEnergy = directEnergy == null ? null : (double[])directEnergy.Clone();
            this.unwantedEnergy = unwantedEnergy == null ? null : (double[])unwantedEnergy.Clone();
            this.wantedEnergy = wantedEnergy == null ? null : (double[])wantedEnergy.Clone();
            this.evaluatedHours = evaluatedHours;
            this.missingWeatherHours = missingWeatherHours;
        }

        public ApertureDesirability(ApertureDesirability apertureDesirability)
        {
            if (apertureDesirability != null)
            {
                schemaVersion = apertureDesirability.schemaVersion;
                apertureGuid = apertureDesirability.apertureGuid;
                desirabilityStrategyName = apertureDesirability.desirabilityStrategyName;
                year = apertureDesirability.year;
                timeShiftInMinutes = apertureDesirability.timeShiftInMinutes;
                directEnergy = apertureDesirability.directEnergy == null ? null : (double[])apertureDesirability.directEnergy.Clone();
                unwantedEnergy = apertureDesirability.unwantedEnergy == null ? null : (double[])apertureDesirability.unwantedEnergy.Clone();
                wantedEnergy = apertureDesirability.wantedEnergy == null ? null : (double[])apertureDesirability.wantedEnergy.Clone();
                evaluatedHours = apertureDesirability.evaluatedHours;
                missingWeatherHours = apertureDesirability.missingWeatherHours;
                maximumWeightMagnitude = apertureDesirability.maximumWeightMagnitude;
            }
        }

        public ApertureDesirability(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public int SchemaVersion
        {
            get
            {
                return schemaVersion;
            }
        }

        public Guid ApertureGuid
        {
            get
            {
                return apertureGuid;
            }
        }

        /// <summary>Full type name of the strategy that produced these weights (provenance).</summary>
        public string DesirabilityStrategyName
        {
            get
            {
                return desirabilityStrategyName;
            }
        }

        public int Year
        {
            get
            {
                return year;
            }
        }

        /// <summary>Sun-position sampling offset applied to each weather timestamp, minutes.</summary>
        public double TimeShiftInMinutes
        {
            get
            {
                return timeShiftInMinutes;
            }
        }

        public int GroupCount
        {
            get
            {
                return directEnergy?.Length ?? 0;
            }
        }

        /// <summary>Per-group total aperture-plane beam energy, kWh/m2 (desirability-independent).</summary>
        public double[] DirectEnergyPerGroup
        {
            get
            {
                return directEnergy == null ? null : (double[])directEnergy.Clone();
            }
        }

        /// <summary>Per-group energy that should be blocked (positive-weight hours), kWh/m2, &gt;= 0.</summary>
        public double[] UnwantedEnergyPerGroup
        {
            get
            {
                return unwantedEnergy == null ? null : (double[])unwantedEnergy.Clone();
            }
        }

        /// <summary>Per-group energy that should be preserved (negative-weight hours), kWh/m2, &gt;= 0.</summary>
        public double[] WantedEnergyPerGroup
        {
            get
            {
                return wantedEnergy == null ? null : (double[])wantedEnergy.Clone();
            }
        }

        /// <summary>Net per-group desirability, kWh/m2: Unwanted - Wanted. Positive = net block-me.</summary>
        public double[] NetDesirabilityPerGroup
        {
            get
            {
                if (unwantedEnergy == null || wantedEnergy == null)
                {
                    return null;
                }

                double[] result = new double[unwantedEnergy.Length];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = unwantedEnergy[i] - wantedEnergy[i];
                }

                return result;
            }
        }

        /// <summary>Year total aperture-plane beam energy, kWh/m2.</summary>
        public double TotalDirectEnergy
        {
            get
            {
                return Sum(directEnergy);
            }
        }

        /// <summary>Year total energy that should be blocked, kWh/m2.</summary>
        public double TotalUnwantedEnergy
        {
            get
            {
                return Sum(unwantedEnergy);
            }
        }

        /// <summary>Year total energy that should be preserved, kWh/m2.</summary>
        public double TotalWantedEnergy
        {
            get
            {
                return Sum(wantedEnergy);
            }
        }

        /// <summary>Net year desirability, kWh/m2: TotalUnwanted - TotalWanted.</summary>
        public double NetDesirability
        {
            get
            {
                return TotalUnwantedEnergy - TotalWantedEnergy;
            }
        }

        /// <summary>
        /// Year total direct energy the brief claimed NEITHER way, kWh/m2: Direct - Unwanted -
        /// Wanted. The residual an engineer needs to reconcile a baseline against its two named
        /// parts.
        ///
        /// Under the default seasonal brief this is real, physical energy: the spring and autumn
        /// beam that is in neither the unwanted nor the wanted period. A north window admitting
        /// 61.3 kWh of which 46.0 kWh is unwanted and none is wanted has 15.3 kWh here, and that is
        /// the whole of the apparent discrepancy.
        ///
        /// GENERAL CASE. Unwanted and Wanted are WEIGHTED energies (sum of |w| x energy over the
        /// hours of each sign), not slices of a partition. The residual is therefore a physical
        /// energy, and non-negative, exactly when every weight the strategy applied lay within
        /// [-1, 1] — see <see cref="WeightsWithinUnitMagnitude"/>, which is MEASURED from the
        /// weights actually applied rather than declared by the strategy. A strategy that returns
        /// weights beyond unit magnitude is over-claiming the beam relative to its physical size,
        /// and this residual then turns negative: that is a true statement about the brief, not an
        /// error, and it is reported rather than clamped.
        /// </summary>
        public double TotalNeutralEnergy
        {
            get
            {
                return TotalDirectEnergy - TotalUnwantedEnergy - TotalWantedEnergy;
            }
        }

        /// <summary>
        /// The largest |weight| the strategy actually applied to an energy-carrying hour, or NaN
        /// when it was not recorded (a pre-schema-2 object) or nothing was evaluated.
        /// </summary>
        public double MaximumWeightMagnitude
        {
            get
            {
                return maximumWeightMagnitude;
            }
        }

        /// <summary>
        /// True when every applied weight lay within [-1, 1], so unwanted + wanted + neutral is a
        /// genuine partition of the admitted direct beam. False when a weight exceeded unit
        /// magnitude. NULL when it was not recorded and therefore cannot be claimed either way.
        /// </summary>
        public bool? WeightsWithinUnitMagnitude
        {
            get
            {
                if (double.IsNaN(maximumWeightMagnitude))
                {
                    return null;
                }

                return maximumWeightMagnitude <= 1.0 + 1e-9;
            }
        }

        /// <summary>Hours that contributed (valid weather, sun above the horizon gate).</summary>
        public int EvaluatedHours
        {
            get
            {
                return evaluatedHours;
            }
        }

        /// <summary>Bin member hours skipped for missing/invalid weather values.</summary>
        public int MissingWeatherHours
        {
            get
            {
                return missingWeatherHours;
            }
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("SchemaVersion"))
            {
                schemaVersion = jObject["SchemaVersion"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("ApertureGuid"))
            {
                Guid.TryParse(jObject["ApertureGuid"]?.GetValue<string>(), out apertureGuid);
            }

            if (jObject.ContainsKey("DesirabilityStrategyName"))
            {
                desirabilityStrategyName = jObject["DesirabilityStrategyName"]?.GetValue<string>();
            }

            if (jObject.ContainsKey("Year"))
            {
                year = jObject["Year"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("TimeShiftInMinutes"))
            {
                timeShiftInMinutes = jObject["TimeShiftInMinutes"]?.GetValue<double>() ?? default;
            }

            directEnergy = Doubles(jObject, "DirectEnergy");
            unwantedEnergy = Doubles(jObject, "UnwantedEnergy");
            wantedEnergy = Doubles(jObject, "WantedEnergy");

            if (jObject.ContainsKey("EvaluatedHours"))
            {
                evaluatedHours = jObject["EvaluatedHours"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("MissingWeatherHours"))
            {
                missingWeatherHours = jObject["MissingWeatherHours"]?.GetValue<int>() ?? default;
            }

            // Absent in schema 1. NaN is the honest reading — "not recorded" — and is what makes
            // WeightsWithinUnitMagnitude return null instead of asserting a partition it cannot know.
            maximumWeightMagnitude = jObject.ContainsKey("MaximumWeightMagnitude")
                ? (jObject["MaximumWeightMagnitude"]?.GetValue<double>() ?? double.NaN)
                : double.NaN;

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            jObject.Add("SchemaVersion", schemaVersion);
            jObject.Add("ApertureGuid", apertureGuid.ToString());

            if (desirabilityStrategyName != null)
            {
                jObject.Add("DesirabilityStrategyName", desirabilityStrategyName);
            }

            jObject.Add("Year", year);
            jObject.Add("TimeShiftInMinutes", timeShiftInMinutes);

            Add(jObject, "DirectEnergy", directEnergy);
            Add(jObject, "UnwantedEnergy", unwantedEnergy);
            Add(jObject, "WantedEnergy", wantedEnergy);

            jObject.Add("EvaluatedHours", evaluatedHours);
            jObject.Add("MissingWeatherHours", missingWeatherHours);
            jObject.Add("MaximumWeightMagnitude", maximumWeightMagnitude);

            return jObject;
        }

        private static double Sum(double[] values)
        {
            if (values == null)
            {
                return double.NaN;
            }

            double result = 0;
            foreach (double value in values)
            {
                result += value;
            }

            return result;
        }

        private static void Add(JsonObject jObject, string name, double[] values)
        {
            if (values == null)
            {
                return;
            }

            JsonArray jArray = new JsonArray();
            foreach (double value in values)
            {
                jArray.Add(value);
            }
            jObject.Add(name, jArray);
        }

        private static double[] Doubles(JsonObject jObject, string name)
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

            List<double> values = new List<double>(jArray.Count);
            foreach (JsonNode jNode in jArray)
            {
                values.Add(jNode?.GetValue<double>() ?? double.NaN);
            }

            return values.ToArray();
        }
    }
}
