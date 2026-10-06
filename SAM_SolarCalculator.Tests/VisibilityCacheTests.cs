// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Stage 2 tests: sun-position binning and the per-cell direct-beam visibility cache.
    /// The bias study compares cache-backed lit fractions against the exact per-hour sampled
    /// simulation (Simulate_Coverage) on a synthetic south window with an overhang.
    /// </summary>
    public class VisibilityCacheTests
    {
        private readonly ITestOutputHelper output;

        public VisibilityCacheTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static Location London()
        {
            Location location = new Location("London", -0.1278, 51.5074, 0);
            location.SetValue(LocationParameter.TimeZone, "UTC+00:00");
            return location;
        }

        /// <summary>South-facing window (outward normal (0,-1,0)), 2 m wide x 1 m high, sill at z = 1.</summary>
        private static Face3D WindowFace()
        {
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 1),
                new Point3D(2, 0, 1),
                new Point3D(2, 0, 2),
                new Point3D(0, 0, 2),
            }));
        }

        /// <summary>Horizontal overhang slab, 1.5 m deep, 0.4 m above the window head.</summary>
        private static Face3D OverhangFace()
        {
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(-0.5, 0, 2.4),
                new Point3D(2.5, 0, 2.4),
                new Point3D(2.5, -1.5, 2.4),
                new Point3D(-0.5, -1.5, 2.4),
            }));
        }

        private static List<LinkedFace3D> Occluders()
        {
            return new List<LinkedFace3D>
            {
                new LinkedFace3D(Guid.NewGuid(), OverhangFace()),
            };
        }

        [Fact]
        public void SunBins_Compression_And_Content()
        {
            Location location = London();
            int year = 2018;

            List<SunBin> bins = Weather.SolarCalculator.Create.SunBins(location, year, 2.0);
            Assert.NotNull(bins);
            Assert.NotEmpty(bins);

            // Daylight hours at London ~ 4400-4500; bins must be at least 4x fewer.
            int daylightHours = bins.Sum(x => x.HoursOfYear.Count);
            output.WriteLine($"2 deg bins: {bins.Count} bins covering {daylightHours} daylight hours");
            Assert.InRange(daylightHours, 4000, 4700);
            Assert.True(bins.Count * 4 <= daylightHours, $"expected >=4x compression, got {daylightHours}/{bins.Count}");

            // Member hours are within the year, sorted, and unique across bins.
            HashSet<int> all = new HashSet<int>();
            foreach (SunBin bin in bins)
            {
                Assert.NotNull(bin.RepresentativeDirection);
                Assert.True(System.Math.Abs(bin.RepresentativeDirection.Length - 1.0) < 1e-9);
                Assert.All(bin.HoursOfYear, h => Assert.InRange(h, 0, 8759));
                Assert.All(bin.HoursOfYear, h => Assert.True(all.Add(h), "hour assigned to two bins"));
            }

            // Bin centre direction must quantise back to the same bin.
            foreach (SunBin bin in bins)
            {
                Assert.True(Geometry.SolarCalculator.Query.TryGetSunAngles(bin.RepresentativeDirection, out double altitude, out double azimuth));
                Assert.Equal(bin.AltitudeBin * 2.0 + 1.0, altitude, 6);
                Assert.Equal(bin.AzimuthBin * 2.0 + 1.0, azimuth, 6);
            }
        }

        [Fact]
        public void SunBins_Deterministic_And_WeatherIndependent()
        {
            // Binning is a pure function of location + dates + resolution: two builds are identical,
            // and no weather data is taken (so no weather change can alter the bins).
            List<SunBin> a = Weather.SolarCalculator.Create.SunBins(London(), 2018, 2.0);
            List<SunBin> b = Weather.SolarCalculator.Create.SunBins(London(), 2018, 2.0);
            Assert.Equal(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].AltitudeBin, b[i].AltitudeBin);
                Assert.Equal(a[i].AzimuthBin, b[i].AzimuthBin);
                Assert.Equal(a[i].HoursOfYear, b[i].HoursOfYear);
                Assert.Equal(a[i].RepresentativeDirection.ToString(), b[i].RepresentativeDirection.ToString());
            }
        }

        [Fact]
        public void GeometryHash_Invalidates_On_Geometry_Change()
        {
            Face3D window = WindowFace();
            List<LinkedFace3D> occluders = Occluders();
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(window, 0.5);

            string hash1 = Geometry.SolarCalculator.Query.GeometryHash(occluders);
            string hash2 = Geometry.SolarCalculator.Query.GeometryHash(occluders);
            Assert.Equal(hash1, hash2);

            string targetHash1 = Geometry.SolarCalculator.Query.TargetHash(cells);
            Assert.Equal(targetHash1, Geometry.SolarCalculator.Query.TargetHash(cells));

            // Move the overhang 10 cm -> context changed -> hash must change.
            Face3D moved = Core.Query.Clone(OverhangFace());
            moved = moved.GetMoved(new Vector3D(0, 0, 0.1)) as Face3D;
            string hash3 = Geometry.SolarCalculator.Query.GeometryHash(new List<LinkedFace3D> { new LinkedFace3D(Guid.NewGuid(), moved) });
            Assert.NotEqual(hash1, hash3);

            // Reordering the same OCCLUDER set does NOT change the context hash.
            List<LinkedFace3D> reversed = new List<LinkedFace3D>(occluders);
            reversed.Reverse();
            Assert.Equal(hash1, Geometry.SolarCalculator.Query.GeometryHash(reversed));

            // A start-index-shifted but identical polygon keeps the same target hash.
            Face3D rotated = new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(2, 0, 2),
                new Point3D(0, 0, 2),
                new Point3D(0, 0, 1),
                new Point3D(2, 0, 1),
            }));
            List<AnalysisCell> cellsRotated = Geometry.SolarCalculator.Query.AnalysisCells(rotated, 0.5);
            Assert.Equal(targetHash1, Geometry.SolarCalculator.Query.TargetHash(cellsRotated));
        }

        [Fact]
        public void Cache_Json_RoundTrip_Preserves_Bitset()
        {
            Location location = London();
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(WindowFace(), 0.5);
            SolarVisibilityCache cache = Weather.SolarCalculator.Create.SolarVisibilityCache(location, 2018, 2.0, Occluders(), cells, 0.5);
            Assert.NotNull(cache);

            SolarVisibilityCache roundTripped = new SolarVisibilityCache(cache.ToJsonObject());
            Assert.Equal(cache.GetIdentity(), roundTripped.GetIdentity());
            Assert.Equal(cache.BinCount, roundTripped.BinCount);
            Assert.Equal(cache.CellCount, roundTripped.CellCount);

            for (int b = 0; b < cache.BinCount; b++)
            {
                for (int c = 0; c < cache.CellCount; c++)
                {
                    Assert.Equal(cache.IsLit(b, c), roundTripped.IsLit(b, c));
                }
            }
        }

        [Fact]
        public void Cache_Build_Deterministic()
        {
            Location location = London();
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(WindowFace(), 0.5);

            SolarVisibilityCache a = Weather.SolarCalculator.Create.SolarVisibilityCache(location, 2018, 2.0, Occluders(), cells, 0.5);
            SolarVisibilityCache b = Weather.SolarCalculator.Create.SolarVisibilityCache(location, 2018, 2.0, Occluders(), cells, 0.5);
            Assert.Equal(a.GetIdentity(), b.GetIdentity());
            for (int bin = 0; bin < a.BinCount; bin++)
            {
                for (int c = 0; c < a.CellCount; c++)
                {
                    Assert.Equal(a.IsLit(bin, c), b.IsLit(bin, c));
                }
            }
        }

        [Fact]
        public void Cache_Obstructed_And_Unobstructed()
        {
            Location location = London();
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(WindowFace(), 0.5);

            // No occluders: every cell lit for every bin (front-facing by construction of bin set? no -
            // bins include northerly summer morning/evening sun behind the wall -> front-facing test
            // must mark those shaded).
            SolarVisibilityCache open = Weather.SolarCalculator.Create.SolarVisibilityCache(location, 2018, 2.0, new List<LinkedFace3D>(), cells, 0.5);
            int litCount = 0;
            int total = 0;
            for (int b = 0; b < open.BinCount; b++)
            {
                for (int c = 0; c < open.CellCount; c++)
                {
                    total++;
                    if (open.IsLit(b, c)) litCount++;
                }
            }
            output.WriteLine($"unobstructed: {litCount}/{total} lit");
            Assert.True(litCount > 0);
            Assert.True(litCount < total, "northerly bins behind the south wall must be shaded");

            // Full enclosure: a hood around the window (front + top + bottom + both sides), open
            // only toward the wall -> every outward ray from every cell hits a face.
            List<LinkedFace3D> enclosure = new List<LinkedFace3D>
            {
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D> { new Point3D(-1, -0.5, 0), new Point3D(3, -0.5, 0), new Point3D(3, -0.5, 3), new Point3D(-1, -0.5, 3) }))),   // front
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D> { new Point3D(-1, 0, 3), new Point3D(3, 0, 3), new Point3D(3, -0.5, 3), new Point3D(-1, -0.5, 3) }))),       // top
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D> { new Point3D(-1, -0.5, 0), new Point3D(3, -0.5, 0), new Point3D(3, 0, 0), new Point3D(-1, 0, 0) }))),       // bottom
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D> { new Point3D(-1, -0.5, 0), new Point3D(-1, 0, 0), new Point3D(-1, 0, 3), new Point3D(-1, -0.5, 3) }))),   // left
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D> { new Point3D(3, 0, 0), new Point3D(3, -0.5, 0), new Point3D(3, -0.5, 3), new Point3D(3, 0, 3) }))),       // right
            };
            SolarVisibilityCache blocked = Weather.SolarCalculator.Create.SolarVisibilityCache(location, 2018, 2.0, enclosure, cells, 0.5);
            for (int b = 0; b < blocked.BinCount; b++)
            {
                for (int c = 0; c < blocked.CellCount; c++)
                {
                    Assert.False(blocked.IsLit(b, c), $"bin {b} cell {c} should be shaded by the full cover");
                }
            }
        }

        [Fact]
        public void Cache_BinningBias_Against_Exact_Sampled_Coverage()
        {
            // Exact baseline: per-hour sampled Simulate_Coverage on a SolarModel holding the window +
            // overhang. The sampled path subdivides the window with the SAME cell grid
            // (Query.AnalysisCells), so per-hour lit fractions are directly comparable with the cache's
            // area-weighted lit fraction.
            Location location = London();
            const int year = 2018;
            const double cellSize = 0.5;

            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(WindowFace(), cellSize);
            List<double> cellAreas = cells.ConvertAll(x => x.Area);
            double totalArea = cellAreas.Sum();

            List<DateTime> dateTimes = new List<DateTime>();
            DateTime start = new DateTime(year, 1, 1);
            for (int i = 0; i < 8760; i++)
            {
                dateTimes.Add(start.AddHours(i));
            }

            SolarModel solarModel = new SolarModel(location);
            Guid windowGuid = Guid.NewGuid();
            solarModel.Add(new LinkedFace3D(windowGuid, WindowFace()));
            foreach (LinkedFace3D occluder in Occluders())
            {
                solarModel.Add(occluder);
            }

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            List<SolarCoverageSimulationResult> exact = solarModel.Simulate_Coverage(dateTimes, sampleSize: cellSize);
            stopwatch.Stop();
            long exactMs = stopwatch.ElapsedMilliseconds;
            Assert.NotNull(exact);

            SolarCoverageSimulationResult windowCoverage = exact.Find(x => x.Reference == windowGuid.ToString());
            Assert.NotNull(windowCoverage);
            Dictionary<DateTime, double> exactByDateTime = new Dictionary<DateTime, double>();
            foreach (Tuple<DateTime, double> tuple in windowCoverage.Coverage)
            {
                exactByDateTime[tuple.Item1] = tuple.Item2;
            }

            output.WriteLine($"exact per-hour sampled baseline: {exactMs} ms ({dateTimes.Count} hours)");

            foreach (double binSize in new[] { 1.0, 2.0, 5.0 })
            {
                stopwatch.Restart();
                SolarVisibilityCache cache = Weather.SolarCalculator.Create.SolarVisibilityCache(location, year, binSize, Occluders(), cells, cellSize);
                stopwatch.Stop();
                long buildMs = stopwatch.ElapsedMilliseconds;
                Assert.NotNull(cache);

                // Per daylight hour: cache lit fraction vs exact lit fraction.
                double sumAbs = 0;
                double sumAbsWeighted = 0;
                double sumWeight = 0;
                int count = 0;
                double alt = double.NaN;
                double az = double.NaN;
                foreach (SunBin binProbe in cache.Bins)
                {
                    foreach (int hourOfYear in binProbe.HoursOfYear)
                    {
                        DateTime dateTime = start.AddHours(hourOfYear);

                        double cacheFraction = 0;
                        bool hasAngles = Geometry.SolarCalculator.Query.TryGetSunAngles(Geometry.SolarCalculator.Query.SunDirection(location, dateTime, true), out alt, out az);
                        Assert.True(hasAngles);
                        int binIndex = cache.FindBin(alt, az);
                        Assert.True(binIndex >= 0, "every daylight hour must resolve to a bin");
                        for (int c = 0; c < cache.CellCount; c++)
                        {
                            if (cache.IsLit(binIndex, c))
                            {
                                cacheFraction += cellAreas[c];
                            }
                        }
                        cacheFraction /= totalArea;

                        Assert.True(exactByDateTime.TryGetValue(dateTime, out double exactFraction), $"hour {dateTime:O} missing from exact series");

                        sumAbs += Math.Abs(cacheFraction - exactFraction);
                        // Weight by a representative clear-sky-ish DNI proxy so the energy-relevant
                        // error is visible, not just the flat hour average.
                        double weight = Math.Max(0.0001, Math.Sin(alt * Math.PI / 180.0));
                        sumAbsWeighted += Math.Abs(cacheFraction - exactFraction) * weight;
                        sumWeight += weight;
                        count++;
                    }
                }

                double mae = sumAbs / count;
                double maeWeighted = sumAbsWeighted / sumWeight;
                output.WriteLine($"bin {binSize:0.#} deg: bins={cache.BinCount} daylightHours={count} buildMs={buildMs} MAE={mae:0.0000} DNIweightedMAE={maeWeighted:0.0000}");

                // The plan's accuracy gate: 2 deg bins must stay within 2% MAE of the exact per-hour
                // baseline. Measured on this model: ~0.015. 1 deg (~0.013) and 5 deg (~0.029) are
                // reported above for the record.
                if (Math.Abs(binSize - 2.0) < 1e-9)
                {
                    Assert.True(mae < 0.02, $"2 deg binning MAE {mae:0.0000} exceeds the 2% gate");
                }
            }
        }
    }
}
