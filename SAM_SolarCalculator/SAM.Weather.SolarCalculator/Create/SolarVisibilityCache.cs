// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Core;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SAM.Weather.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// Builds the direct-beam visibility cache: sun bins for the full given year at the given
        /// angular resolution, then ONE occlusion pass per bin (parallel, thread-confined) storing
        /// the per-cell lit bits. This is the expensive geometric pass; evaluating any
        /// AnalysisPeriod afterwards is arithmetic only.
        ///
        /// The sun-position sampling shift is part of the cache's bin definition: bins are built
        /// from the sun positions at weather-hour + sunPositionShiftInMinutes, and the evaluation
        /// applies the same shift read back from the cache — the two timelines can never drift
        /// apart (a shift change is an identity change and rebuilds). Weather data is NOT part of
        /// the identity.
        /// </summary>
        /// <param name="location">Site location (defines the sun path; fractional time zone preserved).</param>
        /// <param name="year">Weather year the bins cover.</param>
        /// <param name="binSizeDegrees">Sun-bin angular resolution (altitude x azimuth), degrees.</param>
        /// <param name="occluders">Context faces (every face occludes, regardless of orientation).</param>
        /// <param name="analysisCells">Target cells (outward-oriented).</param>
        /// <param name="cellSize">Cell size the targets were subdivided with (identity only).</param>
        /// <param name="minHorizonAngle">Minimum sun altitude, RADIANS.</param>
        /// <param name="sunPositionShiftInMinutes">Sun-position sampling offset of the timeline, minutes (+30 for interval-start EPW weather, -30 for TAS EDSL compatibility).</param>
        public static SolarVisibilityCache SolarVisibilityCache(this Location location, int year, double binSizeDegrees, List<LinkedFace3D> occluders, List<AnalysisCell> analysisCells, double cellSize, double minHorizonAngle = Core.Tolerance.Angle, double sunPositionShiftInMinutes = 0, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Snap = Core.Tolerance.MacroDistance, double tolerance_Angle = Core.Tolerance.Angle, double tolerance_Distance = Core.Tolerance.Distance)
        {
            if (location == null || analysisCells == null || analysisCells.Count == 0 || double.IsNaN(binSizeDegrees) || binSizeDegrees <= 0)
            {
                return null;
            }

            List<SunBin> bins = SunBins(location, year, binSizeDegrees, minHorizonAngle, sunPositionShiftInMinutes);
            if (bins == null || bins.Count == 0)
            {
                return null;
            }

            List<Point3D> points = new List<Point3D>(analysisCells.Count);
            List<Vector3D> normals = new List<Vector3D>(analysisCells.Count);
            foreach (AnalysisCell analysisCell in analysisCells)
            {
                points.Add(analysisCell?.InternalPoint3D);
                normals.Add(analysisCell?.Face3D?.GetPlane()?.Normal?.Unit);
            }

            string contextGeometryHash = Geometry.SolarCalculator.Query.GeometryHash(occluders, tolerance_Distance);
            string targetGeometryHash = Geometry.SolarCalculator.Query.TargetHash(analysisCells, tolerance_Distance);
            double timeZoneOffset = Geometry.SolarCalculator.Query.TimeZoneOffset(location);

            ulong[][] litBits = global::SAM.Weather.SolarCalculator.SolarVisibilityCache.CreateBitStorage(bins.Count, analysisCells.Count);

            Parallel.For(0, bins.Count, b =>
            {
                Vector3D representativeDirection = bins[b]?.RepresentativeDirection;
                if (representativeDirection == null || !representativeDirection.IsValid())
                {
                    return;
                }

                // RepresentativeDirection is stored in the sun -> surface convention; the raycast
                // works toward the source.
                bool[] lit = Query.CellVisibility(occluders, points, normals, representativeDirection.GetNegated(), tolerance_Area, tolerance_Angle, tolerance_Distance, tolerance_Snap);
                if (lit == null)
                {
                    return;
                }

                ulong[] bits = litBits[b];
                for (int c = 0; c < lit.Length; c++)
                {
                    if (lit[c])
                    {
                        bits[c >> 6] |= 1UL << (c & 63);
                    }
                }
            });

            return new global::SAM.Weather.SolarCalculator.SolarVisibilityCache(contextGeometryHash, targetGeometryHash, cellSize, binSizeDegrees, minHorizonAngle, tolerance_Area, tolerance_Snap, tolerance_Angle, tolerance_Distance, location.Latitude, location.Longitude, timeZoneOffset, sunPositionShiftInMinutes, year, analysisCells.Count, bins, litBits);
        }
    }
}
