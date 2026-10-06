// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System;
using System.Collections.Generic;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        // Tregenza sky: 7 altitude bands of 12 deg, patch counts per band (Tregenza & Waters 1983,
        // as used by Radiance/Daysim). Zenith patch closes the dome.
        private static readonly int[] TregenzaBandCounts = { 30, 30, 24, 24, 18, 12, 6 };

        /// <summary>
        /// Sky-dome patch directions with solid angles. Tregenza145 = 145 patches
        /// (7 x 12 deg bands + zenith); Reinhart577 = MF:2 subdivision (577 patches, for
        /// validation). Directions point from the receiving surface TOWARD the patch; azimuth is
        /// compass degrees clockwise from north (+Y); the sum of solid angles is 2*pi.
        /// </summary>
        public static List<SkyPatch> SkyPatchDirections(this SkyPatchSubdivision skyPatchSubdivision)
        {
            switch (skyPatchSubdivision)
            {
                case SkyPatchSubdivision.Tregenza145:
                    return SkyPatches(1);

                case SkyPatchSubdivision.Reinhart577:
                    return SkyPatches(2);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Mirrored ground-dome patches (directions with Z &lt; 0), used for ground-reflected
        /// view factors under the isotropic-ground assumption. Not part of the Tregenza sky.
        /// </summary>
        public static List<SkyPatch> GroundPatchDirections(this SkyPatchSubdivision skyPatchSubdivision)
        {
            List<SkyPatch> skyPatches = SkyPatchDirections(skyPatchSubdivision);
            if (skyPatches == null)
            {
                return null;
            }

            List<SkyPatch> result = new List<SkyPatch>(skyPatches.Count);
            foreach (SkyPatch skyPatch in skyPatches)
            {
                Vector3D direction = skyPatch?.Direction;
                if (direction == null)
                {
                    continue;
                }

                result.Add(new SkyPatch(result.Count, new Vector3D(direction.X, direction.Y, -direction.Z), skyPatch.SolidAngle, skyPatch.IsHorizonBand));
            }

            return result;
        }

        private static List<SkyPatch> SkyPatches(int multiplier)
        {
            List<SkyPatch> result = new List<SkyPatch>();

            double bandWidth = 12.0 / multiplier;
            for (int band = 0; band < TregenzaBandCounts.Length; band++)
            {
                double altitudeMin = band * 12.0;
                int count = TregenzaBandCounts[band] * multiplier;
                for (int sub = 0; sub < multiplier; sub++)
                {
                    double altitudeLow = altitudeMin + sub * bandWidth;
                    double altitudeHigh = altitudeLow + bandWidth;
                    double altitudeCentre = (altitudeLow + altitudeHigh) / 2.0;

                    double solidAngleBand = 2.0 * Math.PI * (Math.Sin(altitudeHigh * Math.PI / 180.0) - Math.Sin(altitudeLow * Math.PI / 180.0));
                    double solidAngle = solidAngleBand / count;

                    for (int i = 0; i < count; i++)
                    {
                        double azimuthCentre = (i + 0.5) * 360.0 / count;
                        result.Add(new SkyPatch(result.Count, Direction(altitudeCentre, azimuthCentre), solidAngle, band == 0));
                    }
                }
            }

            // Zenith patch: 84 deg to the pole (unchanged by the Reinhart multiplier).
            double solidAngleZenith = 2.0 * Math.PI * (1.0 - Math.Sin(84.0 * Math.PI / 180.0));
            result.Add(new SkyPatch(result.Count, Vector3D.WorldZ, solidAngleZenith, false));

            return result;
        }

        private static Vector3D Direction(double altitude, double azimuth)
        {
            double altitude_Radians = altitude * Math.PI / 180.0;
            double azimuth_Radians = azimuth * Math.PI / 180.0;

            return new Vector3D(
                Math.Cos(altitude_Radians) * Math.Sin(azimuth_Radians),
                Math.Cos(altitude_Radians) * Math.Cos(azimuth_Radians),
                Math.Sin(altitude_Radians));
        }
    }
}
