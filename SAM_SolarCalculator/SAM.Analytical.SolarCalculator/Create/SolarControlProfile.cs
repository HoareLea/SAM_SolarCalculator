// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Core.SolarCalculator;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// The hourly shading control schedule of one aperture, from weather alone — no geometry
        /// pass, no ray tracing, no analytical model.
        ///
        /// For every hour of the weather year the aperture-plane direct beam is evaluated from the
        /// window's own orientation (see <see cref="ApertureSolarHour"/>), the control rule is
        /// applied, and the hours that qualify are collected. The cost is one sun-position
        /// evaluation per hour and nothing else — on the order of a second for a whole
        /// aperture-year, against minutes for a single ray-tracing pass. Restrict it with an
        /// AnalysisPeriod when only part of the year is of interest.
        ///
        /// THE RULE, exactly as applied:
        ///
        ///   daylight        = solar elevation &gt; 0. A property of the SITE.
        ///   aperture sun    = daylight AND the sun is in FRONT of this opening AND the beam on its
        ///                     plane is non-zero. A property of the WINDOW, and the gate on
        ///                     EVERYTHING below: shading a window the sun is behind achieves
        ///                     nothing, so no criterion is even evaluated outside these hours.
        ///   solar demand    = aperture sun AND aperture-plane beam &gt;= MinimumApertureIrradiance.
        ///   temperature     = aperture sun AND outdoor dry-bulb &gt;= MinimumOutdoorTemperature,
        ///                     when in use.
        ///   shade demand    = aperture sun AND, when the temperature criterion is in use,
        ///                     And: solar threshold met AND temperature threshold met
        ///                     Or : solar threshold met OR  temperature threshold met
        ///                     With the temperature criterion NOT in use, both reduce to the solar
        ///                     demand — an absent criterion can neither block an AND nor satisfy
        ///                     an OR.
        ///                     The aperture-sun gate sits OUTSIDE the OR deliberately: without it a
        ///                     hot summer afternoon would request shading for a façade facing
        ///                     completely away from the sun.
        ///   wind safe       = no constraint, OR the recorded wind speed &lt;= MaximumWindSpeed, OR
        ///                     the hour carries no wind speed at all (see below).
        ///   shade on        = shade demand AND wind safe.
        ///
        /// MISSING VALUES ARE NEVER INVENTED, and a criterion that cannot be evaluated is set
        /// aside rather than decided:
        ///   a missing dry-bulb DOES NOT satisfy the temperature criterion — no demand is
        ///   asserted from data that is not there — and it is counted in MissingTemperatureHours;
        ///   a missing wind speed makes the wind criterion UNAVAILABLE for that hour, not
        ///   violated. The hour is left operable and counted in MissingWindSpeedHours; it is
        ///   NEVER reported as a high-wind hour. This is an ANNUAL DESIGN PREPROCESSOR, not a
        ///   live safety controller: turning an absent reading into a retraction would understate
        ///   what a device can do all year on the strength of a gap in the weather file, and the
        ///   diagnostic count is there to make that gap visible instead.
        ///   An hour whose radiation fields or sun position are unusable is skipped entirely and
        ///   counted in MissingWeatherHours.
        /// </summary>
        /// <param name="target">The aperture. Its OUTWARD normal is what makes the result orientation-specific.</param>
        /// <param name="weatherData">Hourly weather; its Location drives the sun position.</param>
        /// <param name="solarControlSettings">The control rule. Null = the default rule (solar threshold only).</param>
        /// <param name="year">Weather year. Values not present in the weather fall back to its first year.</param>
        /// <param name="analysisPeriod">Optional restriction to part of the year. Null = the whole year. A period built for a different year is used for its HOUR-OF-YEAR structure on this year, the same way Create.ReRoot treats one.</param>
        /// <param name="sunTimeConvention">Timestamp convention of the weather timeline. IntervalStart (+30 min) is the SAM/EPW convention and the default everywhere else in this library.</param>
        public static SolarControlProfile SolarControlProfile(this ApertureSolarTarget target, WeatherData weatherData, SolarControlSettings solarControlSettings = null, int year = -1, AnalysisPeriod analysisPeriod = null, SunTimeConvention sunTimeConvention = SunTimeConvention.IntervalStart)
        {
            // No context visibility: the historical weather-only behaviour, preserved exactly. A null
            // cache leaves the aperture-sun gate as pure geometry (sun in front + non-zero beam), so
            // nothing about the existing public contract changes.
            return SolarControlProfile(target, weatherData, solarControlSettings, null, 0, year, analysisPeriod, sunTimeConvention);
        }

        /// <summary>
        /// The hourly shading control schedule with CONTEXT VISIBILITY: the same rule as the
        /// weather-only overload, but the aperture-sun gate additionally requires direct sun to
        /// actually reach the aperture through the surroundings, read from an already-built
        /// <see cref="SolarVisibilityCache"/>.
        ///
        /// The cache is the SAME one the aperture-irradiance and shading workflows use, built once
        /// over the production aperture cells (<see cref="ApertureSolarTarget.AnalysisCells"/>) and the
        /// context occluders. Nothing is rebuilt here: the aperture's per-bin visibility is precomputed
        /// once from the cache's lit bits and each hour is then a single bin lookup.
        ///
        /// THE CONTEXT GATE. An hour whose sun is in front of the opening and carries a non-zero beam
        /// still does NOT count as an aperture-sun hour unless at least one analysis cell is lit for
        /// that hour's sun bin. Context-shaded solar is therefore never treated as available direct
        /// solar, and every criterion downstream (solar, temperature, wind, shade demand, shade on) is
        /// gated on the same aperture-sun hours — exactly as in the weather-only path.
        ///
        /// SEMANTICS ARE OTHERWISE UNCHANGED. The full-year geometric vs operation-weighted
        /// distinction of the shading-operation profile is untouched; wind still never enters the
        /// weights; the year, hour-of-year numbering, weather-interval convention, sun-position shift,
        /// timezone and minimum horizon angle are read from the cache and must match the profile's own
        /// timeline. A cache whose year or sun-position shift disagrees with the resolved timeline is
        /// refused (null), never approximated.
        ///
        /// With <paramref name="solarVisibilityCache"/> null this is EXACTLY the weather-only
        /// behaviour: no visibility gating is applied and every hour the sun is in front of the
        /// opening is an aperture-sun hour.
        /// </summary>
        /// <param name="target">The aperture. Its outward normal and analysis cells drive the result.</param>
        /// <param name="weatherData">Hourly weather; its Location drives the sun position.</param>
        /// <param name="solarControlSettings">The control rule. Null = the default rule (solar threshold only).</param>
        /// <param name="solarVisibilityCache">Visibility with context, built for the same aperture cells, timeline and year. Null = no context (full visibility).</param>
        /// <param name="cellIndexOffset">This target's first cell index within the cache's shared cell space. 0 when the cache covers only this aperture.</param>
        /// <param name="year">Weather year. Values not present in the weather fall back to its first year.</param>
        /// <param name="analysisPeriod">Optional restriction to part of the year. Null = the whole year.</param>
        /// <param name="sunTimeConvention">Timestamp convention of the weather timeline. IntervalStart (+30 min) is the SAM/EPW convention.</param>
        public static SolarControlProfile SolarControlProfile(this ApertureSolarTarget target, WeatherData weatherData, SolarControlSettings solarControlSettings, SolarVisibilityCache solarVisibilityCache, int cellIndexOffset = 0, int year = -1, AnalysisPeriod analysisPeriod = null, SunTimeConvention sunTimeConvention = SunTimeConvention.IntervalStart)
        {
            if (weatherData == null)
            {
                return null;
            }

            double timeShiftInMinutes = sunTimeConvention.TimeShiftInMinutes();
            if (double.IsNaN(timeShiftInMinutes))
            {
                return null;
            }

            // Same resolution rule as Create.ApertureSolarContext: a year the weather does not carry
            // falls back to the weather's own first year rather than producing an empty result.
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

            return SolarControlProfile(target, weatherData, solarControlSettings, year, timeShiftInMinutes, analysisPeriod, solarVisibilityCache, cellIndexOffset);
        }

        /// <summary>
        /// The context-aware control schedule for one aperture, reusing a production
        /// <see cref="ApertureSolarContext"/>: the target, its analysis cells, the
        /// <see cref="SolarVisibilityCache"/>, the cell-space offset, the weather, the year and the
        /// timeline shift all come from the context, so the profile is guaranteed to sit on the SAME
        /// visibility representation the aperture-irradiance and shading workflows use — no geometry
        /// is reconstructed and the cache is not rebuilt.
        /// </summary>
        /// <param name="context">The resolved aperture solar context (targets + cells + visibility cache).</param>
        /// <param name="apertureGuid">The aperture within the context to produce the schedule for.</param>
        /// <param name="solarControlSettings">The control rule. Null = the default rule (solar threshold only).</param>
        /// <param name="analysisPeriod">Optional restriction to part of the year. Null = the whole year.</param>
        public static SolarControlProfile SolarControlProfile(this ApertureSolarContext context, Guid apertureGuid, SolarControlSettings solarControlSettings = null, AnalysisPeriod analysisPeriod = null)
        {
            if (context == null)
            {
                return null;
            }

            ApertureSolarTarget target = context.Target(apertureGuid);
            if (target == null)
            {
                return null;
            }

            int cellIndexOffset = context.CellIndexOffset(apertureGuid);
            if (cellIndexOffset < 0)
            {
                return null;
            }

            return SolarControlProfile(target, context.WeatherData, solarControlSettings, context.Year, context.TimeShiftInMinutes, analysisPeriod, context.SolarVisibilityCache, cellIndexOffset);
        }

        private static SolarControlProfile SolarControlProfile(ApertureSolarTarget target, WeatherData weatherData, SolarControlSettings solarControlSettings, int year, double timeShiftInMinutes, AnalysisPeriod analysisPeriod, SolarVisibilityCache solarVisibilityCache, int cellIndexOffset)
        {
            if (target == null || weatherData == null || double.IsNaN(timeShiftInMinutes))
            {
                return null;
            }

            SolarControlSettings settings = solarControlSettings == null ? new SolarControlSettings() : new SolarControlSettings(solarControlSettings);
            if (!settings.IsValid(out string _))
            {
                return null;
            }

            // Context visibility: when a cache is supplied, the schedule and the cache must describe
            // the same window and the same timeline, otherwise the answer would be about a different
            // sun. A mismatch is refused, never approximated.
            bool contextInUse = solarVisibilityCache != null;
            bool[] apertureVisibleByBin = null;
            if (contextInUse)
            {
                if (solarVisibilityCache.Year != year
                    || Math.Abs(solarVisibilityCache.SunPositionShiftInMinutes - timeShiftInMinutes) > 1e-6
                    || cellIndexOffset < 0
                    || cellIndexOffset + target.CellCount > solarVisibilityCache.CellCount)
                {
                    return null;
                }

                apertureVisibleByBin = ApertureVisibilityByBin(solarVisibilityCache, target.CellCount, cellIndexOffset);
                if (apertureVisibleByBin == null)
                {
                    return null;
                }
            }

            List<ApertureSolarHour> apertureSolarHours = target.ApertureSolarHours(weatherData, year, timeShiftInMinutes, analysisPeriod);
            if (apertureSolarHours == null)
            {
                return null;
            }

            int hourCount = analysisPeriod == null
                ? (DateTime.IsLeapYear(year) ? 8784 : 8760)
                : (analysisPeriod.HoursOfYear()?.Count ?? 0);

            List<int> daylightHoursOfYear = new List<int>();
            List<int> apertureSunHoursOfYear = new List<int>();
            List<int> solarDemandHoursOfYear = new List<int>();
            List<int> temperatureDemandHoursOfYear = new List<int>();
            List<int> windSafeHoursOfYear = new List<int>();
            List<int> shadeDemandHoursOfYear = new List<int>();
            List<int> shadeOnHoursOfYear = new List<int>();
            List<int> highWindHoursOfYear = new List<int>();
            Dictionary<int, double> weightByHourOfYear = new Dictionary<int, double>();

            int missingTemperatureHours = 0;
            int missingWindSpeedHours = 0;

            bool temperatureInUse = settings.TemperatureCriterionInUse;
            bool windInUse = settings.WindConstraintInUse;

            foreach (ApertureSolarHour apertureSolarHour in apertureSolarHours)
            {
                if (!apertureSolarHour.SunAboveHorizon)
                {
                    continue;
                }

                int hourOfYear = apertureSolarHour.HourOfYear;
                daylightHoursOfYear.Add(hourOfYear);

                // ---- the aperture-sun gate. Daylight belongs to the site; sun ON THIS WINDOW is
                // what a shading device can act on. Everything below is evaluated only here, so a
                // hot hour can never request shading for a façade the sun is behind.
                if (!apertureSolarHour.SunInFrontOfAperture || !(apertureSolarHour.ApertureDirectIrradiance > 0))
                {
                    continue;
                }

                // ---- context visibility: the sun is in front and carries beam, but it must also
                // actually reach the aperture through the surroundings. A fully context-shaded
                // aperture is not "sun on the window", so every criterion below is gated on it too.
                if (contextInUse)
                {
                    int binIndex = solarVisibilityCache.FindBin(apertureSolarHour.SolarElevation, apertureSolarHour.SolarAzimuth);
                    if (binIndex < 0 || !apertureVisibleByBin[binIndex])
                    {
                        continue;
                    }
                }

                apertureSunHoursOfYear.Add(hourOfYear);

                // ---- solar criterion: the DIRECT beam on this window's own plane.
                bool solarDemand = apertureSolarHour.ApertureDirectIrradiance >= settings.MinimumApertureIrradiance;
                if (solarDemand)
                {
                    solarDemandHoursOfYear.Add(hourOfYear);
                }

                // ---- temperature criterion, when in use. A missing value asserts nothing.
                bool temperatureDemand = false;
                if (temperatureInUse)
                {
                    double dryBulbTemperature = apertureSolarHour.DryBulbTemperature;
                    if (double.IsNaN(dryBulbTemperature))
                    {
                        missingTemperatureHours++;
                    }
                    else
                    {
                        temperatureDemand = dryBulbTemperature >= settings.MinimumOutdoorTemperature;
                        if (temperatureDemand)
                        {
                            temperatureDemandHoursOfYear.Add(hourOfYear);
                        }
                    }
                }

                // ---- the combination. A criterion not in use takes no part in it.
                bool shadeDemand;
                if (!temperatureInUse)
                {
                    shadeDemand = solarDemand;
                }
                else if (settings.ControlLogic == SolarControlLogic.Or)
                {
                    shadeDemand = solarDemand || temperatureDemand;
                }
                else
                {
                    shadeDemand = solarDemand && temperatureDemand;
                }

                // ---- wind: an OPERATING constraint, evaluated independently of the demand.
                bool windSafe = true;
                if (windInUse)
                {
                    double windSpeed = apertureSolarHour.WindSpeed;
                    if (double.IsNaN(windSpeed))
                    {
                        // The criterion is UNAVAILABLE this hour, not violated. The absent
                        // reading is counted and the hour is left operable; it is never called
                        // high wind.
                        missingWindSpeedHours++;
                    }
                    else
                    {
                        windSafe = windSpeed <= settings.MaximumWindSpeed;
                    }
                }

                if (windSafe)
                {
                    windSafeHoursOfYear.Add(hourOfYear);
                }

                if (!shadeDemand)
                {
                    continue;
                }

                shadeDemandHoursOfYear.Add(hourOfYear);

                // Phase 1 weights are binary. They follow the DEMAND, never the deployment: wind is
                // a hardware limit, not a statement about whether the solar was welcome.
                weightByHourOfYear[hourOfYear] = 1.0;

                if (windSafe)
                {
                    shadeOnHoursOfYear.Add(hourOfYear);
                }
                else
                {
                    highWindHoursOfYear.Add(hourOfYear);
                }
            }

            return new SolarControlProfile(
                target.ApertureGuid, year, timeShiftInMinutes, settings,
                daylightHoursOfYear, apertureSunHoursOfYear, solarDemandHoursOfYear, temperatureDemandHoursOfYear, windSafeHoursOfYear,
                shadeDemandHoursOfYear, shadeOnHoursOfYear, highWindHoursOfYear, weightByHourOfYear,
                apertureSolarHours.Count, Math.Max(0, hourCount - apertureSolarHours.Count), missingTemperatureHours, missingWindSpeedHours);
        }

        /// <summary>
        /// One flag per sun bin: true when at least one of this aperture's analysis cells is lit by
        /// direct beam in that bin. Precomputed once so the hourly loop is a single bin lookup — the
        /// lit bits are never scanned per hour and the cache is never rebuilt.
        /// </summary>
        private static bool[] ApertureVisibilityByBin(SolarVisibilityCache solarVisibilityCache, int cellCount, int cellIndexOffset)
        {
            List<SunBin> bins = solarVisibilityCache.Bins;
            if (bins == null)
            {
                return null;
            }

            bool[] result = new bool[bins.Count];
            for (int b = 0; b < bins.Count; b++)
            {
                for (int c = 0; c < cellCount; c++)
                {
                    if (solarVisibilityCache.IsLit(b, cellIndexOffset + c))
                    {
                        result[b] = true;
                        break;
                    }
                }
            }

            return result;
        }
    }
}
