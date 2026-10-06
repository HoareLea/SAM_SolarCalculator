// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SAM.Core.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// PR 3 — the Kołobrzeg office as a REAL-PROJECT regression fixture.
    ///
    /// The fixture preserves the numerical-resolution evidence found on a real office model rather
    /// than on synthetic apertures, and it does so through the same production entry points
    /// Grasshopper's ApertureSolarTargets / RationaliseShading use. Nothing here introduces new
    /// production behaviour, and nothing may be tuned specifically to this project: the tests pin
    /// the engineering conclusions — fixture integrity, the coarse-grid sampling artefact,
    /// family/decision stability and the PR 2 grid guidance — not incidental optimiser decimals.
    ///
    /// The cheap half (integrity + guidance) runs in the FAST suite; the optimisation runs behind
    /// the artefact and stability contracts are LongRunning.
    /// </summary>
    public class KolobrzegRegressionTests
    {
        private readonly ITestOutputHelper output;

        public KolobrzegRegressionTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static List<ApertureSolarTarget> Targets(AnalyticalModel analyticalModel, double gridSize)
        {
            return analyticalModel.ApertureSolarTargets(null, gridSize);
        }

        // ------------------------------------------------------- A. fixture integrity ----

        [Fact]
        public void Fixture_Loads_With_The_Three_Studied_Apertures_And_Their_Expected_Geometry()
        {
            // The fixture is the contract: if it is replaced, its GUIDs drift, its geometry is
            // re-exported differently or the sampling pattern changes, the whole resolution story
            // this file preserves would be describing a different model. This is the cheap check
            // that catches all of that before any expensive run is made.
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();

            List<ApertureSolarTarget> targets = Targets(analyticalModel, KolobrzegFixture.HistoricalGridSize);
            Assert.Equal(KolobrzegFixture.StudiedApertures.Count, targets.Count);

            foreach (KolobrzegFixture.Expected expected in KolobrzegFixture.StudiedApertures)
            {
                ApertureSolarTarget target = targets.Find(x => x.ApertureGuid == expected.Guid);
                Assert.NotNull(target);

                // Local planar extents — the same bounds mechanism the production resolution rules
                // measure the aperture with.
                Assert.True(Analytical.SolarCalculator.Query.TryGetApertureLocalBounds(
                    target, out double minX, out double maxX, out double minY, out double maxY));

                double spanAcross = maxX - minX;
                double spanUp = maxY - minY;

                Assert.InRange(spanAcross, expected.SpanAcross - 0.05, expected.SpanAcross + 0.05);
                Assert.InRange(spanUp, expected.SpanUp - 0.05, expected.SpanUp + 0.05);
                Assert.InRange(target.GrossArea, expected.Area * 0.99, expected.Area * 1.01);

                // The historical sampling pattern: at the default 0.5 m grid the study reproduced
                // exactly 10 / 9 / 6 analysis cells. A re-export that changes the cell pattern
                // changes the artefact story too.
                Assert.Equal(expected.CellsAtHalfMetre, target.CellCount);

                // Every aperture is fully sampled at 0.5 m — no partial cells to explain the
                // coarse-grid behaviour away.
                Assert.Equal(1.0, target.SampledAreaFraction, 9);

                output.WriteLine($"{expected.Guid} {spanAcross:0.##} x {spanUp:0.##} m, " +
                    $"{target.GrossArea:0.###} m2, {target.CellCount} cells at 0.5 m, azimuth {target.Azimuth:0.##}");
            }

            // All three apertures sit on one façade: a shared orientation is part of the fixture's
            // identity, so a re-export that reorients one panel is detectable here.
            Assert.Single(targets.Select(x => Math.Round(x.Azimuth)).Distinct());
        }

        // ------------------------------------------ B. PR 2 guidance on the real set ----

        [Fact]
        public void The_Half_Metre_Default_Is_Coarser_Than_The_Recommended_Grid_And_Nothing_Is_Changed_By_The_Guidance()
        {
            // PR 2 behaviour exercised on the real selected aperture set, through the actual current
            // geometry rather than an assumed number.
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            List<ApertureSolarTarget> targets = Targets(analyticalModel, KolobrzegFixture.HistoricalGridSize);

            // The recommendation the current geometry implies. The smallest aperture's shortest
            // side is 0.60 m, so its geometric half-width is 0.30 m — which is FINER than the
            // design-grade guidance figure, so the design-grade figure governs the set.
            double smallestShortestSide = double.NaN;
            foreach (ApertureSolarTarget target in targets)
            {
                Assert.True(Analytical.SolarCalculator.Query.TryGetApertureLocalBounds(
                    target, out double minX, out double maxX, out double minY, out double maxY));

                double shortest = Math.Min(maxX - minX, maxY - minY);
                if (double.IsNaN(smallestShortestSide) || shortest < smallestShortestSide)
                {
                    smallestShortestSide = shortest;
                }
            }

            Assert.Equal(0.60, smallestShortestSide, 6);

            double designGrade = Analytical.SolarCalculator.Query.DesignGradeGridSize;
            double expected = Math.Min(designGrade, smallestShortestSide / 2.0);
            Assert.Equal(0.25, expected, 12);

            double recommended = Analytical.SolarCalculator.Query.RecommendedGridSize(targets);
            Assert.Equal(expected, recommended, 12);
            Assert.Equal(0.25, recommended, 12);

            // One shared set-level number, governed by the smallest aperture: the recommendation
            // for the smallest aperture alone is the same as for the whole set.
            List<ApertureSolarTarget> smallest = targets.FindAll(x => x.ApertureGuid == KolobrzegFixture.SmallApertureGuid);
            Assert.Single(smallest);
            Assert.Equal(recommended, Analytical.SolarCalculator.Query.RecommendedGridSize(smallest), 12);

            // The historical 0.5 m default is coarser than it, and the guidance says so.
            Assert.True(Analytical.SolarCalculator.Query.CoarserThanRecommended(
                KolobrzegFixture.HistoricalGridSize, recommended, out string message));
            Assert.NotNull(message);
            output.WriteLine(message);

            // Guidance, not enforcement: after the recommendation has been computed, the supplied
            // grid is unchanged, the calculation still runs at 0.5 m, and the sampling pattern is
            // the one the study was built on.
            List<ApertureSolarTarget> after = Targets(analyticalModel, KolobrzegFixture.HistoricalGridSize);
            foreach (KolobrzegFixture.Expected studied in KolobrzegFixture.StudiedApertures)
            {
                ApertureSolarTarget target = after.Find(x => x.ApertureGuid == studied.Guid);
                Assert.NotNull(target);
                Assert.Equal(studied.CellsAtHalfMetre, target.CellCount);
            }

            Assert.All(after.SelectMany(x => x.AnalysisCells), x => Assert.InRange(x.Area, 0.0, 0.25 + 1e-9));
        }

        // --------------------------------- C. the coarse-grid sampling artefact ----

        [Fact]
        [Trait("Category", "LongRunning")]
        public void The_Smallest_Aperture_At_Half_A_Metre_Is_A_Coarse_Grid_Sampling_Artefact()
        {
            // THE most important real-project regression. The smallest studied aperture
            // (0.60 x 1.39 m) at the historical 0.5 m default recommends a very thin single
            // vertical fin with substantial apparent blocking; the same physical device, measured
            // independently on finer grids, does essentially nothing. The 0.5 m recommendation is a
            // plausible-looking coarse-grid sampling artefact, not a device.
            //
            // Nothing here hard-codes the artefact's incidental decimals: the coarse device is
            // whatever today's production optimiser actually returns at 0.5 m, and the assertions
            // are on the engineering facts — the coarse winner looks materially different from the
            // design-grade recommendation, its apparent performance evaporates when the SAME device
            // is re-measured on an appropriately finer grid, and even the finest re-measurement
            // never approaches the coarse claim.
            List<OptimisedShadingResult> coarseResults = KolobrzegFixture.Optimise(KolobrzegFixture.SmallApertureGuid, 0.5);
            OptimisedShadingResult coarseWinner = coarseResults[0];

            // The artefact's structural signature: one vertical element. (A single thin element is
            // the recorded single-element blind spot — with no array pitch there is nothing for the
            // resolution rules to evaluate.)
            Assert.Equal("VerticalFins", coarseWinner.TypologyName);
            Assert.Equal(1.0, coarseWinner.GetParameter("Count"), 9);

            // It LOOKS like a triumph on the grid it was found on.
            double coarseBlocked = 100.0 * coarseWinner.UnwantedSolarBlocked;
            output.WriteLine($"0.5 m winner: {coarseWinner.TypologyName} " +
                $"depth {coarseWinner.GetParameter("Depth"):0.###} m, count {coarseWinner.GetParameter("Count"):0}, " +
                $"tilt {coarseWinner.GetParameter("TiltDegrees"):0}°, blocked {coarseBlocked:0.#} %");
            Assert.True(coarseBlocked > 50.0,
                $"the coarse winner must claim substantial apparent performance; it reports {coarseBlocked:0.#} %");

            // The design-grade and finer recommendations are a DIFFERENT ANSWER — a different
            // family, not a re-tuned version of the fin.
            foreach (double gridSize in new double[] { 0.25, 0.2 })
            {
                OptimisedShadingResult fineWinner = KolobrzegFixture.Optimise(KolobrzegFixture.SmallApertureGuid, gridSize)[0];
                Assert.Equal("HorizontalLouvres", fineWinner.TypologyName);
                Assert.NotEqual(coarseWinner.TypologyName, fineWinner.TypologyName);
                output.WriteLine($"{gridSize} m winner: {fineWinner.TypologyName} blocked {100.0 * fineWinner.UnwantedSolarBlocked:0.#} %");
            }

            // The decisive measurement: the SAME PHYSICAL DEVICE, re-measured independently on
            // grids that can actually see it, blocks essentially nothing.
            IShadingTypology coarseDevice = coarseWinner.Typology();
            foreach (double gridSize in new double[] { 0.25, 0.2 })
            {
                ApertureShadingSetup setup = KolobrzegFixture.Setup(KolobrzegFixture.SmallApertureGuid, gridSize);
                ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                    setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability,
                    setup.Context.ContextOccluders, coarseDevice, setup.CellIndexOffset);

                double blocked = 100.0 * performance.UnwantedSolarBlocked;
                output.WriteLine($"coarse device re-measured at {gridSize} m: blocked {blocked:0.#} %");
                Assert.True(blocked < 5.0,
                    $"the coarse device re-measured at {gridSize} m reports {blocked:0.#} % — the coarse claim was a sampling artefact");
            }

            // Even at the finest grid the same device never approaches its coarse claim — its true
            // performance is bounded a long way below the 0.5 m figure.
            ApertureShadingSetup reference = KolobrzegFixture.Setup(KolobrzegFixture.SmallApertureGuid, 0.1);
            ShadingPerformance atReference = Analytical.SolarCalculator.Create.ShadingPerformance(
                reference.Target, reference.Context.SolarVisibilityCache, reference.Desirability,
                reference.Context.ContextOccluders, coarseDevice, reference.CellIndexOffset);

            double blockedAtReference = 100.0 * atReference.UnwantedSolarBlocked;
            output.WriteLine($"coarse device re-measured at 0.1 m: blocked {blockedAtReference:0.#} %");
            Assert.True(blockedAtReference < 30.0,
                $"at 0.1 m the coarse device blocks {blockedAtReference:0.#} % — nowhere near its {coarseBlocked:0.#} % coarse-grid claim");
        }

        // --------------------------------- D. family / decision stability matrix ----

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Family_Recommendations_Are_Stable_At_Design_Grade_Grids_Where_The_Coarse_Grid_Differs()
        {
            // The smallest useful real-project matrix: the coarse default (0.5 m), the design-grade
            // grid this set's PR 2 recommendation points at (0.25 m) and one finer reference
            // (0.2 m), for all three studied apertures.
            //
            // WHAT IS ASSERTED AND WHAT IS NOT. Decision/family stability is asserted — which
            // family wins, and whether the coarse answer differs from the design-grade one. Exact
            // geometry is deliberately NOT asserted for the tall aperture: the study found its
            // exact depth/count keep changing across these grids, so convergence here is a
            // family-level conclusion, not parameter equality.
            List<double> gridSizes = new List<double> { 0.5, 0.25, 0.2 };

            Dictionary<Guid, Dictionary<double, string>> winners = new Dictionary<Guid, Dictionary<double, string>>();
            foreach (KolobrzegFixture.Expected expected in KolobrzegFixture.StudiedApertures)
            {
                Dictionary<double, string> perGrid = new Dictionary<double, string>();
                foreach (double gridSize in gridSizes)
                {
                    OptimisedShadingResult winner = KolobrzegFixture.Optimise(expected.Guid, gridSize)[0];
                    perGrid[gridSize] = winner.TypologyName;
                    output.WriteLine($"{expected.Guid.ToString().Substring(0, 8)} @ {gridSize} m: {winner.TypologyName} " +
                        $"({string.Join(", ", winner.ParameterNames.Select(n => $"{n} {winner.GetParameter(n):0.###}"))}), blocked {100.0 * winner.UnwantedSolarBlocked:0.#} %");
                }

                winners[expected.Guid] = perGrid;
            }

            // SMALL aperture: unstable at the coarse default, one family from the design-grade grid
            // down. The coarse answer is the artefact the dedicated test dissects; here the point
            // is that the family/decision level STABILISES at 0.25 m and stays there finer.
            Assert.Equal("VerticalFins", winners[KolobrzegFixture.SmallApertureGuid][0.5]);
            Assert.Equal("HorizontalLouvres", winners[KolobrzegFixture.SmallApertureGuid][0.25]);
            Assert.Equal("HorizontalLouvres", winners[KolobrzegFixture.SmallApertureGuid][0.2]);

            // MID aperture: the family/decision was already stable at the coarse default and stays
            // HorizontalLouvres throughout.
            Assert.Equal("HorizontalLouvres", winners[KolobrzegFixture.MidApertureGuid][0.5]);
            Assert.Equal("HorizontalLouvres", winners[KolobrzegFixture.MidApertureGuid][0.25]);
            Assert.Equal("HorizontalLouvres", winners[KolobrzegFixture.MidApertureGuid][0.2]);

            // TALL aperture: the HorizontalLouvres family remained stable at every grid studied,
            // even though its exact geometry moved substantially with resolution. Only the family
            // is asserted — see the WHAT IS ASSERTED comment above.
            Assert.Equal("HorizontalLouvres", winners[KolobrzegFixture.TallApertureGuid][0.5]);
            Assert.Equal("HorizontalLouvres", winners[KolobrzegFixture.TallApertureGuid][0.25]);
            Assert.Equal("HorizontalLouvres", winners[KolobrzegFixture.TallApertureGuid][0.2]);
        }

        // ------------------------------- E. PR 2 resolution-cap warning, natural case ----

        [Fact]
        [Trait("Category", "LongRunning")]
        public void The_Coarse_Grid_Winner_Rides_The_Resolution_Cap_And_Carries_The_Guidance_Warning()
        {
            // PR 2's resolution-cap warning exercised on a case the real fixture produces NATURALLY
            // — no forcing. At 0.5 m the small aperture's grid-derived count maximum for fins is 1
            // (a 0.6 m span cannot carry two elements at the 1 m minimum pitch), the winner is a
            // single fin, and that winner is the very artefact the test above exposes. Both halves
            // of the warning's invariant hold on the real model: the grid genuinely narrowed the
            // typology's own maximum AND the winner sits exactly on the narrowed maximum.
            OptimisedShadingResult coarseWinner = KolobrzegFixture.Optimise(KolobrzegFixture.SmallApertureGuid, 0.5)[0];

            // The grid really did narrow the declared maximum: the recorded search bound for the
            // count is 1, far below the typology's own declared maximum.
            List<ShadingParameter> bounds = coarseWinner.Bounds;
            Assert.NotNull(bounds);

            ShadingParameter countBound = bounds.Find(x => x.Name == "Count");
            Assert.NotNull(countBound);
            Assert.Equal(1.0, countBound.Maximum, 9);

            Assert.True(coarseWinner.Typology().TryGetBounds("Count", out double _, out double declaredMaximum));
            Assert.True(countBound.Maximum < declaredMaximum - 1e-9,
                "the grid cap must genuinely narrow the typology's declared maximum for this case to exercise the warning");

            // And the winner reaches it — so the warning fires, with the behaviour (not the
            // punctuation) asserted: it names the family and the grid.
            Assert.True(Analytical.SolarCalculator.Query.GridResolutionCapReached(coarseWinner, out string message));
            Assert.NotNull(message);
            Assert.Contains("VerticalFins", message);
            Assert.Contains("0.5", message);
            output.WriteLine(message);

            // The contrast case from the same fixture: the fine-grid winners sit below their
            // resolution caps and carry no warning.
            foreach (double gridSize in new double[] { 0.25, 0.2 })
            {
                OptimisedShadingResult fineWinner = KolobrzegFixture.Optimise(KolobrzegFixture.SmallApertureGuid, gridSize)[0];
                Assert.False(Analytical.SolarCalculator.Query.GridResolutionCapReached(fineWinner, out string _));
            }
        }
    }
}
