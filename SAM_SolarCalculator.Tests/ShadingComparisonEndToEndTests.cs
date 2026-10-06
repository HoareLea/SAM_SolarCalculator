// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SolarCreate = SAM.Analytical.SolarCalculator.Create;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The comparison run END TO END through the production entry point on a real exported model
    /// (the same MultiAzimuth fixture the other stages use): assembly, one shared context,
    /// verification, ranking and report. Fast CI coverage of the whole pipeline; the Kołobrzeg
    /// acceptance test is the LongRunning companion.
    /// </summary>
    public class ShadingComparisonEndToEndTests
    {
        private const double GridSize = 0.5;
        private const double SunAngleStep = 2.0;

        private static AnalyticalModel Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MultiAzimuth.sam");
            Assert.True(File.Exists(path), $"Test fixture missing: {path}");
            return SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);
        }

        [Fact]
        public void The_Full_Comparison_Assembles_Verifies_Ranks_And_Reports()
        {
            AnalyticalModel model = Load();
            WeatherData weatherData = model.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            Assert.NotNull(weatherData);
            int year = weatherData.WeatherYears.First(x => x != null).Year;

            // Two south-facing apertures (the south wall of the fixture carries several).
            List<ApertureSolarTarget> southTargets = model.ApertureSolarTargets(null, GridSize)
                .Where(x => Math.Abs(x.Azimuth - 180.0) < 1.0)
                .Take(2)
                .ToList();
            Assert.Equal(2, southTargets.Count);

            ShadingScheme Shade(string name, IShadingTypology typology)
            {
                return new ShadingScheme(name, "RationaliseShading", southTargets.Select(x => x.ApertureGuid), new List<Guid> { southTargets[0].PanelGuid },
                    southTargets.Select(t => new ShadingDevice(t.ApertureGuid, typology)),
                    new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                    ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());
            }

            List<ShadingScheme> schemes = new List<ShadingScheme>
            {
                new ShadingScheme("No Shade", "Baseline", southTargets.Select(x => x.ApertureGuid), new List<Guid> { southTargets[0].PanelGuid },
                    new List<ShadingDevice>(), new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                    ShadingDesignStatus.NoShading, new List<string>(), null, new List<string>()),
                Shade("Overhang", new Overhang(1.0)),
                Shade("VerticalFins", new VerticalFins(0.5, 2)),
            };

            ShadingComparisonResult result = SolarCreate.ShadingComparison(
                model, southTargets, schemes, 1.0, 0.1, MaterialCostReference.AdmittedDirectEnergy, out string message,
                weatherData, null, null, null, GridSize, SunAngleStep, false);

            Assert.Null(message);
            Assert.NotNull(result);
            Assert.Equal(3, result.SuppliedCount);
            Assert.Equal(3, result.RankedCount);
            Assert.True(result.BaselineAnchored);

            // The top-ranked row is the arithmetic maximum among rankable rows.
            ShadingComparisonRow top = result.TopRankedRow;
            Assert.NotNull(top);
            Assert.True(top.TopRanked);
            double maxScore = result.Rows.Where(x => x.Rank >= 1).Max(x => x.ObjectiveScore);
            Assert.Equal(maxScore, top.ObjectiveScore, 9);

            // The No Shade baseline is measured at exactly 0.
            ShadingComparisonRow noShade = result.Rows.First(x => x.Status == ShadingComparisonStatus.NoShadeBaseline);
            Assert.Equal(0.0, noShade.ObjectiveScore, 9);
            Assert.True(noShade.BaselineDirectSolar > 0);

            // Every ranked row reconciles and the deltas are right.
            foreach (ShadingComparisonRow row in result.Rows.Where(x => x.Rank >= 1))
            {
                Assert.Equal(row.Benefit - row.WantedSolarPenalty * row.Harm - row.MaterialPenalty * row.Cost, row.ObjectiveScore, 9);
                Assert.Equal(top.ObjectiveScore - row.ObjectiveScore, row.ScoreDeltaToTopRanked, 9);
            }

            // The report exists, its audit line sums, and it never claims a selection.
            string report = result.ReportMarkdown;
            Assert.NotNull(report);
            Assert.Contains("3 schemes supplied", report);
            Assert.Contains("TOP RANKED", report);
            Assert.DoesNotContain("SELECTED", report);
            Assert.Contains("Not yet recorded.", report);

            // The CSV has one row per scheme plus the header.
            Assert.Equal(4, result.ReportCsv.Split('\n').Count(x => x.TrimEnd('\r').Length > 0));
        }

        [Fact]
        public void A_Single_Grid_Comparison_Is_Provisional_Not_Ready()
        {
            // READY is reserved for a comparison whose convergence checks have been confirmed. A
            // single-grid run never confirms convergence, so a non-No-Shade leader must stay
            // PROVISIONAL, never READY. Pinned end-to-end through the production entry point.
            AnalyticalModel model = Load();
            WeatherData weatherData = model.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);

            List<ApertureSolarTarget> southTargets = model.ApertureSolarTargets(null, GridSize)
                .Where(x => Math.Abs(x.Azimuth - 180.0) < 1.0)
                .Take(2)
                .ToList();

            ShadingScheme Shade(string name, IShadingTypology typology)
            {
                return new ShadingScheme(name, "RationaliseShading", southTargets.Select(x => x.ApertureGuid), new List<Guid> { southTargets[0].PanelGuid },
                    southTargets.Select(t => new ShadingDevice(t.ApertureGuid, typology)),
                    new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                    ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());
            }

            List<ShadingScheme> schemes = new List<ShadingScheme>
            {
                new ShadingScheme("No Shade", "Baseline", southTargets.Select(x => x.ApertureGuid), new List<Guid> { southTargets[0].PanelGuid },
                    new List<ShadingDevice>(), new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                    ShadingDesignStatus.NoShading, new List<string>(), null, new List<string>()),
                Shade("Overhang", new Overhang(1.0)),
                Shade("VerticalFins", new VerticalFins(0.5, 2)),
            };

            ShadingComparisonResult result = SolarCreate.ShadingComparison(
                model, southTargets, schemes, 1.0, 0.1, MaterialCostReference.AdmittedDirectEnergy, out string message,
                weatherData, null, null, null, GridSize, SunAngleStep, false);

            Assert.Null(message);

            // Convergence is never confirmed by a single-grid run, so READY is unreachable.
            Assert.False(result.GridConvergenceConfirmed);
            Assert.NotEqual(ShadingRecommendationStatus.Ready, result.RecommendationStatus);

            // A non-No-Shade, non-indeterminate leader is PROVISIONAL, with the convergence check as
            // the reason.
            if (result.TopRankedRow != null
                && result.TopRankedRow.Status != ShadingComparisonStatus.NoShadeBaseline
                && result.RecommendationStatus != ShadingRecommendationStatus.Indeterminate)
            {
                Assert.Equal(ShadingRecommendationStatus.Provisional, result.RecommendationStatus);
                Assert.Contains("grid convergence has not been demonstrated", result.OpenChecks);
            }

            Assert.Contains("GRID CONVERGENCE: NOT CONFIRMED", result.ReportMarkdown);
            Assert.Contains("READY is reserved for a comparison where the required convergence checks have", result.ReportMarkdown);
            Assert.Contains("A single-grid comparison without such confirmation remains", result.ReportMarkdown);
        }

        [Fact]
        public void The_Full_Comparison_Is_Deterministic()
        {
            AnalyticalModel model = Load();
            WeatherData weatherData = model.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            int year = weatherData.WeatherYears.First(x => x != null).Year;

            List<ApertureSolarTarget> southTargets = model.ApertureSolarTargets(null, GridSize)
                .Where(x => Math.Abs(x.Azimuth - 180.0) < 1.0)
                .Take(2)
                .ToList();

            ShadingScheme Shade(string name, IShadingTypology typology)
            {
                return new ShadingScheme(name, "RationaliseShading", southTargets.Select(x => x.ApertureGuid), new List<Guid> { southTargets[0].PanelGuid },
                    southTargets.Select(t => new ShadingDevice(t.ApertureGuid, typology)),
                    new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                    ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());
            }

            List<ShadingScheme> schemes = new List<ShadingScheme>
            {
                Shade("Overhang", new Overhang(1.0)),
                Shade("VerticalFins", new VerticalFins(0.5, 2)),
            };

            ShadingComparisonResult first = SolarCreate.ShadingComparison(
                model, southTargets, schemes, 1.0, 0.1, MaterialCostReference.AdmittedDirectEnergy, out string _,
                weatherData, null, null, null, GridSize, SunAngleStep, false);

            ShadingComparisonResult second = SolarCreate.ShadingComparison(
                model, southTargets, new List<ShadingScheme> { schemes[1], schemes[0] }, 1.0, 0.1, MaterialCostReference.AdmittedDirectEnergy, out string _,
                weatherData, null, null, null, GridSize, SunAngleStep, false);

            Assert.Equal(first.ReportMarkdown, second.ReportMarkdown);
            Assert.Equal(first.ReportCsv, second.ReportCsv);
            Assert.Equal(first.ToJsonObject().ToJsonString(), second.ToJsonObject().ToJsonString());
        }

        [Fact]
        public void Duplicated_Targets_And_Duplicated_Schemes_Are_Refused_Not_Silently_Collapsed()
        {
            // The refusal fires during input materialisation, before any solar context is built, so
            // a null model exercises it.
            List<ApertureSolarTarget> southTargets = new List<ApertureSolarTarget>();
            foreach (int ordinal in new int[] { 1, 2 })
            {
                SAM.Geometry.Spatial.Face3D face = SyntheticTargets.Face(SyntheticTargets.South, new Point3D(2.0 * ordinal, 0, 5), 1.0, 2.0);
                southTargets.Add(new ApertureSolarTarget(new Guid("eeeeeee1-0000-0000-0000-00000000000" + ordinal), new Guid("fffffff1-0000-0000-0000-000000000001"), face, Geometry.SolarCalculator.Query.AnalysisCells(face, 0.5)));
            }

            ShadingScheme scheme = new ShadingScheme("Overhang", "RationaliseShading", southTargets.Select(x => x.ApertureGuid), new List<Guid> { southTargets[0].PanelGuid },
                southTargets.Select(t => new ShadingDevice(t.ApertureGuid, (IShadingTypology)new Overhang(0.5))),
                new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());

            // Duplicated target.
            ShadingComparisonResult duplicateTargets = ((AnalyticalModel)null).ShadingComparison(
                new List<ApertureSolarTarget> { southTargets[0], southTargets[1], southTargets[0] },
                new List<ShadingScheme> { scheme }, 1.0, 0.1, MaterialCostReference.AdmittedDirectEnergy, out string duplicateTargetMessage,
                null, null, null, null, GridSize, 2.0, false);
            Assert.Contains("more than once", duplicateTargetMessage);
            Assert.Equal(ShadingComparisonOutcome.NoComparableOptions, duplicateTargets.Outcome);

            // Duplicated scheme (same SchemeGuid supplied twice).
            ShadingComparisonResult duplicateSchemes = ((AnalyticalModel)null).ShadingComparison(
                southTargets, new List<ShadingScheme> { scheme, new ShadingScheme(scheme) }, 1.0, 0.1, MaterialCostReference.AdmittedDirectEnergy, out string duplicateSchemeMessage,
                null, null, null, null, GridSize, 2.0, false);
            Assert.Contains("more than once", duplicateSchemeMessage);
            Assert.Equal(ShadingComparisonOutcome.NoComparableOptions, duplicateSchemes.Outcome);
        }

        [Fact]
        public void A_Scheme_With_Empty_Identity_Is_Refused_Not_Ranked_As_No_Shade()
        {
            // A scheme reconstructed from corrupted JSON is inert (empty SchemeGuid). The comparison
            // boundary must refuse it with an actionable message, never classify it as the No Shade
            // baseline.
            List<ApertureSolarTarget> southTargets = new List<ApertureSolarTarget>();
            foreach (int ordinal in new int[] { 1, 2 })
            {
                SAM.Geometry.Spatial.Face3D face = SyntheticTargets.Face(SyntheticTargets.South, new Point3D(2.0 * ordinal, 0, 5), 1.0, 2.0);
                southTargets.Add(new ApertureSolarTarget(new Guid("eeeeeee1-0000-0000-0000-00000000000" + ordinal), new Guid("fffffff1-0000-0000-0000-000000000001"), face, Geometry.SolarCalculator.Query.AnalysisCells(face, 0.5)));
            }

            ShadingScheme scheme = new ShadingScheme("Overhang", "RationaliseShading", southTargets.Select(x => x.ApertureGuid), new List<Guid> { southTargets[0].PanelGuid },
                southTargets.Select(t => new ShadingDevice(t.ApertureGuid, (IShadingTypology)new Overhang(0.5))),
                new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());

            JsonObject json = scheme.ToJsonObject();
            ((JsonArray)json["Devices"])[0] = new JsonObject { ["_type"] = "Not.A.ShadingDevice" };

            ShadingScheme corrupted = SAM.Core.Create.IJSAMObject<ShadingScheme>(json);
            Assert.NotNull(corrupted);
            Assert.Equal(Guid.Empty, corrupted.SchemeGuid);

            ShadingComparisonResult result = ((AnalyticalModel)null).ShadingComparison(
                southTargets, new List<ShadingScheme> { corrupted }, 1.0, 0.1, MaterialCostReference.AdmittedDirectEnergy, out string message,
                null, null, null, null, GridSize, 2.0, false);

            Assert.Contains("no resolved identity", message);
            Assert.Equal(ShadingComparisonOutcome.NoComparableOptions, result.Outcome);
            Assert.Equal(ShadingRecommendationStatus.NoDecision, result.RecommendationStatus);
        }

        [Fact]
        public void Assembly_And_Comparison_Compose_Through_The_Production_Entry_Points()
        {
            // The same chain the Grasshopper workflow runs: assemble from optimisation results, then
            // compare. The optimisation itself is the slow part and lives in the LongRunning tests;
            // here the assembly runs over hand-made results and the comparison verifies the schemes.
            AnalyticalModel model = Load();
            WeatherData weatherData = model.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            int year = weatherData.WeatherYears.First(x => x != null).Year;

            List<ApertureSolarTarget> southTargets = model.ApertureSolarTargets(null, GridSize)
                .Where(x => Math.Abs(x.Azimuth - 180.0) < 1.0)
                .Take(2)
                .ToList();

            List<OptimisedShadingResult> results = new List<OptimisedShadingResult>();
            foreach (ApertureSolarTarget target in southTargets)
            {
                OptimisedShadingResult result = new OptimisedShadingResult();
                result.ApertureGuid = target.ApertureGuid;
                result.TypologyName = "Overhang";
                result.SetParameters(new List<string> { "Depth" }, new Dictionary<string, double> { { "Depth", 0.8 } }, new Dictionary<string, double>(), new List<ShadingParameter> { new ShadingParameter("Depth", 0.05, 3.0, 0.01) });
                result.Objective = new ShadingObjective(1.0, 0.1);
                result.SetRun(10, 2, 1.0, ShadingOptimisationTermination.StepBelowGranularity, false, 0.0, 2, 2);
                result.SetProvenance(target.ApertureGuid, "SeasonalDesirability", GridSize, SunAngleStep, 30.0, year, "ctx", "tgt", "tbl", new List<Guid>());
                results.Add(result);
            }

            List<ShadingScheme> schemes = SolarCreate.ShadingSchemes(southTargets, results, null, true, out string assemblyMessage);
            Assert.Null(assemblyMessage);
            Assert.NotNull(schemes);
            Assert.Contains(schemes, x => x.Name == "Overhang");
            Assert.Contains(schemes, x => x.Name == "No Shade");

            ShadingComparisonResult comparison = SolarCreate.ShadingComparison(
                model, southTargets, schemes, 1.0, 0.1, MaterialCostReference.AdmittedDirectEnergy, out string comparisonMessage,
                weatherData, null, null, null, GridSize, SunAngleStep, false);

            Assert.Null(comparisonMessage);
            Assert.Equal(2, comparison.RankedCount);
        }
    }
}
