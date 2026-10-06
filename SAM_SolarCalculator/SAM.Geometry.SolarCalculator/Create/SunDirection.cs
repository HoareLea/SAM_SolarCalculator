// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// Sun direction vector from solar angles, in the same convention as
        /// Query.SunDirection(Location, DateTime): the vector points FROM the sun TOWARD the
        /// surface, so Z &lt; 0 while the sun is above the horizon.
        /// </summary>
        /// <param name="altitude">Solar altitude (elevation) above the horizon, degrees.</param>
        /// <param name="azimuth">Solar azimuth, compass degrees clockwise from north (+Y): 0 = north, 90 = east, 180 = south.</param>
        public static Vector3D SunDirection(double altitude, double azimuth)
        {
            double altitude_Radians = altitude * Math.PI / 180.0;
            double azimuth_Radians = azimuth * Math.PI / 180.0;

            return new Vector3D(
                -Math.Cos(altitude_Radians) * Math.Sin(azimuth_Radians),
                -Math.Cos(altitude_Radians) * Math.Cos(azimuth_Radians),
                -Math.Sin(altitude_Radians));
        }
    }
}
