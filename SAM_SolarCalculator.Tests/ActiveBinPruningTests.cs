// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// PR 1: active-bin pruning of Stage-9 candidate attribution.
    ///
    /// THE CONTRACT. Candidate attribution is traced only at sun bins whose Benefit/Harm
    /// contribution can be non-zero under the existing accounting, and the winning device is
    /// re-measured ONCE through the complete, unpruned attribution before the result is built. The
    /// pruning is answer-preserving: identical scores, identical search, identical winner, identical
    /// final reporting — only the per-candidate ray-tracing work changes.
    ///
    /// The gate these tests pin is subtle enough to deserve the full statement: a bin counts as
    /// active only when (unwanted != 0 || wanted != 0) AND the accounting's own skip gate
    /// (|direct| + |unwanted| + |wanted| &lt; 1e-12) does not discard it. A bare
    /// "unwanted != 0 || wanted != 0" rule would trace and score sub-gate bins the accounting
    /// skips, which changes scores. The admitted sums are NEVER pruned — pruning them would
    /// silently shrink AdmittedDirectEnergy and with it the material-cost term.
    ///
    /// The production mask lives in Create.ActiveDesirabilityBins (internal). These tests pin it
    /// through PUBLIC paths only: the rule is re-derived here from the documented contract
    /// (DocumentedActiveMask) for the cache-level equivalence, and the production implementation
    /// itself is exercised end-to-end by comparing the pruned search against the unpruned one —
    /// including a crafted desirability that probes the 1e-12 gate boundary directly.
    /// </summary>
    public class ActiveBinPruningTests
    {
        private readonly ITestOutputHelper output;

        public ActiveBinPruningTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>
        /// The active-bin rule AS DOCUMENTED, re-derived in the test rather than read from the
        /// production helper: a test-local copy of the contract means a divergence between the
        /// production implementation and the documented rule cannot hide behind both sharing one
        /// expression. The production implementation is pinned separately, end-to-end.
        /// </summary>
        private static bool[] DocumentedActiveMask(ApertureDesirability desirability, int binCount)
        {
            double[] direct = desirability.DirectEnergyPerGroup;
            double[] unwanted = desirability.UnwantedEnergyPerGroup;
            double[] wanted = desirability.WantedEnergyPerGroup;
            if (direct == null || unwanted == null || wanted == null
                || direct.Length != binCount || unwanted.Length != binCount || wanted.Length != binCount)
            {
                return null;
            }

            bool[] mask = new bool[binCount];
            for (int b = 0; b < binCount; b++)
            {
                mask[b] = (unwanted[b] != 0 || wanted[b] != 0)
                    && !(Math.Abs(direct[b]) + Math.Abs(unwanted[b]) + Math.Abs(wanted[b]) < 1e-12);
            }

            return mask;
        }

        /// <summary>
        /// A desirability over the given bin count whose first nine bins sit exactly ON the 1e-12
        /// gate boundary: the edge cases the pruning rule must get right. All other bins are zero.
        /// </summary>
        private static ApertureDesirability GateProbeDesirability(int binCount)
        {
            double[] direct = new double[binCount];
            double[] unwanted = new double[binCount];
            double[] wanted = new double[binCount];

            // Sub-gate unwanted: the accounting skips it, so the mask must not activate it.
            unwanted[0] = 5e-13;
            // Sub-gate combined sum, each part non-zero.
            direct[1] = 2e-13; unwanted[1] = 1e-13;
            // Unwanted exactly AT the gate: not skipped -> active.
            unwanted[2] = 1e-12;
            // Sub-gate unwanted carried past the gate by direct: the accounting reads it -> active.
            direct[3] = 1; unwanted[3] = 1e-13;
            // Neutral: direct only -> inactive.
            direct[4] = 1;
            // Sub-gate wanted alone: skipped.
            wanted[5] = 5e-13;
            // Wanted exactly AT the gate: active.
            wanted[6] = 1e-12;
            // Wanted sub-gate, direct past the gate: active.
            direct[7] = 1; wanted[7] = 1e-13;
            // Wanted with a unit weight: active.
            wanted[8] = 1;

            return new ApertureDesirability(Guid.NewGuid(), "GateProbe", 2018, 0.0, direct, unwanted, wanted, 0, 0);
        }

        private static SolarAttributionCache Attribution(SolarVisibilityCache baseCache, List<ShadingElement> elements, List<AnalysisCell> cells, int cellIndexOffset, bool[] activeBins)
        {
            List<LinkedFace3D> occluders = new List<LinkedFace3D>();
            foreach (ShadingElement element in elements)
            {
                if (element?.LinkedFace3D != null)
                {
                    occluders.Add(element.LinkedFace3D);
                }
            }

            return SAM.Weather.SolarCalculator.Create.SolarAttributionCache(
                baseCache, occluders, cells, cellIndexOffset,
                SAM.Core.Tolerance.MacroDistance, SAM.Core.Tolerance.MacroDistance, SAM.Core.Tolerance.Angle, SAM.Core.Tolerance.Distance,
                true, activeBins);
        }

        /// <summary>
        /// STRUCTURAL traced-bin count: a row is traced iff it carries at least one entry that is
        /// not the untraced sentinel. Deterministic — the reduction is measured by counting rows,
        /// never by wall clock.
        /// </summary>
        private static int TracedBins(SolarAttributionCache cache)
        {
            int traced = 0;
            for (int b = 0; b < cache.BinCount; b++)
            {
                for (int c = 0; c < cache.CellCount; c++)
                {
                    if (cache.FirstHitIndex(b, c) != SAM.Weather.SolarCalculator.Query.FirstHitBackFacing)
                    {
                        traced++;
                        break;
                    }
                }
            }

            return traced;
        }

        /// <summary>
        /// THE PRODUCTION SCORING CALL: masked cache AND masked scorer, which is the combination the
        /// optimiser actually runs. Scoring a masked cache through the PUBLIC unmasked overload
        /// reaches the same accumulators by a different route — an untraced row reads as the
        /// negative first-hit sentinel and takes the same `continue` — so it is a valid check but it
        /// is not the shipped code. Both are compared against the complete pass.
        /// </summary>
        private static ShadingPerformance ScoreMasked(ApertureSolarTarget target, SolarVisibilityCache baseCache, SolarAttributionCache attributionCache, ApertureDesirability desirability, List<ShadingElement> elements, IShadingTypology device, bool[] activeBins)
        {
            return SAM.Analytical.SolarCalculator.Create.ShadingPerformance(
                target, baseCache, attributionCache, desirability, elements,
                device.Name, device.MaterialFraction(target), 0, activeBins);
        }

        /// <summary>
        /// The objective components and the admitted sums, EXACTLY — no tolerance, because a
        /// tolerance here would hide the drift these tests exist to forbid.
        /// </summary>
        private static void AssertIdenticalScoring(ShadingPerformance expected, ShadingPerformance actual)
        {
            Assert.NotNull(expected);
            Assert.NotNull(actual);

            ShadingObjective objective = new ShadingObjective(1.0, 0.1);
            Assert.Equal(objective.Benefit(expected), objective.Benefit(actual));
            Assert.Equal(objective.Harm(expected), objective.Harm(actual));
            Assert.Equal(objective.Cost(expected), objective.Cost(actual));
            Assert.Equal(objective.Score(expected), objective.Score(actual));

            // The admitted accounting must be identical: pruning reads, not the baseline.
            Assert.Equal(expected.AdmittedDirectEnergy, actual.AdmittedDirectEnergy);
            Assert.Equal(expected.AdmittedUnwantedEnergy, actual.AdmittedUnwantedEnergy);
            Assert.Equal(expected.AdmittedWantedEnergy, actual.AdmittedWantedEnergy);
        }

        private static void AssertIdenticalResult(OptimisedShadingResult pruned, OptimisedShadingResult full)
        {
            Assert.NotNull(pruned);
            Assert.NotNull(full);

            Assert.Equal(full.TypologyName, pruned.TypologyName);
            Assert.Equal(full.RecommendsNoShading, pruned.RecommendsNoShading);
            Assert.Equal(full.Termination, pruned.Termination);
            Assert.Equal(full.Evaluations, pruned.Evaluations);
            Assert.Equal(full.Iterations, pruned.Iterations);
            Assert.Equal(full.CoarseStartsAvailable, pruned.CoarseStartsAvailable);
            Assert.Equal(full.CoarseStartsRefined, pruned.CoarseStartsRefined);

            // The objective components and the score, exactly — no tolerance: the pruning must not
            // move a single bit.
            Assert.Equal(full.ObjectiveScore, pruned.ObjectiveScore);
            Assert.Equal(full.Benefit, pruned.Benefit);
            Assert.Equal(full.Harm, pruned.Harm);
            Assert.Equal(full.Cost, pruned.Cost);

            foreach (string name in full.ParameterNames)
            {
                Assert.Equal(full.GetParameter(name), pruned.GetParameter(name));
            }

            // Final reported performance: the winner's complete pass must reproduce the full path.
            Assert.Equal(full.AdmittedDirectEnergy, pruned.AdmittedDirectEnergy);
            Assert.Equal(full.AdmittedUnwantedEnergy, pruned.AdmittedUnwantedEnergy);
            Assert.Equal(full.AdmittedWantedEnergy, pruned.AdmittedWantedEnergy);
            Assert.Equal(full.DirectSolarIntercepted, pruned.DirectSolarIntercepted);
            Assert.Equal(full.UnwantedSolarIntercepted, pruned.UnwantedSolarIntercepted);
            Assert.Equal(full.WantedSolarBlocked, pruned.WantedSolarBlocked);
            Assert.Equal(full.UnattributedInterceptedEnergy, pruned.UnattributedInterceptedEnergy);
            Assert.Equal(full.MaterialFraction, pruned.MaterialFraction);

            // Provenance and attribution identity.
            Assert.Equal(full.AttributionTableHash, pruned.AttributionTableHash);
            Assert.Equal(full.ContextGeometryHash, pruned.ContextGeometryHash);
            Assert.Equal(full.TargetGeometryHash, pruned.TargetGeometryHash);
            Assert.Equal(full.ElementGuids, pruned.ElementGuids);
        }

        [Fact]
        public void The_Pruned_Scoring_Honours_The_Accounting_Gate_At_The_Edge_Cases()
        {
            // A crafted desirability whose energies sit exactly ON the 1e-12 gate boundary, scored
            // through the PUBLIC accounting with a full cache and a masked cache. The truth table
            // states the documented rule; the equality asserts that pruning with the documented
            // mask leaves Benefit, Harm, Cost, Score and the admitted sums untouched at the edges.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();
            int binCount = scenario.BaseCache.BinCount;
            ApertureDesirability probe = GateProbeDesirability(binCount);

            bool[] mask = DocumentedActiveMask(probe, binCount);
            Assert.NotNull(mask);

            bool[] expected = new bool[] { false, false, true, true, false, false, true, true, true };
            for (int b = 0; b < expected.Length; b++)
            {
                Assert.Equal(expected[b], mask[b]);
            }

            IShadingTypology device = new HorizontalLouvres(0.3, 3, 0.0);
            List<ShadingElement> elements = device.ShadingElements(scenario.Target);

            SolarAttributionCache complete = Attribution(scenario.BaseCache, elements, scenario.Target.AnalysisCells, 0, null);
            SolarAttributionCache pruned = Attribution(scenario.BaseCache, elements, scenario.Target.AnalysisCells, 0, mask);

            ShadingPerformance fromComplete = SAM.Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Target, scenario.BaseCache, complete, probe, elements,
                device.Name, device.MaterialFraction(scenario.Target));

            ShadingPerformance fromPruned = SAM.Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Target, scenario.BaseCache, pruned, probe, elements,
                device.Name, device.MaterialFraction(scenario.Target));

            // The shipped combination: masked cache AND masked scorer, at the gate boundary.
            ShadingPerformance fromMasked = ScoreMasked(
                scenario.Target, scenario.BaseCache, pruned, probe, elements, device, mask);

            Assert.NotNull(fromComplete);
            Assert.NotNull(fromPruned);

            AssertIdenticalScoring(fromComplete, fromPruned);
            AssertIdenticalScoring(fromComplete, fromMasked);
        }

        [Fact]
        public void The_Production_Pruning_Rule_Matches_The_Full_Path_At_The_Gate_Edges()
        {
            // THE PRODUCTION MASK ITSELF, exercised end-to-end: the gate-probe desirability is fed
            // to the optimiser with pruning on and off. Because the probe sits exactly on the 1e-12
            // boundary, any divergence between the production rule and the accounting gate moves a
            // candidate score and therefore the result. This is the public-path pin for the
            // (internal) Create.ActiveDesirabilityBins implementation.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();
            ApertureDesirability probe = GateProbeDesirability(scenario.BaseCache.BinCount);
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);

            foreach (string family in new string[] { "HorizontalLouvres", "VerticalFins", "Overhang", "EggCrate" })
            {
                OptimisedShadingResult pruned = Optimise.ShadingTypology(
                    scenario.Target, scenario.BaseCache, probe, scenario.Context,
                    family, objective, null, null, 40, 3, 0, 0, true);

                OptimisedShadingResult full = Optimise.ShadingTypology(
                    scenario.Target, scenario.BaseCache, probe, scenario.Context,
                    family, objective, null, null, 40, 3, 0, 0, false);

                AssertIdenticalResult(pruned, full);
            }
        }

        [Fact]
        public void The_Scenario_Masks_Align_With_The_Bins_And_The_Briefs()
        {
            foreach (OptimisationFixture.Scenario scenario in new OptimisationFixture.Scenario[]
            {
                OptimisationFixture.SouthSeasonal(),
                OptimisationFixture.SouthAllUnwanted(),
                OptimisationFixture.SouthAllWanted(),
                OptimisationFixture.SouthOneDayUnwanted(),
                OptimisationFixture.SouthOneDayBoth(),
            })
            {
                int binCount = scenario.BaseCache.BinCount;
                bool[] mask = DocumentedActiveMask(scenario.Desirability, binCount);

                Assert.NotNull(mask);
                Assert.Equal(binCount, mask.Length);

                int active = 0;
                foreach (bool entry in mask)
                {
                    if (entry) { active++; }
                }

                // A brief that only claims a day or two of the year must activate a small slice of
                // the sky; a full-year brief must activate a substantial one.
                if (scenario == OptimisationFixture.SouthOneDayUnwanted() || scenario == OptimisationFixture.SouthOneDayBoth())
                {
                    Assert.True(active < binCount / 10, $"a one-day brief activated {active} of {binCount} bins — the narrow-brief regime is not what this fixture produces");
                }
                else
                {
                    Assert.True(active > 0 && active < binCount, $"a seasonal brief should activate a strict subset of the {binCount} bins, not {active}");
                }

                output.WriteLine($"{scenario.Desirability.DesirabilityStrategyName}: {active} active of {binCount} bins");
            }
        }

        [Fact]
        public void Pruned_And_Full_Attribution_Give_Identical_Scores_Exactly()
        {
            // The equivalence at the accounting level: the SAME scoring path fed the full cache and
            // the masked cache must return bit-identical Benefit, Harm, Cost and Score — no
            // tolerance, because a tolerance here would hide exactly the drift this PR must not
            // introduce. The admitted sums are compared too: the pruning must never touch them.
            foreach (OptimisationFixture.Scenario scenario in new OptimisationFixture.Scenario[]
            {
                OptimisationFixture.SouthSeasonal(),
                OptimisationFixture.NorthSeasonal(),
                OptimisationFixture.EastSeasonal(),
                OptimisationFixture.SouthBlocked(),
                OptimisationFixture.SouthAllWanted(),
                OptimisationFixture.SouthAllUnwanted(),
                OptimisationFixture.SouthOneDayUnwanted(),
                OptimisationFixture.SouthOneDayBoth(),
            })
            {
                bool[] mask = DocumentedActiveMask(scenario.Desirability, scenario.BaseCache.BinCount);

                foreach (IShadingTypology device in new IShadingTypology[]
                {
                    new Overhang(0.6, 0.1, 0.2),
                    new HorizontalLouvres(0.3, 3, 15.0),
                    new VerticalFins(0.25, 4, -20.0),
                    new EggCrate(0.3, 2, 3),
                    new NoShading(),
                })
                {
                    List<ShadingElement> elements = device.ShadingElements(scenario.Target);

                    SolarAttributionCache complete = Attribution(scenario.BaseCache, elements, scenario.Target.AnalysisCells, 0, null);
                    SolarAttributionCache pruned = Attribution(scenario.BaseCache, elements, scenario.Target.AnalysisCells, 0, mask);

                    Assert.NotNull(complete);
                    Assert.NotNull(pruned);
                    Assert.Equal(complete.BinCount, pruned.BinCount);
                    Assert.Equal(complete.CellCount, pruned.CellCount);
                    Assert.Equal(complete.AttributionTableHash, pruned.AttributionTableHash);

                    ShadingPerformance fromComplete = SAM.Analytical.SolarCalculator.Create.ShadingPerformance(
                        scenario.Target, scenario.BaseCache, complete, scenario.Desirability, elements,
                        device.Name, device.MaterialFraction(scenario.Target));

                    ShadingPerformance fromPruned = SAM.Analytical.SolarCalculator.Create.ShadingPerformance(
                        scenario.Target, scenario.BaseCache, pruned, scenario.Desirability, elements,
                        device.Name, device.MaterialFraction(scenario.Target));

                    // The shipped combination: masked cache AND masked scorer.
                    ShadingPerformance fromMasked = ScoreMasked(
                        scenario.Target, scenario.BaseCache, pruned, scenario.Desirability, elements, device, mask);

                    Assert.NotNull(fromComplete);
                    Assert.NotNull(fromPruned);

                    AssertIdenticalScoring(fromComplete, fromPruned);
                    AssertIdenticalScoring(fromComplete, fromMasked);
                }
            }
        }

        [Fact]
        public void A_Mask_That_Does_Not_Cover_Every_Bin_Is_Rejected_Rather_Than_Indexed()
        {
            // The scorer indexes the mask by bin, so a mask of any other length is a wiring error.
            // The accounting must refuse it with the same null it returns for every other shape
            // mismatch — never an IndexOutOfRangeException, and never a silent "the missing tail is
            // inactive", which would drop Benefit/Harm at bins the attribution did trace.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();
            int binCount = scenario.BaseCache.BinCount;
            Assert.True(binCount > 1);

            IShadingTypology device = new HorizontalLouvres(0.3, 3, 0.0);
            List<ShadingElement> elements = device.ShadingElements(scenario.Target);
            SolarAttributionCache complete = Attribution(scenario.BaseCache, elements, scenario.Target.AnalysisCells, 0, null);

            foreach (bool[] wrong in new bool[][] { new bool[binCount - 1], new bool[binCount + 1], new bool[0] })
            {
                Assert.Null(ScoreMasked(
                    scenario.Target, scenario.BaseCache, complete, scenario.Desirability, elements, device, wrong));
            }

            // A correctly sized mask is still accepted — including the all-inactive extreme, which
            // scores a real (fully pruned) performance rather than failing.
            Assert.NotNull(ScoreMasked(
                scenario.Target, scenario.BaseCache, complete, scenario.Desirability, elements, device, new bool[binCount]));
        }

        [Fact]
        public void The_Optimiser_Returns_Identical_Results_With_And_Without_Pruning()
        {
            // The differential end-to-end contract, family by family: the pruned search and the
            // unpruned search are driven by identical scores, so every statistic and every reported
            // number must agree exactly.
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);

            foreach (OptimisationFixture.Scenario scenario in new OptimisationFixture.Scenario[]
            {
                OptimisationFixture.SouthSeasonal(),
                OptimisationFixture.SouthBlocked(),
                OptimisationFixture.SouthAllUnwanted(),
                OptimisationFixture.SouthOneDayUnwanted(),
            })
            {
                foreach (string family in SAM.Analytical.SolarCalculator.Create.ShadingTypologyNames)
                {
                    OptimisedShadingResult pruned = Optimise.ShadingTypology(
                        scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                        family, objective, null, null, 60, 3, 0, 0, true);

                    OptimisedShadingResult full = Optimise.ShadingTypology(
                        scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                        family, objective, null, null, 60, 3, 0, 0, false);

                    AssertIdenticalResult(pruned, full);
                }
            }
        }

        [Fact]
        public void The_Default_Multi_Family_Path_Matches_The_Per_Family_Pruned_Runs()
        {
            // The production wrapper (what Grasshopper calls) must hand back exactly the pruned
            // per-family results, best first — so the pruning is proven on the real entry point.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);

            List<OptimisedShadingResult> results = Optimise.ShadingDevice(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                objective, null, null, 60, 0);

            Assert.NotNull(results);
            Assert.NotEmpty(results);

            foreach (OptimisedShadingResult result in results)
            {
                OptimisedShadingResult perFamily = Optimise.ShadingTypology(
                    scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                    result.TypologyName, objective, null, null, 60, 3, 0, 0, true);

                AssertIdenticalResult(result, perFamily);
            }
        }

        [Fact]
        public void One_Day_Briefs_Return_No_Shade_Exactly_As_The_Full_Path()
        {
            // The short-period regime must keep its legitimate NO SHADE answer — the pruning must
            // not change a single candidate score, so the null device wins identically.
            ShadingObjective objective = new ShadingObjective(1.0, 0.1);

            foreach (OptimisationFixture.Scenario scenario in new OptimisationFixture.Scenario[]
            {
                OptimisationFixture.SouthOneDayUnwanted(),
                OptimisationFixture.SouthOneDayBoth(),
            })
            {
                List<OptimisedShadingResult> pruned = Optimise.ShadingDevice(
                    scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                    objective, null, null, 400, 0);

                Assert.NotNull(pruned);
                Assert.NotEmpty(pruned);

                foreach (OptimisedShadingResult result in pruned)
                {
                    Assert.True(result.RecommendsNoShading, $"{result.TypologyName} should not beat the null device under a one-day brief");
                    Assert.True(result.ObjectiveScore <= 0);
                }
            }
        }

        [Fact]
        public void The_Pruned_Optimiser_Is_Deterministic()
        {
            // Two runs of the pruned path must be byte-identical, serialisation included — except
            // the three TIMING fields, which are wall-clock measurements and therefore legitimately
            // differ between runs. Everything else in the result is the engineering answer and the
            // search statistics, and those must not move by a single bit.
            foreach (OptimisationFixture.Scenario scenario in new OptimisationFixture.Scenario[]
            {
                OptimisationFixture.SouthSeasonal(),
                OptimisationFixture.SouthBlocked(),
            })
            {
                List<OptimisedShadingResult> first = Optimise.ShadingDevice(
                    scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                    new ShadingObjective(1.0, 0.1), null, null, 60, 0);

                List<OptimisedShadingResult> second = Optimise.ShadingDevice(
                    scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                    new ShadingObjective(1.0, 0.1), null, null, 60, 0);

                Assert.Equal(first.Count, second.Count);
                for (int i = 0; i < first.Count; i++)
                {
                    System.Text.Json.Nodes.JsonObject firstJson = first[i].ToJsonObject();
                    System.Text.Json.Nodes.JsonObject secondJson = second[i].ToJsonObject();

                    foreach (string timing in new string[] { "ElapsedMilliseconds", "GeometryMilliseconds", "EvaluationMilliseconds" })
                    {
                        firstJson.Remove(timing);
                        secondJson.Remove(timing);
                    }

                    Assert.Equal(firstJson.ToJsonString(), secondJson.ToJsonString());
                }
            }
        }

        [Fact]
        public void Pruning_Structurally_Reduces_Traced_Bins()
        {
            // The saving, measured by COUNTING rows rather than by wall clock: a flaky timing test
            // would not survive shared CI hardware, and a structural count does not need to.
            OptimisationFixture.Scenario seasonal = OptimisationFixture.SouthSeasonal();
            OptimisationFixture.Scenario oneDay = OptimisationFixture.SouthOneDayUnwanted();
            List<ShadingElement> elements = new HorizontalLouvres(0.3, 3, 0.0).ShadingElements(seasonal.Target);

            bool[] seasonalMask = DocumentedActiveMask(seasonal.Desirability, seasonal.BaseCache.BinCount);
            bool[] oneDayMask = DocumentedActiveMask(oneDay.Desirability, oneDay.BaseCache.BinCount);

            SolarAttributionCache seasonalFull = Attribution(seasonal.BaseCache, elements, seasonal.Target.AnalysisCells, 0, null);
            SolarAttributionCache seasonalPruned = Attribution(seasonal.BaseCache, elements, seasonal.Target.AnalysisCells, 0, seasonalMask);

            SolarAttributionCache oneDayFull = Attribution(oneDay.BaseCache, elements, oneDay.Target.AnalysisCells, 0, null);
            SolarAttributionCache oneDayPruned = Attribution(oneDay.BaseCache, elements, oneDay.Target.AnalysisCells, 0, oneDayMask);

            int seasonalTracedFull = TracedBins(seasonalFull);
            int seasonalTracedPruned = TracedBins(seasonalPruned);
            int oneDayTracedFull = TracedBins(oneDayFull);
            int oneDayTracedPruned = TracedBins(oneDayPruned);

            output.WriteLine("case            full bins   pruned bins   fraction retained   reduction factor");
            output.WriteLine($"seasonal brief  {seasonalTracedFull,8}   {seasonalTracedPruned,11}   {100.0 * seasonalTracedPruned / seasonalTracedFull,16:0.#} %   {(double)seasonalTracedFull / seasonalTracedPruned,13:0.##}x");
            output.WriteLine($"one-day brief   {oneDayTracedFull,8}   {oneDayTracedPruned,11}   {100.0 * oneDayTracedPruned / oneDayTracedFull,16:0.#} %   {(double)oneDayTracedFull / oneDayTracedPruned,13:0.##}x");

            Assert.True(seasonalTracedPruned < seasonalTracedFull, "the seasonal pruned build must trace strictly fewer bins than the full one");

            Assert.True(oneDayTracedFull > 10 * oneDayTracedPruned, $"a one-day brief should reduce traced bins by far more than {oneDayTracedFull} -> {oneDayTracedPruned}");

            // Supporting wall-clock evidence only — deliberately NOT asserted, so it can never be a
            // flaky gate on shared hardware.
            Attribution(seasonal.BaseCache, elements, seasonal.Target.AnalysisCells, 0, null);
            Attribution(seasonal.BaseCache, elements, seasonal.Target.AnalysisCells, 0, seasonalMask);

            Stopwatch fullWatch = Stopwatch.StartNew();
            for (int i = 0; i < 3; i++) { Attribution(seasonal.BaseCache, elements, seasonal.Target.AnalysisCells, 0, null); }
            fullWatch.Stop();

            Stopwatch prunedWatch = Stopwatch.StartNew();
            for (int i = 0; i < 3; i++) { Attribution(seasonal.BaseCache, elements, seasonal.Target.AnalysisCells, 0, seasonalMask); }
            prunedWatch.Stop();

            output.WriteLine($"wall-clock (3 builds each, seasonal): full {fullWatch.Elapsed.TotalMilliseconds / 3.0:0.0} ms, pruned {prunedWatch.Elapsed.TotalMilliseconds / 3.0:0.0} ms per build");
        }
    }
}
