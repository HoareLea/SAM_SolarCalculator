// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        // Perez et al. 1990, "Modeling daylight availability and irradiance components from direct
        // and global irradiance", Solar Energy 44(5), 271-289, Table 1: circumsolar (F1) and
        // horizon-brightening (F2) coefficient rows per sky clearness bin.
        private static readonly double[][] PerezTable =
        {
            new[] { -0.008,  0.588, -0.062, -0.060,  0.072, -0.022 },
            new[] {  0.130,  0.683, -0.151, -0.019,  0.066, -0.029 },
            new[] {  0.330,  0.487, -0.221,  0.055, -0.064, -0.026 },
            new[] {  0.568,  0.187, -0.295,  0.109, -0.152, -0.014 },
            new[] {  0.873, -0.392, -0.362,  0.226, -0.462,  0.001 },
            new[] {  1.132, -1.237, -0.412,  0.288, -0.823,  0.056 },
            new[] {  1.060, -1.600, -0.359,  0.264, -1.127,  0.131 },
            new[] {  0.678, -0.327, -0.250,  0.156, -1.377,  0.251 },
        };

        private static readonly double[] PerezEpsilonUpperBounds = { 1.065, 1.230, 1.500, 1.950, 2.800, 4.500, 6.200, double.MaxValue };

        /// <summary>
        /// Perez 1990 sky clearness (epsilon), brightness (delta) and the F1/F2 circumsolar and
        /// horizon-brightening coefficients for one timestep.
        /// </summary>
        /// <param name="directNormalIrradiance">DNI, W/m2.</param>
        /// <param name="diffuseHorizontalIrradiance">DHI, W/m2.</param>
        /// <param name="solarElevation">Solar altitude above the horizon, DEGREES.</param>
        /// <param name="dayOfYear">Day of year (1-366), for the extraterrestrial irradiance correction.</param>
        /// <param name="f1">Circumsolar coefficient.</param>
        /// <param name="f2">Horizon-brightening coefficient.</param>
        /// <param name="epsilon">Sky clearness.</param>
        /// <param name="delta">Sky brightness.</param>
        public static bool TryGetPerezCoefficients(double directNormalIrradiance, double diffuseHorizontalIrradiance, double solarElevation, int dayOfYear, out double f1, out double f2, out double epsilon, out double delta)
        {
            f1 = double.NaN;
            f2 = double.NaN;
            epsilon = double.NaN;
            delta = double.NaN;

            if (double.IsNaN(directNormalIrradiance) || double.IsNaN(diffuseHorizontalIrradiance) || double.IsNaN(solarElevation) || solarElevation <= 0)
            {
                return false;
            }

            double zenithRadians = (90.0 - solarElevation) * Math.PI / 180.0;
            double zenithDegrees = 90.0 - solarElevation;

            // Sky clearness (Perez 1990 eq. 3), kappa = 1.041 for zenith in radians.
            const double kappa = 1.041;
            double dhi = Math.Max(diffuseHorizontalIrradiance, 1e-9);
            epsilon = ((dhi + Math.Max(directNormalIrradiance, 0.0)) / dhi + kappa * zenithRadians * zenithRadians * zenithRadians) / (1.0 + kappa * zenithRadians * zenithRadians * zenithRadians);

            // Sky brightness (Perez 1990 eq. 4): delta = m * DHI / I0n, with Kasten-Young air mass
            // and the eccentricity-corrected extraterrestrial normal irradiance (~1367 W/m2).
            double cosZenith = Math.Sin(solarElevation * Math.PI / 180.0);
            double airMass = 1.0 / (cosZenith + 0.50572 * Math.Pow(96.07995 - zenithDegrees, -1.6364));
            double i0n = 1367.0 * (1.0 + 0.033 * Math.Cos(2.0 * Math.PI * dayOfYear / 365.0));
            delta = airMass * Math.Max(diffuseHorizontalIrradiance, 0.0) / i0n;

            int bin = 0;
            while (bin < PerezEpsilonUpperBounds.Length - 1 && epsilon > PerezEpsilonUpperBounds[bin])
            {
                bin++;
            }

            double[] row = PerezTable[bin];
            f1 = Math.Max(0.0, row[0] + row[1] * delta + row[2] * zenithRadians);
            f2 = row[3] + row[4] * delta + row[5] * zenithRadians;

            return true;
        }
    }
}
