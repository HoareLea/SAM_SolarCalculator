// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using SAM.Core;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.Spatial;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Macro tests over two real exported AnalyticalModels:
    ///   ModelA.sam                 = SAMAnalytical.FromTBD with _importSurfaceShades_ = true  (TAS shade surfaces)
    ///   ModelB-SolarSimulation.sam = SAMAnalytical.FromTBD (shades=false) -> SAMAnalytical.SolarSimulation
    /// Fixtures are stored in SAM's native compressed .sam (zip) format and loaded via Convert.ToSAM.
    /// They reproduce the surface-set mismatch that motivated the _useModelSolarModel_ toggle on
    /// SAMAnalytical.SolarSimulation, and prove the toggle makes SAM evaluate the SAME surfaces as
    /// the TAS import (a 1:1 benchmark set) instead of the AdjacencyCluster-filtered panel set.
    /// </summary>
    public class CompareSolarCoverageTests
    {
        // Surface counts baked into the committed fixtures (== SAMAnalytical.CompareSolarCoverage's
        // "panels total" diagnostic for these two models).
        private const int ExpectedSurfaces_A = 36;
        private const int ExpectedSurfaces_B = 8;

        private const double GeometryTolerance = 1e-6;

        private static readonly List<DateTime> SampleDateTimes = new List<DateTime>
        {
            // A few daylight hours on the summer solstice — enough to exercise the full
            // sun-visibility pipeline without the cost of a whole-year run.
            new DateTime(2018, 6, 21, 9, 0, 0),
            new DateTime(2018, 6, 21, 12, 0, 0),
            new DateTime(2018, 6, 21, 15, 0, 0),
        };

        // Test fixtures live under SAM_SolarCalculator.Tests/Fixtures/ and are copied (preserving that
        // subfolder) next to the test assembly via the csproj's Fixtures\*.sam copy rule.
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        private static AnalyticalModel Load(string fileName)
        {
            string path = Path.Combine(FixturesDirectory, fileName);
            Assert.True(File.Exists(path), $"Test fixture missing: {path}");

            List<AnalyticalModel> analyticalModels = SAM.Core.Convert.ToSAM<AnalyticalModel>(path);
            Assert.NotNull(analyticalModels);

            AnalyticalModel analyticalModel = analyticalModels.FirstOrDefault(x => x != null);
            Assert.NotNull(analyticalModel);
            return analyticalModel;
        }

        private static SolarModel GetSolarModel(AnalyticalModel analyticalModel)
        {
            return analyticalModel.GetValue<SolarModel>(AnalyticalModelParameter.SolarModel);
        }

        private static List<Point3D> InternalPoints(SolarModel solarModel)
        {
            return solarModel.GetLinkedFace3Ds()
                .Where(x => x?.Face3D != null)
                .Select(x => x.Face3D.InternalPoint3D())
                .Where(x => x != null)
                .ToList();
        }

        [Fact]
        public void Fixtures_have_mismatched_surface_sets()
        {
            SolarModel solarModel_A = GetSolarModel(Load("ModelA.sam"));
            SolarModel solarModel_B = GetSolarModel(Load("ModelB-SolarSimulation.sam"));

            Assert.NotNull(solarModel_A);
            Assert.NotNull(solarModel_B);

            Assert.Equal(ExpectedSurfaces_A, solarModel_A.GetLinkedFace3Ds().Count);
            Assert.Equal(ExpectedSurfaces_B, solarModel_B.GetLinkedFace3Ds().Count);

            // The problem in one assertion: the default SolarSimulation path covers far fewer
            // surfaces than the TAS import, so comparing the two directly is not 1:1.
            Assert.True(
                solarModel_B.GetLinkedFace3Ds().Count < solarModel_A.GetLinkedFace3Ds().Count,
                "Expected the SolarSimulation model to carry fewer surfaces than the TAS import.");
        }

        [Fact]
        public void UseModelSolarModel_recomputes_coverage_on_the_same_surfaces()
        {
            AnalyticalModel analyticalModel = Load("ModelA.sam");

            SolarModel solarModel_TAS = GetSolarModel(analyticalModel);
            Assert.NotNull(solarModel_TAS);

            List<Point3D> points_TAS = InternalPoints(solarModel_TAS);
            int surfaceCount = solarModel_TAS.GetLinkedFace3Ds().Count;
            Assert.Equal(ExpectedSurfaces_A, surfaceCount);

            List<SolarCoverageSimulationResult> results =
                analyticalModel.Simulate_Coverage(SampleDateTimes, SAM.Core.Tolerance.Angle, SAM.Core.Tolerance.MacroDistance, SAM.Core.Tolerance.MacroDistance, SAM.Core.Tolerance.Angle, SAM.Core.Tolerance.Distance, double.NaN, true);

            // SAM coverage now exists for the SAME surface set as the TAS import (1:1), not the
            // 8-panel AdjacencyCluster-filtered set.
            Assert.NotNull(results);
            Assert.Equal(surfaceCount, results.Count);

            SolarModel solarModel_SAM = GetSolarModel(analyticalModel);
            Assert.NotNull(solarModel_SAM);
            Assert.Equal(surfaceCount, solarModel_SAM.GetLinkedFace3Ds().Count);

            // Every TAS face has a geometrically-coincident SAM face (geometry preserved exactly).
            List<Point3D> points_SAM = InternalPoints(solarModel_SAM);
            int aligned = points_TAS.Count(point_TAS =>
                points_SAM.Any(point_SAM => point_SAM.Distance(point_TAS) <= GeometryTolerance));

            Assert.Equal(points_TAS.Count, aligned);
        }

        [Fact]
        public void ClassifyPanels_accounts_for_every_Model_A_surface()
        {
            AnalyticalModel analyticalModel_B = Load("ModelB-SolarSimulation.sam");

            // Classify Model B's analytical panels by the same rule the SolarModel builder uses.
            List<PanelSolarClassification> classifications = analyticalModel_B.ClassifyPanelsForSolarModel();
            Assert.NotNull(classifications);
            Assert.NotEmpty(classifications);

            // Map each of Model A's surfaces to the nearest Model B panel and tally the verdict.
            SolarModel solarModel_A = GetSolarModel(Load("ModelA.sam"));
            Assert.NotNull(solarModel_A);

            int surfaces = 0, kept = 0, dropped = 0, noPanel = 0;
            foreach (LinkedFace3D face_A in solarModel_A.GetLinkedFace3Ds())
            {
                Point3D point_A = face_A?.Face3D?.InternalPoint3D();
                if (point_A == null) continue;
                surfaces++;

                PanelSolarClassification nearest = null;
                double best = double.MaxValue;
                foreach (PanelSolarClassification c in classifications)
                {
                    if (c.InternalPoint3D == null) continue;
                    double distance = point_A.Distance(c.InternalPoint3D);
                    if (distance <= 0.5 && distance < best) { best = distance; nearest = c; }
                }

                if (nearest == null) noPanel++;
                else if (nearest.Kept) kept++;
                else dropped++;
            }

            // Accounting integrity: every Model A surface is explained — kept, dropped, or absent —
            // and the gap (surfaces SAM omits) is non-empty, i.e. the diagnostic explains the mismatch.
            Assert.Equal(ExpectedSurfaces_A, surfaces);
            Assert.Equal(surfaces, kept + dropped + noPanel);
            Assert.True(dropped + noPanel > 0, "diagnostic should explain why some Model A surfaces are not in SAM's SolarModel");
        }

        // 14 window apertures across the 8 exposed panels of the committed fixture.
        private const int ExpectedApertures = 14;

        [Fact]
        public void ToSAM_SolarModel_includeApertures_matches_TAS_surface_set_one_to_one()
        {
            AnalyticalModel analyticalModel = Load("ModelB-SolarSimulation.sam");

            // includeApertures = true (the coverage path): 8 opaque exposed panels + per window an opening
            // AND an inset glazing pane (14 each), mirroring the TAS import: 8 + 14 + 14 = 36.
            SolarModel withApertures = analyticalModel.ToSAM_SolarModel(true);
            Assert.NotNull(withApertures);
            Assert.Equal(ExpectedSurfaces_B + 2 * ExpectedApertures, withApertures.GetLinkedFace3Ds().Count);
            Assert.Equal(ExpectedSurfaces_A, withApertures.GetLinkedFace3Ds().Count);

            // Default (panels only) — the regular face-simulation path is unaffected by the aperture change.
            SolarModel panelsOnly = analyticalModel.ToSAM_SolarModel();
            Assert.NotNull(panelsOnly);
            Assert.Equal(ExpectedSurfaces_B, panelsOnly.GetLinkedFace3Ds().Count);
        }

        [Fact]
        public void Default_path_now_covers_the_full_TAS_equivalent_surface_set()
        {
            AnalyticalModel analyticalModel = Load("ModelA.sam");

            List<SolarCoverageSimulationResult> results =
                analyticalModel.Simulate_Coverage(SampleDateTimes);

            // With apertures included, the standalone (no-toggle) path now produces a coverage result
            // for every one of the 36 surfaces — opaque panels, window openings and glazing panes —
            // matching the TAS import 1:1.
            Assert.NotNull(results);
            Assert.Equal(ExpectedSurfaces_A, results.Count);
        }

        [Fact]
        public void Simulate_Coverage_attaches_pane_and_frame_results_to_apertures()
        {
            // ModelB-SolarSimulation.sam is a SAM (non-TAS-import) model whose apertures are NOT
            // registered as top-level cluster objects, so it exercises the robust register-then-relate
            // overload — not merely apertures that happened to be pre-registered by a TAS import.
            AnalyticalModel analyticalModel = Load("ModelB-SolarSimulation.sam");
            Assert.Equal(ExpectedApertures, analyticalModel.GetApertures().Count);

            List<SolarCoverageSimulationResult> results = analyticalModel.Simulate_Coverage(SampleDateTimes);
            Assert.NotNull(results);
            Assert.Equal(ExpectedSurfaces_A, results.Count); // 8 panels + 14 openings + 14 panes

            // Every aperture now carries a "… -pane" AND a "… -frame" coverage result, related back to
            // the Aperture object (previously these fell through to Guid.Empty and were orphaned). The
            // "-pane"/"-frame" names match the TAS-import convention that UpdateShading reads.
            int paneApertures = 0;
            int frameApertures = 0;
            foreach (Aperture aperture in analyticalModel.GetApertures())
            {
                List<SolarCoverageSimulationResult> apertureResults = analyticalModel.GetResults<SolarCoverageSimulationResult>(aperture);
                Assert.NotNull(apertureResults);

                if (apertureResults.Any(x => x?.Name != null && x.Name.EndsWith("-pane"))) paneApertures++;
                if (apertureResults.Any(x => x?.Name != null && x.Name.EndsWith("-frame"))) frameApertures++;
            }

            Assert.Equal(ExpectedApertures, paneApertures);
            Assert.Equal(ExpectedApertures, frameApertures);
        }

        // ---- With-Shade engine benchmark (matched shading on both sides) -------------------------------
        // ModelA-WithShade.sam            = FromTBD(_importSurfaceShades_=true) -> TAS, 36 surfaces, source TAS, at origin.
        // ModelB-WithShadeSolarSimulation = FromTBD(false) -> SolarSimulation -> SAM, 64 surfaces (8 + 28 windows
        //                                   + 28 Shade occluders), drawn ~+25 m away in X.
        // Locks in the live benchmark: after aligning B onto A by the non-Shade bounding box, all 36 TAS
        // surfaces match a SAM surface 1:1, the 28 Shade occluders are unmatched (no TAS counterpart), and
        // SAM agrees with TAS to well within tolerance (live overallMeanAbsDelta was 0.0088).

        private struct HourKey : IEquatable<HourKey>
        {
            public readonly byte Month, Day, Hour;
            public HourKey(DateTime dt)
            {
                DateTime c = (dt.Minute == 0 && dt.Second == 0 && dt.Millisecond == 0) ? dt : new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0).AddHours(1);
                Month = (byte)c.Month; Day = (byte)c.Day; Hour = (byte)c.Hour;
            }
            public bool Equals(HourKey o) => Month == o.Month && Day == o.Day && Hour == o.Hour;
            public override bool Equals(object obj) => obj is HourKey o && Equals(o);
            public override int GetHashCode() => (Month << 16) | (Day << 8) | Hour;
        }

        private static Dictionary<HourKey, double> HourMap(SolarCoverageSimulationResult r)
        {
            Dictionary<HourKey, double> m = new Dictionary<HourKey, double>();
            if (r?.Coverage == null) return m;
            foreach (Tuple<DateTime, double> t in r.Coverage)
            {
                if (t == null || double.IsNaN(t.Item2)) continue;
                HourKey k = new HourKey(t.Item1);
                if (!m.ContainsKey(k)) m[k] = t.Item2;
            }
            return m;
        }

        // (internalPoint, coverage result, isShade) for every surface that carries a coverage result.
        private static List<Tuple<Point3D, SolarCoverageSimulationResult, bool>> ResultPairs(SolarModel solarModel, HashSet<Guid> shadeGuids)
        {
            Dictionary<string, SolarCoverageSimulationResult> byReference = new Dictionary<string, SolarCoverageSimulationResult>();
            foreach (SolarCoverageSimulationResult r in solarModel.SolarCoverageSimulationResults ?? new List<SolarCoverageSimulationResult>())
            {
                if (r?.Reference != null) byReference[r.Reference] = r;
            }

            List<Tuple<Point3D, SolarCoverageSimulationResult, bool>> result = new List<Tuple<Point3D, SolarCoverageSimulationResult, bool>>();
            foreach (LinkedFace3D lf in solarModel.GetLinkedFace3Ds() ?? new List<LinkedFace3D>())
            {
                if (lf?.Face3D == null) continue;
                if (!byReference.TryGetValue(lf.Guid.ToString(), out SolarCoverageSimulationResult r)) continue;
                Point3D p = lf.Face3D.InternalPoint3D();
                if (p == null) continue;
                result.Add(Tuple.Create(p, r, shadeGuids.Contains(lf.Guid)));
            }
            return result;
        }

        [Fact]
        public void WithShade_SAM_matches_TAS_within_tolerance()
        {
            AnalyticalModel a = Load("ModelA-WithShade.sam");
            AnalyticalModel b = Load("ModelB-WithShadeSolarSimulation.sam");

            SolarModel smA = GetSolarModel(a);
            SolarModel smB = GetSolarModel(b);
            Assert.NotNull(smA);
            Assert.NotNull(smB);
            Assert.Equal(36, smA.GetLinkedFace3Ds().Count);
            Assert.Equal(64, smB.GetLinkedFace3Ds().Count);

            // 28 of B's surfaces are Shade occluders (TAS has none).
            HashSet<Guid> shadeGuids = new HashSet<Guid>(
                b.ClassifyPanelsForSolarModel().Where(c => c.PanelType == PanelType.Shade).Select(c => c.Guid));
            List<Tuple<Point3D, SolarCoverageSimulationResult, bool>> pairsA = ResultPairs(smA, new HashSet<Guid>());
            List<Tuple<Point3D, SolarCoverageSimulationResult, bool>> pairsB = ResultPairs(smB, shadeGuids);
            Assert.Equal(36, pairsA.Count);
            Assert.Equal(64, pairsB.Count);
            Assert.Equal(28, pairsB.Count(x => x.Item3));

            // Align B onto A by the min corner of the NON-Shade points (the models are a pure translation
            // apart: TAS at origin, SAM drawn ~+25 m). Shade points are excluded so the offset comes from
            // the common surfaces, not shade overhang.
            double[] bbA = BBoxMin(pairsA.Where(x => !x.Item3).Select(x => x.Item1));
            double[] bbB = BBoxMin(pairsB.Where(x => !x.Item3).Select(x => x.Item1));
            double dx = bbA[0] - bbB[0], dy = bbA[1] - bbB[1], dz = bbA[2] - bbB[2];
            Assert.True(Math.Abs(dx) > 1.0, "expected a real X offset between TAS and SAM models");

            // Greedy nearest-neighbour 1:1 match (A -> aligned B) within 0.5 m, then per-hour mean abs delta.
            const double tolerance = 0.5;
            HashSet<int> usedB = new HashSet<int>();
            int matched = 0;
            double sumAbs = 0; int overlapAll = 0;
            foreach (Tuple<Point3D, SolarCoverageSimulationResult, bool> pa in pairsA)
            {
                int best = -1; double bestDist = double.MaxValue;
                for (int j = 0; j < pairsB.Count; j++)
                {
                    if (usedB.Contains(j)) continue;
                    Point3D pb = pairsB[j].Item1;
                    double d = pa.Item1.Distance(new Point3D(pb.X + dx, pb.Y + dy, pb.Z + dz));
                    if (d <= tolerance && d < bestDist) { bestDist = d; best = j; }
                }
                if (best == -1) continue;
                usedB.Add(best);
                matched++;

                Dictionary<HourKey, double> mapA = HourMap(pa.Item2);
                Dictionary<HourKey, double> mapB = HourMap(pairsB[best].Item2);
                foreach (KeyValuePair<HourKey, double> e in mapA)
                {
                    if (!mapB.TryGetValue(e.Key, out double vB)) continue;
                    sumAbs += Math.Abs(vB - e.Value);
                    overlapAll++;
                }
            }

            // Every TAS surface matches a SAM surface 1:1; the 28 Shade occluders stay unmatched.
            Assert.Equal(36, matched);
            Assert.Equal(28, pairsB.Count - usedB.Count);

            double overallMeanAbsDelta = overlapAll == 0 ? double.NaN : sumAbs / overlapAll;
            output.WriteLine($"SAM-vs-TAS with-shade benchmark: matched={matched} surfaces, overlapping hours={overlapAll}, overallMeanAbsDelta={overallMeanAbsDelta:0.00000} (gate < 0.02)");
            // SAM reproduces TAS shading to well within tolerance (live run: 0.0088). Guard generously.
            Assert.True(overlapAll > 0, "expected overlapping hours between matched pairs");
            Assert.True(overallMeanAbsDelta < 0.02, $"SAM-vs-TAS overallMeanAbsDelta unexpectedly high: {overallMeanAbsDelta:0.0000}");
        }

        private static double[] BBoxMin(IEnumerable<Point3D> points)
        {
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            foreach (Point3D p in points)
            {
                if (p == null) continue;
                if (p.X < minX) minX = p.X; if (p.Y < minY) minY = p.Y; if (p.Z < minZ) minZ = p.Z;
            }
            return new double[] { minX, minY, minZ };
        }

        // ---- Option-2 (via-TBD) round-trip regression ---------------------------------------------------
        // ModelB-SAMToTasFromTas.sam = ModelB SolarSimulation -> SAMAnalytical.TBD (ToTBD) -> SAMAnalytical.FromTBD,
        // compared against the TAS benchmark ModelA-Tas. This path used to read back one hour EARLY (the
        // entire ~0.106 CompareSolarCoverage gap); the SAM_Tas shade-slot fix (write hour H at slot H, not
        // H-1) brought it to ~0.016, matching the direct path. The test breaks the per-pair delta down by
        // surface area-class (wall > 5 / pane 0.5..5 / ring < 0.5) and asserts the round-trip stays healthy
        // (< 0.02). No-op only if the fixture is absent.

        private readonly ITestOutputHelper output;

        public CompareSolarCoverageTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static string AreaClass(double area)
        {
            if (area < 0.5) return "ring";
            if (area <= 5.0) return "pane";
            return "wall";
        }

        // (internalPoint, area, coverage result) for every coverage-bearing surface in the SolarModel.
        private static List<Tuple<Point3D, double, SolarCoverageSimulationResult>> AreaPairs(SolarModel solarModel)
        {
            Dictionary<string, SolarCoverageSimulationResult> byReference = new Dictionary<string, SolarCoverageSimulationResult>();
            foreach (SolarCoverageSimulationResult r in solarModel.SolarCoverageSimulationResults ?? new List<SolarCoverageSimulationResult>())
            {
                if (r?.Reference != null) byReference[r.Reference] = r;
            }

            List<Tuple<Point3D, double, SolarCoverageSimulationResult>> result = new List<Tuple<Point3D, double, SolarCoverageSimulationResult>>();
            foreach (LinkedFace3D lf in solarModel.GetLinkedFace3Ds() ?? new List<LinkedFace3D>())
            {
                if (lf?.Face3D == null) continue;
                if (!byReference.TryGetValue(lf.Guid.ToString(), out SolarCoverageSimulationResult r)) continue;
                Point3D p = lf.Face3D.InternalPoint3D();
                if (p == null) continue;
                result.Add(Tuple.Create(p, lf.Face3D.GetArea(), r));
            }
            return result;
        }

        [Fact]
        public void ViaTBD_roundtrip_delta_breakdown_by_surface_class()
        {
            string pathB = Path.Combine(FixturesDirectory, "ModelB-SAMToTasFromTas.sam");
            if (!File.Exists(pathB))
            {
                output.WriteLine("SKIP: ModelB-SAMToTasFromTas.sam not present yet — export the option-2 FromTBD output into the test folder.");
                return;
            }

            SolarModel smA = GetSolarModel(Load("ModelA-Tas.sam"));
            SolarModel smB = GetSolarModel(Load("ModelB-SAMToTasFromTas.sam"));
            Assert.NotNull(smA);
            Assert.NotNull(smB);

            List<Tuple<Point3D, double, SolarCoverageSimulationResult>> pairsA = AreaPairs(smA);
            List<Tuple<Point3D, double, SolarCoverageSimulationResult>> pairsB = AreaPairs(smB);
            output.WriteLine($"A surfaces={pairsA.Count}  B surfaces={pairsB.Count}");

            // Align B onto A by the min corner (pure translation, as the node does).
            double[] bbA = BBoxMin(pairsA.Select(x => x.Item1));
            double[] bbB = BBoxMin(pairsB.Select(x => x.Item1));
            double dx = bbA[0] - bbB[0], dy = bbA[1] - bbB[1], dz = bbA[2] - bbB[2];

            const double tolerance = 0.5;
            HashSet<int> usedB = new HashSet<int>();

            // Per area-class accumulators: sum of |delta| and overlapping-hour count.
            Dictionary<string, double> sumAbsByClass = new Dictionary<string, double> { ["wall"] = 0, ["pane"] = 0, ["ring"] = 0 };
            Dictionary<string, int> hoursByClass = new Dictionary<string, int> { ["wall"] = 0, ["pane"] = 0, ["ring"] = 0 };
            Dictionary<string, int> pairsByClass = new Dictionary<string, int> { ["wall"] = 0, ["pane"] = 0, ["ring"] = 0 };

            double sumAbsAll = 0; int hoursAll = 0; int matched = 0;

            foreach (Tuple<Point3D, double, SolarCoverageSimulationResult> pa in pairsA)
            {
                int best = -1; double bestDist = double.MaxValue;
                for (int j = 0; j < pairsB.Count; j++)
                {
                    if (usedB.Contains(j)) continue;
                    Point3D pb = pairsB[j].Item1;
                    double d = pa.Item1.Distance(new Point3D(pb.X + dx, pb.Y + dy, pb.Z + dz));
                    if (d <= tolerance && d < bestDist) { bestDist = d; best = j; }
                }
                if (best == -1) continue;
                usedB.Add(best);
                matched++;

                // Classify by the larger of the two areas so a ring paired to a ring counts as "ring".
                string cls = AreaClass(Math.Max(pa.Item2, pairsB[best].Item2));

                Dictionary<HourKey, double> mapA = HourMap(pa.Item3);
                Dictionary<HourKey, double> mapB = HourMap(pairsB[best].Item3);
                double pairSumAbs = 0; int pairHours = 0;
                foreach (KeyValuePair<HourKey, double> e in mapA)
                {
                    if (!mapB.TryGetValue(e.Key, out double vB)) continue;
                    double ad = Math.Abs(vB - e.Value);
                    pairSumAbs += ad; pairHours++;
                }

                sumAbsByClass[cls] += pairSumAbs; hoursByClass[cls] += pairHours; pairsByClass[cls]++;
                sumAbsAll += pairSumAbs; hoursAll += pairHours;

                double pairMean = pairHours == 0 ? double.NaN : pairSumAbs / pairHours;
                output.WriteLine($"  [{cls}] areaA={pa.Item2:0.###} areaB={pairsB[best].Item2:0.###} centroid={pa.Item1} hours={pairHours} meanAbs={pairMean:0.0000}");
            }

            output.WriteLine("--- Per area-class ---");
            foreach (string cls in new[] { "wall", "pane", "ring" })
            {
                double mean = hoursByClass[cls] == 0 ? double.NaN : sumAbsByClass[cls] / hoursByClass[cls];
                output.WriteLine($"  {cls}: pairs={pairsByClass[cls]} hours={hoursByClass[cls]} meanAbsDelta={mean:0.0000}");
            }
            double overall = hoursAll == 0 ? double.NaN : sumAbsAll / hoursAll;
            output.WriteLine($"overall meanAbsDelta={overall:0.0000}  matched={matched}");

            Assert.Equal(36, matched);
            // After the SAM_Tas shade-slot fix (write hour H at slot H, not H-1) the via-TBD round-trip
            // matches the TAS benchmark to ~0.016 — the same quality as the direct path. Guard against a
            // regression of the 1-hour offset, which drove this to ~0.106.
            Assert.True(overall < 0.02, $"via-TBD round-trip regressed: overall meanAbsDelta {overall:0.0000} (expected < 0.02 after the hour-slot fix)");
        }

        // Linear hour index with the year forced to a common non-leap value, so A and B align on
        // (month, day, hour) regardless of their stored years. Shiftable by whole hours.
        private static Dictionary<int, double> LinearHourMap(SolarCoverageSimulationResult r)
        {
            Dictionary<int, double> result = new Dictionary<int, double>();
            if (r?.Coverage == null) return result;
            foreach (Tuple<DateTime, double> t in r.Coverage)
            {
                if (t == null || double.IsNaN(t.Item2)) continue;
                DateTime dt = t.Item1;
                int doy = new DateTime(2001, dt.Month, dt.Day).DayOfYear;
                int key = doy * 24 + dt.Hour;
                if (!result.ContainsKey(key)) result[key] = t.Item2;
            }
            return result;
        }

        // This probe localised the original ~0.106 gap: before the SAM_Tas fix the delta minimised at a
        // B shift of -1 hour (B[h] == A[h+1]) — the via-TBD coverage landed one hour EARLY vs TAS, the
        // entire gap (not geometry/rings/storage). Root cause: WriteImportedCoverageShades wrote hour H to
        // slot H-1, which reads back at H-1 through FromTBD's read-write open. Fixed by writing at slot H;
        // the delta now minimises at shift 0 (asserted below).
        [Fact]
        public void ViaTBD_hour_shift_probe()
        {
            string pathB = Path.Combine(FixturesDirectory, "ModelB-SAMToTasFromTas.sam");
            if (!File.Exists(pathB))
            {
                output.WriteLine("SKIP: ModelB-SAMToTasFromTas.sam not present yet.");
                return;
            }

            SolarModel smA = GetSolarModel(Load("ModelA-Tas.sam"));
            SolarModel smB = GetSolarModel(Load("ModelB-SAMToTasFromTas.sam"));
            Assert.NotNull(smA);
            Assert.NotNull(smB);

            List<Tuple<Point3D, double, SolarCoverageSimulationResult>> pairsA = AreaPairs(smA);
            List<Tuple<Point3D, double, SolarCoverageSimulationResult>> pairsB = AreaPairs(smB);

            double[] bbA = BBoxMin(pairsA.Select(x => x.Item1));
            double[] bbB = BBoxMin(pairsB.Select(x => x.Item1));
            double dx = bbA[0] - bbB[0], dy = bbA[1] - bbB[1], dz = bbA[2] - bbB[2];

            // Match A->B once, then score the whole set under several whole-hour shifts of B.
            const double tol = 0.5;
            HashSet<int> usedB = new HashSet<int>();
            List<Tuple<Dictionary<int, double>, Dictionary<int, double>>> matchedMaps = new List<Tuple<Dictionary<int, double>, Dictionary<int, double>>>();
            foreach (Tuple<Point3D, double, SolarCoverageSimulationResult> pa in pairsA)
            {
                int best = -1; double bestDist = double.MaxValue;
                for (int j = 0; j < pairsB.Count; j++)
                {
                    if (usedB.Contains(j)) continue;
                    Point3D pb = pairsB[j].Item1;
                    double d = pa.Item1.Distance(new Point3D(pb.X + dx, pb.Y + dy, pb.Z + dz));
                    if (d <= tol && d < bestDist) { bestDist = d; best = j; }
                }
                if (best == -1) continue;
                usedB.Add(best);
                matchedMaps.Add(Tuple.Create(LinearHourMap(pa.Item3), LinearHourMap(pairsB[best].Item3)));
            }

            output.WriteLine($"matched pairs={matchedMaps.Count}");
            output.WriteLine("shift(h) | overlap | overallMeanAbsDelta");
            Dictionary<int, double> deltaByShift = new Dictionary<int, double>();
            for (int shift = -3; shift <= 3; shift++)
            {
                double sumAbs = 0; int hours = 0;
                foreach (var mm in matchedMaps)
                {
                    foreach (KeyValuePair<int, double> e in mm.Item1)
                    {
                        if (!mm.Item2.TryGetValue(e.Key + shift, out double vB)) continue;
                        sumAbs += Math.Abs(vB - e.Value); hours++;
                    }
                }
                double mean = hours == 0 ? double.NaN : sumAbs / hours;
                deltaByShift[shift] = mean;
                output.WriteLine($"  {shift,2}     | {hours,6} | {mean:0.0000}");
            }

            // Raw series for the first matched surface around midday on day-of-year 181 — shows whether
            // B[h] == A[h-1] (a clean integer index shift) or a smear.
            if (matchedMaps.Count > 0)
            {
                Dictionary<int, double> a = matchedMaps[0].Item1;
                Dictionary<int, double> b = matchedMaps[0].Item2;
                output.WriteLine("doy181 hour | A | B | A[h-1]");
                for (int h = 5; h <= 20; h++)
                {
                    int key = 181 * 24 + h;
                    string av = a.TryGetValue(key, out double va) ? va.ToString("0.000") : "  -  ";
                    string bv = b.TryGetValue(key, out double vb) ? vb.ToString("0.000") : "  -  ";
                    string aPrev = a.TryGetValue(key - 1, out double vap) ? vap.ToString("0.000") : "  -  ";
                    output.WriteLine($"  {h,2} | {av} | {bv} | {aPrev}");
                }
            }

            Assert.NotEmpty(matchedMaps);
            // The hours align 1:1 with TAS, so no whole-hour shift should beat the unshifted score.
            // (Before the SAM_Tas shade-slot fix the minimum was at -1h; this guards the fix.)
            Assert.True(deltaByShift[0] < deltaByShift[-1] && deltaByShift[0] < deltaByShift[1],
                $"via-TBD coverage is hour-shifted vs TAS: delta(0)={deltaByShift[0]:0.0000} should be below delta(-1)={deltaByShift[-1]:0.0000} and delta(+1)={deltaByShift[1]:0.0000}");
        }
    }
}
