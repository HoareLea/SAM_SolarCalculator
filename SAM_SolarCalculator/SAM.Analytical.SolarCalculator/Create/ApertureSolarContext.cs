// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// Default sun-altitude cutoff, radians (~2 deg). Numerically equal to Core.Tolerance.Angle
        /// but named independently: that one is a geometry tolerance, this is a physics gate.
        /// </summary>
        internal const double DefaultMinHorizonAngle = 0.0349066;

        /// <summary>
        /// Resolves everything the aperture workflows sit on: targets, context occluders, the two
        /// visibility calculations, the weather and the analysis year.
        ///
        /// This is the ONE place the reuse rule lives. A previous calculation stored on the model's
        /// SolarModel is reused only when every identity input matches — context and target
        /// geometry, grid size, sun-angle step, horizon cutoff, tolerances, site latitude/longitude
        /// and timezone, the timeline offset, the year and the cell count. Anything else rebuilds.
        /// Because Stage 4 irradiance and Stages 6-9 shading both come through here, a calculation
        /// paid for by one is available to the others.
        ///
        /// Weather precedence: supplied weatherData -> the model's own
        /// AnalyticalModelParameter.WeatherData -> null return. No weather, no analysis.
        ///
        /// The year is resolved against the weather: the requested year when the weather has it,
        /// otherwise the weather's own first year (the caller re-roots its period to
        /// <see cref="ApertureSolarContext.Year"/>).
        ///
        /// An unresolved timezone returns null rather than silently assuming Greenwich: the sun
        /// position would be wrong by the whole offset while every internal consistency check still
        /// passed, because the calculation would stay consistent with the wrong sun path.
        /// </summary>
        /// <param name="analyticalModel">Model; supplies geometry, and WeatherData when none is passed.</param>
        /// <param name="year">Requested analysis year; resolved against the weather data.</param>
        /// <param name="weatherData">Weather to use. Null = the WeatherData associated with the model.</param>
        /// <param name="apertureGuids">Null/empty = all apertures on sun-exposed external panels.</param>
        /// <param name="gridSize">Aperture analysis-grid size, m.</param>
        /// <param name="sunAngleStep">Angular resolution used to group similar sun positions, degrees.</param>
        /// <param name="recalculate">Force a rebuild even when a previous calculation could be reused.</param>
        /// <param name="timeShiftInMinutes">Sun-position sampling offset applied to each weather timestamp.</param>
        /// <param name="minHorizonAngle">Minimum sun altitude, RADIANS.</param>
        /// <param name="tolerance_Area">Area tolerance.</param>
        /// <param name="tolerance_Snap">Snap tolerance (also the ray-start offset).</param>
        /// <param name="tolerance_Angle">Angle tolerance, RADIANS.</param>
        /// <param name="tolerance_Distance">Distance tolerance.</param>
        public static ApertureSolarContext ApertureSolarContext(this AnalyticalModel analyticalModel, int year, WeatherData weatherData = null, IEnumerable<Guid> apertureGuids = null, double gridSize = 0.5, double sunAngleStep = 2.0, bool recalculate = false, double timeShiftInMinutes = 30.0, double minHorizonAngle = DefaultMinHorizonAngle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance)
        {
            if (analyticalModel == null || double.IsNaN(timeShiftInMinutes))
            {
                return null;
            }

            // Precedence: supplied weather first, then the model's own.
            if (weatherData == null)
            {
                weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            }

            if (weatherData == null)
            {
                return null;
            }

            WeatherYear weatherYear = weatherData[year];
            if (weatherYear == null)
            {
                weatherYear = weatherData.WeatherYears?.Find(x => x != null);
                if (weatherYear == null)
                {
                    return null;
                }

                year = weatherYear.Year;
            }

            List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(apertureGuids, gridSize, tolerance_Area, tolerance_Distance);
            if (targets == null || targets.Count == 0)
            {
                return null;
            }

            List<LinkedFace3D> occluders = Convert.ToSAM_OccluderLinkedFace3Ds(analyticalModel);
            if (occluders == null)
            {
                occluders = new List<LinkedFace3D>();
            }

            // Flatten the targets into the shared cell space.
            List<AnalysisCell> cells = new List<AnalysisCell>();
            List<Vector3D> cellNormals = new List<Vector3D>();
            foreach (ApertureSolarTarget target in targets)
            {
                List<AnalysisCell> targetCells = target.AnalysisCells;
                Vector3D outward = target.OutwardNormal;
                foreach (AnalysisCell cell in targetCells)
                {
                    cells.Add(cell);
                    cellNormals.Add(outward);
                }
            }

            if (cells.Count == 0)
            {
                return null;
            }

            Core.Location location = weatherData.Location ?? analyticalModel.Location;
            if (location == null)
            {
                return null;
            }

            double timeZoneOffset = Geometry.SolarCalculator.Query.TimeZoneOffset(location);
            if (double.IsNaN(timeZoneOffset))
            {
                return null;
            }

            string contextGeometryHash = Geometry.SolarCalculator.Query.GeometryHash(occluders, tolerance_Distance);
            string targetGeometryHash = Geometry.SolarCalculator.Query.TargetHash(cells, tolerance_Distance);

            SolarModel solarModel = analyticalModel.GetValue<SolarModel>(AnalyticalModelParameter.SolarModel);
            SolarVisibilityCache solarVisibilityCache = recalculate ? null : solarModel?.GetValue<SolarVisibilityCache>(SolarModelParameter.SolarVisibilityCache);
            SkyVisibilityCache skyVisibilityCache = recalculate ? null : solarModel?.GetValue<SkyVisibilityCache>(SolarModelParameter.SkyVisibilityCache);

            if (solarVisibilityCache != null && !solarVisibilityCache.Matches(contextGeometryHash, targetGeometryHash, gridSize, sunAngleStep, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, location.Latitude, location.Longitude, timeZoneOffset, timeShiftInMinutes, year, cells.Count))
            {
                solarVisibilityCache = null;
            }

            if (skyVisibilityCache != null && !skyVisibilityCache.Matches(contextGeometryHash, targetGeometryHash, gridSize, SkyPatchSubdivision.Tregenza145, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, cells.Count))
            {
                skyVisibilityCache = null;
            }

            bool reusedPreviousCalculation = false;
            if (solarVisibilityCache == null || skyVisibilityCache == null)
            {
                solarVisibilityCache = Weather.SolarCalculator.Create.SolarVisibilityCache(location, year, sunAngleStep, occluders, cells, gridSize, minHorizonAngle, timeShiftInMinutes, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance);
                skyVisibilityCache = Weather.SolarCalculator.Create.SkyVisibilityCache(occluders, cells, gridSize, SkyPatchSubdivision.Tregenza145, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance);
                if (solarVisibilityCache == null || skyVisibilityCache == null)
                {
                    return null;
                }

                if (solarModel == null)
                {
                    solarModel = new SolarModel(location);
                    foreach (LinkedFace3D occluder in occluders)
                    {
                        solarModel.Add(occluder);
                    }

                    analyticalModel.SetValue(AnalyticalModelParameter.SolarModel, solarModel);
                }

                solarModel.SetValue(SolarModelParameter.SolarVisibilityCache, solarVisibilityCache);
                solarModel.SetValue(SolarModelParameter.SkyVisibilityCache, skyVisibilityCache);
            }
            else
            {
                reusedPreviousCalculation = true;
            }

            return new ApertureSolarContext(targets, cells, cellNormals, occluders, solarVisibilityCache, skyVisibilityCache, weatherData, location, year, gridSize, sunAngleStep, timeShiftInMinutes, contextGeometryHash, targetGeometryHash, reusedPreviousCalculation);
        }

        /// <summary>
        /// The <see cref="SunTimeConvention"/> overload: the timeline convention rather than a raw
        /// offset. Prefer it — the semantics are then self-describing and stored with the result.
        /// </summary>
        public static ApertureSolarContext ApertureSolarContext(this AnalyticalModel analyticalModel, int year, WeatherData weatherData, IEnumerable<Guid> apertureGuids, double gridSize, double sunAngleStep, bool recalculate, SunTimeConvention sunTimeConvention, double minHorizonAngle = DefaultMinHorizonAngle, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance)
        {
            double timeShiftInMinutes = sunTimeConvention.TimeShiftInMinutes();
            if (double.IsNaN(timeShiftInMinutes))
            {
                return null;
            }

            return ApertureSolarContext(analyticalModel, year, weatherData, apertureGuids, gridSize, sunAngleStep, recalculate, timeShiftInMinutes, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance);
        }

        /// <summary>
        /// Why a solar context could not be built, or null when one could.
        ///
        /// Stage 11 added this because "the context is null" was the end of the story for every
        /// cause, and the causes are not alike: a grid size finer than the geometry tolerance can
        /// represent is a typo in one input, an opening below the area tolerance is a modelling
        /// problem, and a missing timezone is a site problem. Kept as a separate diagnostic pass
        /// rather than threaded through the build so the hot path is untouched — it runs only once
        /// something has already failed, where an extra pass over the apertures costs nothing.
        /// </summary>
        /// <param name="analyticalModel">The model that was asked for.</param>
        /// <param name="apertureGuids">The aperture selection that was asked for. Null = the default set.</param>
        /// <param name="gridSize">The grid size that was asked for, m.</param>
        /// <param name="weatherData">Weather, if one was supplied.</param>
        /// <param name="tolerance_Area">Area tolerance the cells would be built to, m2.</param>
        public static string ApertureSolarContextFailureReason(this AnalyticalModel analyticalModel, IEnumerable<Guid> apertureGuids, double gridSize, WeatherData weatherData = null, double tolerance_Area = Core.Tolerance.MacroDistance)
        {
            if (analyticalModel == null)
            {
                return "No model was supplied.";
            }

            // The grid size first: it is the only cause that is a property of the request alone,
            // and if it is the cause then nothing about the model is worth reporting.
            if (Query.GridSizeValidity(gridSize, out string gridSizeMessage, tolerance_Area) != GridSizeValidity.Valid)
            {
                return gridSizeMessage;
            }

            if (weatherData == null)
            {
                weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            }

            if (weatherData == null)
            {
                return "No weather data. Supply WeatherData, or attach it to the AnalyticalModel.";
            }

            ApertureSolarTargets(analyticalModel, apertureGuids, gridSize, out string targetsMessage, tolerance_Area);
            if (targetsMessage != null)
            {
                return targetsMessage;
            }

            Core.Location location = weatherData.Location ?? analyticalModel.Location;
            if (location == null)
            {
                return "The site location is missing, so the sun position cannot be found. Set the location on the model or on the weather data.";
            }

            if (double.IsNaN(Geometry.SolarCalculator.Query.TimeZoneOffset(location)))
            {
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "The site location (latitude {0:0.###}, longitude {1:0.###}) does not resolve to a time zone, so the sun cannot be placed on the weather timeline.",
                    location.Latitude, location.Longitude);
            }

            return null;
        }
    }
}
