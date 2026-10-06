// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SAM.Analytical.SolarCalculator;
using SolarCreate = SAM.Analytical.SolarCalculator.Create;
using SAM.Core.SolarCalculator;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The review's separation of ranking and selection: the comparison reports the analytical
    /// leader; the engineer selects downstream, by SchemeGuid, with a reason whenever the choice
    /// departs from rank 1. A selection never changes scores, ranks or robustness figures.
    /// </summary>
    public class ShadingSelectionTests
    {
        private static readonly Guid PanelGuid = new Guid("fffffff1-0000-0000-0000-000000000001");

        private static ApertureSolarTarget Target(int ordinal)
        {
            SAM.Geometry.Spatial.Face3D face = SyntheticTargets.Face(SyntheticTargets.South, new Point3D(2.0 * ordinal, 0, 5), 1.0, 2.0);
            return new ApertureSolarTarget(new Guid("eeeeeee1-0000-0000-0000-00000000000" + ordinal), PanelGuid, face, Geometry.SolarCalculator.Query.AnalysisCells(face, 0.5));
        }

        private static List<ApertureSolarTarget> Targets(int count)
        {
            List<ApertureSolarTarget> result = new List<ApertureSolarTarget>();
            for (int i = 1; i <= count; i++)
            {
                result.Add(Target(i));
            }

            return result;
        }

        private static ShadingAnalysisSignature Signature()
        {
            return new ShadingAnalysisSignature(
                new List<Guid> { new Guid("eeeeeee1-0000-0000-0000-000000000001"), new Guid("eeeeeee1-0000-0000-0000-000000000002"), new Guid("eeeeeee1-0000-0000-0000-000000000003") },
                new List<int> { 8, 8, 8 }, "ctx", "tgt", 0.5, 2.0, 2018, 30.0, 51.5, -0.13, 0.0, "weather", "desir", "SeasonalDesirability");
        }

        private static ShadingScheme Scheme(string name, List<ApertureSolarTarget> targets, IShadingTypology typology)
        {
            return new ShadingScheme(name, "RationaliseShading", targets.Select(x => x.ApertureGuid), new List<Guid> { PanelGuid },
                targets.Select(t => new ShadingDevice(t.ApertureGuid, typology)),
                new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());
        }

        private static ShadingComparisonRow Row(ShadingScheme scheme, double score, ShadingDesignStatus status = ShadingDesignStatus.Ok)
        {
            ShadingSchemePerformance performance = new ShadingSchemePerformance(
                new List<ShadingPerformance>(), 1.0, true, 6.0, new Dictionary<Guid, Guid>());

            ShadingComparisonRow row = new ShadingComparisonRow();
            row.SchemeGuid = scheme.SchemeGuid;
            row.OptionName = scheme.Name;
            row.DesignMethod = scheme.DesignMethod;
            row.PhysicalDeviceCount = scheme.PhysicalDeviceCount;
            row.ApertureCount = scheme.ApertureGuids.Count;
            row.ApertureGuids = scheme.ApertureGuids;
            row.ObjectiveScore = score;
            row.Benefit = score + 2.0;
            row.Harm = 1.0;
            row.Cost = 10.0;
            row.WantedSolarPenalty = 1.0;
            row.MaterialPenalty = 0.1;
            row.MaterialAvailable = true;
            row.PhysicalDeviceArea = 1.0;
            row.MaterialFraction = 1.0 / 6.0;
            row.VerifiedResult = new VerifiedShadingSchemeResult(scheme, performance, Signature(), status, new List<string>(), "summary", "hash", double.NaN, false);
            row.Status = ShadingComparisonStatus.Ranked;
            return row;
        }

        private static ShadingComparisonResult Comparison(ShadingScheme topScheme, ShadingScheme secondScheme, double topScore = 30.0, double secondScore = 20.0)
        {
            List<ShadingComparisonRow> rows = new List<ShadingComparisonRow> { Row(topScheme, topScore), Row(secondScheme, secondScore) };
            SolarCreate.RankRows(rows, new ShadingObjective(1.0, 0.1));
            rows[0].TopRanked = true;

            ShadingComparisonResult result = new ShadingComparisonResult();
            result.Rows = rows;
            result.Objective = new ShadingObjective(1.0, 0.1);
            result.ReferenceSignature = Signature();
            result.Hours = new ShadingAnalysisHours(8760, 8760, 0, 4400, 1900, 1200, 40.0, 600, 200, 400, new Dictionary<Guid, int>());
            result.BaselineAnchored = true;
            result.Targets = Targets(3);
            result.ModelName = "TestModel.sam";
            result.SuppliedCount = 2;
            result.RankedCount = 2;
            result.VerifiedCount = 2;
            result.Incomplete = false;
            result.Outcome = ShadingComparisonOutcome.TopRanked;
            result.RecommendationStatus = ShadingRecommendationStatus.Provisional;
            result.ProjectInputs = new List<ShadingProjectInput>();
            return result;
        }

        [Fact]
        public void Selecting_The_Analytical_Leader_Needs_No_Reason()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingScheme leader = Scheme("Overhang", targets, new Overhang(0.5));
            ShadingScheme other = Scheme("VerticalFins", targets, new VerticalFins(0.4, 2));
            ShadingComparisonResult result = Comparison(leader, other);

            ShadingSelectionDecision decision = result.SelectShadingScheme(leader, null);

            Assert.True(decision.Successful);
            Assert.Null(decision.Message);
            Assert.Equal(leader.SchemeGuid, decision.SelectedSchemeGuid);
            Assert.Equal(1, decision.SelectedRank);
            Assert.Equal(ShadingSelectionAlignment.AgreesWithLeader, decision.SelectionAlignment);
            Assert.Contains("agrees with the analytical ranking", decision.DecisionSummary);
        }

        [Fact]
        public void Selecting_A_Lower_Rank_Requires_A_Reason()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingScheme leader = Scheme("Overhang", targets, new Overhang(0.5));
            ShadingScheme other = Scheme("VerticalFins", targets, new VerticalFins(0.4, 2));
            ShadingComparisonResult result = Comparison(leader, other);

            // Without a reason: refused.
            ShadingSelectionDecision refused = result.SelectShadingScheme(other, null);
            Assert.False(refused.Successful);
            Assert.Contains("reason", refused.Message);

            // With a reason: recorded.
            ShadingSelectionDecision decision = result.SelectShadingScheme(other, "Lower material quantity and simpler maintenance.");
            Assert.True(decision.Successful);
            Assert.Equal(2, decision.SelectedRank);
            Assert.Equal(ShadingSelectionAlignment.DepartsFromLeader, decision.SelectionAlignment);

            string finalReport = decision.FinalReportMarkdown;
            Assert.Contains("ENGINEERING DECISION", finalReport);
            Assert.Contains("VerticalFins selected", finalReport);
            Assert.Contains("Trade-off against the analytical leader", finalReport);
            Assert.Contains("The engineering selection does not alter the analytical ranking", decision.DecisionSummary);
        }

        [Fact]
        public void Selection_Is_By_SchemeGuid_Not_By_Name_Or_Rank()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingScheme leader = Scheme("Overhang", targets, new Overhang(0.5));
            ShadingScheme other = Scheme("VerticalFins", targets, new VerticalFins(0.4, 2));
            ShadingComparisonResult result = Comparison(leader, other);

            // A COPY of the rank-2 scheme, carrying the same identity, must be selectable.
            ShadingScheme copy = new ShadingScheme(other);
            ShadingSelectionDecision decision = result.SelectShadingScheme(copy, "reason");
            Assert.True(decision.Successful);
            Assert.Equal(other.SchemeGuid, decision.SelectedSchemeGuid);
        }

        [Fact]
        public void A_Scheme_Not_In_The_Comparison_Is_Invalidated()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingScheme leader = Scheme("Overhang", targets, new Overhang(0.5));
            ShadingScheme other = Scheme("VerticalFins", targets, new VerticalFins(0.4, 2));
            ShadingComparisonResult result = Comparison(leader, other);

            // A scheme the comparison does not contain (e.g. after a recalculation changed the set).
            ShadingScheme stale = Scheme("Stale", targets, new Overhang(0.7));
            ShadingSelectionDecision decision = result.SelectShadingScheme(stale, "whatever");
            Assert.False(decision.Successful);
            Assert.Contains("not part of this comparison", decision.Message);
        }

        [Fact]
        public void Incomparable_And_NotRankable_Schemes_Cannot_Be_Selected()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingScheme leader = Scheme("Overhang", targets, new Overhang(0.5));
            ShadingScheme incompatible = Scheme("Incomparable", targets, new Overhang(0.5));
            ShadingScheme notRankable = Scheme("NotRankable", targets, new Overhang(0.5));

            ShadingComparisonResult result = Comparison(leader, Scheme("Second", targets, new VerticalFins(0.4, 2)));

            List<ShadingComparisonRow> rows = result.Rows;
            ShadingComparisonRow incompatibleRow = Row(incompatible, 10.0);
            incompatibleRow.Status = ShadingComparisonStatus.Incomparable;
            incompatibleRow.ComparabilityReason = "GridSize differs";
            rows.Add(incompatibleRow);

            ShadingComparisonRow notRankableRow = Row(notRankable, 5.0);
            notRankableRow.Status = ShadingComparisonStatus.NotRankable;
            notRankableRow.ComparabilityReason = "MaterialFraction unavailable";
            rows.Add(notRankableRow);

            result.Rows = rows;

            ShadingSelectionDecision incompatibleDecision = result.SelectShadingScheme(incompatible, null);
            Assert.False(incompatibleDecision.Successful);
            Assert.Contains("Re-run this option on the comparison basis", incompatibleDecision.Message);

            ShadingSelectionDecision notRankableDecision = result.SelectShadingScheme(notRankable, null);
            Assert.False(notRankableDecision.Successful);
            Assert.Contains("Resolve the missing material", notRankableDecision.Message);
        }

        [Fact]
        public void Indeterminate_Comparison_Still_Allows_An_Explicit_Selection()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingScheme leader = Scheme("Overhang", targets, new Overhang(0.5));
            ShadingScheme other = Scheme("VerticalFins", targets, new VerticalFins(0.4, 2));
            ShadingComparisonResult result = Comparison(leader, other);
            result.RecommendationStatus = ShadingRecommendationStatus.Indeterminate;

            ShadingSelectionDecision decision = result.SelectShadingScheme(other, "Preferred for maintenance and winter-solar retention.");
            Assert.True(decision.Successful);
            Assert.Equal(ShadingSelectionAlignment.LeaderNotDistinguishable, decision.SelectionAlignment);
            Assert.Contains("could not be separated", decision.DecisionSummary);
        }

        [Fact]
        public void Indeterminate_Rank_One_Also_Requires_A_Reason()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingScheme leader = Scheme("Overhang", targets, new Overhang(0.5));
            ShadingScheme other = Scheme("VerticalFins", targets, new VerticalFins(0.4, 2));
            ShadingComparisonResult result = Comparison(leader, other);
            result.RecommendationStatus = ShadingRecommendationStatus.Indeterminate;

            // An INDETERMINATE comparison has no single analytical recommendation: choosing rank 1
            // is an explicit engineering decision and must be recorded, exactly like a lower rank.
            ShadingSelectionDecision refused = result.SelectShadingScheme(leader, null);
            Assert.False(refused.Successful);
            Assert.Contains("requires a recorded reason", refused.Message);

            ShadingSelectionDecision accepted = result.SelectShadingScheme(leader, "Confirmed against the winter-solar requirement.");
            Assert.True(accepted.Successful);
            Assert.Equal(1, accepted.SelectedRank);
            Assert.Equal(ShadingSelectionAlignment.LeaderNotDistinguishable, accepted.SelectionAlignment);
            Assert.Contains("could not be separated", accepted.DecisionSummary);
        }

        [Fact]
        public void A_Selection_Never_Changes_Scores_Ranks_Or_Robustness()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingScheme leader = Scheme("Overhang", targets, new Overhang(0.5));
            ShadingScheme other = Scheme("VerticalFins", targets, new VerticalFins(0.4, 2));
            ShadingComparisonResult result = Comparison(leader, other);

            string before = result.CsvReport();

            ShadingSelectionDecision decision = result.SelectShadingScheme(other, "reason");
            Assert.True(decision.Successful);

            // The original comparison is untouched by the selection.
            Assert.Equal(before, result.CsvReport());
            Assert.False(result.Rows.First(x => x.OptionName == "Overhang").EngineerSelected);

            // The selection's snapshot marks the chosen row, and the final report says so.
            ShadingComparisonResult snapshot = decision.ComparisonResult;
            Assert.True(snapshot.Rows.First(x => x.OptionName == "VerticalFins").EngineerSelected);
            Assert.Contains("ENGINEER SELECTED", decision.FinalReportMarkdown);
            Assert.Equal(1, snapshot.Rows[0].Rank); // ranks unchanged
        }

        [Fact]
        public void Selecting_No_Shade_When_It_Is_Not_The_Leader_Is_Recorded_With_A_Warning()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingScheme leader = Scheme("Overhang", targets, new Overhang(0.5));
            ShadingScheme noShade = new ShadingScheme("No Shade", "Baseline", targets.Select(x => x.ApertureGuid), new List<Guid> { PanelGuid },
                new List<ShadingDevice>(), new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.NoShading, new List<string>(), null, new List<string>());

            ShadingComparisonResult result = Comparison(leader, noShade, topScore: 30.0, secondScore: 0.0);
            List<ShadingComparisonRow> rows = result.Rows;
            rows[1].Status = ShadingComparisonStatus.NoShadeBaseline;
            result.Rows = rows;
            result.BaselineAnchored = true;

            ShadingSelectionDecision decision = result.SelectShadingScheme(noShade, "No budget for any device this year.");
            Assert.True(decision.Successful);
            Assert.Contains("worse than building nothing", decision.FinalReportMarkdown);
        }
    }
}
