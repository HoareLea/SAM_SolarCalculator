// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The SunAngleStep resolution study: what the 2 degree default actually costs and actually
    /// buys, measured on a workload where raycasting genuinely dominates rather than on a toy
    /// aperture where the numbers are setup noise.
    ///
    /// This is a BENCHMARK, not a correctness test, and it builds several whole-year visibility
    /// caches over a few thousand analysis cells — minutes, not seconds. It is therefore gated on
    /// the SAM_SOLAR_BENCHMARKS environment variable and does nothing in a normal run:
    ///
    ///     SAM_SOLAR_BENCHMARKS=1 dotnet test --filter FullyQualifiedName~SunAngleStepScaling
    ///
    /// Accuracy is measured against an EXACT per-hour baseline rather than a merely finer binning.
    /// A bin size small enough that no two distinct sun positions share a bin gives every daylight
    /// hour its own bin, which is by construction the same answer a per-hour CellVisibility sweep
    /// would give, and it is reachable through the public cache API.
    /// </summary>
    public class SunAngleStepScalingTests
    {
        private readonly ITestOutputHelper output;

        public SunAngleStepScalingTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static readonly int Year = 2018;

        private static bool Enabled
        {
            get
            {
                return Environment.GetEnvironmentVariable("SAM_SOLAR_BENCHMARKS") == "1";
            }
        }

        /// <summary>South-facing wall, 8 m wide x 4.5 m high, sill at z = 1.</summary>
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

        /// <summary>A few occluders so the raycast has real candidate work to do.</summary>
        private static List<LinkedFace3D> Occluders()
        {
            return new List<LinkedFace3D>
            {
                // Overhanging slab above the wall.
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D>
                {
                    new Point3D(-1, 0, 6.0), new Point3D(9, 0, 6.0),
                    new Point3D(9, -2.0, 6.0), new Point3D(-1, -2.0, 6.0),
                }))),
                // Vertical fin off the east jamb.
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D>
                {
                    new Point3D(8.2, 0, 1), new Point3D(8.2, -1.5, 1),
                    new Point3D(8.2, -1.5, 5.5), new Point3D(8.2, 0, 5.5),
                }))),
                // Detached block to the south-west.
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D>
                {
                    new Point3D(-3, -6, 0), new Point3D(1, -6, 0),
                    new Point3D(1, -6, 8), new Point3D(-3, -6, 8),
                }))),
            };
        }

        [Fact]
        public void SunAngleStep_Cost_And_Accuracy_Scaling()
        {
            if (!Enabled)
            {
                output.WriteLine("SKIPPED: set SAM_SOLAR_BENCHMARKS=1 to run the SunAngleStep scaling study.");
                return;
            }

            double gridSize = 0.12;
            Face3D face = WallFace();
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(face, gridSize);
            List<LinkedFace3D> occluders = Occluders();
            Location location = TestHelpers.London();

            output.WriteLine($"workload: {face.GetArea():0.#} m2 wall, gridSize {gridSize} m, {cells.Count} analysis cells, {occluders.Count} occluders");
            Assert.InRange(cells.Count, 2000, 3500);

            // Exact per-hour baseline: a bin small enough that no two sun positions share one.
            Stopwatch stopwatch = Stopwatch.StartNew();
            SolarVisibilityCache exact = Build(location, 0.02, occluders, cells, gridSize);
            stopwatch.Stop();
            long exactMilliseconds = stopwatch.ElapsedMilliseconds;
            Dictionary<int, int> exactHourToBin = HourToBin(exact);
            output.WriteLine($"exact per-hour baseline: {exact.BinCount} bins over {exactHourToBin.Count} daylight hours, build {exactMilliseconds} ms, storage {StorageBytes(exact) / 1024.0:0.#} KiB");
            double[] steps = new double[] { 1.0, 2.0, 5.0 };
            int[] groups = new int[steps.Length];
            long[] milliseconds = new long[steps.Length];
            double[] errors = new double[steps.Length];
            double[] storage = new double[steps.Length];

            for (int s = 0; s < steps.Length; s++)
            {
                stopwatch.Restart();
                SolarVisibilityCache cache = Build(location, steps[s], occluders, cells, gridSize);
                stopwatch.Stop();

                groups[s] = cache.BinCount;
                milliseconds[s] = stopwatch.ElapsedMilliseconds;
                errors[s] = Disagreement(cache, exact, exactHourToBin, cells.Count);
                storage[s] = StorageBytes(cache) / 1024.0;

                // Binning must never be worse than a few percent of cell-hours at these steps.
                Assert.InRange(errors[s], 0.0, 0.10);
            }

            int baseline = Array.IndexOf(steps, 2.0);
            output.WriteLine("");
            output.WriteLine("step  | groups | cells | build ms | relative | disagreeing cell-hours | storage KiB");
            for (int s = 0; s < steps.Length; s++)
            {
                output.WriteLine($"{steps[s]:0.#} deg | {groups[s]} | {cells.Count} | {milliseconds[s]} | " +
                    $"{(double)milliseconds[s] / milliseconds[baseline]:0.00}x | {errors[s] * 100:0.###} % | {storage[s]:0.#}");
            }

            output.WriteLine($"exact | {exact.BinCount} | {cells.Count} | {exactMilliseconds} | " +
                $"{(double)exactMilliseconds / milliseconds[baseline]:0.00}x | 0 % (definition) | {StorageBytes(exact) / 1024.0:0.#}");

            // Finer binning must monotonically reduce the disagreement with the exact sun position.
            for (int s = 1; s < steps.Length; s++)
            {
                Assert.True(errors[s] > errors[s - 1],
                    $"a coarser step must disagree more: {steps[s]} deg {errors[s] * 100:0.###} % vs {steps[s - 1]} deg {errors[s - 1] * 100:0.###} %");
            }

            output.WriteLine("");
            output.WriteLine("Build time is NOT proportional to bin count on this workload: there is a large fixed cost " +
                "(whole-year sun positions and cell preparation) that only 3 occluders cannot outweigh. Fitting the three " +
                $"points gives about {(milliseconds[0] - milliseconds[2]) / (double)(groups[0] - groups[2]):0.00} ms per bin " +
                $"plus roughly {milliseconds[2] - (long)(groups[2] * (milliseconds[0] - milliseconds[2]) / (double)(groups[0] - groups[2]))} ms fixed. " +
                "On a real model with hundreds of occluders the per-bin term dominates and the relative column approaches " +
                "the ratio of the group counts.");
        }

        private static SolarVisibilityCache Build(Location location, double step, List<LinkedFace3D> occluders, List<AnalysisCell> cells, double gridSize)
        {
            return Weather.SolarCalculator.Create.SolarVisibilityCache(
                location, Year, step, occluders, cells,
                cellSize: gridSize, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);
        }

        /// <summary>Inverts SunBin.HoursOfYear into hour-of-year -> bin index.</summary>
        private static Dictionary<int, int> HourToBin(SolarVisibilityCache cache)
        {
            Dictionary<int, int> result = new Dictionary<int, int>();
            List<SunBin> bins = cache.Bins;
            for (int b = 0; b < bins.Count; b++)
            {
                List<int> hours = bins[b]?.HoursOfYear;
                if (hours == null)
                {
                    continue;
                }

                foreach (int hour in hours)
                {
                    result[hour] = b;
                }
            }

            return result;
        }

        /// <summary>
        /// Fraction of (daylight hour x cell) pairs whose lit state differs from the exact per-hour
        /// baseline. This is the quantity that actually matters: how often the binned cache tells a
        /// cell it is lit when the true sun position for that hour says otherwise, or vice versa.
        /// </summary>
        private static double Disagreement(SolarVisibilityCache cache, SolarVisibilityCache exact, Dictionary<int, int> exactHourToBin, int cellCount)
        {
            Dictionary<int, int> hourToBin = HourToBin(cache);
            long compared = 0;
            long differing = 0;

            foreach (KeyValuePair<int, int> pair in exactHourToBin)
            {
                if (!hourToBin.TryGetValue(pair.Key, out int binIndex))
                {
                    continue;
                }

                for (int c = 0; c < cellCount; c++)
                {
                    compared++;
                    if (cache.IsLit(binIndex, c) != exact.IsLit(pair.Value, c))
                    {
                        differing++;
                    }
                }
            }

            return compared == 0 ? double.NaN : (double)differing / compared;
        }

        /// <summary>Packed lit-bit storage: one bit per cell per bin.</summary>
        private static long StorageBytes(SolarVisibilityCache cache)
        {
            return (long)cache.BinCount * ((cache.CellCount + 63) / 64) * 8;
        }
    }
}
