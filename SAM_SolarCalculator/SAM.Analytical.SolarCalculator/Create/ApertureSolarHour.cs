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
        /// The solar and environmental state of one aperture in one weather hour — the SINGLE
        /// implementation of the aperture-plane solar equations, shared by the Stage 5 desirability
        /// weighting (<see cref="ApertureDesirability(ApertureSolarTarget, SolarVisibilityCache, IDesirabilityStrategy, WeatherData)"/>)
        /// and by the hourly control profile (<see cref="SAM.Analytical.SolarCalculator.SolarControlProfile"/>),
        /// so the two can never drift apart.
        ///
        /// Per hour h, with the sun sampled at h + <paramref name="timeShiftInMinutes"/>:
        ///   DNI(h)         = max(0, GHI(h) - DHI(h)) / max(sin(elevation), sin 5 deg)
        ///   cos(thetaI)(h) = aperture outward normal . unit vector toward the sun
        ///   aperture beam  = DNI(h) x max(0, cos(thetaI)(h))                          [W/m2]
        ///
        /// RAW GHI/DHI fields only — the ambiguous DirectSolarRadiation field is never read (see
        /// <see cref="Query.DirectNormalIrradiance"/>). The sun position comes from
        /// Geometry.SolarCalculator.Query.TryGetSunAngles, the same source the sun bins are built
        /// from, so incidence angles and bin membership cannot disagree.
        ///
        /// Returns null when the hour CANNOT be evaluated: no weather hour, a missing raw radiation
        /// field, or an unresolvable sun position. It never substitutes a value for a missing one.
        /// An hour whose sun is behind the aperture IS returned, carrying exactly zero aperture beam.
        /// </summary>
        /// <param name="location">Site (latitude, longitude, fractional UTC offset).</param>
        /// <param name="weatherHour">The weather values at h. Fields it does not carry arrive as NaN.</param>
        /// <param name="outwardNormal">The aperture OUTWARD normal.</param>
        /// <param name="dateTime">The weather-timeline timestamp of h.</param>
        /// <param name="hourOfYear">Hour of the year of h, 0-based.</param>
        /// <param name="timeShiftInMinutes">Sun-position sampling offset of the timeline, minutes (+30 for SAM/EPW).</param>
        public static ApertureSolarHour ApertureSolarHour(Core.Location location, WeatherHour weatherHour, Vector3D outwardNormal, DateTime dateTime, int hourOfYear, double timeShiftInMinutes)
        {
            if (location == null || weatherHour == null || outwardNormal == null || double.IsNaN(timeShiftInMinutes))
            {
                return null;
            }

            Vector3D outward = outwardNormal.Unit;
            if (outward == null || !outward.IsValid())
            {
                return null;
            }

            double globalSolarRadiation = weatherHour.GlobalSolarRadiation;
            double diffuseSolarRadiation = weatherHour.DiffuseSolarRadiation;
            if (double.IsNaN(globalSolarRadiation) || double.IsNaN(diffuseSolarRadiation))
            {
                return null;
            }

            DateTime sunTime = timeShiftInMinutes == 0 ? dateTime : dateTime.AddMinutes(timeShiftInMinutes);
            if (!Geometry.SolarCalculator.Query.TryGetSunAngles(location, sunTime, out double elevationDegrees, out double azimuthDegrees))
            {
                return null;
            }

            double elevationRadians = elevationDegrees * Math.PI / 180.0;
            double azimuthRadians = azimuthDegrees * Math.PI / 180.0;

            // Unit vector from the surface TOWARD the sun (compass convention, +Y = north).
            double cosElevation = Math.Cos(elevationRadians);
            double sunX = cosElevation * Math.Sin(azimuthRadians);
            double sunY = cosElevation * Math.Cos(azimuthRadians);
            double sunZ = Math.Sin(elevationRadians);

            double cosIncidence = outward.X * sunX + outward.Y * sunY + outward.Z * sunZ;
            double directNormalIrradiance = Query.DirectNormalIrradiance(globalSolarRadiation, diffuseSolarRadiation, elevationDegrees);
            double apertureDirectIrradiance = cosIncidence > 0 ? directNormalIrradiance * cosIncidence : 0.0;

            return new ApertureSolarHour(hourOfYear, dateTime, elevationDegrees, azimuthDegrees, cosIncidence, directNormalIrradiance, apertureDirectIrradiance, weatherHour.DryBulbTemperature, weatherHour.WindSpeed);
        }

        /// <summary>
        /// Every evaluable hour of the year for one aperture, ascending — the whole hourly picture
        /// the control rules are then applied to.
        ///
        /// Hours that cannot be evaluated are OMITTED rather than filled in, so the caller can see
        /// how much of the year the weather file actually supports (compare the count against 8760 /
        /// 8784). Cheap: hourly arithmetic and one sun-position evaluation per hour, no geometry.
        /// </summary>
        /// <param name="target">The aperture; its outward normal decides the incidence angles.</param>
        /// <param name="weatherData">Hourly weather; its Location drives the sun position.</param>
        /// <param name="year">Weather year to walk.</param>
        /// <param name="timeShiftInMinutes">Sun-position sampling offset of the timeline, minutes.</param>
        /// <param name="analysisPeriod">Optional restriction to a subset of the year. Null = every hour.</param>
        public static List<ApertureSolarHour> ApertureSolarHours(this ApertureSolarTarget target, WeatherData weatherData, int year, double timeShiftInMinutes, AnalysisPeriod analysisPeriod = null)
        {
            if (target == null || weatherData == null || double.IsNaN(timeShiftInMinutes))
            {
                return null;
            }

            Vector3D outward = target.OutwardNormal;
            if (outward == null || !outward.IsValid())
            {
                return null;
            }

            Core.Location location = weatherData.Location;
            if (location == null)
            {
                return null;
            }

            List<int> hoursOfYear = analysisPeriod == null ? null : analysisPeriod.HoursOfYear();
            if (hoursOfYear == null)
            {
                int count = DateTime.IsLeapYear(year) ? 8784 : 8760;
                hoursOfYear = new List<int>(count);
                for (int i = 0; i < count; i++)
                {
                    hoursOfYear.Add(i);
                }
            }

            DateTime yearStart = new DateTime(year, 1, 1);

            List<ApertureSolarHour> result = new List<ApertureSolarHour>(hoursOfYear.Count);
            foreach (int hourOfYear in hoursOfYear)
            {
                DateTime dateTime = yearStart.AddHours(hourOfYear);

                ApertureSolarHour apertureSolarHour = ApertureSolarHour(location, weatherData.GetWeatherHour(dateTime), outward, dateTime, hourOfYear, timeShiftInMinutes);
                if (apertureSolarHour != null)
                {
                    result.Add(apertureSolarHour);
                }
            }

            return result;
        }
    }
}
