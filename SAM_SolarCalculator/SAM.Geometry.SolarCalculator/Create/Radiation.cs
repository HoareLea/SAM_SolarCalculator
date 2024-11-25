using Innovative.Geometry;
using Innovative.SolarCalculator;
using System;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Create
    {
        public static Radiation Radiation(this SolarTimes solarTimes, double directNormalIrradiance, double diffuseHorizontalIrradiance, double globalHorizontalIrradiance, double skyViewFactor = 1, double groundViewFactor = 1, double albedo = 0.2)
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

            // 1. Get solar position
            double solarElevation = System.Convert.ToDouble(angle_SolarElevation.Radians);
            double solarAzimuth = System.Convert.ToDouble(angle_SolarAzimuth.Radians) + (Math.PI / 2);


            // 2. Define surface orientation
            double tilt = 30;  // Surface tilt angle in degrees
            double surfaceAzimuth = 180;  // Surface azimuth (e.g., south-facing)

            // 3. Calculate angle of incidence
            double cosThetaI = Math.Sin(solarElevation) * Math.Cos(tilt * Math.PI / 180) +
                               Math.Cos(solarElevation) * Math.Sin(tilt * Math.PI / 180) *
                               Math.Cos((solarAzimuth - surfaceAzimuth) * Math.PI / 180);

            // 4. Calculate direct, diffuse, and reflected radiation
            double directNormalRadiance = directNormalIrradiance * Math.Max(0, cosThetaI);
            double diffuseHorizontalRadiance = diffuseHorizontalIrradiance * skyViewFactor;  // Assume skyViewFactor is predefined
            double globalHorizontalRadiance = globalHorizontalIrradiance * albedo * groundViewFactor;  // Assume albedo is predefined

            // 5. Total incident radiation
            //double I_total = I_direct + I_diffuse + I_reflected;


            return new Radiation(directNormalRadiance, diffuseHorizontalRadiance, globalHorizontalRadiance);
        }
    }
}
