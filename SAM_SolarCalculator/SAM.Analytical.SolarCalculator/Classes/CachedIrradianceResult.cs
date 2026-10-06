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
    /// Per-cell irradiance accumulated over an AnalysisPeriod from the visibility caches -
    /// the cheap re-weighting stage. Units: energy densities are kWh/m2 (weather W/m2 integrated
    /// over whole hours and divided by 1000); sunlit hours are a count of weather-timeline hours.
    /// </summary>
    public class CachedIrradianceResult : IJSAMObject, ISolarObject
    {
        private List<DateTime> dateTimes;
        private double[] direct;
        private double[] diffuse;
        private double[] groundReflected;
        private double[] sunlitHours;
        private int missedBinHours;
        private int belowHorizonHours;
        private int missingWeatherHours;

        public CachedIrradianceResult(IEnumerable<DateTime> dateTimes, double[] direct, double[] diffuse, double[] groundReflected, double[] sunlitHours, int missedBinHours = 0, int belowHorizonHours = 0, int missingWeatherHours = 0)
        {
            this.dateTimes = dateTimes == null ? null : new List<DateTime>(dateTimes);
            this.direct = direct;
            this.diffuse = diffuse;
            this.groundReflected = groundReflected;
            this.sunlitHours = sunlitHours;
            this.missedBinHours = missedBinHours;
            this.belowHorizonHours = belowHorizonHours;
            this.missingWeatherHours = missingWeatherHours;
        }

        public CachedIrradianceResult(CachedIrradianceResult cachedIrradianceResult)
        {
            if (cachedIrradianceResult != null)
            {
                dateTimes = cachedIrradianceResult.dateTimes == null ? null : new List<DateTime>(cachedIrradianceResult.dateTimes);
                direct = cachedIrradianceResult.direct == null ? null : (double[])cachedIrradianceResult.direct.Clone();
                diffuse = cachedIrradianceResult.diffuse == null ? null : (double[])cachedIrradianceResult.diffuse.Clone();
                groundReflected = cachedIrradianceResult.groundReflected == null ? null : (double[])cachedIrradianceResult.groundReflected.Clone();
                sunlitHours = cachedIrradianceResult.sunlitHours == null ? null : (double[])cachedIrradianceResult.sunlitHours.Clone();
                missedBinHours = cachedIrradianceResult.missedBinHours;
                belowHorizonHours = cachedIrradianceResult.belowHorizonHours;
                missingWeatherHours = cachedIrradianceResult.missingWeatherHours;
            }
        }

        public CachedIrradianceResult(JsonObject jObject)
        {
            FromJsonObject(jObject);
        }

        public int CellCount
        {
            get
            {
                return direct?.Length ?? 0;
            }
        }

        /// <summary>Weather-timeline hours that were evaluated (valid weather and sun above the horizon).</summary>
        public List<DateTime> DateTimes
        {
            get
            {
                return dateTimes == null ? null : new List<DateTime>(dateTimes);
            }
        }

        /// <summary>Evaluated hour count (== DateTimes.Count).</summary>
        public int EvaluatedHours
        {
            get
            {
                return dateTimes?.Count ?? 0;
            }
        }

        /// <summary>
        /// Hours whose sun position fell outside the cached bins and were skipped. Always 0 when the
        /// cache's SunPositionShiftInMinutes matches the timeline — a non-zero value here is a
        /// configuration error and must be investigated, not ignored.
        /// </summary>
        public int MissedBinHours
        {
            get
            {
                return missedBinHours;
            }
        }

        /// <summary>Period hours skipped because the sun was below the minimum horizon angle.</summary>
        public int BelowHorizonHours
        {
            get
            {
                return belowHorizonHours;
            }
        }

        /// <summary>Period hours skipped because no valid weather values exist for them.</summary>
        public int MissingWeatherHours
        {
            get
            {
                return missingWeatherHours;
            }
        }

        /// <summary>Direct-beam energy density per cell, kWh/m2.</summary>
        public double[] Direct
        {
            get
            {
                return direct == null ? null : (double[])direct.Clone();
            }
        }

        /// <summary>Diffuse-sky energy density per cell, kWh/m2 (isotropic or Perez per the chosen SkyModel).</summary>
        public double[] Diffuse
        {
            get
            {
                return diffuse == null ? null : (double[])diffuse.Clone();
            }
        }

        /// <summary>Ground-reflected energy density per cell, kWh/m2.</summary>
        public double[] GroundReflected
        {
            get
            {
                return groundReflected == null ? null : (double[])groundReflected.Clone();
            }
        }

        /// <summary>Hours with direct sun on the cell (lit and in front of the surface).</summary>
        public double[] SunlitHours
        {
            get
            {
                return sunlitHours == null ? null : (double[])sunlitHours.Clone();
            }
        }

        /// <summary>Total (direct + diffuse + ground-reflected) energy density of a cell, kWh/m2.</summary>
        public double GetTotal(int cellIndex)
        {
            double result = 0;
            if (direct != null && cellIndex >= 0 && cellIndex < direct.Length)
            {
                result = direct[cellIndex] + diffuse[cellIndex] + groundReflected[cellIndex];
            }

            return result;
        }

        public bool FromJsonObject(JsonObject jObject)
        {
            if (jObject == null)
            {
                return false;
            }

            dateTimes = null;
            if (jObject.ContainsKey("DateTimes"))
            {
                JsonArray jArray = jObject["DateTimes"] as JsonArray;
                if (jArray != null)
                {
                    dateTimes = new List<DateTime>();
                    foreach (JsonNode jNode in jArray)
                    {
                        dateTimes.Add(jNode?.GetValue<DateTime>() ?? default);
                    }
                }
            }

            direct = Doubles(jObject, "Direct");
            diffuse = Doubles(jObject, "Diffuse");
            groundReflected = Doubles(jObject, "GroundReflected");
            sunlitHours = Doubles(jObject, "SunlitHours");

            if (jObject.ContainsKey("MissedBinHours"))
            {
                missedBinHours = jObject["MissedBinHours"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("BelowHorizonHours"))
            {
                belowHorizonHours = jObject["BelowHorizonHours"]?.GetValue<int>() ?? default;
            }

            if (jObject.ContainsKey("MissingWeatherHours"))
            {
                missingWeatherHours = jObject["MissingWeatherHours"]?.GetValue<int>() ?? default;
            }

            return true;
        }

        public JsonObject ToJsonObject()
        {
            JsonObject jObject = new JsonObject();
            jObject.Add("_type", Core.Query.FullTypeName(this));

            if (dateTimes != null)
            {
                JsonArray jArray = new JsonArray();
                foreach (DateTime dateTime in dateTimes)
                {
                    jArray.Add(dateTime);
                }
                jObject.Add("DateTimes", jArray);
            }

            Add(jObject, "Direct", direct);
            Add(jObject, "Diffuse", diffuse);
            Add(jObject, "GroundReflected", groundReflected);
            Add(jObject, "SunlitHours", sunlitHours);

            jObject.Add("MissedBinHours", missedBinHours);
            jObject.Add("BelowHorizonHours", belowHorizonHours);
            jObject.Add("MissingWeatherHours", missingWeatherHours);

            return jObject;
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
