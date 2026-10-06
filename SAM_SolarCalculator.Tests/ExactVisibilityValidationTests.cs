// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Quantifies how much geometric accuracy is lost when the per-hour EXACT projective
    /// visibility engine (SolarSimulation / Modify.Simulate / ExposedExactLinkedFace3Ds) is
    /// replaced by the AnalysisCell + SunBin approximation (SolarVisibilityCache).
    ///
    /// The two engines are deliberately NOT mathematically identical; this file measures their
    /// difference and decomposes it into independently-measured components:
    ///
    ///   A. SPATIAL   — exact polygon coverage vs per-hour SAMPLED cell coverage (same hourly sun
    ///                  directions on both sides; no SunBin approximation). Measured over the hours
    ///                  the target actually FACES the sun (see the front/back finding below).
    ///   B. ANGULAR   — per-hour sampled coverage vs cache coverage (same AnalysisCell grid on both
    ///                  sides; only the sun direction changes: actual hour vs bin centre).
    ///   C. COMBINED  — exact polygon coverage vs cache coverage over front-facing hours.
    ///
    /// Two findings this measurement surfaced, both reported here rather than "fixed":
    ///
    ///   F1. The exact polygon primitive (VisibleLinkedFace3Ds / ExposedExactLinkedFace3Ds) does
    ///       NOT apply the front-facing orientation test that the sampled and cache paths apply
    ///       (IsSolarCandidate / CellVisibility). It therefore reports a BACK-facing target as lit,
    ///       which is why a naive exact-vs-cache MAE is dominated by this semantic gap rather than
    ///       by discretisation. Comparisons A and C are therefore restricted to front-facing hours.
    ///
    ///   F2. The panel-oriented exact path MERGES coplanar faces, so an aperture target coplanar
    ///       with its host facade is absorbed/occluded by that facade and reports ~0 coverage in a
    ///       real building. The exact path is therefore NOT a usable aperture-level reference for
    ///       real models; the sampled path (same front/back test and cell grid as the cache) is used
    ///       for the real-model angular study instead.
    ///
    ///   Because of F1, A and C are evaluated on front-facing daylight hours only, while B is evaluated
    ///   over all daylight hours (sampled and cache share the front-facing semantics); the three MAE
    ///   values are therefore each independently useful but are NOT strictly commensurable over an
    ///   identical hour population.
    ///
    /// No solar physics, shading optimisation, control-profile behaviour or cache algorithm is
    /// changed here — this is a measurement harness only.
    /// </summary>
    public class ExactVisibilityValidationTests
    {
        private readonly ITestOutputHelper output;

        public ExactVisibilityValidationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        // The physics gate every engine applies (Core.Tolerance.Angle = 2 deg).
        private const double MinHorizonAngle = Core.Tolerance.Angle;

        private static double MinAltitudeDegrees
        {
            get { return MinHorizonAngle * 180.0 / Math.PI; }
        }

        // ---------------------------------------------------------------------------------------
        // Synthetic geometry: a south-facing window with a horizontal overhang, matching the
        // geometry established by VisibilityCacheTests so the two studies are comparable.
        // ---------------------------------------------------------------------------------------

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

        private static List<LinkedFace3D> OverhangOccluders()
        {
            return new List<LinkedFace3D> { new LinkedFace3D(Guid.NewGuid(), OverhangFace()) };
        }

        /// <summary>A single vertical face in front of the window (y = -1) that fully shades it.</summary>
        private static List<LinkedFace3D> FullCoverOccluders()
        {
            return new List<LinkedFace3D>
            {
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D>
                {
                    new Point3D(-1, -1, 0),
                    new Point3D(3, -1, 0),
                    new Point3D(3, -1, 3),
                    new Point3D(-1, -1, 3),
                }))),
            };
        }

        // ---------------------------------------------------------------------------------------
        // Timeline alignment: ONE sun-position helper used by both the exact/sampled reference and
        // the cache comparisons, so the two engines can never evaluate different solar timestamps.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The sun direction the exact/sampled solver evaluates for weather hour <paramref name="dateTime"/>.
        /// It samples the sun at dateTime + <paramref name="shiftMinutes"/> and builds the vector from the
        /// SAME angles (Geometry.SolarCalculator.Query.TryGetSunAngles) and the SAME builder
        /// (Geometry.SolarCalculator.Create.SunDirection) that SolarVisibilityCache's SunBins use for bin
        /// membership and representative directions. The shift is therefore the sunPositionShiftInMinutes
        /// recorded in the cache identity — never "hour vs hour + shift" drift.
        /// </summary>
        private static Vector3D SunDirectionAt(Location location, DateTime dateTime, double shiftMinutes)
        {
            DateTime sunTime = shiftMinutes == 0 ? dateTime : dateTime.AddMinutes(shiftMinutes);
            if (!Geometry.SolarCalculator.Query.TryGetSunAngles(location, sunTime, out double altitude, out double azimuth))
            {
                return null;
            }

            return Geometry.SolarCalculator.Create.SunDirection(altitude, azimuth);
        }

        private static List<int> ValidDaylightHours(Location location, int year, double shiftMinutes)
        {
            int count = DateTime.IsLeapYear(year) ? 8784 : 8760;
            DateTime start = new DateTime(year, 1, 1);
            List<int> result = new List<int>();
            for (int h = 0; h < count; h++)
            {
                DateTime sunTime = start.AddHours(h).AddMinutes(shiftMinutes);
                if (Geometry.SolarCalculator.Query.TryGetSunAngles(location, sunTime, out double altitude, out _) && altitude >= MinAltitudeDegrees)
                {
                    result.Add(h);
                }
            }

            return result;
        }

        /// <summary>Hours whose shifted sun direction is in front of the target's outward normal (normal . sun &lt; 0).</summary>
        private static HashSet<int> FrontFacingHours(Location location, int year, List<int> validHours, double shiftMinutes, Vector3D targetNormal)
        {
            DateTime start = new DateTime(year, 1, 1);
            HashSet<int> result = new HashSet<int>();
            foreach (int hour in validHours)
            {
                Vector3D sun = SunDirectionAt(location, start.AddHours(hour), shiftMinutes);
                if (sun != null && targetNormal.DotProduct(sun) < 0)
                {
                    result.Add(hour);
                }
            }

            return result;
        }

        private static Dictionary<int, double> FilterHours(Dictionary<int, double> series, HashSet<int> hours)
        {
            return series.Where(kv => hours.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        /// <summary>Clear-sky DNI proxy (sin of altitude) — a weighting proxy only, NOT fabricated weather.</summary>
        private static Dictionary<int, double> ClearSkyProxyWeights(Location location, int year, double shiftMinutes, IEnumerable<int> hoursOfYear)
        {
            DateTime start = new DateTime(year, 1, 1);
            Dictionary<int, double> weights = new Dictionary<int, double>();
            foreach (int h in hoursOfYear)
            {
                DateTime sunTime = start.AddHours(h).AddMinutes(shiftMinutes);
                if (Geometry.SolarCalculator.Query.TryGetSunAngles(location, sunTime, out double altitude, out _))
                {
                    weights[h] = Math.Max(0.0001, Math.Sin(altitude * Math.PI / 180.0));
                }
            }

            return weights;
        }

        // ---------------------------------------------------------------------------------------
        // Coverage series builders. Every series is returned as LIT AREA (m2) per hour-of-year so
        // the three comparisons share one metric path with an explicit denominator per comparison.
        // ---------------------------------------------------------------------------------------

        private static Dictionary<int, double> LitAreaViaSimulateCoverage(
            Face3D targetFace, List<LinkedFace3D> occluders, Location location, int year,
            List<int> hoursOfYear, double shiftMinutes, double sampleSize, out long elapsedMs)
        {
            SolarModel solarModel = new SolarModel(location);
            Guid targetGuid = Guid.NewGuid();
            solarModel.Add(new LinkedFace3D(targetGuid, targetFace));
            if (occluders != null)
            {
                foreach (LinkedFace3D occluder in occluders)
                {
                    if (occluder?.Face3D != null)
                    {
                        solarModel.Add(occluder);
                    }
                }
            }

            Dictionary<DateTime, Vector3D> directionDictionary = new Dictionary<DateTime, Vector3D>();
            DateTime yearStart = new DateTime(year, 1, 1);
            foreach (int hour in hoursOfYear)
            {
                DateTime dateTime = yearStart.AddHours(hour);
                Vector3D sunDirection = SunDirectionAt(location, dateTime, shiftMinutes);
                if (sunDirection != null)
                {
                    directionDictionary[dateTime] = sunDirection;
                }
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            List<SolarCoverageSimulationResult> results = solarModel.Simulate_Coverage(directionDictionary, minHorizonAngle: MinHorizonAngle, sampleSize: sampleSize);
            stopwatch.Stop();
            elapsedMs = stopwatch.ElapsedMilliseconds;

            double faceArea = targetFace.GetArea();
            Dictionary<int, double> litArea = new Dictionary<int, double>();
            SolarCoverageSimulationResult targetResult = results?.Find(x => x.Reference == targetGuid.ToString());
            if (targetResult?.Coverage != null)
            {
                foreach (Tuple<DateTime, double> tuple in targetResult.Coverage)
                {
                    litArea[(int)(tuple.Item1 - yearStart).TotalHours] = tuple.Item2 * faceArea;
                }
            }

            return litArea;
        }

        private static Dictionary<int, double> LitAreaViaCache(
            SolarVisibilityCache cache, List<AnalysisCell> cells, Location location, int year,
            List<int> hoursOfYear, double shiftMinutes, out long evalMs)
        {
            List<double> cellAreas = cells.ConvertAll(x => x.Area);
            DateTime yearStart = new DateTime(year, 1, 1);

            Stopwatch stopwatch = Stopwatch.StartNew();
            Dictionary<int, double> litArea = new Dictionary<int, double>();
            foreach (int hour in hoursOfYear)
            {
                DateTime sunTime = yearStart.AddHours(hour).AddMinutes(shiftMinutes);
                if (!Geometry.SolarCalculator.Query.TryGetSunAngles(location, sunTime, out double altitude, out double azimuth))
                {
                    continue;
                }

                int bin = cache.FindBin(altitude, azimuth);
                if (bin < 0)
                {
                    continue;
                }

                double lit = 0;
                for (int c = 0; c < cache.CellCount; c++)
                {
                    if (cache.IsLit(bin, c))
                    {
                        lit += cellAreas[c];
                    }
                }

                litArea[hour] = lit;
            }

            stopwatch.Stop();
            evalMs = stopwatch.ElapsedMilliseconds;
            return litArea;
        }

        // ---------------------------------------------------------------------------------------
        // Error metrics.
        // ---------------------------------------------------------------------------------------

        private sealed class ErrorSummary
        {
            public int N;
            public double Mae;
            public double Rmse;
            public double Bias;
            public double MaxAbs;
            public double P95Abs;
            public double WeightedMae;
            public double ReferenceAreaHours;
            public double CandidateAreaHours;
        }

        /// <summary>
        /// Coverage error of <paramref name="candidateArea"/> against <paramref name="referenceArea"/>,
        /// both in m2, normalised by <paramref name="denominator"/> (face area for A/C, total cell area
        /// for B) so every metric is a dimensionless coverage fraction. Error = candidate - reference.
        /// </summary>
        private static ErrorSummary ComputeError(
            Dictionary<int, double> referenceArea, Dictionary<int, double> candidateArea, double denominator, Dictionary<int, double> weights)
        {
            Assert.Equal(referenceArea.Count, candidateArea.Count);
            Assert.True(new HashSet<int>(referenceArea.Keys).SetEquals(candidateArea.Keys), "reference and candidate must evaluate the same hours");

            List<int> hours = referenceArea.Keys.OrderBy(x => x).ToList();
            double sumAbs = 0;
            double sumSq = 0;
            double sumSigned = 0;
            double sumAbsWeighted = 0;
            double sumWeight = 0;
            double refAreaHours = 0;
            double candAreaHours = 0;
            List<double> absErrors = new List<double>(hours.Count);

            foreach (int hour in hours)
            {
                double error = (candidateArea[hour] - referenceArea[hour]) / denominator;
                sumAbs += Math.Abs(error);
                sumSq += error * error;
                sumSigned += error;
                refAreaHours += referenceArea[hour];
                candAreaHours += candidateArea[hour];
                absErrors.Add(Math.Abs(error));

                double weight = weights != null && weights.TryGetValue(hour, out double w) ? w : 1.0;
                sumAbsWeighted += Math.Abs(error) * weight;
                sumWeight += weight;
            }

            absErrors.Sort();
            int p95Index = (int)Math.Ceiling(0.95 * absErrors.Count) - 1;
            if (p95Index < 0)
            {
                p95Index = 0;
            }

            return new ErrorSummary
            {
                N = hours.Count,
                Mae = sumAbs / hours.Count,
                Rmse = Math.Sqrt(sumSq / hours.Count),
                Bias = sumSigned / hours.Count,
                MaxAbs = absErrors[absErrors.Count - 1],
                P95Abs = absErrors[p95Index],
                WeightedMae = sumWeight > 0 ? sumAbsWeighted / sumWeight : double.NaN,
                ReferenceAreaHours = refAreaHours,
                CandidateAreaHours = candAreaHours,
            };
        }

        // ---------------------------------------------------------------------------------------
        // FAST — 1. exact Simulate vs exact Simulate_Coverage cross-check (shared primitive proof).
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Exact_Simulate_And_Exact_SimulateCoverage_Agree_On_Shared_Primitive()
        {
            Location location = TestHelpers.London();
            const int year = 2018;

            List<DateTime> sampleDateTimes = new List<DateTime>
            {
                new DateTime(year, 6, 21, 9, 0, 0),
                new DateTime(year, 6, 21, 12, 0, 0),
                new DateTime(year, 6, 21, 15, 0, 0),
                new DateTime(year, 3, 21, 8, 0, 0),
                new DateTime(year, 3, 21, 11, 0, 0),
            };

            Face3D window = WindowFace();
            List<LinkedFace3D> occluders = OverhangOccluders();

            Dictionary<DateTime, Vector3D> directionDictionary = new Dictionary<DateTime, Vector3D>();
            foreach (DateTime dateTime in sampleDateTimes)
            {
                Vector3D direction = SunDirectionAt(location, dateTime, 0.0);
                if (direction != null)
                {
                    directionDictionary[dateTime] = direction;
                }
            }

            SolarModel fullModel = new SolarModel(location);
            Guid windowGuid = Guid.NewGuid();
            fullModel.Add(new LinkedFace3D(windowGuid, window));
            foreach (LinkedFace3D occluder in occluders)
            {
                fullModel.Add(occluder);
            }

            List<SolarFaceSimulationResult> faceResults = fullModel.Simulate(directionDictionary, false, minHorizonAngle: MinHorizonAngle);
            Assert.NotNull(faceResults);
            Assert.NotEmpty(faceResults);

            SolarFaceSimulationResult faceResult = faceResults.Find(x => x.Reference == windowGuid.ToString());
            Assert.NotNull(faceResult);

            List<SolarCoverageSimulationResult> coverageResults = fullModel.Simulate_Coverage(directionDictionary, minHorizonAngle: MinHorizonAngle, sampleSize: double.NaN);
            Assert.NotNull(coverageResults);
            SolarCoverageSimulationResult coverageResult = coverageResults.Find(x => x.Reference == windowGuid.ToString());
            Assert.NotNull(coverageResult);

            double totalArea = window.GetArea();
            foreach (DateTime dateTime in directionDictionary.Keys)
            {
                // Both materialisation paths call the same ComputeSunExposure / ExposedExactLinkedFace3Ds
                // primitive, so the lit-area/face-area ratio must agree.
                List<Face3D> litFragments = faceResult.GetSunExposureFace3Ds(dateTime);
                double litAreaFromPolygons = litFragments == null ? 0 : litFragments.Sum(x => x?.GetArea() ?? 0);
                double coverageFromPolygons = litAreaFromPolygons / totalArea;
                double coverageFromCoverage = coverageResult.GetCoverage(dateTime);

                Assert.False(double.IsNaN(coverageFromCoverage), $"hour {dateTime:O} missing from coverage series");
                Assert.Equal(coverageFromPolygons, coverageFromCoverage, 9);
                output.WriteLine($"hour {dateTime:O}: polygon-lit area = {litAreaFromPolygons:0.######} m2, coverage = {coverageFromCoverage:0.######}");
            }
        }

        // ---------------------------------------------------------------------------------------
        // FAST — 2. trivial geometry: fully-lit and fully-shaded agree across all three engines.
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Trivial_Geometry_Exact_Sampled_And_Cache_Agree_FullyLit_And_FullyShaded()
        {
            Location location = TestHelpers.London();
            const int year = 2018;

            DateTime start = new DateTime(year, 1, 1);
            List<int> litHours = new List<int>();
            foreach (int clock in new[] { 12, 13, 14 })
            {
                litHours.Add((int)(new DateTime(year, 3, 21, clock, 0, 0) - start).TotalHours);
            }

            Face3D window = WindowFace();
            const double cellSize = 0.25;
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(window, cellSize);
            double cellAreaTotal = cells.Sum(x => x.Area);
            Assert.Equal(window.GetArea(), cellAreaTotal, 9); // 0.25 m grid divides the 2 x 1 window exactly.

            Dictionary<int, double> exactLit = LitAreaViaSimulateCoverage(window, new List<LinkedFace3D>(), location, year, litHours, 0.0, double.NaN, out _);
            Dictionary<int, double> sampledLit = LitAreaViaSimulateCoverage(window, new List<LinkedFace3D>(), location, year, litHours, 0.0, cellSize, out _);
            SolarVisibilityCache openCache = Weather.SolarCalculator.Create.SolarVisibilityCache(location, year, 2.0, new List<LinkedFace3D>(), cells, cellSize, MinHorizonAngle, 0.0);
            Dictionary<int, double> cacheLit = LitAreaViaCache(openCache, cells, location, year, litHours, 0.0, out _);

            foreach (int hour in litHours)
            {
                Assert.Equal(window.GetArea(), exactLit[hour], 6);
                Assert.Equal(window.GetArea(), sampledLit[hour], 6);
                Assert.Equal(window.GetArea(), cacheLit[hour], 6);
            }

            List<LinkedFace3D> cover = FullCoverOccluders();
            Dictionary<int, double> exactShaded = LitAreaViaSimulateCoverage(window, cover, location, year, litHours, 0.0, double.NaN, out _);
            Dictionary<int, double> sampledShaded = LitAreaViaSimulateCoverage(window, cover, location, year, litHours, 0.0, cellSize, out _);
            SolarVisibilityCache blockedCache = Weather.SolarCalculator.Create.SolarVisibilityCache(location, year, 2.0, cover, cells, cellSize, MinHorizonAngle, 0.0);
            Dictionary<int, double> cacheShaded = LitAreaViaCache(blockedCache, cells, location, year, litHours, 0.0, out _);

            foreach (int hour in litHours)
            {
                Assert.Equal(0.0, exactShaded[hour], 9);
                Assert.Equal(0.0, sampledShaded[hour], 9);
                Assert.Equal(0.0, cacheShaded[hour], 9);
            }

            output.WriteLine("fully-lit and fully-shaded trivial cases: exact == sampled == cache within tolerance");
        }

        // ---------------------------------------------------------------------------------------
        // FAST — 3. finding F1: the exact path omits the front-facing test.
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// CHARACTERIZATION TEST for known defect F1. The exact polygon path omits the front-facing
        /// orientation test that the sampled and cache paths apply, so a back-facing target is
        /// reported lit. This pins EXISTING behaviour only — it is not a statement of desired solver
        /// semantics — and must be updated/removed when the future exact-engine fix is implemented.
        /// </summary>
        [Fact]
        public void KnownDefect_F1_ExactPath_Omits_FrontFacing_Test_BackFacing_Hours_Are_Reported_Lit()
        {
            Location location = TestHelpers.London();
            const int year = 2018;
            Face3D window = WindowFace();
            Vector3D normal = window.GetPlane().Normal;

            // 07:00 on the summer solstice: the sun is north-east, BEHIND the south-facing window.
            DateTime backFacing = new DateTime(year, 6, 21, 7, 0, 0);
            Assert.True(normal.DotProduct(SunDirectionAt(location, backFacing, 0.0)) > 0, "07:00 must be a back-facing hour for a south window");

            List<int> hours = new List<int> { (int)(backFacing - new DateTime(year, 1, 1)).TotalHours };
            const double cellSize = 0.25;
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(window, cellSize);

            // Exact polygon coverage: reports the back-facing window as fully lit.
            Dictionary<int, double> exact = LitAreaViaSimulateCoverage(window, new List<LinkedFace3D>(), location, year, hours, 0.0, double.NaN, out _);

            // Cache (front-facing test present): reports shaded.
            SolarVisibilityCache cache = Weather.SolarCalculator.Create.SolarVisibilityCache(location, year, 2.0, new List<LinkedFace3D>(), cells, cellSize, MinHorizonAngle, 0.0);
            Dictionary<int, double> cached = LitAreaViaCache(cache, cells, location, year, hours, 0.0, out _);

            output.WriteLine($"back-facing hour {backFacing:O}: exact coverage = {exact[hours[0]] / window.GetArea():0.0000}, cache coverage = {cached[hours[0]] / window.GetArea():0.0000}");

            // Document the semantic gap: the exact path has no front-facing test, so it over-reports.
            // The cache is the physically correct one here (a back-facing window receives no direct sun).
            Assert.Equal(window.GetArea(), exact[hours[0]], 6);
            Assert.Equal(0.0, cached[hours[0]], 9);
        }

        // ---------------------------------------------------------------------------------------
        // FAST — 4. shadow-boundary hours: where the overhang shadow edge crosses the target.
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Shadow_Boundary_Hours_Report_Spatial_And_Angular_Decomposition()
        {
            Location location = TestHelpers.London();
            const int year = 2018;
            const double cellSize = 0.25;
            const double binSize = 2.0;

            Face3D window = WindowFace();
            Vector3D normal = window.GetPlane().Normal;
            List<LinkedFace3D> occluders = OverhangOccluders();
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(window, cellSize);
            double cellAreaTotal = cells.Sum(x => x.Area);

            DateTime start = new DateTime(year, 1, 1);
            List<int> hours = new List<int>();
            foreach (int clockHour in new[] { 7, 8, 9, 10, 11, 12, 13 })
            {
                hours.Add((int)(new DateTime(year, 6, 21, clockHour, 0, 0) - start).TotalHours);
            }

            Dictionary<int, double> exactLit = LitAreaViaSimulateCoverage(window, occluders, location, year, hours, 0.0, double.NaN, out long exactMs);
            Dictionary<int, double> sampledLit = LitAreaViaSimulateCoverage(window, occluders, location, year, hours, 0.0, cellSize, out long sampledMs);

            Stopwatch sw = Stopwatch.StartNew();
            SolarVisibilityCache cache = Weather.SolarCalculator.Create.SolarVisibilityCache(location, year, binSize, occluders, cells, cellSize, MinHorizonAngle, 0.0);
            sw.Stop();
            long buildMs = sw.ElapsedMilliseconds;

            Dictionary<int, double> cacheLit = LitAreaViaCache(cache, cells, location, year, hours, 0.0, out long evalMs);

            output.WriteLine($"shadow-boundary hours (cell {cellSize} m, bin {binSize} deg): exact={exactMs} ms, sampled={sampledMs} ms, cacheBuild={buildMs} ms, cacheEval={evalMs} ms");
            output.WriteLine("hour | facing | exact | sampled | cache | spatialErr(front only) | angularErr(sampled-cache)");
            foreach (int hour in hours)
            {
                DateTime dateTime = start.AddHours(hour);
                bool frontFacing = normal.DotProduct(SunDirectionAt(location, dateTime, 0.0)) < 0;
                double exact = exactLit[hour] / window.GetArea();
                double sampled = sampledLit[hour] / window.GetArea();
                double cacheF = cacheLit[hour] / window.GetArea();

                double spatialErr = frontFacing ? (sampled - exact) : double.NaN;
                double angularErr = (cacheLit[hour] - sampledLit[hour]) / cellAreaTotal;

                output.WriteLine(
                    $"{dateTime:HH:mm} | {(frontFacing ? "FRONT" : "BACK")} | {exact:0.0000} | {sampled:0.0000} | {cacheF:0.0000} | {(double.IsNaN(spatialErr) ? "   -    " : spatialErr.ToString("+0.0000;-0.0000;0"))} | {angularErr:+0.0000;-0.0000;0}");
            }

            foreach (int hour in hours)
            {
                Assert.InRange(exactLit[hour] / window.GetArea(), 0.0, 1.0);
                Assert.InRange(sampledLit[hour] / window.GetArea(), 0.0, 1.0);
                Assert.InRange(cacheLit[hour] / window.GetArea(), 0.0, 1.0);
            }
        }

        // ---------------------------------------------------------------------------------------
        // FAST — 5. relationship assertions (decomposition invariance).
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Bin_Size_Does_Not_Change_The_Cell_Grid_And_Cell_Size_Does_Not_Change_The_Exact_Reference()
        {
            Face3D window = WindowFace();
            List<LinkedFace3D> occluders = OverhangOccluders();
            Location location = TestHelpers.London();
            const int year = 2018;
            const double cellSize = 0.25;

            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(window, cellSize);

            SolarVisibilityCache bin1 = Weather.SolarCalculator.Create.SolarVisibilityCache(location, year, 1.0, occluders, cells, cellSize, MinHorizonAngle, 0.0);
            SolarVisibilityCache bin5 = Weather.SolarCalculator.Create.SolarVisibilityCache(location, year, 5.0, occluders, cells, cellSize, MinHorizonAngle, 0.0);
            Assert.Equal(cells.Count, bin1.CellCount);
            Assert.Equal(cells.Count, bin5.CellCount);
            Assert.Equal(bin1.TargetGeometryHash, bin5.TargetGeometryHash);

            DateTime start = new DateTime(year, 1, 1);
            List<int> hours = new List<int> { (int)(new DateTime(year, 6, 21, 9, 0, 0) - start).TotalHours };

            Dictionary<int, double> exactA = LitAreaViaSimulateCoverage(window, occluders, location, year, hours, 0.0, double.NaN, out _);
            Dictionary<int, double> exactB = LitAreaViaSimulateCoverage(window, occluders, location, year, hours, 0.0, double.NaN, out _);
            Assert.Equal(exactA[hours[0]], exactB[hours[0]], 12);
        }

        // ---------------------------------------------------------------------------------------
        // FAST — 6. timeline equivalence made explicit.
        // ---------------------------------------------------------------------------------------

        [Fact]
        public void Exact_And_Cache_Sample_The_Sun_At_The_Same_Shifted_Instant()
        {
            Location location = TestHelpers.London();
            const int year = 2018;
            DateTime start = new DateTime(year, 1, 1);
            DateTime probe = new DateTime(year, 6, 21, 9, 0, 0);
            int hourOfYear = (int)(probe - start).TotalHours;

            Vector3D shifted = SunDirectionAt(location, probe, 30.0);
            Geometry.SolarCalculator.Query.TryGetSunAngles(shifted, out double shiftedAltitude, out double shiftedAzimuth);

            Geometry.SolarCalculator.Query.TryGetSunAngles(location, probe.AddMinutes(30), out double expectedAltitude, out double expectedAzimuth);
            Assert.Equal(expectedAltitude, shiftedAltitude, 9);
            Assert.Equal(expectedAzimuth, shiftedAzimuth, 9);

            Face3D window = WindowFace();
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(window, 0.5);
            SolarVisibilityCache cache = Weather.SolarCalculator.Create.SolarVisibilityCache(location, year, 2.0, new List<LinkedFace3D>(), cells, 0.5, MinHorizonAngle, 30.0);
            Assert.Equal(30.0, cache.SunPositionShiftInMinutes);

            int shiftedBin = cache.FindBin(shiftedAltitude, shiftedAzimuth);
            Assert.True(shiftedBin >= 0);
            Assert.Contains(hourOfYear, cache.Bins[shiftedBin].HoursOfYear);

            Geometry.SolarCalculator.Query.TryGetSunAngles(location, probe, out double onTheHourAltitude, out double onTheHourAzimuth);
            int onTheHourBin = cache.FindBin(onTheHourAltitude, onTheHourAzimuth);
            output.WriteLine($"probe {probe:O}: shifted bin {shiftedBin} (sun at +30 min), on-the-hour bin {onTheHourBin}");
        }

        // ---------------------------------------------------------------------------------------
        // LONGRUNNING — 7. synthetic overhang, full annual multi-resolution accuracy matrix.
        // ---------------------------------------------------------------------------------------

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Synthetic_Overhang_Annual_MultiResolution_Accuracy_Matrix()
        {
            Location location = TestHelpers.London();
            const int year = 2018;
            Face3D window = WindowFace();
            Vector3D normal = window.GetPlane().Normal;
            List<LinkedFace3D> occluders = OverhangOccluders();
            double faceArea = window.GetArea();

            List<int> validHours = ValidDaylightHours(location, year, 0.0);
            HashSet<int> frontFacing = FrontFacingHours(location, year, validHours, 0.0, normal);
            Dictionary<int, double> weights = ClearSkyProxyWeights(location, year, 0.0, validHours);

            Dictionary<int, double> exactLit = LitAreaViaSimulateCoverage(window, occluders, location, year, validHours, 0.0, double.NaN, out long exactMs);
            Assert.Equal(validHours.Count, exactLit.Count);
            output.WriteLine($"annual exact polygon baseline: {exactMs} ms over {validHours.Count} daylight hours ({frontFacing.Count} front-facing)");

            // F1 report: hours the exact path reports lit even though the face is back-facing.
            int backFacingFalseLit = validHours.Count(h => exactLit[h] > 1e-9 && !frontFacing.Contains(h));
            output.WriteLine($"F1: exact path reports {backFacingFalseLit} back-facing hours as lit (of {validHours.Count - frontFacing.Count} back-facing hours)");

            output.WriteLine("scenario | cell | bin | spatialMAE(front) | angularMAE | combinedMAE(front) | RMSE | p95 | maxErr | wMAE");

            foreach (double cellSize in new[] { 0.10, 0.25, 0.50 })
            {
                List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(window, cellSize);
                double cellAreaTotal = cells.Sum(x => x.Area);

                Dictionary<int, double> sampledLit = LitAreaViaSimulateCoverage(window, occluders, location, year, validHours, 0.0, cellSize, out long sampledMs);

                ErrorSummary spatial = ComputeError(FilterHours(exactLit, frontFacing), FilterHours(sampledLit, frontFacing), faceArea, weights);

                foreach (double binSize in new[] { 1.0, 2.0, 5.0 })
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    SolarVisibilityCache cache = Weather.SolarCalculator.Create.SolarVisibilityCache(location, year, binSize, occluders, cells, cellSize, MinHorizonAngle, 0.0);
                    sw.Stop();
                    long buildMs = sw.ElapsedMilliseconds;

                    Dictionary<int, double> cacheLit = LitAreaViaCache(cache, cells, location, year, validHours, 0.0, out long evalMs);

                    // Angular MAE is computed inside the cell-size loop on purpose: both sides share
                    // the same grid, but the number of shadow-boundary cells (and so the discretisation
                    // of the direction change) still varies slightly with cell size, so the 2 deg
                    // angular MAE drifts a little between rows (e.g. 0.0154 at 0.50 m vs 0.0155 at
                    // 0.25 m). Each (cell, bin) row is therefore its own measurement, not a repeat.
                    ErrorSummary angular = ComputeError(sampledLit, cacheLit, cellAreaTotal, weights);
                    ErrorSummary combined = ComputeError(FilterHours(exactLit, frontFacing), FilterHours(cacheLit, frontFacing), faceArea, weights);

                    System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
                    output.WriteLine(string.Format(invariant,
                        "synthetic | {0:0.00} | {1:0} | {2:0.0000} | {3:0.0000} | {4:0.0000} | {5:0.0000} | {6:0.0000} | {7:0.0000} | {8:0.0000}  (bins={9} cells={10} sampledMs={11} buildMs={12} evalMs={13})",
                        cellSize, binSize, spatial.Mae, angular.Mae, combined.Mae, combined.Rmse, combined.P95Abs, combined.MaxAbs, combined.WeightedMae, cache.BinCount, cells.Count, sampledMs, buildMs, evalMs));

                    // Regression ceilings (not physical gates). Each MAE is a normalised coverage
                    // fraction in [0, 1] by construction, so a plain [0, 1] range assertion would be
                    // tautological and let a cache that ignored geometry pass. These ceilings sit well
                    // above the measured values on this benchmark (spatial 0.0073-0.0458, angular
                    // 0.0123-0.0293, combined 0.0192-0.0620 across the grids/bins) but well below a
                    // broken engine (~0.4-1.0), so they fail only on a genuine regression.
                    Assert.True(spatial.Mae < 0.10, $"spatial MAE {spatial.Mae:0.0000} exceeds the 0.10 regression ceiling");
                    Assert.True(angular.Mae < 0.05, $"angular MAE {angular.Mae:0.0000} exceeds the 0.05 regression ceiling");
                    Assert.True(combined.Mae < 0.15, $"combined MAE {combined.Mae:0.0000} exceeds the 0.15 regression ceiling");

                    // The released 2 deg angular gate (comparison B only) must hold.
                    if (Math.Abs(binSize - 2.0) < 1e-9)
                    {
                        Assert.True(angular.Mae < 0.02, $"2 deg angular MAE {angular.Mae:0.0000} exceeds the existing 2% gate");
                    }
                }

                // Empirical benchmark invariant, not a general theorem: on this synthetic geometry the
                // sampled path (which applies the front-facing test) never over-lights the exact
                // reference (which, per F1, additionally reports back-facing hours as lit), so the
                // sampled area-hours never exceed the exact area-hours beyond floating-point noise.
                // It characterises this benchmark only and is not a constraint on any production solver.
                Assert.True(sampledLit.Values.Sum() <= exactLit.Values.Sum() * (1.0 + 1e-9));
            }
        }

        // ---------------------------------------------------------------------------------------
        // LONGRUNNING — 8. real Kołobrzeg aperture study.
        // ---------------------------------------------------------------------------------------

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Kolobrzeg_Real_Model_Study()
        {
            AnalyticalModel model = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(model);
            Guid apertureGuid = KolobrzegFixture.TallApertureGuid;
            const double shiftMinutes = 30.0; // IntervalStart (SAM/EPW weather) — the released workflow default.
            const double binSize = 2.0;

            // Reuse the production aperture/context pipeline: single-aperture selection so the shared
            // cell space is exactly this aperture's cells, and the cache is the production one.
            ApertureSolarContext context = Analytical.SolarCalculator.Create.ApertureSolarContext(
                model, year, null, new List<Guid> { apertureGuid }, KolobrzegFixture.HistoricalGridSize, binSize, true, shiftMinutes);

            Assert.NotNull(context);
            ApertureSolarTarget target = context.Target(apertureGuid);
            Assert.NotNull(target);

            Location location = context.Location;
            List<LinkedFace3D> occluders = context.ContextOccluders;
            List<AnalysisCell> cells = target.AnalysisCells;
            Face3D targetFace = target.Face3D;
            Vector3D normal = targetFace.GetPlane().Normal;
            double faceArea = targetFace.GetArea();
            double cellAreaTotal = cells.Sum(x => x.Area);
            SolarVisibilityCache cache = context.SolarVisibilityCache;

            List<int> validHours = ValidDaylightHours(location, year, shiftMinutes);

            // The exact/sampled paths re-run the per-hour projection machinery and are ~two orders of
            // magnitude slower than the cache on a real building context (measured ~70 ms/hour sampled,
            // ~140 ms/hour exact). The real-model comparison therefore runs on a deterministic stratified
            // subset of the annual daylight timeline (every 24th daylight-list entry).
            List<int> stratifiedHours = validHours.Where((h, i) => i % 24 == 0).ToList();

            DateTime yearStart = new DateTime(year, 1, 1);
            List<int> representativeHours = new List<int>();
            foreach (int clock in new[] { 5, 8, 11, 14, 17, 20 })
            {
                representativeHours.Add((int)(new DateTime(year, 6, 21, clock, 0, 0) - yearStart).TotalHours);
            }

            // CHARACTERIZATION TEST for known defect F2: the panel-oriented exact path merges the
            // aperture into its coplanar host facade and reports ~0 coverage all day (it is not an
            // aperture-level reference for real models). This pins EXISTING behaviour, not desired
            // semantics, and the assertion below must be updated/removed when the exact engine is fixed.
            Dictionary<int, double> exactLit = LitAreaViaSimulateCoverage(targetFace, occluders, location, year, representativeHours, shiftMinutes, double.NaN, out long exactMs);

            // The sampled path (sampleSize = grid) ALSO goes through the panel-oriented merge, so it too
            // merges the aperture into the coplanar facade. On real aperture-in-facade geometry this makes
            // it a different geometry set from the cache (which treats the aperture cells as a distinct
            // target raycast against the cut facade). The sampled-vs-cache gap below is therefore a
            // GEOMETRY-SET difference, not a clean angular-binning measure — the clean angular measure is
            // the synthetic overhang (target/context non-degenerate), reported in the matrix test.
            Dictionary<int, double> sampledLit = LitAreaViaSimulateCoverage(targetFace, occluders, location, year, stratifiedHours, shiftMinutes, KolobrzegFixture.HistoricalGridSize, out long sampledMs);
            Dictionary<int, double> cacheLit = LitAreaViaCache(cache, cells, location, year, stratifiedHours, shiftMinutes, out long evalMs);
            Dictionary<int, double> cacheRepresentative = LitAreaViaCache(cache, cells, location, year, representativeHours, shiftMinutes, out _);

            ErrorSummary panelVsApertureGap = ComputeError(sampledLit, cacheLit, cellAreaTotal, null);

            System.Globalization.CultureInfo invariant = System.Globalization.CultureInfo.InvariantCulture;
            output.WriteLine($"Kolobrzeg aperture {apertureGuid}: faceArea={faceArea:0.####} m2, cellAreaTotal={cellAreaTotal:0.####} m2 (sampledAreaFraction={cellAreaTotal / faceArea:0.####}), cells={cells.Count}, occluders={occluders.Count}, daylightHours={validHours.Count}, stratifiedHours={stratifiedHours.Count}");
            output.WriteLine(string.Format(invariant,
                "Kolobrzeg | cell {0:0.00} | bin {1:0} | sampled-vs-cache MAE={2:0.0000} RMSE={3:0.0000} p95={4:0.0000} maxErr={5:0.0000} (panel-oriented sampled merges the aperture into the coplanar facade -> geometry-set gap, NOT a clean angular measure)",
                KolobrzegFixture.HistoricalGridSize, binSize, panelVsApertureGap.Mae, panelVsApertureGap.Rmse, panelVsApertureGap.P95Abs, panelVsApertureGap.MaxAbs));

            output.WriteLine("F2 exact path (representative summer day) vs cache:");
            foreach (int hour in representativeHours)
            {
                if (!exactLit.TryGetValue(hour, out double exactArea))
                {
                    continue; // below the horizon gate: not part of either series
                }

                DateTime dateTime = yearStart.AddHours(hour);
                bool front = normal.DotProduct(SunDirectionAt(location, dateTime, shiftMinutes)) < 0;
                double exactCov = exactArea / faceArea;
                double cacheCov = cacheRepresentative[hour] / faceArea;
                output.WriteLine($"   {dateTime:MM-dd HH:mm} {(front ? "FRONT" : "BACK")} exact={exactCov:0.0000} cache={cacheCov:0.0000}");
            }

            output.WriteLine($"   perf: exact({representativeHours.Count}h)={exactMs} ms sampled({stratifiedHours.Count}h)={sampledMs} ms cacheEval({stratifiedHours.Count}h)={evalMs} ms (bins={cache.BinCount}, cacheBuild is part of the context above)");

            Assert.Equal(stratifiedHours.Count, sampledLit.Count);
            Assert.Equal(stratifiedHours.Count, cacheLit.Count);
            Assert.InRange(panelVsApertureGap.Mae, 0.0, 1.0);

            // F2 characterization assertion: the exact polygon path reports the aperture as ~fully
            // shaded even on front-facing afternoon hours, because the coplanar host facade absorbs
            // the target. Pins EXISTING behaviour; update/remove with the future exact-engine fix.
            foreach (int hour in representativeHours)
            {
                if (!exactLit.TryGetValue(hour, out double exactArea))
                {
                    continue;
                }

                Assert.True(exactArea < faceArea * 0.5, $"expected exact path to under-report the aperture at hour {hour} (coplanar facade), got {exactArea / faceArea:0.0000}");
            }
        }
    }
}
