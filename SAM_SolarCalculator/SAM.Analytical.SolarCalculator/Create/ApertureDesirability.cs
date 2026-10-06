// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// Stage 5: the desirability-weighted direct-solar energy of one aperture, per sun group.
        ///
        /// For every weather-timeline hour h belonging to sun group g of the cache:
        ///   energy(h) = DNI(h) x max(0, cos thetaI(h)) x 1 h / 1000        [kWh/m2]
        ///   DNI(h)    = max(0, GHI(h) - DHI(h)) / max(sin(elevation), sin 5 deg)
        /// exactly as Query.CachedIrradiance derives them (raw GHI/DHI fields only — the ambiguous
        /// DirectSolarRadiation field is never read; the sun is sampled at h + the shift recorded
        /// on the cache, so desirability and irradiance can never sit on different timelines).
        /// The per-hour solar arithmetic itself lives in Create.ApertureSolarHour, which is the one
        /// implementation this and the hourly SolarControlProfile both use.
        ///
        /// With w(h) = desirabilityStrategy.Weight(h):
        ///   DirectEnergy[g]   += energy(h)                    (all hours)
        ///   UnwantedEnergy[g] += w(h) x energy(h)             (hours with w &gt; 0)
        ///   WantedEnergy[g]   += -w(h) x energy(h)            (hours with w &lt; 0)
        ///
        /// Context visibility is deliberately NOT applied here: desirability is a property of the
        /// sun's energy toward the aperture; Stage 6 combines it with the lit bits so a voxel never
        /// gets credit for blocking sun that context already blocks. A zero-energy hour (night,
        /// back-facing sun, fully overcast) contributes exactly zero regardless of its weight —
        /// desirability is ENERGY-weighted, not hour-counted.
        /// </summary>
        /// <param name="target">The aperture (its outward normal defines cos thetaI).</param>
        /// <param name="solarVisibilityCache">Sun groups + timeline convention the weighting aligns to.</param>
        /// <param name="desirabilityStrategy">Signed per-hour weighting strategy.</param>
        /// <param name="weatherData">Hourly weather; its Location drives the sun position.</param>
        public static ApertureDesirability ApertureDesirability(this ApertureSolarTarget target, SolarVisibilityCache solarVisibilityCache, IDesirabilityStrategy desirabilityStrategy, WeatherData weatherData)
        {
            if (target == null || solarVisibilityCache == null || desirabilityStrategy == null || weatherData == null)
            {
                return null;
            }

            Vector3D outward = target.OutwardNormal?.Unit;
            if (outward == null || !outward.IsValid())
            {
                return null;
            }

            Core.Location location = weatherData.Location;
            if (location == null)
            {
                return null;
            }

            List<SunBin> bins = solarVisibilityCache.Bins;
            if (bins == null)
            {
                return null;
            }

            int groupCount = bins.Count;
            double[] direct = new double[groupCount];
            double[] unwanted = new double[groupCount];
            double[] wanted = new double[groupCount];

            int evaluatedHours = 0;
            int missingWeatherHours = 0;

            // The largest |weight| actually applied to an energy-carrying hour. Recorded rather than
            // asked of the strategy: whether unwanted + wanted + neutral partitions the admitted
            // beam is a property of the weights that were USED, and a strategy that declared itself
            // bounded and was not would produce an accounting identity that silently does not hold.
            double maximumWeightMagnitude = double.NaN;

            int year = solarVisibilityCache.Year;
            double timeShiftInMinutes = solarVisibilityCache.SunPositionShiftInMinutes;
            DateTime yearStart = new DateTime(year, 1, 1);

            for (int g = 0; g < groupCount; g++)
            {
                List<int> hoursOfYear = bins[g]?.HoursOfYear;
                if (hoursOfYear == null)
                {
                    continue;
                }

                foreach (int hourOfYear in hoursOfYear)
                {
                    DateTime dateTime = yearStart.AddHours(hourOfYear);

                    WeatherHour weatherHour = weatherData.GetWeatherHour(dateTime);
                    if (weatherHour == null)
                    {
                        missingWeatherHours++;
                        continue;
                    }

                    // The shared hourly evaluation: RAW GHI/DHI fields only (B6), the sun sampled at
                    // h + the cache's own shift. Null means the hour cannot be evaluated at all — a
                    // missing raw field or an unresolvable sun position — never a substituted value.
                    // Context visibility is deliberately not asked for here (see remarks).
                    ApertureSolarHour apertureSolarHour = ApertureSolarHour(location, weatherHour, outward, dateTime, hourOfYear, timeShiftInMinutes);
                    if (apertureSolarHour == null)
                    {
                        missingWeatherHours++;
                        continue;
                    }

                    if (!apertureSolarHour.SunInFrontOfAperture)
                    {
                        // Back-facing sun carries no beam onto this aperture: zero contribution,
                        // regardless of the hour's weight.
                        continue;
                    }

                    // Wh/m2 over the whole hour, then kWh/m2 (x 1 h / 1000).
                    double energy = apertureSolarHour.ApertureDirectIrradiance / 1000.0;

                    double weight = desirabilityStrategy.Weight(dateTime, weatherHour, target);
                    if (double.IsNaN(weight))
                    {
                        weight = 0;
                    }

                    direct[g] += energy;
                    if (weight > 0)
                    {
                        unwanted[g] += weight * energy;
                    }
                    else if (weight < 0)
                    {
                        wanted[g] += -weight * energy;
                    }

                    double magnitude = Math.Abs(weight);
                    if (double.IsNaN(maximumWeightMagnitude) || magnitude > maximumWeightMagnitude)
                    {
                        maximumWeightMagnitude = magnitude;
                    }

                    evaluatedHours++;
                }
            }

            return new ApertureDesirability(target.ApertureGuid, Core.Query.FullTypeName(desirabilityStrategy), year, timeShiftInMinutes, direct, unwanted, wanted, evaluatedHours, missingWeatherHours, maximumWeightMagnitude);
        }
    }
}
