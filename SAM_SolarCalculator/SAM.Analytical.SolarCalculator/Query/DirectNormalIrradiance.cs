// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// Solar elevation, in degrees, below which the beam-horizontal to direct-normal conversion
        /// is capped. Near the horizon 1 / sin(elevation) diverges, so an ordinary beam-horizontal
        /// value would be amplified without limit.
        /// </summary>
        public const double MinimumElevationDegrees_DirectNormal = 5.0;

        /// <summary>
        /// Direct NORMAL irradiance (W/m2) derived from the raw hourly global and diffuse horizontal
        /// values: DNI = max(0, GHI - DHI) / max(sin(elevation), sin 5 deg).
        ///
        /// This is the ONE implementation of that conversion. The raw fields are used deliberately:
        /// WeatherHour's Calculated* helpers fall back to the ambiguous DirectSolarRadiation field,
        /// whose meaning varies by creation route (beam-horizontal from a SAM-internal producer,
        /// true DNI from an EPW field 14 reader). The caller decides what to do with an hour whose
        /// raw fields are absent; this query only refuses to invent one, returning NaN.
        /// </summary>
        /// <param name="globalSolarRadiation">Global horizontal irradiance, W/m2 (raw field).</param>
        /// <param name="diffuseSolarRadiation">Diffuse horizontal irradiance, W/m2 (raw field).</param>
        /// <param name="elevationDegrees">Solar elevation above the horizon, degrees.</param>
        /// <returns>Direct normal irradiance, W/m2; NaN when an input is missing.</returns>
        public static double DirectNormalIrradiance(double globalSolarRadiation, double diffuseSolarRadiation, double elevationDegrees)
        {
            if (double.IsNaN(globalSolarRadiation) || double.IsNaN(diffuseSolarRadiation) || double.IsNaN(elevationDegrees))
            {
                return double.NaN;
            }

            double beamHorizontal = Math.Max(0.0, globalSolarRadiation - diffuseSolarRadiation);
            double minimumSinElevation = Math.Sin(MinimumElevationDegrees_DirectNormal * Math.PI / 180.0);

            return beamHorizontal / Math.Max(Math.Sin(elevationDegrees * Math.PI / 180.0), minimumSinElevation);
        }
    }
}
