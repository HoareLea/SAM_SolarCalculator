using SAM.Geometry.Spatial;
using System.Linq;
using System;

namespace SAM.Weather.SolarCalculator
{
    public static partial class Query
    {
        public static Vector3D SunDirection(this WeatherData weatherData, int hourOfYear, int year = -1, bool includeNight = false)
        {
            if(weatherData == null)
            {
                return null;
            }

            int year_Temp = year;
            if(year_Temp == -1)
            {
                WeatherYear weatherYear = weatherData.WeatherYears?.FirstOrDefault();
                if(weatherYear == null)
                {
                    return null;
                }

                year_Temp = weatherYear.Year;
            }

            DateTime dateTime = new DateTime(year_Temp, 1, 1).AddHours(hourOfYear);

            return Geometry.SolarCalculator.Query.SunDirection(weatherData.Location, dateTime, includeNight);
        }
    }
}
