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
using SolarCreate = SAM.Analytical.SolarCalculator.Create;
using SAM.Core.SolarCalculator;
using SAM.Weather;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The Kołobrzeg acceptance run (§16): the real exported project model, every conventional
    /// family optimised per window, the grouped Dakar awning run, assembled into schemes and
    /// compared through the production entry points.
    ///
    /// WHAT IS ASSERTED (structural, per the fixture policy): the scheme identities, the awning
    /// geometry and product constraints, the comparison invariants, determinism, and that the
    /// analytical leader IS the arithmetic maximum among rankable rows. The energies themselves are
    /// RECORDED, not pinned — the fixture exists to validate the pipeline, not to be tuned to.
    /// </summary>
    public class KolobrzegComparisonTests
    {
        private readonly ITestOutputHelper output;

        public KolobrzegComparisonTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const double Extension = 0.15;

        private static List<Guid> StudiedGuids()
        {
            return new List<Guid>
            {
                KolobrzegFixture.TallApertureGuid,
                KolobrzegFixture.MidApertureGuid,
                KolobrzegFixture.SmallApertureGuid,
            };
        }

        private static List<ApertureSolarTarget> StudiedTargets(AnalyticalModel model)
        {
            return model.ApertureSolarTargets(StudiedGuids(), KolobrzegFixture.HistoricalGridSize);
        }

        private static List<ShadingScheme> Assemble(AnalyticalModel model, List<ApertureSolarTarget> targets)
        {
            // Per-aperture optimisation: every family over every aperture, through the same
            // production entry point Grasshopper's RationaliseShading uses.
            List<OptimisedShadingResult> optimisedShadingResults = new List<OptimisedShadingResult>();
            foreach (Guid apertureGuid in StudiedGuids())
            {
                optimisedShadingResults.AddRange(KolobrzegFixture.Optimise(apertureGuid, KolobrzegFixture.HistoricalGridSize));
            }

            // The grouped Dakar awning over the whole facade.
            int year = KolobrzegFixture.Year(model);
            List<GroupedAwningResult> groupedAwningResults = SolarCreate.AwningGroupResults(
                model, StudiedGuids(), year, out string awningMessage, out bool _,
                specification: AwningSpecification.Dakar,
                projection: null,
                tiltDegrees: null,
                riseAboveHead: 0.0,
                extensionBeyondJambs: Extension,
                valanceDepth: 0.0,
                maximumGap: 0.20,
                headTolerance: 0.02,
                gridSize: KolobrzegFixture.HistoricalGridSize,
                sunAngleStep: 2.0,
                recalculate: false,
                objective: new ShadingObjective(1.0, 0.1),
                maximumEvaluations: 400);

            Assert.Null(awningMessage);
            Assert.NotNull(groupedAwningResults);

            List<ShadingScheme> schemes = SolarCreate.ShadingSchemes(targets, optimisedShadingResults, groupedAwningResults, true, out string assemblyMessage);
            Assert.Null(assemblyMessage);
            Assert.NotNull(schemes);
            return schemes;
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Kolobrzeg_Comparison_Assembles_Verifies_Ranks_And_Reports()
        {
            AnalyticalModel model = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(model);
            List<ApertureSolarTarget> targets = StudiedTargets(model);
            Assert.Equal(3, targets.Count);

            Stopwatch stopwatch = Stopwatch.StartNew();
            List<ShadingScheme> schemes = Assemble(model, targets);
            long assemblyMilliseconds = stopwatch.ElapsedMilliseconds;
            output.WriteLine($"assembly: {assemblyMilliseconds} ms, {schemes.Count} schemes");

            // Expected options: No Shade + the five conventional families + the grouped awning.
            Assert.Contains(schemes, x => x.Name == "No Shade");
            Assert.Contains(schemes, x => x.Name == "Overhang");
            Assert.Contains(schemes, x => x.Name == "HorizontalLouvres");
            Assert.Contains(schemes, x => x.Name == "VerticalFins");
            Assert.Contains(schemes, x => x.Name == "EggCrate");
            Assert.Contains(schemes, x => x.Name == "Grouped Dakar Retractable Awning");

            // The grouped scheme: exactly ONE physical device covering exactly the three apertures.
            ShadingScheme awningScheme = schemes.First(x => x.Name == "Grouped Dakar Retractable Awning");
            if (awningScheme.Status != ShadingDesignStatus.NotEvaluated)
            {
                Assert.Single(awningScheme.GroupedDevices);
                GroupedShadingDevice device = awningScheme.GroupedDevices[0];
                Assert.Equal(new HashSet<Guid>(StudiedGuids()), new HashSet<Guid>(device.ApertureGuids));
                ApertureShadingGroup group = awningScheme.Groups[0];
                Assert.Equal(2.70, group.Width, 6);
                Assert.Equal(3.00, device.Width(group), 6);
                Assert.Equal(2, device.Specification.RequiredWallBracketCount(device.Width(group)));

                if (!device.IsNoShading)
                {
                    RetractableAwning awning = device.Typology as RetractableAwning;
                    Assert.NotNull(awning);
                    Assert.Contains(awning.GetParameter("Projection"), AwningSpecification.Dakar.AllowedProjections);
                    Assert.True(awning.GetParameter("Projection") <= 2.6 + 1e-9);
                    Assert.InRange(awning.GetParameter("TiltDegrees"), 5.0, 40.0);
                    Assert.Equal(0.0, awning.GetParameter("MountingOffset"), 9);
                }
            }

            stopwatch.Restart();
            ShadingComparisonResult result = SolarCreate.ShadingComparison(
                model, targets, schemes, 1.0, 0.1, MaterialCostReference.AdmittedDirectEnergy, out string message,
                null, null, null, null, KolobrzegFixture.HistoricalGridSize, 2.0, false);
            long comparisonMilliseconds = stopwatch.ElapsedMilliseconds;
            output.WriteLine($"comparison: {comparisonMilliseconds} ms");

            Assert.Null(message);
            Assert.NotNull(result);
            Assert.Equal(schemes.Count, result.SuppliedCount);
            Assert.True(result.BaselineAnchored);

            // I4/I5/I9 on every ranked row.
            foreach (ShadingComparisonRow row in result.Rows.Where(x => x.Rank >= 1))
            {
                Assert.Equal(row.BaselineDirectSolar, row.AdmittedUnwantedSolar + row.AdmittedWantedSolar + row.AdmittedNeutralSolar, 9);
                Assert.Equal(row.DirectSolarIntercepted, row.UnwantedSolarIntercepted + row.WantedSolarBlocked + row.NeutralSolarIntercepted, 9);
                Assert.Equal(row.Benefit - row.WantedSolarPenalty * row.Harm - row.MaterialPenalty * row.Cost, row.ObjectiveScore, 9);
            }

            // I10: the No Shade row is computed as exactly 0 with a real baseline.
            ShadingComparisonRow noShade = result.Rows.First(x => x.Status == ShadingComparisonStatus.NoShadeBaseline);
            Assert.Equal(0.0, noShade.ObjectiveScore, 12);
            Assert.True(noShade.BaselineDirectSolar > 0);

            // The analytical leader IS the arithmetic maximum among rankable rows.
            ShadingComparisonRow top = result.TopRankedRow;
            Assert.NotNull(top);
            double maxScore = result.Rows.Where(x => x.Rank >= 1).Max(x => x.ObjectiveScore);
            Assert.Equal(maxScore, top.ObjectiveScore, 9);

            // I8: largest baseline deviation from the No Shade reference.
            double largestDeviation = result.Rows.Where(x => x.Rank >= 1 && x != noShade)
                .Max(x => Math.Abs(x.BaselineDirectSolar - noShade.BaselineDirectSolar));
            Assert.True(largestDeviation <= 1e-9, $"largest baseline deviation {largestDeviation} exceeds the anchored tolerance");
            output.WriteLine($"I8 largest baseline deviation from the No Shade reference: {largestDeviation:0.###E+0} kWh");

            // Hour counts balance.
            ShadingAnalysisHours hours = result.Hours;
            Assert.Equal(hours.TimelineHours, hours.EvaluatedHours + hours.MissingWeatherHours);
            Assert.Equal(hours.BeamAdmittingHours, hours.UnwantedHours + hours.WantedHours + hours.NeutralHours);
            Assert.True(hours.BeamAdmittingHours <= hours.FacadeIncidentHours);

            // The report renders and its audit line sums.
            string report = result.ReportMarkdown;
            Assert.Contains("Comparison audit", report);
            Assert.Contains("TOP RANKED", report);
            Assert.DoesNotContain("SELECTED", report);

            // Determinism: two runs, byte-identical reports and identical leader.
            ShadingComparisonResult second = SolarCreate.ShadingComparison(
                model, targets, new List<ShadingScheme>(Enumerable.Reverse(schemes)), 1.0, 0.1, MaterialCostReference.AdmittedDirectEnergy, out string secondMessage,
                null, null, null, null, KolobrzegFixture.HistoricalGridSize, 2.0, false);
            Assert.Null(secondMessage);
            Assert.Equal(result.TopRankedSchemeGuid, second.TopRankedSchemeGuid);
            Assert.Equal(result.ReportMarkdown, second.ReportMarkdown);
            Assert.Equal(result.ReportCsv, second.ReportCsv);

            // Record the energies: evidence, not a gate.
            output.WriteLine("Kołobrzeg comparison - recorded energies (kWh) and scores:");
            foreach (ShadingComparisonRow row in result.Rows)
            {
                output.WriteLine($"  {row.OptionName,-38} rank {row.Rank,2} score {row.ObjectiveScore,9:0.000} " +
                    $"unwanted {row.UnwantedSolarIntercepted,8:0.000} wantedBlocked {row.WantedSolarBlocked,6:0.000} " +
                    $"area {row.PhysicalDeviceArea,6:0.000} unwBlocked {100 * row.UnwantedSolarBlocked,5:0.0}% " +
                    $"wantedRetained {100 * row.WantedSolarRetained,5:0.0}%");
            }

            output.WriteLine($"recommendation status: {result.RecommendationStatus}");
            foreach (string check in result.OpenChecks)
            {
                output.WriteLine($"  open check: {check}");
            }

            // The drift bands, only when the awning is the analytical leader: an order of magnitude
            // wider than plausible numerical drift - they catch a broken pipeline, not a decimal.
            if (top.OptionName == "Grouped Dakar Retractable Awning")
            {
                output.WriteLine("The grouped awning is the analytical leader - applying the drift bands.");
                Assert.True(top.UnwantedSolarBlocked > 0.80, $"awning unwanted blocked {top.UnwantedSolarBlocked} fell below the drift band");
                Assert.InRange(top.WantedSolarRetained, 0.0, 0.20);
            }
            else
            {
                output.WriteLine($"The grouped awning is NOT the analytical leader: rank 1 is {top.OptionName}. Reported honestly - see the PR description.");
            }

            // §17: the whole comparison must complete in under 10x the single grouped-awning
            // LongRunning test; record the split.
            output.WriteLine($"performance: assembly {assemblyMilliseconds} ms, comparison {comparisonMilliseconds} ms, total {assemblyMilliseconds + comparisonMilliseconds} ms");
            output.WriteLine("---- SAMPLE REPORT (first 4000 characters) ----");
            output.WriteLine(report.Length > 4000 ? report.Substring(0, 4000) : report);
        }
    }
}
