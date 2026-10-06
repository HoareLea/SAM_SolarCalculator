// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Xunit;
using SAM.Analytical.SolarCalculator;
using SolarCreate = SAM.Analytical.SolarCalculator.Create;
using SolarQuery = SAM.Analytical.SolarCalculator.Query;
using SAM.Core.SolarCalculator;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The ranking and report contract (§15.7): the comparator's total order, the row states, the
    /// recommendation statuses, the break-even algebra, and the byte-determinism of the generated
    /// report under shuffled input and a foreign culture.
    /// </summary>
    public class ShadingComparisonRankingAndReportTests
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

        private static ShadingScheme Scheme(string name, List<ApertureSolarTarget> targets, Dictionary<Guid, IShadingTypology> devices, string method = "RationaliseShading")
        {
            List<ShadingDevice> shadingDevices = new List<ShadingDevice>();
            foreach (ApertureSolarTarget target in targets)
            {
                IShadingTypology typology = devices.TryGetValue(target.ApertureGuid, out IShadingTypology found) ? found : new NoShading();
                shadingDevices.Add(new ShadingDevice(target.ApertureGuid, typology));
            }

            return new ShadingScheme(name, method, targets.Select(x => x.ApertureGuid), new List<Guid> { PanelGuid },
                shadingDevices, new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());
        }

        private static ShadingSchemePerformance Performance(
            List<ApertureSolarTarget> targets, string name,
            double admittedDirect, double admittedUnwanted, double admittedWanted,
            double intercepted, double interceptedUnwanted, double interceptedWanted,
            double deviceArea, bool materialAvailable)
        {
            List<ShadingPerformance> members = new List<ShadingPerformance>();
            foreach (ApertureSolarTarget target in targets)
            {
                double fraction = 1.0 / targets.Count;
                members.Add(new ShadingPerformance(
                    target.ApertureGuid, name,
                    admittedDirect * fraction, admittedUnwanted * fraction, admittedWanted * fraction,
                    intercepted * fraction, interceptedUnwanted * fraction, interceptedWanted * fraction,
                    0.0, double.NaN, new Dictionary<Guid, double>(), new Dictionary<Guid, string>()));
            }

            double gross = targets.Sum(x => x.GrossArea);
            return new ShadingSchemePerformance(members, deviceArea, materialAvailable, gross, new Dictionary<Guid, Guid>());
        }

        /// <summary>A row built the way the production builder fills it.</summary>
        private static ShadingComparisonRow Row(
            ShadingScheme scheme, List<ApertureSolarTarget> targets, ShadingObjective objective,
            double admittedDirect, double admittedUnwanted, double admittedWanted,
            double intercepted, double interceptedUnwanted, double interceptedWanted,
            double deviceArea, bool materialAvailable,
            double designScore = double.NaN, double designBenefit = double.NaN, double designHarm = double.NaN, double designCost = double.NaN,
            ShadingOptimisationTermination termination = ShadingOptimisationTermination.StepBelowGranularity,
            int evaluations = 100, ShadingComparisonStatus status = ShadingComparisonStatus.Ranked)
        {
            ShadingSchemePerformance performance = Performance(targets, scheme.Name, admittedDirect, admittedUnwanted, admittedWanted, intercepted, interceptedUnwanted, interceptedWanted, deviceArea, materialAvailable);

            ShadingComparisonRow row = new ShadingComparisonRow();
            row.SchemeGuid = scheme.SchemeGuid;
            row.OptionName = scheme.Name;
            row.DesignMethod = scheme.DesignMethod;
            row.TypologyNames = string.Join(", ", scheme.TypologyNames);
            row.ProductPreset = scheme.ProductPreset;
            row.PhysicalDeviceCount = scheme.PhysicalDeviceCount;
            row.ApertureCount = scheme.ApertureGuids.Count;
            row.ApertureGuids = scheme.ApertureGuids;
            row.DesignEvaluations = evaluations;
            row.DesignTermination = termination.ToString();
            row.DesignBudgetExhausted = termination == ShadingOptimisationTermination.EvaluationBudgetExhausted;
            row.DesignTimeScore = designScore;
            row.WantedSolarPenalty = objective.WantedSolarPenalty;
            row.MaterialPenalty = objective.MaterialPenalty;
            row.DesignObjectiveMatchesComparisonObjective = true;

            row.BaselineDirectSolar = performance.AdmittedDirectEnergy;
            row.AdmittedUnwantedSolar = performance.AdmittedUnwantedEnergy;
            row.AdmittedWantedSolar = performance.AdmittedWantedEnergy;
            row.AdmittedNeutralSolar = performance.AdmittedNeutralEnergy;
            row.DirectSolarIntercepted = performance.DirectSolarIntercepted;
            row.UnwantedSolarIntercepted = performance.UnwantedSolarIntercepted;
            row.WantedSolarBlocked = performance.WantedSolarBlocked;
            row.NeutralSolarIntercepted = performance.NeutralSolarIntercepted;
            row.UnattributedEnergy = performance.UnattributedInterceptedEnergy;
            row.DirectShadingEfficiency = performance.DirectShadingEfficiency;
            row.UnwantedSolarBlocked = performance.UnwantedSolarBlocked;
            row.WantedSolarRetained = performance.WantedSolarRetained;
            row.PhysicalDeviceArea = performance.PhysicalDeviceArea;
            row.MaterialFraction = performance.MaterialFraction;
            row.MaterialAvailable = performance.MaterialAvailable;
            row.Benefit = objective.Benefit(performance);
            row.Harm = objective.Harm(performance);
            row.Cost = objective.Cost(performance);
            row.ObjectiveScore = objective.Score(performance);

            double grossArea = targets.Sum(x => x.GrossArea);
            if (!double.IsNaN(grossArea) && grossArea > 0)
            {
                row.UnwantedInterceptedPerGrossArea = row.UnwantedSolarIntercepted / grossArea;
                row.ObjectiveScorePerGrossArea = row.ObjectiveScore / grossArea;
            }

            // The verified result COPIES the scheme, so the design provenance must be set on the
            // scheme before the copy is taken.
            scheme.DesignTimeScore = designScore;
            scheme.DesignTimeBenefit = designBenefit;
            scheme.DesignTimeHarm = designHarm;
            scheme.DesignTimeCost = designCost;
            scheme.DesignEvaluations = evaluations;
            scheme.DesignTermination = termination;

            row.VerifiedResult = new VerifiedShadingSchemeResult(scheme, performance, Signature(), ShadingDesignStatus.Ok, new List<string>(), "summary", "hash", double.NaN, false);

            // The production rankability rule: NaN never ranks; unknown material with mu > 0 is not rankable.
            row.Status = status;
            if (status == ShadingComparisonStatus.Ranked || status == ShadingComparisonStatus.NoShadeBaseline)
            {
                if (double.IsNaN(row.ObjectiveScore))
                {
                    row.Status = ShadingComparisonStatus.NotRankable;
                    row.ComparabilityReason = "The verified score is not a number, so the scheme cannot be ranked.";
                }
                else if (objective.MaterialPenalty != 0 && !row.MaterialAvailable)
                {
                    row.Status = ShadingComparisonStatus.NotRankable;
                    row.ComparabilityReason = "MaterialFraction is unavailable and the material penalty is > 0. Measured energy is shown; the scheme cannot be scored.";
                }
            }

            return row;
        }

        private static ShadingComparisonRow NoShadeRow(string name, List<ApertureSolarTarget> targets)
        {
            ShadingScheme scheme = new ShadingScheme(name, "Baseline", targets.Select(x => x.ApertureGuid), new List<Guid> { PanelGuid },
                new List<ShadingDevice>(), new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.NoShading, new List<string>(), null, new List<string>());
            return Row(scheme, targets, new ShadingObjective(1.0, 0.1), 100, 60, 20, 0, 0, 0, 0.0, true, status: ShadingComparisonStatus.NoShadeBaseline);
        }

        private static ShadingComparisonResult Result(
            List<ShadingComparisonRow> rows, ShadingObjective objective,
            ShadingAnalysisHours hours = null, bool baselineAnchored = true,
            List<string> openChecks = null, List<string> required = null,
            List<ShadingProjectInput> projectInputs = null)
        {
            ShadingComparisonResult result = new ShadingComparisonResult();
            result.Rows = rows;
            result.Objective = objective;
            result.ReferenceSignature = Signature();
            result.Hours = hours ?? Hours();
            result.BaselineAnchored = baselineAnchored;
            result.OpenChecks = openChecks ?? new List<string>();
            result.RequiredBeforeFreeze = required ?? new List<string>();
            result.ProjectInputs = projectInputs ?? new List<ShadingProjectInput>();
            result.Targets = Targets(3);
            result.ModelName = "TestModel.sam";
            result.SiteDescription = "Test — 51.500 °N, 0.130 °W";
            result.WeatherDescription = "Synthetic";
            result.SuppliedCount = rows.Count;
            result.RankedCount = rows.Count(x => x.Rank >= 1);
            result.VerifiedCount = rows.Count(x => x.Status != ShadingComparisonStatus.NotEvaluated && x.Status != ShadingComparisonStatus.Incomparable);
            result.IncomparableCount = rows.Count(x => x.Status == ShadingComparisonStatus.Incomparable);
            result.NotEvaluatedCount = rows.Count(x => x.Status == ShadingComparisonStatus.NotEvaluated);
            result.NotRankableCount = rows.Count(x => x.Status == ShadingComparisonStatus.NotRankable);
            result.Incomplete = result.SuppliedCount != result.RankedCount;

            ShadingComparisonRow top = rows.FirstOrDefault(x => x.TopRanked);
            if (top != null)
            {
                result.Outcome = top.Status == ShadingComparisonStatus.NoShadeBaseline
                    ? ShadingComparisonOutcome.NoShadeTopRanked
                    : ShadingComparisonOutcome.TopRanked;
            }
            else
            {
                result.Outcome = ShadingComparisonOutcome.NoComparableOptions;
            }

            return result;
        }

        private static ShadingAnalysisHours Hours(int timeline = 8760, int evaluated = 8760, int missing = 0, int sunUp = 4400, int facade = 1900, int beam = 1200, int unwanted = 600, int wanted = 200)
        {
            return new ShadingAnalysisHours(timeline, evaluated, missing, sunUp, facade, beam, 40.0, unwanted, wanted, beam - unwanted - wanted, new Dictionary<Guid, int>());
        }

        /// <summary>Ranks the rows with the production rule and marks the top row, then builds a result.</summary>
        private static ShadingComparisonResult Ranked(List<ShadingComparisonRow> rows, ShadingObjective objective)
        {
            SolarCreate.RankRows(rows, objective);
            if (rows.Count > 0 && rows[0].Rank >= 1)
            {
                rows[0].TopRanked = true;
                foreach (ShadingComparisonRow row in rows)
                {
                    if (row.Rank >= 1)
                    {
                        row.ScoreDeltaToTopRanked = rows[0].ObjectiveScore - row.ObjectiveScore;
                    }
                }

                ShadingComparisonRow noShade = rows.FirstOrDefault(x => x.Status == ShadingComparisonStatus.NoShadeBaseline);
                foreach (ShadingComparisonRow row in rows)
                {
                    if (row.Rank >= 1)
                    {
                        // Positive when the option beats the baseline (the production convention).
                        row.ScoreDeltaToNoShade = noShade == null ? double.NaN : row.ObjectiveScore - noShade.ObjectiveScore;
                    }
                }
            }

            return Result(rows, objective);
        }

        // ---------------------------------------------------------------- ranking ----

        [Fact]
        public void Higher_Verified_Score_Wins()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            Dictionary<Guid, IShadingTypology> devices = targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(0.5));

            ShadingComparisonRow low = Row(Scheme("Overhang", targets, devices), targets, objective, 100, 60, 20, 40, 30, 5, 1.5, true);
            ShadingComparisonRow high = Row(Scheme("BetterOverhang", targets, devices), targets, objective, 100, 60, 20, 50, 40, 5, 1.5, true);

            ShadingComparisonResult result = Ranked(new List<ShadingComparisonRow> { low, high }, objective);

            Assert.Equal(2, result.RankedCount);
            Assert.Equal("BetterOverhang", result.Rows[0].OptionName);
            Assert.True(result.Rows[0].TopRanked);
            Assert.Equal(1, result.Rows[0].Rank);
            Assert.Equal(2, result.Rows[1].Rank);
        }

        [Fact]
        public void A_Scheme_Scoring_Below_Zero_Loses_To_No_Shade()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            Dictionary<Guid, IShadingTypology> devices = targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(1.0));

            // Benefit 2, harm 5, cost 30: score = 2 - 5 - 3 = -6 < 0.
            ShadingComparisonRow bad = Row(Scheme("BadOverhang", targets, devices), targets, objective, 100, 60, 20, 7, 2, 5, 3.0, true);
            ShadingComparisonRow noShade = NoShadeRow("No Shade", targets);

            ShadingComparisonResult result = Ranked(new List<ShadingComparisonRow> { bad, noShade }, objective);

            Assert.Equal("No Shade", result.Rows[0].OptionName);
            Assert.True(result.Rows[0].TopRanked);
            Assert.Equal(ShadingComparisonStatus.NoShadeBaseline, result.Rows[0].Status);
            Assert.Equal(2, result.Rows[1].Rank);
        }

        [Fact]
        public void A_NaN_Score_Is_NotRankable_And_Never_Wins()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            Dictionary<Guid, IShadingTypology> devices = targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(0.5));

            ShadingComparisonRow nan = Row(Scheme("NaN", targets, devices), targets, objective, 100, 60, 20, 40, 30, 5, 1.5, true);
            nan.ObjectiveScore = double.NaN;
            nan.Status = ShadingComparisonStatus.NotRankable; // NaN is set after the builder ran its rankability check

            ShadingComparisonRow good = Row(Scheme("Good", targets, devices), targets, objective, 100, 60, 20, 40, 30, 5, 1.5, true);

            ShadingComparisonResult result = Ranked(new List<ShadingComparisonRow> { nan, good }, objective);

            Assert.Equal(1, result.RankedCount);
            Assert.Equal("Good", result.Rows[0].OptionName);
            ShadingComparisonRow nanRow = result.Rows.First(x => x.OptionName == "NaN");
            Assert.Equal(-1, nanRow.Rank);
            Assert.Equal(ShadingComparisonStatus.NotRankable, nanRow.Status);
        }

        [Fact]
        public void NotEvaluated_Never_Wins_And_Stays_Visible()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            Dictionary<Guid, IShadingTypology> devices = targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(0.5));

            ShadingComparisonRow fault = Row(Scheme("Broken", targets, devices), targets, objective, 100, 60, 20, 40, 30, 5, 1.5, true, status: ShadingComparisonStatus.NotEvaluated);
            fault.ComparabilityReason = "verification failed on aperture x";
            ShadingComparisonRow good = Row(Scheme("Good", targets, devices), targets, objective, 100, 60, 20, 40, 30, 5, 1.5, true);

            ShadingComparisonResult result = Ranked(new List<ShadingComparisonRow> { fault, good }, objective);

            Assert.Equal("Good", result.Rows[0].OptionName);
            Assert.True(result.Incomplete);
            string report = result.MarkdownReport(null);
            Assert.Contains("Broken", report);
            Assert.Contains("NOT EVALUATED", report);
        }

        [Fact]
        public void Unknown_Material_With_Positive_Mu_Is_NotRankable_With_The_Reason()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            Dictionary<Guid, IShadingTypology> devices = targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(0.5));

            ShadingComparisonRow unknown = Row(Scheme("Unknown", targets, devices), targets, objective, 100, 60, 20, 40, 30, 5, double.NaN, false);
            ShadingComparisonRow good = Row(Scheme("Good", targets, devices), targets, objective, 100, 60, 20, 40, 30, 5, 1.5, true);

            ShadingComparisonResult result = Ranked(new List<ShadingComparisonRow> { unknown, good }, objective);
            ShadingComparisonRow unknownRow = result.Rows.First(x => x.OptionName == "Unknown");
            Assert.Equal(ShadingComparisonStatus.NotRankable, unknownRow.Status);

            // With mu = 0 the energy score still ranks - built under the zero-mu objective so the
            // rankability check runs with it.
            ShadingComparisonRow unknownZeroMu = Row(Scheme("Unknown", targets, devices), targets, new ShadingObjective(1.0, 0.0), 100, 60, 20, 40, 30, 5, double.NaN, false);
            ShadingComparisonRow goodZeroMu = Row(Scheme("Good", targets, devices), targets, new ShadingObjective(1.0, 0.0), 100, 60, 20, 40, 30, 5, 1.5, true);
            ShadingComparisonResult zeroMu = Ranked(new List<ShadingComparisonRow> { unknownZeroMu, goodZeroMu }, new ShadingObjective(1.0, 0.0));
            Assert.Equal(2, zeroMu.RankedCount);
        }

        [Fact]
        public void A_Deterministic_Tie_Resolves_At_The_Documented_Level_Regardless_Of_Input_Order()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            // mu = 0: the score excludes cost entirely, so two schemes differing only in material
            // tie at level 1 and are separated at level 2 (area).
            ShadingObjective objective = new ShadingObjective(1.0, 0.0);

            // Identical scores; differ only on material area (level 2). X has less material.
            ShadingComparisonRow x = Row(Scheme("Lean", targets, targets.ToDictionary(t => t.ApertureGuid, t => (IShadingTypology)new Overhang(0.5))), targets, objective, 100, 60, 20, 40, 30, 5, 1.0, true);
            ShadingComparisonRow y = Row(Scheme("Full", targets, targets.ToDictionary(t => t.ApertureGuid, t => (IShadingTypology)new Overhang(1.0))), targets, objective, 100, 60, 20, 40, 30, 5, 2.0, true);

            ShadingComparisonResult forward = Ranked(new List<ShadingComparisonRow> { x, y }, objective);
            ShadingComparisonResult backward = Ranked(new List<ShadingComparisonRow> { y, x }, objective);

            Assert.Equal("Lean", forward.Rows[0].OptionName);
            Assert.Equal("Lean", backward.Rows[0].OptionName);
            Assert.Equal(2, forward.Rows[0].TieBreakLevel);
        }

        [Fact]
        public void Tie_Break_Level_5_Puts_The_Baseline_Before_A_Searched_And_Lost_Family()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);

            // A family whose every aperture returned RecommendsNoShading: physically identical to the
            // baseline - same zero score, area, harm and count - distinguishable only by level 5.
            ShadingScheme familyScheme = new ShadingScheme("EggCrate (all no shade)", "RationaliseShading", targets.Select(x => x.ApertureGuid), new List<Guid> { PanelGuid },
                targets.Select(t => new ShadingDevice(t.ApertureGuid, (IShadingTypology)new NoShading())),
                new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.NoShading, new List<string>(), null, new List<string>());
            ShadingComparisonRow family = Row(familyScheme, targets, objective, 100, 60, 20, 0, 0, 0, 0.0, true, status: ShadingComparisonStatus.Ranked);
            ShadingComparisonRow noShade = NoShadeRow("No Shade", targets);

            // The two rows are indistinguishable through levels 1-4: the deciding level is 5.
            Assert.Equal(5, SolarCreate.DecidingLevel(family, noShade));

            // The baseline ranks first in BOTH input orders, whatever the two SchemeGuid values.
            ShadingComparisonResult forward = Ranked(new List<ShadingComparisonRow> { family, noShade }, objective);
            ShadingComparisonResult backward = Ranked(new List<ShadingComparisonRow> { noShade, family }, objective);

            Assert.Equal("No Shade", forward.Rows[0].OptionName);
            Assert.Equal("No Shade", backward.Rows[0].OptionName);
            Assert.Equal(5, forward.Rows[0].TieBreakLevel);
            Assert.Equal(2, forward.Rows[1].Rank);
        }

        [Fact]
        public void Sorting_Never_Throws_With_NaN_And_Infinities()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);

            ShadingComparisonRow nan = Row(Scheme("NaN", targets, targets.ToDictionary(t => t.ApertureGuid, t => (IShadingTypology)new Overhang(0.5))), targets, objective, 100, 60, 20, 40, 30, 5, 1.5, true);
            nan.ObjectiveScore = double.NaN;
            ShadingComparisonRow posInf = Row(Scheme("PosInf", targets, targets.ToDictionary(t => t.ApertureGuid, t => (IShadingTypology)new Overhang(0.5))), targets, objective, 100, 60, 20, 40, 30, 5, 1.5, true);
            posInf.ObjectiveScore = double.PositiveInfinity;
            ShadingComparisonRow negInf = Row(Scheme("NegInf", targets, targets.ToDictionary(t => t.ApertureGuid, t => (IShadingTypology)new Overhang(0.5))), targets, objective, 100, 60, 20, 40, 30, 5, 1.5, true);
            negInf.ObjectiveScore = double.NegativeInfinity;
            ShadingComparisonRow good = Row(Scheme("Good", targets, targets.ToDictionary(t => t.ApertureGuid, t => (IShadingTypology)new Overhang(0.5))), targets, objective, 100, 60, 20, 40, 30, 5, 1.5, true);

            ShadingComparisonResult result = Ranked(new List<ShadingComparisonRow> { nan, posInf, negInf, good }, objective);
            Assert.NotNull(result.Rows);
        }

        // ------------------------------------------------- recommendation statuses ----

        [Fact]
        public void Recommendation_Status_Assignment_Table()
        {
            Assert.Equal(ShadingRecommendationStatus.NoDecision, SolarCreate.DetermineRecommendationStatus(true, false, false, 0));
            Assert.Equal(ShadingRecommendationStatus.Indeterminate, SolarCreate.DetermineRecommendationStatus(false, true, false, 5));
            Assert.Equal(ShadingRecommendationStatus.NoShadingRecommended, SolarCreate.DetermineRecommendationStatus(false, false, true, 5));
            Assert.Equal(ShadingRecommendationStatus.Provisional, SolarCreate.DetermineRecommendationStatus(false, false, false, 1));
            Assert.Equal(ShadingRecommendationStatus.Ready, SolarCreate.DetermineRecommendationStatus(false, false, false, 0));
        }

        // ------------------------------------------------------------ robustness ----

        [Fact]
        public void Break_Even_Substituted_Back_Reproduces_The_Tie()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            Dictionary<Guid, IShadingTypology> devices = targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(0.5));

            // Challenger has more benefit but also more harm and more material: both break-evens are
            // reachable and positive.
            ShadingComparisonRow top = Row(Scheme("Top", targets, devices), targets, objective, 100, 60, 20, 50, 40, 5, 2.0, true);
            ShadingComparisonRow challenger = Row(Scheme("Challenger", targets, devices), targets, objective, 100, 60, 20, 58, 50, 8, 2.5, true);

            BreakEvenResult beLambda = SolarQuery.BreakEvenWantedSolarPenalty(top, challenger, 0.1);
            Assert.False(beLambda.Never);
            Assert.False(beLambda.NotReachable);

            // Score_s = Score_c at the break-even lambda.
            double scoreTop = top.Benefit - beLambda.Value * top.Harm - 0.1 * top.Cost;
            double scoreChallenger = challenger.Benefit - beLambda.Value * challenger.Harm - 0.1 * challenger.Cost;
            Assert.Equal(scoreTop, scoreChallenger, 9);

            BreakEvenResult beMu = SolarQuery.BreakEvenMaterialPenalty(top, challenger, 1.0);
            Assert.False(beMu.Never);
            Assert.False(beMu.NotReachable);
            scoreTop = top.Benefit - 1.0 * top.Harm - beMu.Value * top.Cost;
            scoreChallenger = challenger.Benefit - 1.0 * challenger.Harm - beMu.Value * challenger.Cost;
            Assert.Equal(scoreTop, scoreChallenger, 9);
        }

        [Fact]
        public void Break_Even_Special_States()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            Dictionary<Guid, IShadingTypology> devices = targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(0.5));

            ShadingComparisonRow top = Row(Scheme("Top", targets, devices), targets, objective, 100, 60, 20, 50, 40, 5, 2.0, true);

            // Equal harm: lambda can never flip the pair.
            ShadingComparisonRow sameHarm = Row(Scheme("SameHarm", targets, devices), targets, objective, 100, 60, 20, 40, 30, 5, 1.0, true);
            Assert.True(SolarQuery.BreakEvenWantedSolarPenalty(top, sameHarm, 0.1).Never);

            // Equal cost: mu can never flip the pair.
            ShadingComparisonRow sameCost = Row(Scheme("SameCost", targets, devices), targets, objective, 100, 60, 20, 40, 30, 8, 2.0, true);
            Assert.True(SolarQuery.BreakEvenMaterialPenalty(top, sameCost, 1.0).Never);

            // A negative break-even is outside the >= 0 domain: the challenger costs more material
            // and blocks no more unwanted solar.
            ShadingComparisonRow negative = Row(Scheme("Negative", targets, devices), targets, objective, 100, 60, 20, 48, 40, 8, 3.0, true);
            BreakEvenResult beLambda = SolarQuery.BreakEvenWantedSolarPenalty(top, negative, 0.1);
            Assert.False(beLambda.Never);
            Assert.True(beLambda.NotReachable);
        }

        [Fact]
        public void Verdict_Thresholds_Map_Exactly()
        {
            Assert.Equal("SENSITIVE", SolarQuery.RobustnessVerdict(new BreakEvenResult(1.24, false, false), 1.0, double.NaN, double.NaN));
            Assert.Equal("MARGINAL", SolarQuery.RobustnessVerdict(new BreakEvenResult(1.25, false, false), 1.0, double.NaN, double.NaN));
            Assert.Equal("MARGINAL", SolarQuery.RobustnessVerdict(new BreakEvenResult(1.99, false, false), 1.0, double.NaN, double.NaN));
            Assert.Equal("STABLE", SolarQuery.RobustnessVerdict(new BreakEvenResult(2.0, false, false), 1.0, double.NaN, double.NaN));
            Assert.Equal("STABLE", SolarQuery.RobustnessVerdict(new BreakEvenResult(double.NaN, true, false), 1.0, double.NaN, double.NaN));

            // A lambda break-even inside the recommended working range is MARGINAL despite ratio >= 1.
            Assert.Equal("MARGINAL", SolarQuery.RobustnessVerdict(new BreakEvenResult(1.9, false, false), 1.0, 0.5, 2.0));
        }

        [Fact]
        public void Axis_Diagram_Is_Byte_Exact_For_A_Known_Case()
        {
            List<string> lines = SolarQuery.RobustnessAxis(
                "lambda  (wanted-solar penalty)", 1.0,
                new List<double> { 2.632, 5.612 }, new List<double> { double.NaN }, new List<double> { 14.007 },
                9.499, 10.0, out List<string> notes);

            Assert.Equal("lambda  (wanted-solar penalty)  domain 0 - 10", lines[0]);
            // Ticks at 0, 2, 4, 6, 8, 10, right-aligned to the tick columns.
            Assert.Equal("0           2           4           6           8          10", lines[1]);
            Assert.Equal("|           |           |           |           |           |", lines[2]);
            Assert.Equal("======o=========X-----------------X----------------------!---", lines[3]);
            Assert.Equal("      1.0 now   2.632             5.612                  9.499", lines[4]);
            Assert.Single(notes);
            Assert.Contains("14.007", notes[0]);
            Assert.Contains("never", notes[0]);
        }

        [Fact]
        public void Axis_Degenerate_Cases_Render()
        {
            // No rankable challenger: only o and the No Shade !.
            List<string> lines = SolarQuery.RobustnessAxis("mu  (material penalty)", 0.1, new List<double>(), new List<double>(), new List<double>(), 0.256, 0.4, out List<string> notes);
            Assert.Contains(lines, x => x.Contains('o'));
            Assert.Contains(lines, x => x.Contains('!'));
            Assert.Contains(lines, x => x.All(c => c != 'X'));

            // Every break-even unreachable: unbroken = span.
            List<string> flat = SolarQuery.RobustnessAxis("mu  (material penalty)", 0.1, new List<double>(), new List<double>(), new List<double>(), double.NaN, 0.4, out List<string> flatNotes);
            Assert.True(flat[3].All(c => c == '=' || c == 'o'));
        }

        [Fact]
        public void NiceCeiling_Follows_The_Ladder()
        {
            Assert.Equal(1.0, SolarQuery.NiceCeiling(0.9));
            Assert.Equal(1.5, SolarQuery.NiceCeiling(1.2));
            Assert.Equal(2.0, SolarQuery.NiceCeiling(1.6));
            Assert.Equal(2.5, SolarQuery.NiceCeiling(2.1));
            Assert.Equal(3.0, SolarQuery.NiceCeiling(2.6));
            Assert.Equal(4.0, SolarQuery.NiceCeiling(3.1));
            Assert.Equal(5.0, SolarQuery.NiceCeiling(4.1));
            Assert.Equal(6.0, SolarQuery.NiceCeiling(5.1));
            Assert.Equal(8.0, SolarQuery.NiceCeiling(6.1));
            Assert.Equal(10.0, SolarQuery.NiceCeiling(8.1));
            Assert.Equal(15.0, SolarQuery.NiceCeiling(12.0));
        }

        // ------------------------------------------------------------ report ----

        private static (List<ShadingComparisonRow> rows, ShadingComparisonResult result) TwoOptionComparison(bool retractableLeader = false, bool budgetExhausted = false)
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);

            ShadingComparisonRow overhang = Row(
                Scheme("Overhang", targets, targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(0.5))),
                targets, objective, 148.970, 53.150, 3.532, 47.040, 31.420, 1.980, 2.037, true,
                designScore: 21.187, designBenefit: 30.0, designHarm: 1.9, designCost: 6.8);

            ShadingComparisonRow leader;
            if (retractableLeader)
            {
                leader = Row(
                    Scheme("Grouped Dakar Retractable Awning", targets, targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new RetractableAwning(2.1, 15.0))),
                    targets, objective, 148.970, 53.150, 3.532, 117.040, 52.300, 3.500, 5.790, true);
            }
            else
            {
                leader = Row(
                    Scheme("VerticalFins", targets, targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new VerticalFins(0.4, 2))),
                    targets, objective, 148.970, 53.150, 3.532, 70.040, 40.520, 2.370, 3.114, true,
                    termination: budgetExhausted ? ShadingOptimisationTermination.EvaluationBudgetExhausted : ShadingOptimisationTermination.StepBelowGranularity);
            }

            ShadingComparisonRow noShade = NoShadeRow("No Shade", targets);

            ShadingComparisonResult result = Ranked(new List<ShadingComparisonRow> { noShade, overhang, leader }, objective);
            return (new List<ShadingComparisonRow> { overhang, leader, noShade }, result);
        }

        [Fact]
        public void Markdown_And_Csv_Are_Byte_Identical_Across_Runs_And_Shuffled_Input()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult result) = TwoOptionComparison();

            string markdownA = result.MarkdownReport(null);
            string csvA = result.CsvReport();
            string markdownB = result.MarkdownReport(null);
            string csvB = result.CsvReport();

            Assert.Equal(markdownA, markdownB);
            Assert.Equal(csvA, csvB);

            // Shuffled construction order.
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            ShadingComparisonRow overhang = Row(Scheme("Overhang", targets, targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(0.5))), targets, objective, 148.970, 53.150, 3.532, 47.040, 31.420, 1.980, 2.037, true, designScore: 21.187, designBenefit: 30.0, designHarm: 1.9, designCost: 6.8);
            ShadingComparisonRow leader = Row(Scheme("VerticalFins", targets, targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new VerticalFins(0.4, 2))), targets, objective, 148.970, 53.150, 3.532, 70.040, 40.520, 2.370, 3.114, true);
            ShadingComparisonRow noShade = NoShadeRow("No Shade", targets);
            ShadingComparisonResult shuffled = Ranked(new List<ShadingComparisonRow> { leader, noShade, overhang }, objective);

            Assert.Equal(markdownA, shuffled.MarkdownReport(null));
            Assert.Equal(csvA, shuffled.CsvReport());
        }

        [Fact]
        public void Markdown_And_Csv_Are_Byte_Identical_Under_A_Foreign_Culture()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult result) = TwoOptionComparison();
            string invariantMarkdown = result.MarkdownReport(null);
            string invariantCsv = result.CsvReport();

            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
                Assert.Equal(invariantMarkdown, result.MarkdownReport(null));
                Assert.Equal(invariantCsv, result.CsvReport());
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void The_Report_Names_The_Analytical_Leader_Not_A_Preselected_Winner()
        {
            // The no-hardcoded-winner rule: whatever rows say, the report names rank 1. An overhang
            // outscores the awning here.
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            ShadingComparisonRow awning = Row(Scheme("Grouped Dakar Retractable Awning", targets, targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new RetractableAwning(2.1, 15.0))), targets, objective, 148.970, 53.150, 3.532, 117.040, 52.300, 3.500, 5.790, true);
            ShadingComparisonRow overhang = Row(Scheme("Overhang", targets, targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(0.5))), targets, objective, 148.970, 53.150, 3.532, 140.0, 70.0, 4.0, 2.0, true);
            ShadingComparisonRow noShade = NoShadeRow("No Shade", targets);

            ShadingComparisonResult result = Ranked(new List<ShadingComparisonRow> { awning, overhang, noShade }, objective);

            Assert.Equal("Overhang", result.Rows[0].OptionName);
            string report = result.MarkdownReport(null);
            Assert.Contains("Overhang ranks 1 of 3", report);
            Assert.Contains("TOP RANKED", report);
            // The comparison never marks anything SELECTED.
            Assert.DoesNotContain("SELECTED", report);
        }

        [Fact]
        public void Engineering_Selection_Is_Not_Recorded_By_Default()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult result) = TwoOptionComparison(retractableLeader: true);
            string report = result.MarkdownReport(null);
            Assert.Contains("ENGINEERING DECISION", report);
            Assert.Contains("Not yet recorded.", report);
        }

        [Fact]
        public void Operating_Assumption_Renders_Only_For_A_Retractable_Leader()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult withAwning) = TwoOptionComparison(retractableLeader: true);
            (List<ShadingComparisonRow> _, ShadingComparisonResult without) = TwoOptionComparison(retractableLeader: false);

            string awningReport = withAwning.MarkdownReport(null);
            string finsReport = without.MarkdownReport(null);

            Assert.Contains("OPERATING ASSUMPTION", awningReport);
            Assert.Contains("modelled fully deployed", awningReport);
            Assert.DoesNotContain("OPERATING ASSUMPTION", finsReport);
        }

        [Fact]
        public void Budget_Exhausted_Scheme_Is_Marked_In_The_Ranked_Table()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult result) = TwoOptionComparison(budgetExhausted: true);
            string report = result.MarkdownReport(null);
            Assert.Contains("OK (provisional)", report);
        }

        [Fact]
        public void Audit_Line_Counts_Sum()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult result) = TwoOptionComparison();
            string report = result.MarkdownReport(null);
            Assert.Contains("3 schemes supplied · 3 verified and comparable · 3 ranked", report);
        }

        [Fact]
        public void Energy_Balance_Closes_And_Every_Term_Renders_Even_When_Zero()
        {
            (List<ShadingComparisonRow> rows, ShadingComparisonResult result) = TwoOptionComparison();
            ShadingComparisonRow leader = result.Rows[0];

            string report = result.MarkdownReport(null);
            // The four columns: direct, unwanted, wanted, neutral.
            double admitted = leader.BaselineDirectSolar;
            double intercepted = leader.DirectSolarIntercepted;
            Assert.Contains((admitted - intercepted).ToString("0.000", CultureInfo.InvariantCulture), report);
            Assert.Contains("Stopped by something else", report);
        }

        [Fact]
        public void Non_Score_Led_Summary_Contains_No_Score()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult result) = TwoOptionComparison();
            string report = result.MarkdownReport(null);
            int section = report.IndexOf("What each option physically does", StringComparison.Ordinal);
            int fenceStart = report.IndexOf("```", section, StringComparison.Ordinal);
            int fenceEnd = report.IndexOf("```", fenceStart + 3, StringComparison.Ordinal);
            string block = report.Substring(fenceStart, fenceEnd - fenceStart);
            Assert.DoesNotContain("score", block, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Cross_Shading_Decomposition_Reconciles_With_All_Three_Terms()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            ShadingComparisonRow overhang = Row(
                Scheme("Overhang", targets, targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(0.5))),
                targets, objective, 148.970, 53.150, 3.532, 47.040, 31.420, 1.980, 2.037, true,
                designScore: 21.187, designBenefit: 29.8, designHarm: 1.910, designCost: 6.803);
            ShadingComparisonRow noShade = NoShadeRow("No Shade", targets);

            ShadingComparisonResult result = Ranked(new List<ShadingComparisonRow> { overhang, noShade }, objective);
            string report = result.MarkdownReport(null);

            Assert.Contains("ΔBenefit", report);
            Assert.Contains("ΔHarm", report);
            Assert.Contains("ΔCost", report);

            // Reconciliation by construction: dScore = dB - lambda x dH - mu x dC.
            double dScore = overhang.ObjectiveScore - (overhang.VerifiedResult.Scheme.DesignTimeBenefit - objective.WantedSolarPenalty * overhang.VerifiedResult.Scheme.DesignTimeHarm - objective.MaterialPenalty * overhang.VerifiedResult.Scheme.DesignTimeCost);
            double dB = overhang.Benefit - overhang.VerifiedResult.Scheme.DesignTimeBenefit;
            double dH = overhang.Harm - overhang.VerifiedResult.Scheme.DesignTimeHarm;
            double dC = overhang.Cost - overhang.VerifiedResult.Scheme.DesignTimeCost;
            Assert.Equal(dScore, dB - objective.WantedSolarPenalty * dH - objective.MaterialPenalty * dC, 9);
        }

        [Fact]
        public void Cross_Shading_Section_Renders_The_Agreement_Sentence_When_Nothing_Differs()
        {
            // Design-time components equal the verified ones exactly: the section reports agreement.
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            double cost = 2.037 / 6.0 * 148.970;
            ShadingComparisonRow overhang = Row(
                Scheme("Overhang", targets, targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(0.5))),
                targets, objective, 148.970, 53.150, 3.532, 47.040, 31.420, 1.980, 2.037, true,
                designScore: 31.420 - 1.0 * 1.980 - 0.1 * cost, designBenefit: 31.420, designHarm: 1.980, designCost: cost);
            ShadingComparisonRow noShade = NoShadeRow("No Shade", targets);
            ShadingComparisonResult result = Ranked(new List<ShadingComparisonRow> { overhang, noShade }, objective);

            string report = result.MarkdownReport(null);
            Assert.Contains("the devices do not interact", report);
        }

        [Fact]
        public void Baseline_Anchoring_Renders_Anchored_And_Unanchored()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult anchored) = TwoOptionComparison();
            Assert.Contains("ANCHORED on the No Shade scheme", anchored.MarkdownReport(null));

            // Remove the No Shade row: unanchored.
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            ShadingComparisonRow overhang = Row(Scheme("Overhang", targets, targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(0.5))), targets, objective, 148.970, 53.150, 3.532, 47.040, 31.420, 1.980, 2.037, true, designScore: 21.187, designBenefit: 30.0, designHarm: 1.9, designCost: 6.8);
            ShadingComparisonResult unanchored = Ranked(new List<ShadingComparisonRow> { overhang }, objective);
            unanchored.BaselineAnchored = false;
            string unanchoredReport = unanchored.MarkdownReport(null);
            Assert.Contains("UNANCHORED", unanchoredReport);
        }

        [Fact]
        public void Every_Supplied_Row_Is_Shown_Without_Collapsing()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            List<ShadingComparisonRow> rows = new List<ShadingComparisonRow>();
            for (int i = 1; i <= 8; i++)
            {
                rows.Add(Row(Scheme("Option" + i, targets, targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(0.5))), targets, objective, 148.970, 53.150, 3.532, 47.040 - i, 31.420 - i, 1.980, 2.037, true));
            }

            ShadingComparisonResult result = Ranked(rows, objective);
            string report = result.MarkdownReport(null);
            for (int i = 1; i <= 8; i++)
            {
                Assert.Contains("Option" + i, report);
            }
        }

        [Fact]
        public void Not_Covered_Block_Is_Present_Verbatim()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult result) = TwoOptionComparison();
            string report = result.MarkdownReport(null);
            Assert.Contains("PHYSICS NOT MODELLED", report);
            Assert.Contains("DISCRETISATION", report);
            Assert.Contains("SCOPE", report);
            Assert.Contains("The material term is a proxy for cost expressed in kWh, not a price.", report);
            Assert.Contains("This report does NOT support:", report);
        }

        [Fact]
        public void Provenance_Version_Is_In_The_Report_And_Absent_From_The_Json()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult result) = TwoOptionComparison();
            string report = result.MarkdownReport(null);
            Assert.Contains("SAM_SolarCalculator", report);

            string json = result.ToJsonObject().ToJsonString();
            Assert.DoesNotContain("SAM_SolarCalculator", json);
        }

        [Fact]
        public void Glossary_Is_Inside_ReportMarkdown_And_Mirrors_ReportGlossary()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult result) = TwoOptionComparison();
            string report = result.MarkdownReport(null);
            string glossary = SolarQuery.ShadingGlossaryMarkdown();

            Assert.Contains("Variables explained", report);
            Assert.Contains("Wanted solar retained", report);
            Assert.Contains(glossary, report);
        }

        [Fact]
        public void Glossary_Completeness_Guard_Fails_On_A_Missing_Entry()
        {
            Dictionary<string, GlossaryEntry> entries = SolarQuery.ShadingReportGlossary();

            foreach (PropertyInfo property in typeof(ShadingComparisonRow).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.True(entries.ContainsKey(property.Name),
                    $"ShadingComparisonRow.{property.Name} has no glossary entry — add one or the glossary rots.");
            }

            foreach (string member in Enum.GetNames(typeof(ShadingComparisonStatus)))
            {
                Assert.True(entries.ContainsKey(member), $"ShadingComparisonStatus.{member} has no glossary entry.");
            }

            foreach (string member in Enum.GetNames(typeof(ShadingOptimisationTermination)))
            {
                Assert.True(entries.ContainsKey(member), $"ShadingOptimisationTermination.{member} has no glossary entry.");
            }

            foreach (string member in Enum.GetNames(typeof(ShadingRecommendationStatus)))
            {
                Assert.True(entries.ContainsKey(member), $"ShadingRecommendationStatus.{member} has no glossary entry.");
            }

            foreach (string member in Enum.GetNames(typeof(ShadingSelectionAlignment)))
            {
                Assert.True(entries.ContainsKey(member), $"ShadingSelectionAlignment.{member} has no glossary entry.");
            }
        }

        [Fact]
        public void Csv_Column_Order_Matches_The_Contract_And_NaN_Is_Empty()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult result) = TwoOptionComparison();
            string csv = result.CsvReport();
            string[] lines = csv.Split('\n');
            string header = lines[0];

            string[] expected = new string[]
            {
                "Rank", "TopRanked", "EngineerSelected", "SchemeGuid", "OptionName", "DesignMethod", "TypologyNames", "ProductPreset",
                "Status", "ComparabilityReason", "PhysicalDeviceCount", "ApertureCount", "ApertureGuids",
                "BaselineDirectSolar", "AdmittedUnwantedSolar", "AdmittedWantedSolar", "AdmittedNeutralSolar",
                "DirectSolarIntercepted", "UnwantedSolarIntercepted", "WantedSolarBlocked", "NeutralSolarIntercepted", "UnattributedEnergy",
                "DirectShadingEfficiency", "UnwantedSolarBlocked", "WantedSolarRetained",
                "PhysicalDeviceArea", "MaterialFraction", "MaterialAvailable",
                "Benefit", "WantedSolarPenalty", "Harm", "MaterialPenalty", "Cost", "ObjectiveScore",
                "DesignTimeScore", "DesignObjectiveMatchesComparisonObjective",
                "ScoreDeltaToTopRanked", "ScoreDeltaToNoShade", "TieBreakLevel",
                "BreakEvenWantedSolarPenalty", "BreakEvenMaterialPenalty",
                "UnwantedInterceptedPerGrossArea", "ObjectiveScorePerGrossArea",
                "DesignEvaluations", "DesignTermination", "DesignBudgetExhausted", "Warnings",
            };
            Assert.Equal(string.Join(",", expected), header.TrimEnd('\r'));

            // Every data row has the same field count as the header.
            foreach (string line in lines.Skip(1).Where(x => x.TrimEnd('\r').Length > 0))
            {
                Assert.Equal(expected.Length, line.TrimEnd('\r').Split(',').Length);
            }

            // No NaN anywhere in the CSV.
            Assert.DoesNotContain("NaN", csv);
        }

        [Fact]
        public void Score_Delta_To_No_Shade_Is_Empty_When_No_No_Shade_Row_Exists()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            ShadingComparisonRow overhang = Row(Scheme("Overhang", targets, targets.ToDictionary(x => x.ApertureGuid, x => (IShadingTypology)new Overhang(0.5))), targets, objective, 148.970, 53.150, 3.532, 47.040, 31.420, 1.980, 2.037, true, designScore: 21.187, designBenefit: 30.0, designHarm: 1.9, designCost: 6.8);

            ShadingComparisonResult result = Ranked(new List<ShadingComparisonRow> { overhang }, objective);
            Assert.True(double.IsNaN(result.Rows[0].ScoreDeltaToNoShade));

            string csv = result.CsvReport();
            string[] fields = csv.Split('\n')[1].TrimEnd('\r').Split(',');
            Assert.Equal(string.Empty, fields[Array.IndexOf("Rank,TopRanked,EngineerSelected,SchemeGuid,OptionName,DesignMethod,TypologyNames,ProductPreset,Status,ComparabilityReason,PhysicalDeviceCount,ApertureCount,ApertureGuids,BaselineDirectSolar,AdmittedUnwantedSolar,AdmittedWantedSolar,AdmittedNeutralSolar,DirectSolarIntercepted,UnwantedSolarIntercepted,WantedSolarBlocked,NeutralSolarIntercepted,UnattributedEnergy,DirectShadingEfficiency,UnwantedSolarBlocked,WantedSolarRetained,PhysicalDeviceArea,MaterialFraction,MaterialAvailable,Benefit,WantedSolarPenalty,Harm,MaterialPenalty,Cost,ObjectiveScore,DesignTimeScore,DesignObjectiveMatchesComparisonObjective,ScoreDeltaToTopRanked,ScoreDeltaToNoShade,TieBreakLevel,BreakEvenWantedSolarPenalty,BreakEvenMaterialPenalty,UnwantedInterceptedPerGrossArea,ObjectiveScorePerGrossArea,DesignEvaluations,DesignTermination,DesignBudgetExhausted,Warnings".Split(','), "ScoreDeltaToNoShade")]);
        }

        [Fact]
        public void Every_Ranked_Row_Reconciles_To_Within_1e_9()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult result) = TwoOptionComparison();
            foreach (ShadingComparisonRow row in result.Rows.Where(x => x.Rank >= 1))
            {
                double reconciled = row.Benefit - row.WantedSolarPenalty * row.Harm - row.MaterialPenalty * row.Cost;
                Assert.Equal(reconciled, row.ObjectiveScore, 9);
            }
        }

        [Fact]
        public void Normalised_Metrics_Are_The_Score_Over_The_Gross_Area()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult result) = TwoOptionComparison();
            double gross = result.Targets.Sum(x => x.GrossArea);
            foreach (ShadingComparisonRow row in result.Rows.Where(x => x.Rank >= 1))
            {
                Assert.Equal(row.ObjectiveScore / gross, row.ObjectiveScorePerGrossArea, 9);
            }
        }

        [Fact]
        public void Unevidenced_Sun_Angle_Step_Renders_Unavailable_And_Nothing_Interpolated()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult result) = TwoOptionComparison();
            result.ReferenceSignature = new ShadingAnalysisSignature(
                result.ReferenceSignature.ApertureGuids, result.ReferenceSignature.CellCounts,
                result.ReferenceSignature.ContextGeometryHash, result.ReferenceSignature.TargetGeometryHash,
                result.ReferenceSignature.GridSize, 3.0, result.ReferenceSignature.Year, result.ReferenceSignature.TimeShiftInMinutes,
                result.ReferenceSignature.Latitude, result.ReferenceSignature.Longitude, result.ReferenceSignature.TimeZoneOffset,
                result.ReferenceSignature.WeatherIdentityHash, result.ReferenceSignature.DesirabilityHash, result.ReferenceSignature.DesirabilityName);

            string report = result.MarkdownReport(null);
            Assert.Contains("UNAVAILABLE for 3", report);
            Assert.DoesNotContain("WELL ABOVE SUN-GROUP", report);
            Assert.DoesNotContain("COMPARABLE TO SUN-GROUP", report);
        }

        [Fact]
        public void The_Report_Never_Claims_Convergence_Or_Safety_Against_Noise()
        {
            (List<ShadingComparisonRow> _, ShadingComparisonResult result) = TwoOptionComparison();
            string report = result.MarkdownReport(null);
            Assert.DoesNotContain("safe against numerical noise", report);
            Assert.Contains("GRID CONVERGENCE: NOT CONFIRMED", report);
        }

        [Fact]
        public void Project_Inputs_List_Defaults_And_Sensitivities()
        {
            (List<ShadingComparisonRow> rows, ShadingComparisonResult result) = TwoOptionComparison();

            // mu at default, sensitive: the result carries the finding.
            result.ProjectInputs = new List<ShadingProjectInput>
            {
                new ShadingProjectInput("Wanted solar penalty lambda", "1.000", "DEFAULT", "decision-stable over 0.5 - 2.0"),
                new ShadingProjectInput("Material penalty mu", "0.100", "DEFAULT", "decision-sensitive"),
                new ShadingProjectInput("Desirability brief", "Default", "DEFAULT", "not sensitivity-tested"),
            };

            string report = result.MarkdownReport(null);
            Assert.Contains("PROJECT INPUTS REQUIRING CONFIRMATION", report);
            Assert.Contains("decision-sensitive", report);
            Assert.Contains("DEFAULT", report);
        }

        [Fact]
        public void An_Explicitly_Supplied_Default_Lambda_And_Mu_Are_Project_Not_Default()
        {
            // lambda = 1.0 and mu = 0.1 are the library defaults, but an engineer who explicitly
            // wired them confirmed them as project values. The provenance rule keys off the supplied
            // flag, never the value, so they must be PROJECT when supplied and DEFAULT when not.
            List<ApertureSolarTarget> targets = Targets(3);

            ShadingComparisonRow top = new ShadingComparisonRow();
            top.Benefit = 60.0;
            top.Harm = 30.0;
            top.Cost = 10.0;

            ShadingComparisonRow challenger = new ShadingComparisonRow();
            challenger.Benefit = 62.0;
            challenger.Harm = 30.5;
            challenger.Cost = 20.0;

            List<ShadingComparisonRow> rankable = new List<ShadingComparisonRow> { top, challenger };

            List<ShadingProjectInput> supplied = SolarCreate.ProjectInputs(1.0, 0.1, 0.5, targets, true, true, false, rankable, top, null);
            ShadingProjectInput lambda = supplied.Single(x => x.Name == "Wanted solar penalty lambda");
            ShadingProjectInput mu = supplied.Single(x => x.Name == "Material penalty mu");
            Assert.Equal("PROJECT", lambda.Source);
            Assert.Equal("decision-sensitive", lambda.Classification);
            Assert.Equal("PROJECT", mu.Source);

            List<ShadingProjectInput> defaulted = SolarCreate.ProjectInputs(1.0, 0.1, 0.5, targets, false, false, false, rankable, top, null);
            ShadingProjectInput defaultLambda = defaulted.Single(x => x.Name == "Wanted solar penalty lambda");
            ShadingProjectInput defaultMu = defaulted.Single(x => x.Name == "Material penalty mu");
            Assert.Equal("DEFAULT", defaultLambda.Source);
            Assert.Equal("DEFAULT", defaultMu.Source);
        }

        [Fact]
        public void Score_Delta_To_No_Shade_Is_Positive_Zero_And_Negative_By_Sign()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);

            // 30 - 1*5 - 0.1*10 = 24 above the baseline; 2 - 1*5 - 0.1*1 = -3.1 below it.
            ShadingComparisonRow above = Row(Scheme("Above", targets, targets.ToDictionary(t => t.ApertureGuid, t => (IShadingTypology)new Overhang(0.5))), targets, objective, 100, 60, 20, 35, 30, 5, 0.6, true);
            ShadingComparisonRow below = Row(Scheme("Below", targets, targets.ToDictionary(t => t.ApertureGuid, t => (IShadingTypology)new Overhang(0.5))), targets, objective, 100, 60, 20, 7, 2, 5, 0.06, true);
            ShadingComparisonRow noShade = NoShadeRow("No Shade", targets);

            ShadingComparisonResult result = Ranked(new List<ShadingComparisonRow> { below, above, noShade }, objective);

            ShadingComparisonRow aboveRow = result.Rows.First(x => x.OptionName == "Above");
            ShadingComparisonRow belowRow = result.Rows.First(x => x.OptionName == "Below");
            ShadingComparisonRow noShadeRow = result.Rows.First(x => x.OptionName == "No Shade");

            Assert.Equal(aboveRow.ObjectiveScore, aboveRow.ScoreDeltaToNoShade, 9);
            Assert.True(aboveRow.ScoreDeltaToNoShade > 0);
            Assert.Equal(0.0, noShadeRow.ScoreDeltaToNoShade, 12);
            Assert.True(belowRow.ScoreDeltaToNoShade < 0);
            Assert.Equal(belowRow.ObjectiveScore, belowRow.ScoreDeltaToNoShade, 9);
        }

        [Fact]
        public void The_Resolution_Block_Reports_The_Runner_Up_Margin_Not_The_Leaders_Zero_Delta()
        {
            // A known non-zero case: the Kołobrzeg numbers. The leader's own delta is 0 by
            // construction; the report must print the runner-up's 6.383 kWh.
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);

            ShadingComparisonRow leader = Row(Scheme("HorizontalLouvres", targets, targets.ToDictionary(t => t.ApertureGuid, t => (IShadingTypology)new HorizontalLouvres(0.3, 3))), targets, objective, 148.970, 53.150, 3.532, 54.911, 51.478, 3.433, 3.612, true);
            ShadingComparisonRow runnerUp = Row(Scheme("Grouped Dakar Retractable Awning", targets, targets.ToDictionary(t => t.ApertureGuid, t => (IShadingTypology)new RetractableAwning(2.1, 15.0))), targets, objective, 148.970, 53.150, 3.532, 55.751, 52.299, 3.452, 5.790, true);
            ShadingComparisonRow noShade = NoShadeRow("No Shade", targets);

            ShadingComparisonResult result = Ranked(new List<ShadingComparisonRow> { runnerUp, leader, noShade }, objective);

            // The leader really is the louvres and the margin really is non-zero.
            Assert.Equal("HorizontalLouvres", result.Rows[0].OptionName);
            Assert.Equal(0.0, result.Rows[0].ScoreDeltaToTopRanked, 12);
            Assert.True(result.Rows[1].ScoreDeltaToTopRanked > 1.0, "the fixture must carry a real margin");

            string report = result.MarkdownReport(null);
            Assert.Contains("Rank 1 to rank 2 margin", report);
            Assert.DoesNotContain("Rank 1 to rank 2 margin         0.000", report);
            Assert.Contains(" of the rank-1 score", report);

            // The margin equals the runner-up delta (and the leader's arithmetic block agrees).
            string margin = result.Rows[1].ScoreDeltaToTopRanked.ToString("0.000", CultureInfo.InvariantCulture);
            Assert.Contains("margin         " + margin + " kWh", report);
        }

        [Fact]
        public void The_Quantisation_Classification_Thresholds_Render()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            ShadingComparisonRow noShade = NoShadeRow("No Shade", targets);

            ShadingComparisonResult Build(double leaderScore, double margin)
            {
                ShadingComparisonRow leader = Row(Scheme("Leader", targets, targets.ToDictionary(t => t.ApertureGuid, t => (IShadingTypology)new Overhang(0.5))), targets, objective, 148.970, 53.150, 3.532, 40, leaderScore, 5, 1.5, true);
                ShadingComparisonRow runnerUp = Row(Scheme("RunnerUp", targets, targets.ToDictionary(t => t.ApertureGuid, t => (IShadingTypology)new Overhang(0.5))), targets, objective, 148.970, 53.150, 3.532, 40, leaderScore - margin, 5, 1.5, true);
                ShadingComparisonResult result = Ranked(new List<ShadingComparisonRow> { runnerUp, leader, noShade }, objective);
                Assert.Equal(3, result.RankedCount);
                Assert.Equal("Leader", result.Rows[0].OptionName);
                return result;
            }

            double indicator = 0.0154; // the documented 2° sun-group MAE

            // 5x the indicator: WELL ABOVE.
            string wellAbove = Build(100.0, 100.0 * 5.0 * indicator).MarkdownReport(null);
            Assert.Contains("WELL ABOVE SUN-GROUP QUANTISATION INDICATOR", wellAbove);

            // 2x: COMPARABLE TO.
            string comparable = Build(100.0, 100.0 * 2.0 * indicator).MarkdownReport(null);
            Assert.Contains("COMPARABLE TO SUN-GROUP QUANTISATION INDICATOR", comparable);

            // 0.5x: WITHIN.
            string within = Build(100.0, 100.0 * 0.5 * indicator).MarkdownReport(null);
            Assert.Contains("WITHIN SUN-GROUP QUANTISATION INDICATOR", within);
        }

        [Fact]
        public void Project_Input_Provenance_Distinguishes_Default_From_Explicitly_Supplied()
        {
            List<ApertureSolarTarget> targets = Targets(3);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);

            // A top/challenger pair whose mu break-even sits exactly on 0.1: mu is decision-
            // sensitive, and the test exercises the provenance rule for BOTH provenance modes.
            ShadingComparisonRow top = Row(Scheme("Top", targets, targets.ToDictionary(t => t.ApertureGuid, t => (IShadingTypology)new Overhang(0.5))), targets, objective, 100, 60, 20, 55, 50, 5, 0.6, true);
            ShadingComparisonRow challenger = Row(Scheme("Challenger", targets, targets.ToDictionary(t => t.ApertureGuid, t => (IShadingTypology)new Overhang(0.5))), targets, objective, 100, 60, 20, 74.8, 60, 14.8, 0.72, true);

            // mu = 0.1 at its default, not supplied: DEFAULT.
            List<ShadingProjectInput> defaults = SolarCreate.ProjectInputs(
                1.0, 0.1, 0.5, targets, false, false, false,
                new List<ShadingComparisonRow> { top, challenger }, top, null);
            ShadingProjectInput muDefault = defaults.First(x => x.Name == "Material penalty mu");
            Assert.Equal("DEFAULT", muDefault.Source);
            Assert.Equal("decision-sensitive", muDefault.Classification);

            // The SAME value 0.1, explicitly supplied by the caller: PROJECT.
            List<ShadingProjectInput> supplied = SolarCreate.ProjectInputs(
                1.0, 0.1, 0.5, targets, false, true, false,
                new List<ShadingComparisonRow> { top, challenger }, top, null);
            ShadingProjectInput muSupplied = supplied.First(x => x.Name == "Material penalty mu");
            Assert.Equal("PROJECT", muSupplied.Source);
            Assert.Equal("decision-sensitive", muSupplied.Classification);
        }

        [Fact]
        public void Resolution_Devices_Below_The_Limit_Use_The_Existing_Query_Verbatim()
        {
            // The report must not invent its own classification: a device with pitch <= gridSize is
            // BelowResolutionLimit with SolarQuery.ShadingResolution's own message.
            ApertureSolarTarget target = Target(1);
            VerticalFins fins = new VerticalFins(0.4, 4); // 4 fins over 1.0 m: pitch 0.333 m < 0.5 m grid
            ShadingResolutionState state = SolarQuery.ShadingResolution(fins, target, 0.5, out string message, out double pitch);
            Assert.Equal(ShadingResolutionState.BelowResolutionLimit, state);
            Assert.NotNull(message);

            VerticalFins coarse = new VerticalFins(0.4, 2); // pitch 1.0 m: resolved
            Assert.Equal(ShadingResolutionState.Resolved, SolarQuery.ShadingResolution(coarse, target, 0.5, out string _, out double _));
        }

        [Fact]
        public void Grid_Resolution_Cap_Is_Detected_On_The_Typology_Overload()
        {
            // A 1.0 m wide aperture at a 0.5 m grid admits at most 2 fins; a 3-fin device sits on
            // the cap.
            ApertureSolarTarget target = Target(1);
            VerticalFins fins = new VerticalFins(0.4, 3);
            Assert.False(SolarQuery.GridResolutionCapReached(fins, target, 0.5, out string _));

            VerticalFins capped = new VerticalFins(0.4, 2);
            Assert.True(SolarQuery.GridResolutionCapReached(capped, target, 0.5, out string message));
            Assert.NotNull(message);
        }
    }
}
