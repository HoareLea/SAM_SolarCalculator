// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Weather;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// Irradiance-threshold desirability: hours whose global horizontal irradiance meets or exceeds
    /// a threshold are unwanted (+1 by default) — the common practitioner glare/overheating-risk
    /// rule. Hours below the threshold take belowThresholdWeight (default 0, neutral; set negative
    /// to treat dim hours as wanted).
    ///
    /// Approximation honesty: the threshold is read on GLOBAL HORIZONTAL irradiance, not on the
    /// aperture-plane irradiance (the strategy has no location, so it cannot compute incidence
    /// angles). It reproduces the intent of the rule, not its exact aperture-plane physics.
    /// </summary>
    public class IrradianceThresholdDesirability : IDesirabilityStrategy, IJSAMObject
    {
        private double threshold = 500.0;
        private double belowThresholdWeight;

        public IrradianceThresholdDesirability(double threshold = 500.0, double belowThresholdWeight = 0.0)
        {
            this.threshold = threshold;
            this.belowThresholdWeight = belowThresholdWeight;
        }

        public IrradianceThresholdDesirability(IrradianceThresholdDesirability irradianceThresholdDesirability)
        {
            if (irradianceThresholdDesirability != null)
            {
                threshold = irradianceThresholdDesirability.threshold;
                belowThresholdWeight = irradianceThresholdDesirability.belowThresholdWeight;
            }
        }

        public IrradianceThresholdDesirability(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>Global horizontal irradiance at or above which the hour is unwanted, W/m2.</summary>
        public double Threshold
        {
            get
            {
                return threshold;
            }
        }

        /// <summary>Weight applied below the threshold (0 = neutral, negative = wanted).</summary>
        public double BelowThresholdWeight
        {
            get
            {
                return belowThresholdWeight;
            }
        }

        public double Weight(DateTime dateTime, WeatherHour weatherHour, ApertureSolarTarget target)
        {
            if (weatherHour == null)
            {
                return 0.0;
            }

            double globalSolarRadiation = weatherHour.GlobalSolarRadiation;
            if (double.IsNaN(globalSolarRadiation))
            {
                return 0.0;
            }

            return globalSolarRadiation >= threshold ? 1.0 : belowThresholdWeight;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("Threshold"))
            {
                threshold = jObject["Threshold"]?.GetValue<double>() ?? 500.0;
            }

            if (jObject.ContainsKey("BelowThresholdWeight"))
            {
                belowThresholdWeight = jObject["BelowThresholdWeight"]?.GetValue<double>() ?? 0.0;
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            jObject.Add("Threshold", threshold);
            jObject.Add("BelowThresholdWeight", belowThresholdWeight);
            return jObject;
        }
    }
}
