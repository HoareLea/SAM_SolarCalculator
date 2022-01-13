using SAM.Geometry.Spatial;
using System;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Query
    {
        public static Vector3D SunDirection(this AnalyticalModel analyticalModel, DateTime dateTime, bool includeNight = false)
        {
            Core.Location location = analyticalModel?.Location;
            if(location == null)
            {
                return null;
            }

            return Geometry.SolarCalculator.Query.SunDirection(location, dateTime, includeNight);
        }
    }
}
