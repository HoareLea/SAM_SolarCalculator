using Innovative.Geometry;
using Innovative.SolarCalculator;
using SAM.Core;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Weather.SolarCalculator
{
    public static partial class Create
    {
        public static Radiation Radiation(WeatherData weatherData, DateTime dateTime, Plane plane, double skyViewFactor = 1, double groundViewFactor = 1, double albedo = 0.2, bool includeNight = false)
        {
            if (weatherData == null || plane == null)
            {
                return null;
            }

            Location location = weatherData.Location;

            Angle angle_Latitude = new Angle(location.Latitude);
            Angle angle_Longitude = new Angle(location.Longitude);

            int timeZoneOffset = 0;
            if (location.TryGetValue(LocationParameter.TimeZone, out string timeZoneString))
            {
                UTC uTC = Core.Query.UTC(timeZoneString);
                if (uTC != UTC.Undefined)
                {
                    timeZoneOffset = System.Convert.ToInt32(Core.Query.Double(uTC));
                }
            }

            SolarTimes solarTimes = new SolarTimes(dateTime, timeZoneOffset, angle_Latitude, angle_Longitude);
            if (!includeNight && (dateTime < solarTimes.Sunrise || dateTime > solarTimes.Sunset))
            {
                return null;
            }

            WeatherHour weatherHour = weatherData.GetWeatherHour(dateTime);
            if(weatherHour == null)
            {
                return null;
            }

            double directSolarRadiation = weatherHour.CalculatedDirectSolarRadiation();
            if(double.IsNaN(directSolarRadiation))
            {
                return null;
            }

            double diffuseSolarRadiation = weatherHour.CalculatedDiffuseSolarRadiation();
            if (double.IsNaN(diffuseSolarRadiation))
            {
                return null;
            }

            double globalSolarRadiation = weatherHour.CalculatedGlobalSolarRadiation();
            if (double.IsNaN(globalSolarRadiation))
            {
                return null;
            }

            double tilt = Geometry.Spatial.Query.Tilt(plane);
            double surfaceAzimuth = Geometry.Spatial.Query.Azimuth(plane, Vector3D.WorldY); 
           
            return Geometry.SolarCalculator.Create.Radiation(solarTimes, tilt, surfaceAzimuth, directSolarRadiation, diffuseSolarRadiation, globalSolarRadiation, skyViewFactor, groundViewFactor, albedo);
        }
    }
}
