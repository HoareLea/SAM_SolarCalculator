// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// Decomposes a SAM sun-direction vector (points FROM the sun TOWARD the surface, so
        /// Z &lt; 0 while the sun is up) into solar altitude and compass azimuth. Inverse of
        /// Create.SunDirection(double, double).
        /// </summary>
        /// <param name="sunDirection">Sun-direction vector in the SAM sun -&gt; surface convention.</param>
        /// <param name="altitude">Solar altitude above the horizon, degrees. Negative when the sun is below the horizon.</param>
        /// <param name="azimuth">Solar azimuth, compass degrees clockwise from north (+Y), 0–360.</param>
        public static bool TryGetSunAngles(this Vector3D sunDirection, out double altitude, out double azimuth)
        {
            altitude = double.NaN;
            azimuth = double.NaN;

            if (sunDirection == null || !sunDirection.IsValid() || sunDirection.Length < Core.Tolerance.Distance)
            {
                return false;
            }

            Vector3D unit = sunDirection.Unit;

            altitude = Math.Asin(Math.Max(-1.0, Math.Min(1.0, -unit.Z))) * 180.0 / Math.PI;

            azimuth = Math.Atan2(-unit.X, -unit.Y) * 180.0 / Math.PI;
            if (azimuth < 0)
            {
                azimuth += 360.0;
            }

            return true;
        }

        /// <summary>
        /// Solar altitude and compass azimuth (both DEGREES) at a location and instant, read
        /// straight from SolarTimes.
        ///
        /// This is the single sun-angle source shared by sun-group construction
        /// (Weather.SolarCalculator.Create.SunBins) and the cached evaluation
        /// (Analytical.SolarCalculator.Query.CachedIrradiance), so the two can never disagree about
        /// which group an hour belongs to. It deliberately reads Angle.Radians: Innovative.Geometry's
        /// Angle.Degrees ROUNDS to whole degrees, which would inject up to 0.5 deg of error into
        /// group lookup, incidence angles and the Perez inputs.
        /// </summary>
        /// <param name="location">Site (latitude, longitude, fractional UTC offset).</param>
        /// <param name="dateTime">Local (site-clock) instant at which the sun position is sampled.</param>
        /// <param name="altitude">Solar altitude above the horizon, degrees. Negative below the horizon.</param>
        /// <param name="azimuth">Solar azimuth, compass degrees clockwise from north, wrapped into 0-360.</param>
        public static bool TryGetSunAngles(this Core.Location location, DateTime dateTime, out double altitude, out double azimuth)
        {
            altitude = double.NaN;
            azimuth = double.NaN;

            Innovative.SolarCalculator.SolarTimes solarTimes = Create.SolarTimes(location, dateTime);
            if (solarTimes?.SolarElevation == null || solarTimes.SolarAzimuth == null)
            {
                return false;
            }

            altitude = System.Convert.ToDouble(solarTimes.SolarElevation.Radians) * 180.0 / Math.PI;

            azimuth = System.Convert.ToDouble(solarTimes.SolarAzimuth.Radians) * 180.0 / Math.PI;
            azimuth %= 360.0;
            if (azimuth < 0)
            {
                azimuth += 360.0;
            }

            if (double.IsNaN(altitude) || double.IsNaN(azimuth))
            {
                return false;
            }

            return true;
        }
    }
}
