// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical.SolarCalculator;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Gate 0 Review E. What the first-hit attribution cache actually costs, measured on a real
    /// aperture rather than extrapolated from the per-sample arithmetic, and what that implies for
    /// a Stage 9 search that evaluates many candidates.
    ///
    /// The theoretical ratio against the 1-bit visibility representation is exactly 32 — 4 bytes a
    /// sample against 1 bit — but the theoretical figure ignores the GUID table, the jagged-array
    /// overhead and the .NET object headers, so it is checked against a measured allocation here.
    /// </summary>
    public class AttributionCacheScaleTests
    {
        private readonly ITestOutputHelper output;

        public AttributionCacheScaleTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static readonly int Year = 2018;

        /// <summary>A large south-facing glazed wall: 8 m x 4.5 m.</summary>
        private static Face3D WallFace()
        {
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 1),
                new Point3D(8, 0, 1),
                new Point3D(8, 0, 5.5),
                new Point3D(0, 0, 5.5),
            }));
        }

        /// <summary>
        /// Process-wide total ALLOCATED bytes — a monotonically increasing counter, not the live
        /// set.
        ///
        /// The live set (GC.GetTotalMemory) is the intuitive measure and is unusable here: xUnit
        /// runs test classes concurrently in one process, so another class collecting a large
        /// object graph between the two samples makes the delta negative. The allocation counter
        /// cannot go backwards, so the delta is a genuine LOWER bound on what this work allocated,
        /// which is the direction that matters — it proves StorageBytes is not an overestimate.
        /// </summary>
        private static long Allocated()
        {
            return GC.GetTotalAllocatedBytes(true);
        }

        [Fact]
        public void Attribution_Storage_Is_Measured_Against_The_Visibility_Cache()
        {
            // 0.25 m cells over a 36 m2 wall, the default 2 degree sun grouping: a workload big
            // enough that the payload dominates the fixed overheads.
            const double gridSize = 0.25;
            Face3D face = WallFace();
            ApertureSolarTarget target = new ApertureSolarTarget(
                Guid.NewGuid(), Guid.NewGuid(), face, SAM.Geometry.SolarCalculator.Query.AnalysisCells(face, gridSize));

            List<AnalysisCell> cells = target.AnalysisCells;

            SolarVisibilityCache visibility = Weather.SolarCalculator.Create.SolarVisibilityCache(
                TestHelpers.London(), Year, 2.0, new List<LinkedFace3D>(), cells,
                cellSize: gridSize, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);

            Assert.NotNull(visibility);

            // A device with a realistic element count, so the GUID table is not a single entry.
            List<ShadingElement> elements = new EggCrate(0.5, 6, 6).ShadingElements(target);
            List<LinkedFace3D> occluders = new List<LinkedFace3D>();
            foreach (ShadingElement element in elements)
            {
                occluders.Add(element.LinkedFace3D);
            }

            long before = Allocated();
            Stopwatch stopwatch = Stopwatch.StartNew();
            SolarAttributionCache attribution = Weather.SolarCalculator.Create.SolarAttributionCache(visibility, occluders, cells);
            stopwatch.Stop();
            long after = Allocated();

            Assert.NotNull(attribution);

            long samples = (long)visibility.BinCount * cells.Count;
            long visibilityBytes = (long)visibility.BinCount * ((cells.Count + 63) / 64) * 8;
            long attributionBytes = attribution.StorageBytes;
            long measuredBytes = after - before;

            output.WriteLine("SunGroups | cells | samples | elements | visibility storage | attribution storage | ratio");
            output.WriteLine($"{visibility.BinCount,9} | {cells.Count,5} | {samples,7} | {elements.Count,8} | {visibilityBytes / 1024.0,15:0.#} kB | {attributionBytes / 1024.0,16:0.#} kB | {(double)attributionBytes / visibilityBytes,5:0.#}x");
            output.WriteLine($"measured managed allocation during the build: {measuredBytes / 1024.0:0.#} kB ({(double)measuredBytes / attributionBytes:0.##}x the payload figure)");
            output.WriteLine($"build time: {stopwatch.ElapsedMilliseconds} ms for {samples} samples ({samples / Math.Max(1.0, stopwatch.ElapsedMilliseconds) / 1000.0:0.###} M samples/s)");
            output.WriteLine($"GUID table: {attribution.OccluderCount} entries, {attribution.OccluderCount * 16} bytes");

            // The payload ratio is the 4-bytes-vs-1-bit arithmetic, and the GUID table is noise at
            // this scale.
            double ratio = (double)attributionBytes / visibilityBytes;
            Assert.InRange(ratio, 31.0, 33.0);

            // A one-sided claim, because only one side is measurable in a shared process: the build
            // really did allocate at least the payload StorageBytes reports, so that figure is not
            // an overestimate that flatters the cache. The upper side is left unasserted — the
            // counter is process-wide and picks up whatever else is running — and reported instead.
            // Measured in isolation the ratio sits near 2x, which is the payload allocated once in
            // the builder and again by the cache's defensive clone.
            Assert.True(measuredBytes >= attributionBytes,
                $"the build allocated {measuredBytes} bytes, less than the {attributionBytes} StorageBytes claims");
        }

        [Fact]
        public void A_Candidate_Sweep_Rebuilds_Attribution_Every_Time()
        {
            // The finding that shapes Stage 9. ShadingPerformance's convenience overload builds a
            // whole SolarAttributionCache per candidate, because the candidate's own faces are part
            // of the occluder set and the first hit therefore changes with every parameter. The
            // cache cannot be reused across candidates — only the BASE visibility cache can, and it
            // is the expensive one to compute.
            //
            // Measured here so Stage 9's cost model rests on a number rather than an assumption.
            const double gridSize = 0.5;
            Face3D face = WallFace();
            ApertureSolarTarget target = new ApertureSolarTarget(
                Guid.NewGuid(), Guid.NewGuid(), face, SAM.Geometry.SolarCalculator.Query.AnalysisCells(face, gridSize));

            SolarVisibilityCache visibility = Weather.SolarCalculator.Create.SolarVisibilityCache(
                TestHelpers.London(), Year, 2.0, new List<LinkedFace3D>(), target.AnalysisCells,
                cellSize: gridSize, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);

            ApertureDesirability desirability = Analytical.SolarCalculator.Create.ApertureDesirability(
                target, visibility,
                new SeasonalDesirability(new AnalysisPeriod(Year, 6, 1, 8, 31), new AnalysisPeriod(Year, 11, 1, 2, 28)),
                TestHelpers.SolarSymmetricWeatherData(Year, TestHelpers.London(), 30.0, 900.0, 0.2));

            int cellCount = target.CellCount;
            output.WriteLine($"{visibility.BinCount} sun groups x {cellCount} cells = {(long)visibility.BinCount * cellCount} samples per candidate");

            double[] depths = new double[] { 0.2, 0.4, 0.6, 0.8, 1.0 };
            Stopwatch stopwatch = Stopwatch.StartNew();
            HashSet<string> tableHashes = new HashSet<string>();
            foreach (double depth in depths)
            {
                List<ShadingElement> elements = new Overhang(depth).ShadingElements(target);
                List<LinkedFace3D> occluders = new List<LinkedFace3D>();
                foreach (ShadingElement element in elements)
                {
                    occluders.Add(element.LinkedFace3D);
                }

                SolarAttributionCache attribution = Weather.SolarCalculator.Create.SolarAttributionCache(visibility, occluders, target.AnalysisCells);
                tableHashes.Add(attribution.AttributionTableHash);
            }

            stopwatch.Stop();
            double perCandidate = stopwatch.Elapsed.TotalMilliseconds / depths.Length;
            output.WriteLine($"{depths.Length} candidates evaluated in {stopwatch.ElapsedMilliseconds} ms, {perCandidate:0.#} ms per candidate");
            output.WriteLine($"{tableHashes.Count} distinct attribution table hashes across {depths.Length} candidates");

            // Every candidate has its own identity, so no cache is reusable between them. This is
            // the correct behaviour — silently reusing one would attribute energy to the wrong
            // geometry — and it is why Stage 9's cost is dominated by attribution rebuilds.
            Assert.Equal(depths.Length, tableHashes.Count);

            // The base visibility cache is the shared, reusable part.
            Assert.Equal(cellCount, visibility.CellCount);
        }
    }
}
