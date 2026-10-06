// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors
using Innovative.Geometry;
using Innovative.SolarCalculator;
using System;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// Isotropic (Liu-Jordan) irradiance on a surface. Legacy convention, kept byte-for-byte
        /// compatible: tilt_Temp = 180 - tilt and solarAzimuth = (radians + pi/2) * 180/pi, matching
        /// the historical behaviour of this method (see remarks on the SkyModel overload).
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

        /// <summary>
        /// Irradiance on a surface under an explicit sky model, PHYSICAL convention path.
        ///
        /// The receiving surface is given as its OUTWARD-oriented <see cref="SAM.Geometry.Spatial.Plane"/>
        /// (plane normal = the direction the surface receives radiation on). This is deliberately a
        /// different API shape from the legacy overload above: the legacy (tilt, surfaceAzimuth)
        /// doubles keep their historical meaning (inward-normal tilt and a +90 degree solar-azimuth
        /// rotation, kept for compatibility) and must never be mixed with this path.
        ///
        /// Components (Perez et al. 1990, Solar Energy 44(5), 271-289, for PerezAnisotropic):
        ///   beam    = DNI * max(0, cosThetaI)
        ///   diffuse = DHI * [(1-F1)*(1+cosB)/2*SVF_mult + F1*(a/b) + F2*sinB*SVF_mult]   (clamped >= 0)
        ///   ground  = GHI * albedo * GVF_mult * (1-cosB)/2
        /// where B is the receiving-side tilt from horizontal, a = max(0, cosThetaI),
        /// b = max(cos 85 deg, cos theta_z). For Isotropic the same physical conventions apply with
        /// diffuse = DHI * (1+cosB)/2 * SVF_mult (the corrected isotropic, NOT the legacy formula).
        /// directNormalIrradiance is true DNI, W/m2.
        ///
        /// skyViewFactorMultiplier and groundViewFactorMultiplier are RELATIVE MULTIPLIERS on top of
        /// the analytic unobstructed (1+cosB)/2 / (1-cosB)/2 form factors already baked into the
        /// formulas above — 1.0 means unobstructed, not "the sky/ground view factor". They are NOT
        /// the same quantity as SkyVisibilityCache.SkyViewFactor(...) / GroundViewFactor(...), which
        /// are ABSOLUTE view factors that already contain that geometric term (an unobstructed
        /// vertical surface has an absolute SkyViewFactor of ~0.5, but a skyViewFactorMultiplier of
        /// 1.0). Passing an absolute view factor into this multiplier silently double-applies the
        /// geometric factor (e.g. 0.5 * 0.5 = 0.25 instead of 0.5). For component-aware obstruction
        /// (circumsolar removed when the sun is obstructed, horizon term scaled by horizon-band
        /// visibility, absolute view factors) use the SolarVisibilityCache / SkyVisibilityCache
        /// evaluation path (SAM.Analytical.SolarCalculator.Query.CachedIrradiance).
        /// </summary>
        /// <param name="solarTimes">Solar position source.</param>
        /// <param name="plane">OUTWARD-oriented receiving plane (normal = receiving side).</param>
        /// <param name="directNormalIrradiance">True direct normal irradiance (DNI), W/m2.</param>
        /// <param name="diffuseHorizontalIrradiance">DHI, W/m2.</param>
        /// <param name="globalHorizontalIrradiance">GHI, W/m2.</param>
        /// <param name="skyModel">Sky model.</param>
        /// <param name="skyViewFactorMultiplier">Relative multiplier applied on top of the analytic unobstructed sky form factor (1.0 = unobstructed). NOT the same as the absolute SkyVisibilityCache.SkyViewFactor(...).</param>
        /// <param name="groundViewFactorMultiplier">Relative multiplier applied on top of the analytic unobstructed ground form factor (1.0 = unobstructed). NOT the same as the absolute SkyVisibilityCache.GroundViewFactor(...).</param>
        /// <param name="albedo">Ground reflectance.</param>
        public static Radiation Radiation(this SolarTimes solarTimes, Spatial.Plane plane, double directNormalIrradiance, double diffuseHorizontalIrradiance, double globalHorizontalIrradiance, SkyModel skyModel, double skyViewFactorMultiplier = 1, double groundViewFactorMultiplier = 1, double albedo = 0.2)
        {
            if (solarTimes == null || plane == null || double.IsNaN(directNormalIrradiance) || double.IsNaN(diffuseHorizontalIrradiance) || double.IsNaN(globalHorizontalIrradiance))
            {
                return null;
            }

            Angle angle_SolarElevation = solarTimes.SolarElevation;
            Angle angle_SolarAzimuth = solarTimes.SolarAzimuth;
            if (angle_SolarElevation == null || angle_SolarAzimuth == null)
            {
                return null;
            }

            double solarElevation = Convert.ToDouble(angle_SolarElevation.Radians);   // radians
            double solarAzimuth = Convert.ToDouble(angle_SolarAzimuth.Radians);       // radians, compass clockwise from north (NOAA)

            // Receiving-side geometry from the outward plane normal: B = tilt from horizontal
            // (cosB = normal . +Z); the azimuth difference enters through the horizontal dot product.
            Spatial.Vector3D normal = plane.Normal?.Unit;
            if (normal == null)
            {
                return null;
            }

            double cosBeta = System.Math.Max(-1.0, System.Math.Min(1.0, normal.Z));
            double tiltRadians = System.Math.Acos(cosBeta);
            double sinBeta = System.Math.Sin(tiltRadians);

            // cos(incidence) = normal . (surface -> sun unit vector); sun azimuth is compass-from-north.
            double sunX = System.Math.Cos(solarElevation) * System.Math.Sin(solarAzimuth);
            double sunY = System.Math.Cos(solarElevation) * System.Math.Cos(solarAzimuth);
            double sunZ = System.Math.Sin(solarElevation);
            double cosThetaI = normal.X * sunX + normal.Y * sunY + normal.Z * sunZ;

            double beam = directNormalIrradiance * System.Math.Max(0.0, cosThetaI);
            double ground = globalHorizontalIrradiance * albedo * groundViewFactorMultiplier * (1.0 - cosBeta) / 2.0;

            if (skyModel == SkyModel.Isotropic)
            {
                double diffuse_Isotropic = diffuseHorizontalIrradiance * (1.0 + cosBeta) / 2.0 * skyViewFactorMultiplier;
                return new Radiation(beam, System.Math.Max(0.0, diffuse_Isotropic), System.Math.Max(0.0, ground));
            }

            if (skyModel != SkyModel.PerezAnisotropic)
            {
                return null;
            }

            if (!Query.TryGetPerezCoefficients(directNormalIrradiance, diffuseHorizontalIrradiance, solarElevation * 180.0 / System.Math.PI, solarTimes.ForDate.DayOfYear, out double f1, out double f2, out _, out _))
            {
                return null;
            }

            double cosZenith = System.Math.Sin(solarElevation);
            double a = System.Math.Max(0.0, cosThetaI);
            double b = System.Math.Max(System.Math.Cos(85.0 * System.Math.PI / 180.0), cosZenith);

            double diffuse = diffuseHorizontalIrradiance *
                ((1.0 - f1) * (1.0 + cosBeta) / 2.0 * skyViewFactorMultiplier +
                 f1 * a / b +
                 f2 * sinBeta * skyViewFactorMultiplier);

            // Perez's (1-F1) can go negative under very clear skies; clamp the composed component.
            return new Radiation(beam, System.Math.Max(0.0, diffuse), System.Math.Max(0.0, ground));
        }
    }
}

