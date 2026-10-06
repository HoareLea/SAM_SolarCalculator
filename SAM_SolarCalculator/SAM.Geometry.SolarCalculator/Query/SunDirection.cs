// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors
using Innovative.Geometry;
using SAM.Geometry.Spatial;
using Innovative.SolarCalculator;
using System;
using SAM.Core;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        public static Vector3D SunDirection(this SolarTimes solarTimes)
        {
            Angle angle_SolarElevation = solarTimes.SolarElevation;
            if(angle_SolarElevation == null)
            {
                return null;
            }
            
            Angle angle_SolarAzimuth = solarTimes.SolarAzimuth;
            if(angle_SolarAzimuth == null)
            {
                return null;
            }

            double solarElevation = System.Convert.ToDouble(angle_SolarElevation.Radians);
            double solarAzimuth = System.Convert.ToDouble(angle_SolarAzimuth.Radians) + (Math.PI / 2);

            double x = Math.Cos(solarElevation) * Math.Cos(solarAzimuth);
            double y = Math.Cos(solarElevation + Math.PI) * Math.Sin(solarAzimuth);
            double z = Math.Sin(solarElevation + Math.PI);

            return new Vector3D(x, y, z);
        }

        public static Vector3D SunDirection(this Location location, DateTime dateTime, bool includeNight = false)
        {
            if(location == null || dateTime == DateTime.MinValue || dateTime == DateTime.MaxValue)
            {
                return null;
            }

            SolarTimes solarTimes = Create.SolarTimes(location, dateTime);
            if (solarTimes == null)
            {
                return null;
            }

            if (!includeNight && (dateTime < solarTimes.Sunrise || dateTime > solarTimes.Sunset))
            {
                return null;
            }

            return SunDirection(solarTimes);
        }
    }
}
