// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Weather;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// Externally supplied desirability: per-hour-of-year weights provided by the caller. This is
    /// the Phase 2 hook for load-based weighting — a TAS-derived strategy would compute
    /// cooling-minus-heating loads per hour and supply them here (positive load = cooling-dominated
    /// = blocking beneficial), with NO change to any caller of IDesirabilityStrategy.
    ///
    /// Weights are looked up by hour of the year (0-based, from 1 Jan 00:00 of Year). An hour with
    /// no supplied weight is neutral (0). The weather-timeline hour must fall inside Year to be
    /// looked up; hours of other years are neutral.
    /// </summary>
    public class ExternalDesirability : IDesirabilityStrategy, IJSAMObject
    {
        private int year;
        private Dictionary<int, double> weightByHourOfYear;

        public ExternalDesirability(int year, IDictionary<int, double> weightByHourOfYear)
        {
            this.year = year;
            this.weightByHourOfYear = weightByHourOfYear == null ? new Dictionary<int, double>() : new Dictionary<int, double>(weightByHourOfYear);
        }

        public ExternalDesirability(ExternalDesirability externalDesirability)
        {
            if (externalDesirability != null)
            {
                year = externalDesirability.year;
                weightByHourOfYear = externalDesirability.weightByHourOfYear == null ? null : new Dictionary<int, double>(externalDesirability.weightByHourOfYear);
            }
        }

        public ExternalDesirability(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public int Year
        {
            get
            {
                return year;
            }
        }

        /// <summary>Supplied weights by hour of the year (0-based). Defensive copy.</summary>
        public Dictionary<int, double> WeightByHourOfYear
        {
            get
            {
                return weightByHourOfYear == null ? null : new Dictionary<int, double>(weightByHourOfYear);
            }
        }

        public double Weight(DateTime dateTime, WeatherHour weatherHour, ApertureSolarTarget target)
        {
            if (weightByHourOfYear == null || dateTime.Year != year)
            {
                return 0.0;
            }

            int hourOfYear = (int)(dateTime - new DateTime(dateTime.Year, 1, 1)).TotalHours;
            return weightByHourOfYear.TryGetValue(hourOfYear, out double weight) ? weight : 0.0;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            if (jObject.ContainsKey("Year"))
            {
                year = jObject["Year"]?.GetValue<int>() ?? default;
            }

            weightByHourOfYear = null;
            if (jObject.ContainsKey("WeightByHourOfYear"))
            {
                JsonObject jObject_Dictionary = jObject["WeightByHourOfYear"] as JsonObject;
                if (jObject_Dictionary != null)
                {
                    weightByHourOfYear = new Dictionary<int, double>();
                    foreach (KeyValuePair<string, JsonNode> pair in jObject_Dictionary)
                    {
                        if (int.TryParse(pair.Key, out int hourOfYear))
                        {
                            weightByHourOfYear[hourOfYear] = pair.Value?.GetValue<double>() ?? 0.0;
                        }
                    }
                }
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));
            jObject.Add("Year", year);

            if (weightByHourOfYear != null)
            {
                JsonObject jObject_Dictionary = new JsonObject();
                foreach (KeyValuePair<int, double> pair in weightByHourOfYear)
                {
                    jObject_Dictionary.Add(pair.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), pair.Value);
                }
                jObject.Add("WeightByHourOfYear", jObject_Dictionary);
            }

            return jObject;
        }
    }
}
