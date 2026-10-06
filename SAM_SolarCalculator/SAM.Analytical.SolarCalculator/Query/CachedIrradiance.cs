// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Core.SolarCalculator;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// The cheap re-weighting stage: evaluates per-cell irradiance for any AnalysisPeriod from
        /// the two visibility caches with NO geometric pass. Per weather-timeline hour h:
        ///   direct  = DNI(h) * cos(thetaI) * lit(cell, bin(h))
        ///   diffuse = DHI(h) * [(1-F1)*SVF + F1*(a/b)*lit + F2*sinB*HVF]   (Perez, component-aware:
        ///             the isotropic term uses the sky view factor, the circumsolar term the
        ///             direct-beam lit bit at the sun's own direction, the horizon term the
        ///             horizon-band visibility)
        ///           = DHI(h) * SVF                                        (isotropic)
        ///   ground  = GHI(h) * albedo * GVF
        /// integrated over whole hours and converted W/m2 -> kWh/m2 (x 1 h / 1000), clamped so a
        /// component can never go negative.
        ///
        /// Beam-horizontal radiation is derived explicitly as (global - diffuse), read from the RAW
        /// GlobalSolarRadiation and DiffuseSolarRadiation fields. The WeatherHour.Calculated*
        /// helpers are deliberately NOT used: each of them falls back to DirectSolarRadiation when
        /// its own field is absent, which would reintroduce exactly the ambiguity B6 removes (SAM's
        /// EPW importer never populates the direct field, and its semantics vary by creation route —
        /// beam-horizontal from a SAM-internal producer, true DNI from an EPW field 14 reader).
        /// An hour missing either raw field is counted in MissingWeatherHours rather than silently
        /// reinterpreted. DNI = beamHorizontal / sin(elevation), capped at 5 degrees elevation.
        ///
        /// The sun position is sampled at h + SolarVisibilityCache.SunPositionShiftInMinutes — the
        /// shift is read FROM THE CACHE so the bin-construction and evaluation timelines are always
        /// consistent (bin misses are counted, never silently tolerated: see
        /// CachedIrradianceResult.MissedBinHours). Weather values are read at h.
        /// </summary>
        /// <param name="solarVisibilityCache">Direct-beam cache (sun bins x cells lit bits).</param>
        /// <param name="skyVisibilityCache">Sky/horizon/ground cache. Optional: when null, unobstructed view factors are assumed.</param>
        /// <param name="weatherData">Hourly weather; its Location drives the sun position (fractional time zone preserved).</param>
        /// <param name="analysisPeriod">Hours to integrate. Should be within the cache's year.</param>
        /// <param name="cellNormals">Outward unit normals, one per cache cell, in cache cell order.</param>
        /// <param name="skyModel">Isotropic or PerezAnisotropic diffuse.</param>
        /// <param name="albedo">Ground reflectance.</param>
        public static CachedIrradianceResult CachedIrradiance(this SolarVisibilityCache solarVisibilityCache, SkyVisibilityCache skyVisibilityCache, WeatherData weatherData, AnalysisPeriod analysisPeriod, IList<Vector3D> cellNormals, SkyModel skyModel, double albedo = 0.2)
        {
            if (solarVisibilityCache == null || weatherData == null || analysisPeriod == null || cellNormals == null || albedo < 0)
            {
                return null;
            }

            int cellCount = solarVisibilityCache.CellCount;
            if (cellNormals.Count != cellCount || skyModel == SkyModel.Undefined)
            {
                return null;
            }

            if (skyVisibilityCache != null && skyVisibilityCache.CellCount != cellCount)
            {
                return null;
            }

            Core.Location location = weatherData.Location;
            if (location == null)
            {
                return null;
            }

            List<int> hoursOfYear = analysisPeriod.HoursOfYear();
            if (hoursOfYear == null)
            {
                return null;
            }

            double[] direct = new double[cellCount];
            double[] diffuse = new double[cellCount];
            double[] groundReflected = new double[cellCount];
            double[] sunlitHours = new double[cellCount];

            // Per-cell fixed quantities: tilt factors and (cache-free) view factors.
            Vector3D[] normals = new Vector3D[cellCount];
            double[] cosBeta = new double[cellCount];
            double[] sinBeta = new double[cellCount];
            double[] skyViewFactors = new double[cellCount];
            double[] horizonViewFactors = new double[cellCount];
            double[] groundViewFactors = new double[cellCount];
            for (int c = 0; c < cellCount; c++)
            {
                Vector3D normal = cellNormals[c]?.Unit;
                normals[c] = normal;
                double nz = normal == null ? 0 : Math.Max(-1.0, Math.Min(1.0, normal.Z));
                cosBeta[c] = nz;   // tilt of the receiving side from horizontal: cosB = normal . +Z
                sinBeta[c] = Math.Sqrt(Math.Max(0.0, 1.0 - nz * nz));

                skyViewFactors[c] = skyVisibilityCache != null ? skyVisibilityCache.SkyViewFactor(c) : (1.0 + nz) / 2.0;
                horizonViewFactors[c] = skyVisibilityCache != null ? skyVisibilityCache.HorizonViewFactor(c) : 1.0;
                groundViewFactors[c] = skyVisibilityCache != null ? skyVisibilityCache.GroundViewFactor(c) : (1.0 - nz) / 2.0;
            }

            double minHorizonAngleDegrees = solarVisibilityCache.MinHorizonAngle * 180.0 / Math.PI;
            double timeShiftInMinutes = solarVisibilityCache.SunPositionShiftInMinutes;

            int missedBinHours = 0;
            int belowHorizonHours = 0;
            int missingWeatherHours = 0;

            List<DateTime> evaluated = new List<DateTime>();
            DateTime yearStart = new DateTime(analysisPeriod.Year, 1, 1);

            foreach (int hourOfYear in hoursOfYear)
            {
                DateTime dateTime = yearStart.AddHours(hourOfYear);

                WeatherHour weatherHour = weatherData.GetWeatherHour(dateTime);
                if (weatherHour == null)
                {
                    missingWeatherHours++;
                    continue;
                }

                // RAW fields only. WeatherHour.CalculatedGlobalSolarRadiation() would fall back to
                // (direct + diffuse) and CalculatedDiffuseSolarRadiation() to (global - direct):
                // both consume the ambiguous DirectSolarRadiation field. See remarks (B6).
                double globalSolarRadiation = weatherHour.GlobalSolarRadiation;
                double diffuseSolarRadiation = weatherHour.DiffuseSolarRadiation;
                if (double.IsNaN(globalSolarRadiation) || double.IsNaN(diffuseSolarRadiation))
                {
                    missingWeatherHours++;
                    continue;
                }

                // Sun position on the timeline the cache's bins were built from. Query.TryGetSunAngles
                // is the SAME angle source the bins were built with (Angle.Radians, no rounding, no
                // vector round-trip), so bin membership and bin lookup cannot drift apart.
                DateTime sunTime = timeShiftInMinutes == 0 ? dateTime : dateTime.AddMinutes(timeShiftInMinutes);
                if (!Geometry.SolarCalculator.Query.TryGetSunAngles(location, sunTime, out double elevationDegrees, out double azimuthDegrees))
                {
                    missingWeatherHours++;
                    continue;
                }

                double elevationRadians = elevationDegrees * Math.PI / 180.0;
                double azimuthRadians = azimuthDegrees * Math.PI / 180.0;

                if (elevationDegrees < minHorizonAngleDegrees)
                {
                    belowHorizonHours++;
                    continue;
                }

                int binIndex = solarVisibilityCache.FindBin(elevationDegrees, azimuthDegrees);
                if (binIndex < 0)
                {
                    // Should not happen when the cache was built for this shift/year; counted loudly
                    // rather than silently skipping the hour.
                    missedBinHours++;
                    continue;
                }

                // Unit vector from the surface toward the sun (compass convention, +Y = north).
                double sinElevation = Math.Sin(elevationRadians);
                double sunX = Math.Cos(elevationRadians) * Math.Sin(azimuthRadians);
                double sunY = Math.Cos(elevationRadians) * Math.Cos(azimuthRadians);
                double sunZ = sinElevation;

                // Beam-horizontal is derived explicitly from global - diffuse inside
                // Query.DirectNormalIrradiance (see remarks); the direct-solar field is never read,
                // on any code path. That query is the one implementation of this conversion, shared
                // with the aperture-plane hourly evaluation.
                double directNormalIrradiance = DirectNormalIrradiance(globalSolarRadiation, diffuseSolarRadiation, elevationDegrees);

                double f1 = 0;
                double f2 = 0;
                double b = 1;
                if (skyModel == SkyModel.PerezAnisotropic)
                {
                    if (!Geometry.SolarCalculator.Query.TryGetPerezCoefficients(directNormalIrradiance, diffuseSolarRadiation, elevationDegrees, sunTime.DayOfYear, out f1, out f2, out _, out _))
                    {
                        // Unreachable for a valid hour (elevation is already > minHorizonAngle > 0 and
                        // both radiation values are non-NaN), so the only way here is a degenerate
                        // weather value. Counted, never silently dropped.
                        missingWeatherHours++;
                        continue;
                    }

                    b = Math.Max(Math.Cos(85.0 * Math.PI / 180.0), sinElevation);
                }

                evaluated.Add(dateTime);

                for (int c = 0; c < cellCount; c++)
                {
                    Vector3D normal = normals[c];
                    if (normal == null)
                    {
                        continue;
                    }

                    double cosThetaI = normal.X * sunX + normal.Y * sunY + normal.Z * sunZ;
                    bool lit = cosThetaI > 0 && solarVisibilityCache.IsLit(binIndex, c);

                    if (lit)
                    {
                        // Wh/m2 this hour; /1000 below when storing kWh/m2.
                        direct[c] += directNormalIrradiance * cosThetaI;
                        sunlitHours[c] += 1.0;
                    }

                    double diffuseHour;
                    if (skyModel == SkyModel.PerezAnisotropic)
                    {
                        double a = Math.Max(0.0, cosThetaI);
                        diffuseHour = diffuseSolarRadiation *
                            ((1.0 - f1) * skyViewFactors[c] +
                             f1 * (a / b) * (lit ? 1.0 : 0.0) +
                             f2 * sinBeta[c] * horizonViewFactors[c]);
                    }
                    else
                    {
                        diffuseHour = diffuseSolarRadiation * skyViewFactors[c];
                    }

                    // Perez's (1-F1) can go negative under very clear skies; a component of the
                    // composed irradiance can never be physically negative.
                    diffuse[c] += Math.Max(0.0, diffuseHour);
                    groundReflected[c] += Math.Max(0.0, globalSolarRadiation * albedo * groundViewFactors[c]);
                }
            }

            // W/m2 over 1 h -> Wh/m2 (x1) -> kWh/m2 (/1000). The timestep is exactly one hour
            // (AnalysisPeriod is hourly); do not apply any further timestep or area factor here.
            const double toKwh = 1.0 / 1000.0;
            for (int c = 0; c < cellCount; c++)
            {
                direct[c] *= toKwh;
                diffuse[c] *= toKwh;
                groundReflected[c] *= toKwh;
            }

            return new CachedIrradianceResult(evaluated, direct, diffuse, groundReflected, sunlitHours, missedBinHours, belowHorizonHours, missingWeatherHours);
        }
    }
}
