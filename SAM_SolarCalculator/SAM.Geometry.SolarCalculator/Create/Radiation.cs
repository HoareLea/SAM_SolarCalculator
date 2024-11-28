using Innovative.Geometry;
using Innovative.SolarCalculator;
using System;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="solarTimes"></param>
        /// <param name="tilt">Surface tilt angle in degrees</param>
        /// <param name="surfaceAzimuth">Surface azimuth (e.g., south-facing)</param>
        /// <param name="directNormalIrradiance"></param>
        /// <param name="diffuseHorizontalIrradiance"></param>
        /// <param name="globalHorizontalIrradiance"></param>
        /// <param name="skyViewFactor"></param>
        /// <param name="groundViewFactor"></param>
        /// <param name="albedo"></param>
        /// <returns></returns>
        public static Radiation Radiation(this SolarTimes solarTimes, double tilt, double surfaceAzimuth, double directNormalIrradiance, double diffuseHorizontalIrradiance, double globalHorizontalIrradiance, double skyViewFactor = 1, double groundViewFactor = 1, double albedo = 0.2)
        {
            if (solarTimes == null || double.IsNaN(directNormalIrradiance) || double.IsNaN(diffuseHorizontalIrradiance) || double.IsNaN(globalHorizontalIrradiance))
            {
                return null;
            }

            Angle angle_SolarElevation = solarTimes.SolarElevation;
            if (angle_SolarElevation == null)
            {
                return null;
            }

            Angle angle_SolarAzimuth = solarTimes.SolarAzimuth;
            if (angle_SolarAzimuth == null)
            {
                return null;
            }

            double tilt_Temp = 180 - tilt;

            // 1. Get solar position
            double solarElevation = Convert.ToDouble(angle_SolarElevation.Radians);
            double solarAzimuth = (Convert.ToDouble(angle_SolarAzimuth.Radians) + (Math.PI / 2)) * 180 / Math.PI;

            // 2. Calculate angle of incidence
            double cosThetaI = Math.Sin(solarElevation) * Math.Cos(tilt_Temp * Math.PI / 180) +
                               Math.Cos(solarElevation) * Math.Sin(tilt_Temp * Math.PI / 180) *
                               Math.Cos((solarAzimuth - surfaceAzimuth) * Math.PI / 180);

            // 3. Calculate direct, diffuse, and reflected radiation
            double directNormalRadiance = directNormalIrradiance * Math.Max(0, cosThetaI);
            double diffuseHorizontalRadiance = diffuseHorizontalIrradiance * skyViewFactor * Math.Pow(Math.Cos(tilt_Temp * Math.PI / 360), 2); // Assume skyViewFactor is predefined
            double globalHorizontalRadiance = globalHorizontalIrradiance * albedo * groundViewFactor * Math.Pow(Math.Sin(tilt_Temp * Math.PI / 360), 2); // Assume albedo is predefined

            // 4. Total incident radiation
            //double I_total = I_direct + I_diffuse + I_reflected;

            return new Radiation(directNormalRadiance, diffuseHorizontalRadiance, globalHorizontalRadiance);
        }
    }
}
