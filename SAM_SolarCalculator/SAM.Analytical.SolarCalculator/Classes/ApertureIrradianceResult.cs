// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    /// <summary>
    /// Per-aperture irradiance over an AnalysisPeriod, from the cached-visibility evaluation.
    /// Units are explicit:
    ///   per-cell values        kWh/m2   (weather W/m2 integrated over whole hours / 1000)
    ///   aperture totals        kWh      (per-cell kWh/m2 x cell area, summed)
    ///   aperture averages      kWh/m2   (total kWh / gross cell-covered area)
    ///   sunlit hours           h        (count of weather-timeline hours with direct sun on the cell)
    /// Reference = aperture Guid (SolarFaceSimulationResult convention).
    /// </summary>
    public class ApertureIrradianceResult : Result, ISolarSimulationResult
    {
        private AnalysisPeriod analysisPeriod;
        private SkyModel skyModel = SkyModel.Undefined;
        private double gridSize = double.NaN;
        private double sunAngleStep = double.NaN;
        private double albedo = double.NaN;
        private double timeShiftInMinutes;
        private double grossArea = double.NaN;
        private double[] cellAreas;
        private double[] direct;
        private double[] diffuse;
        private double[] groundReflected;
        private double[] sunlitHours;
        private List<DateTime> dateTimes;
        private int missedBinHours;
        private int belowHorizonHours;
        private int missingWeatherHours;

        public ApertureIrradianceResult(string name, string source, string reference, AnalysisPeriod analysisPeriod, SkyModel skyModel, double gridSize, double sunAngleStep, double albedo, double timeShiftInMinutes, double grossArea, double[] cellAreas, double[] direct, double[] diffuse, double[] groundReflected, double[] sunlitHours, IEnumerable<DateTime> dateTimes, int missedBinHours = 0, int belowHorizonHours = 0, int missingWeatherHours = 0)
            : base(name, source, reference)
        {
            this.analysisPeriod = analysisPeriod == null ? null : new AnalysisPeriod(analysisPeriod);
            this.skyModel = skyModel;
            this.gridSize = gridSize;
            this.sunAngleStep = sunAngleStep;
            this.albedo = albedo;
            this.timeShiftInMinutes = timeShiftInMinutes;
            this.grossArea = grossArea;
            this.cellAreas = cellAreas == null ? null : (double[])cellAreas.Clone();
            this.direct = direct == null ? null : (double[])direct.Clone();
            this.diffuse = diffuse == null ? null : (double[])diffuse.Clone();
            this.groundReflected = groundReflected == null ? null : (double[])groundReflected.Clone();
            this.sunlitHours = sunlitHours == null ? null : (double[])sunlitHours.Clone();
            this.dateTimes = dateTimes == null ? null : new List<DateTime>(dateTimes);
            this.missedBinHours = missedBinHours;
            this.belowHorizonHours = belowHorizonHours;
            this.missingWeatherHours = missingWeatherHours;
        }

        public ApertureIrradianceResult(ApertureIrradianceResult apertureIrradianceResult)
            : base(apertureIrradianceResult)
        {
            if (apertureIrradianceResult != null)
            {
                analysisPeriod = apertureIrradianceResult.analysisPeriod == null ? null : new AnalysisPeriod(apertureIrradianceResult.analysisPeriod);
                skyModel = apertureIrradianceResult.skyModel;
                gridSize = apertureIrradianceResult.gridSize;
                sunAngleStep = apertureIrradianceResult.sunAngleStep;
                albedo = apertureIrradianceResult.albedo;
                timeShiftInMinutes = apertureIrradianceResult.timeShiftInMinutes;
                grossArea = apertureIrradianceResult.grossArea;
                cellAreas = apertureIrradianceResult.cellAreas == null ? null : (double[])apertureIrradianceResult.cellAreas.Clone();
                direct = apertureIrradianceResult.direct == null ? null : (double[])apertureIrradianceResult.direct.Clone();
                diffuse = apertureIrradianceResult.diffuse == null ? null : (double[])apertureIrradianceResult.diffuse.Clone();
                groundReflected = apertureIrradianceResult.groundReflected == null ? null : (double[])apertureIrradianceResult.groundReflected.Clone();
                sunlitHours = apertureIrradianceResult.sunlitHours == null ? null : (double[])apertureIrradianceResult.sunlitHours.Clone();
                dateTimes = apertureIrradianceResult.dateTimes == null ? null : new List<DateTime>(apertureIrradianceResult.dateTimes);
                missedBinHours = apertureIrradianceResult.missedBinHours;
                belowHorizonHours = apertureIrradianceResult.belowHorizonHours;
                missingWeatherHours = apertureIrradianceResult.missingWeatherHours;
            }
        }

        public ApertureIrradianceResult(JsonObject jObject)
            : base(jObject)
        {
        }

        public AnalysisPeriod AnalysisPeriod
        {
            get
            {
                return analysisPeriod == null ? null : new AnalysisPeriod(analysisPeriod);
            }
        }

        public SkyModel SkyModel
        {
            get
            {
                return skyModel;
            }
        }

        /// <summary>Aperture analysis-grid size the result was computed with, m.</summary>
        public double GridSize
        {
            get
            {
                return gridSize;
            }
        }

        /// <summary>Angular resolution used to group similar sun positions, degrees.</summary>
        public double SunAngleStep
        {
            get
            {
                return sunAngleStep;
            }
        }

        public double Albedo
        {
            get
            {
                return albedo;
            }
        }

        /// <summary>
        /// Sun-position sampling offset applied to each weather timestamp, minutes
        /// (see <see cref="SunTimeConvention"/>: +30 interval start, 0 on the hour, -30 interval end).
        /// </summary>
        public double TimeShiftInMinutes
        {
            get
            {
                return timeShiftInMinutes;
            }
        }

        /// <summary>
        /// The named timeline convention this result used, or Undefined when the explicit-minutes
        /// overload was called with a non-standard offset.
        /// </summary>
        public SunTimeConvention SunTimeConvention
        {
            get
            {
                if (timeShiftInMinutes == 0)
                {
                    return SunTimeConvention.OnTheHour;
                }

                if (timeShiftInMinutes == 30)
                {
                    return SunTimeConvention.IntervalStart;
                }

                if (timeShiftInMinutes == -30)
                {
                    return SunTimeConvention.IntervalEnd;
                }

                return SunTimeConvention.Undefined;
            }
        }

        public double GrossArea
        {
            get
            {
                return grossArea;
            }
        }

        public int CellCount
        {
            get
            {
                return direct?.Length ?? 0;
            }
        }

        /// <summary>Cell areas, m2.</summary>
        public double[] CellAreas
        {
            get
            {
                return cellAreas == null ? null : (double[])cellAreas.Clone();
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

        /// <summary>Diffuse-sky energy density per cell, kWh/m2.</summary>
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

        /// <summary>Total energy density per cell (direct + diffuse + ground-reflected), kWh/m2.</summary>
        public double[] Total
        {
            get
            {
                if (direct == null)
                {
                    return null;
                }

                double[] result = new double[direct.Length];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = direct[i] + diffuse[i] + groundReflected[i];
                }

                return result;
            }
        }

        /// <summary>Hours with direct sun per cell.</summary>
        public double[] SunlitHours
        {
            get
            {
                return sunlitHours == null ? null : (double[])sunlitHours.Clone();
            }
        }

        /// <summary>Area-weighted aperture total energy, kWh.</summary>
        public double TotalEnergy
        {
            get
            {
                return AreaWeighted(direct) + AreaWeighted(diffuse) + AreaWeighted(groundReflected);
            }
        }

        /// <summary>Area-weighted direct-beam energy, kWh.</summary>
        public double DirectEnergy
        {
            get
            {
                return AreaWeighted(direct);
            }
        }

        /// <summary>Area-weighted diffuse-sky energy, kWh.</summary>
        public double DiffuseEnergy
        {
            get
            {
                return AreaWeighted(diffuse);
            }
        }

        /// <summary>Area-weighted ground-reflected energy, kWh.</summary>
        public double GroundReflectedEnergy
        {
            get
            {
                return AreaWeighted(groundReflected);
            }
        }

        /// <summary>Aperture-average energy density, kWh/m2.</summary>
        public double AverageIrradiance
        {
            get
            {
                double area = CellCoveredArea;
                return area > 0 ? TotalEnergy / area : double.NaN;
            }
        }

        /// <summary>Sum of cell areas (the discretised aperture area), m2.</summary>
        public double CellCoveredArea
        {
            get
            {
                if (cellAreas == null)
                {
                    return double.NaN;
                }

                double result = 0;
                foreach (double area in cellAreas)
                {
                    result += area;
                }

                return result;
            }
        }

        public List<DateTime> DateTimes
        {
            get
            {
                return dateTimes == null ? null : new List<DateTime>(dateTimes);
            }
        }

        /// <summary>Hours skipped because their sun position fell outside the cached bins (0 for a consistent cache/timeline pair).</summary>
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

        /// <summary>Period hours skipped for missing/invalid weather values.</summary>
        public int MissingWeatherHours
        {
            get
            {
                return missingWeatherHours;
            }
        }

        public override bool FromJsonObject(JsonObject jObject)
        {
            if (!base.FromJsonObject(jObject))
            {
                return false;
            }

            if (jObject.ContainsKey("AnalysisPeriod"))
            {
                analysisPeriod = new AnalysisPeriod(jObject["AnalysisPeriod"] as JsonObject);
            }

            if (jObject.ContainsKey("SkyModel"))
            {
                Enum.TryParse(jObject["SkyModel"]?.GetValue<string>(), out skyModel);
            }

            if (jObject.ContainsKey("GridSize"))
            {
                gridSize = jObject["GridSize"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("SunAngleStep"))
            {
                sunAngleStep = jObject["SunAngleStep"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("Albedo"))
            {
                albedo = jObject["Albedo"]?.GetValue<double>() ?? double.NaN;
            }

            if (jObject.ContainsKey("TimeShiftInMinutes"))
            {
                timeShiftInMinutes = jObject["TimeShiftInMinutes"]?.GetValue<double>() ?? default;
            }

            if (jObject.ContainsKey("GrossArea"))
            {
                grossArea = jObject["GrossArea"]?.GetValue<double>() ?? double.NaN;
            }

            cellAreas = Doubles(jObject, "CellAreas");
            direct = Doubles(jObject, "Direct");
            diffuse = Doubles(jObject, "Diffuse");
            groundReflected = Doubles(jObject, "GroundReflected");
            sunlitHours = Doubles(jObject, "SunlitHours");

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

        public override JsonObject ToJsonObject()
        {
            JsonObject jObject = base.ToJsonObject();
            if (jObject == null)
            {
                return null;
            }

            if (analysisPeriod != null)
            {
                jObject.Add("AnalysisPeriod", analysisPeriod.ToJsonObject());
            }

            jObject.Add("SkyModel", skyModel.ToString());
            jObject.Add("GridSize", gridSize);
            jObject.Add("SunAngleStep", sunAngleStep);
            jObject.Add("Albedo", albedo);
            jObject.Add("TimeShiftInMinutes", timeShiftInMinutes);
            jObject.Add("GrossArea", grossArea);

            Add(jObject, "CellAreas", cellAreas);
            Add(jObject, "Direct", direct);
            Add(jObject, "Diffuse", diffuse);
            Add(jObject, "GroundReflected", groundReflected);
            Add(jObject, "SunlitHours", sunlitHours);

            if (dateTimes != null)
            {
                JsonArray jArray = new JsonArray();
                foreach (DateTime dateTime in dateTimes)
                {
                    jArray.Add(dateTime);
                }
                jObject.Add("DateTimes", jArray);
            }

            jObject.Add("MissedBinHours", missedBinHours);
            jObject.Add("BelowHorizonHours", belowHorizonHours);
            jObject.Add("MissingWeatherHours", missingWeatherHours);

            return jObject;
        }

        private double AreaWeighted(double[] values)
        {
            if (values == null || cellAreas == null)
            {
                return double.NaN;
            }

            double result = 0;
            for (int i = 0; i < values.Length; i++)
            {
                result += values[i] * cellAreas[i];
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
