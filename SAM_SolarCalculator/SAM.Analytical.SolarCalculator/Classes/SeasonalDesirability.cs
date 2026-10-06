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
    /// Seasonal desirability: explicit wanted and unwanted AnalysisPeriods (each may carry explicit
    /// hours of the year, so HOY selections are supported through the same type). The simplest, most
    /// explainable strategy: blocking is beneficial inside the unwanted period (+unwantedWeight),
    /// harmful inside the wanted period (-wantedWeight), neutral everywhere else.
    ///
    /// Approximation honesty: this encodes a design INTENT (e.g. "summer sun is unwanted"), not a
    /// thermal truth — a cool July morning's sun may be welcome and a warm March afternoon's may not
    /// be. The load-based Shaderade formulation weights by cooling-minus-heating load instead; this
    /// is the documented Phase 1 stand-in for it.
    ///
    /// Overlap rule: an hour inside BOTH periods takes the UNWANTED weight (an overlapping
    /// definition is almost always a user error, and failing toward shading is the conservative
    /// reading of it). Outside both periods the weight is exactly zero.
    /// </summary>
    public class SeasonalDesirability : IDesirabilityStrategy, IJSAMObject
    {
        private AnalysisPeriod unwantedPeriod;
        private AnalysisPeriod wantedPeriod;
        private double unwantedWeight = 1.0;
        private double wantedWeight = 1.0;

        public SeasonalDesirability(AnalysisPeriod unwantedPeriod, AnalysisPeriod wantedPeriod, double unwantedWeight = 1.0, double wantedWeight = 1.0)
        {
            this.unwantedPeriod = unwantedPeriod == null ? null : new AnalysisPeriod(unwantedPeriod);
            this.wantedPeriod = wantedPeriod == null ? null : new AnalysisPeriod(wantedPeriod);
            this.unwantedWeight = unwantedWeight;
            this.wantedWeight = wantedWeight;
        }

        public SeasonalDesirability(SeasonalDesirability seasonalDesirability)
        {
            if (seasonalDesirability != null)
            {
                unwantedPeriod = seasonalDesirability.unwantedPeriod == null ? null : new AnalysisPeriod(seasonalDesirability.unwantedPeriod);
                wantedPeriod = seasonalDesirability.wantedPeriod == null ? null : new AnalysisPeriod(seasonalDesirability.wantedPeriod);
                unwantedWeight = seasonalDesirability.unwantedWeight;
                wantedWeight = seasonalDesirability.wantedWeight;
            }
        }

        public SeasonalDesirability(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        /// <summary>Period in which direct sun is unwanted (blocking beneficial). May carry explicit HOYs.</summary>
        public AnalysisPeriod UnwantedPeriod
        {
            get
            {
                return unwantedPeriod == null ? null : new AnalysisPeriod(unwantedPeriod);
            }
        }

        /// <summary>Period in which direct sun is wanted (blocking harmful). May carry explicit HOYs.</summary>
        public AnalysisPeriod WantedPeriod
        {
            get
            {
                return wantedPeriod == null ? null : new AnalysisPeriod(wantedPeriod);
            }
        }

        /// <summary>Magnitude of the positive weight applied inside the unwanted period.</summary>
        public double UnwantedWeight
        {
            get
            {
                return unwantedWeight;
            }
        }

        /// <summary>Magnitude of the negative weight applied inside the wanted period.</summary>
        public double WantedWeight
        {
            get
            {
                return wantedWeight;
            }
        }

        public double Weight(DateTime dateTime, WeatherHour weatherHour, ApertureSolarTarget target)
        {
            if (unwantedPeriod != null && unwantedPeriod.Contains(dateTime))
            {
                return unwantedWeight;
            }

            if (wantedPeriod != null && wantedPeriod.Contains(dateTime))
            {
                return -wantedWeight;
            }

            return 0.0;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("UnwantedPeriod"))
            {
                unwantedPeriod = new AnalysisPeriod(jObject["UnwantedPeriod"] as JsonObject);
            }

            if (jObject.ContainsKey("WantedPeriod"))
            {
                wantedPeriod = new AnalysisPeriod(jObject["WantedPeriod"] as JsonObject);
            }

            if (jObject.ContainsKey("UnwantedWeight"))
            {
                unwantedWeight = jObject["UnwantedWeight"]?.GetValue<double>() ?? 1.0;
            }

            if (jObject.ContainsKey("WantedWeight"))
            {
                wantedWeight = jObject["WantedWeight"]?.GetValue<double>() ?? 1.0;
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            if (unwantedPeriod != null)
            {
                jObject.Add("UnwantedPeriod", unwantedPeriod.ToJsonObject());
            }

            if (wantedPeriod != null)
            {
                jObject.Add("WantedPeriod", wantedPeriod.ToJsonObject());
            }

            jObject.Add("UnwantedWeight", unwantedWeight);
            jObject.Add("WantedWeight", wantedWeight);

            return jObject;
        }
    }
}
