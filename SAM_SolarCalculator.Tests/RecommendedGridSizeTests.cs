// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// PR 2 — grid-resolution guidance: the recommended grid size for a selected aperture set, the
    /// warning when the supplied grid is coarser than it, and the resolution-cap warning for an
    /// optimised repeated-element solution whose count was limited by the grid.
    ///
    /// THE CONTRACT UNDER TEST. Guidance, never enforcement: the recommendation is calculated for
    /// the SELECTED aperture set (one shared number, governed by the smallest aperture), it never
    /// alters the supplied grid, it does not claim convergence, and the cap warning fires only when
    /// the grid-resolution cap genuinely narrowed the typology's own maximum AND the winner sits on
    /// that narrowed maximum.
    /// </summary>
    public class RecommendedGridSizeTests
    {
        private readonly ITestOutputHelper output;

        public RecommendedGridSizeTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>A synthetic rectangular aperture target, south-facing, width across by height up.</summary>
        private static ApertureSolarTarget Target(double width, double height)
        {
            Face3D face3D = SyntheticTargets.Face(SyntheticTargets.South, null, width, height);
            return new ApertureSolarTarget(Guid.NewGuid(), Guid.NewGuid(), face3D, null);
        }

        /// <summary>A result as the optimiser stores one: the capped search bounds, a winner and provenance.</summary>
        private static OptimisedShadingResult Result(string typologyName, ApertureSolarTarget target, double gridSize, Dictionary<string, double> winner)
        {
            IShadingTypology typology = Analytical.SolarCalculator.Create.ShadingTypology(typologyName);
            List<ShadingParameter> bounds = Analytical.SolarCalculator.Create.ShadingParameters(typology, double.NaN, target, gridSize);

            OptimisedShadingResult result = new OptimisedShadingResult();
            result.TypologyName = typologyName;
            result.SetParameters(bounds.ConvertAll(x => x.Name), winner, new Dictionary<string, double>(), bounds);
            result.SetProvenance(target.ApertureGuid, null, gridSize, 2.0, 0, 0, null, null, null, null);
            return result;
        }

        // ------------------------------------------------------- the recommendation ----

        [Fact]
        public void The_Design_Grade_Grid_Size_Is_A_Named_Quarter_Metre_Constant()
        {
            // The value the recommendation is capped by. It is an empirical design-grade guidance
            // figure, so it is exposed as a named constant rather than repeated as a bare 0.25.
            Assert.Equal(0.25, Analytical.SolarCalculator.Query.DesignGradeGridSize);
        }

        [Fact]
        public void A_Large_Aperture_Takes_The_Design_Grade_Grid_Size()
        {
            // Shortest side > 0.5 m: half of it is finer than the design-grade figure, so the
            // design-grade guidance governs.
            Assert.Equal(0.25, Analytical.SolarCalculator.Query.RecommendedGridSize(new List<ApertureSolarTarget> { Target(2.0, 2.0) }), 12);

            // The Kołobrzeg 0.90 x 2.25 m window: shortest side 0.90 m -> 0.45 m, still finer than 0.25 m.
            Assert.Equal(0.25, Analytical.SolarCalculator.Query.RecommendedGridSize(new List<ApertureSolarTarget> { Target(0.90, 2.25) }), 12);
        }

        [Fact]
        public void A_Small_Aperture_Halves_Its_Shortest_Side()
        {
            // 0.60 m shortest -> 0.30 m geometric, but 0.25 m is finer: design-grade governs.
            Assert.Equal(0.25, Analytical.SolarCalculator.Query.RecommendedGridSize(new List<ApertureSolarTarget> { Target(0.60, 1.39) }), 12);

            // 0.40 m shortest -> 0.20 m geometric: the aperture now governs.
            Assert.Equal(0.20, Analytical.SolarCalculator.Query.RecommendedGridSize(new List<ApertureSolarTarget> { Target(0.40, 1.39) }), 12);

            // 0.48 m shortest -> 0.24 m.
            Assert.Equal(0.24, Analytical.SolarCalculator.Query.RecommendedGridSize(new List<ApertureSolarTarget> { Target(0.48, 2.0) }), 12);

            // The SHORTEST dimension governs whichever way the aperture is oriented: a 2.0 m wide,
            // 0.30 m high strip is 0.15 m.
            Assert.Equal(0.15, Analytical.SolarCalculator.Query.RecommendedGridSize(new List<ApertureSolarTarget> { Target(2.0, 0.30) }), 12);
        }

        [Fact]
        public void The_Smallest_Selected_Aperture_Governs_The_Set()
        {
            // One shared recommendation for the whole set, decided by the smallest aperture, so a
            // single grid stays valid for the whole calculation.
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget> { Target(2.0, 2.0), Target(0.40, 2.0) };

            Assert.Equal(0.20, Analytical.SolarCalculator.Query.RecommendedGridSize(targets), 12);

            // Order of selection must not matter.
            targets.Reverse();
            Assert.Equal(0.20, Analytical.SolarCalculator.Query.RecommendedGridSize(targets), 12);
        }

        [Fact]
        public void The_Recommendation_Never_Falls_Below_The_Minimum_Grid_Size()
        {
            // 0.05 m shortest -> 0.025 m geometric, below sqrt(1e-3): the existing production
            // minimum-grid constraint clamps it.
            double expected = Analytical.SolarCalculator.Query.MinimumGridSize();
            Assert.Equal(expected, Analytical.SolarCalculator.Query.RecommendedGridSize(new List<ApertureSolarTarget> { Target(0.05, 0.05) }), 12);
            output.WriteLine($"0.05 m aperture -> recommendation clamped to {expected:0.########} m");
        }

        [Fact]
        public void A_Shortest_Side_Of_Twice_The_Design_Grade_Sits_Exactly_On_The_Recommendation()
        {
            // The boundary between the two rules: 0.5 m shortest -> 0.25 m geometric == design-grade.
            Assert.Equal(0.25, Analytical.SolarCalculator.Query.RecommendedGridSize(new List<ApertureSolarTarget> { Target(0.5, 2.0) }), 12);
        }

        [Fact]
        public void An_Empty_Or_Unmeasurable_Selection_Has_No_Recommendation()
        {
            Assert.True(double.IsNaN(Analytical.SolarCalculator.Query.RecommendedGridSize(null)));
            Assert.True(double.IsNaN(Analytical.SolarCalculator.Query.RecommendedGridSize(new List<ApertureSolarTarget>())));
            Assert.True(double.IsNaN(Analytical.SolarCalculator.Query.RecommendedGridSize(new List<ApertureSolarTarget> { null })));

            // A target with no measurable face contributes nothing; alone it yields no number.
            ApertureSolarTarget unmeasurable = new ApertureSolarTarget(Guid.NewGuid(), Guid.NewGuid(), null, null);
            Assert.True(double.IsNaN(Analytical.SolarCalculator.Query.RecommendedGridSize(new List<ApertureSolarTarget> { unmeasurable })));
        }

        // ------------------------------------------------------- the coarse-grid warning ----

        [Fact]
        public void A_Grid_Coarser_Than_The_Recommendation_Warns_And_Names_Both()
        {
            Assert.True(Analytical.SolarCalculator.Query.CoarserThanRecommended(0.5, 0.25, out string message));
            Assert.NotNull(message);
            Assert.Contains("0.5", message);
            Assert.Contains("0.25", message);
            Assert.Contains("coarser", message);

            // The message must say the calculation continues with the supplied grid and must NOT
            // claim convergence.
            Assert.Contains("guidance, not a requirement", message);
            Assert.Contains("does not guarantee convergence", message);
            Assert.DoesNotContain("wrong", message);
            output.WriteLine(message);

            // Any strictly coarser grid warns, however slightly.
            Assert.True(Analytical.SolarCalculator.Query.CoarserThanRecommended(0.251, 0.25, out string _));
        }

        [Fact]
        public void A_Grid_Equal_To_Or_Finer_Than_The_Recommendation_Does_Not_Warn()
        {
            Assert.False(Analytical.SolarCalculator.Query.CoarserThanRecommended(0.25, 0.25, out string message));
            Assert.Null(message);

            Assert.False(Analytical.SolarCalculator.Query.CoarserThanRecommended(0.2, 0.25, out message));
            Assert.Null(message);

            // The boundary is nudged by the same relative epsilon as the resolution rules: two
            // arithmetic routes to the same length must not warn on the last bit.
            Assert.False(Analytical.SolarCalculator.Query.CoarserThanRecommended(0.25 * (1.0 + 1e-12), 0.25, out message));
            Assert.Null(message);

            // Not a length: nothing to say.
            Assert.False(Analytical.SolarCalculator.Query.CoarserThanRecommended(double.NaN, 0.25, out message));
            Assert.Null(message);
        }

        // ------------------------------------------------------- the resolution cap ----

        [Fact]
        public void The_Cap_Warning_Fires_When_The_Winner_Rides_A_Narrowed_Maximum()
        {
            // SouthWindow is 2 m across, 1 m up. At a 0.5 m grid the minimum feature pitch is 1 m,
            // so louvres (which stack UP) are capped at 1/1 + 1 = 2 — far below the declared 24.
            ApertureSolarTarget target = Target(2.0, 1.0);

            Assert.True(Analytical.SolarCalculator.Query.GridResolutionCapReached(
                Result("HorizontalLouvres", target, 0.5, new Dictionary<string, double> { { "Depth", 0.3 }, { "Count", 2 }, { "TiltDegrees", 0 } }),
                out string message));

            Assert.NotNull(message);
            Assert.Contains("HorizontalLouvres", message);
            Assert.Contains("0.5", message);
            Assert.Contains("grid", message);
            // The wording must not overstate causation: the grid MAY be the limit, and confirmation
            // is the reader's next step.
            Assert.Contains("may have limited the element count", message);
            output.WriteLine(message);

            // Fins run ACROSS the aperture: 2/1 + 1 = 3, and a winner at 3 rides the cap too.
            Assert.True(Analytical.SolarCalculator.Query.GridResolutionCapReached(
                Result("VerticalFins", target, 0.5, new Dictionary<string, double> { { "Depth", 0.4 }, { "Count", 3 }, { "TiltDegrees", 0 } }),
                out message));
        }

        [Fact]
        public void No_Cap_Warning_When_The_Winner_Is_Below_The_Narrowed_Cap()
        {
            // The cap narrowed the maximum (2 instead of 24), but the search stopped short of it:
            // the grid did not decide the count.
            ApertureSolarTarget target = Target(2.0, 1.0);

            Assert.False(Analytical.SolarCalculator.Query.GridResolutionCapReached(
                Result("HorizontalLouvres", target, 0.5, new Dictionary<string, double> { { "Depth", 0.3 }, { "Count", 1 }, { "TiltDegrees", 0 } }),
                out string message));
            Assert.Null(message);
        }

        [Fact]
        public void No_Cap_Warning_When_The_Typology_Maximum_Governs()
        {
            // A 50 m tall opening at a 0.5 m grid: the resolution limit is 50/1 + 1 = 51, above the
            // declared 24, so the cap never narrowed anything. A winner at the typology maximum of
            // 24 is an ordinary design answer and says nothing about the grid.
            ApertureSolarTarget target = Target(2.0, 50.0);

            Assert.False(Analytical.SolarCalculator.Query.GridResolutionCapReached(
                Result("HorizontalLouvres", target, 0.5, new Dictionary<string, double> { { "Depth", 0.3 }, { "Count", 24 }, { "TiltDegrees", 0 } }),
                out string message));
            Assert.Null(message);
        }

        [Fact]
        public void No_Cap_Warning_When_The_Grid_Cap_Was_Not_Applied()
        {
            // No grid size supplied to the bounds: the declared maximum survives unchanged, so
            // reaching it is a typology answer.
            ApertureSolarTarget target = Target(2.0, 1.0);

            Assert.False(Analytical.SolarCalculator.Query.GridResolutionCapReached(
                Result("HorizontalLouvres", target, double.NaN, new Dictionary<string, double> { { "Depth", 0.3 }, { "Count", 24 }, { "TiltDegrees", 0 } }),
                out string message));
            Assert.Null(message);
        }

        [Fact]
        public void EggCrate_Cap_Warning_Tracks_Either_Count_Parameter()
        {
            // 2 m across, 1 m up, 0.5 m grid: louvre cap 2 (declared 16), fin cap 3 (declared 16).
            ApertureSolarTarget target = Target(2.0, 1.0);

            Assert.True(Analytical.SolarCalculator.Query.GridResolutionCapReached(
                Result("EggCrate", target, 0.5, new Dictionary<string, double> { { "Depth", 0.3 }, { "LouvreCount", 2 }, { "FinCount", 3 } }),
                out string message));
            Assert.NotNull(message);

            Assert.True(Analytical.SolarCalculator.Query.GridResolutionCapReached(
                Result("EggCrate", target, 0.5, new Dictionary<string, double> { { "Depth", 0.3 }, { "LouvreCount", 1 }, { "FinCount", 3 } }),
                out message));
            Assert.NotNull(message);

            // Below both caps: the grid decided nothing.
            Assert.False(Analytical.SolarCalculator.Query.GridResolutionCapReached(
                Result("EggCrate", target, 0.5, new Dictionary<string, double> { { "Depth", 0.3 }, { "LouvreCount", 1 }, { "FinCount", 2 } }),
                out message));
            Assert.Null(message);
        }

        // ------------------------------------------------------- existing behaviour ----

        [Fact]
        public void The_Default_Grid_Remains_Half_A_Metre_And_Is_Flagged_Against_The_Recommendation()
        {
            // The real controlled model, defaults untouched. The recommendation is guidance on top:
            // nothing about the default 0.5 m grid, the target list or the shared single grid may
            // have changed.
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MultiAzimuth.sam");
            Assert.True(File.Exists(path), $"Test fixture missing: {path}");

            AnalyticalModel analyticalModel = SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);
            Assert.NotNull(analyticalModel);

            // Default grid size: still 0.5 m cells, still one shared set of targets. The openings
            // are 1.0 m and 1.6667 m by 2.5 m, so at 0.5 m no cell can exceed a full 0.25 m2 cell
            // and every opening is fully sampled.
            List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(null);
            Assert.Equal(10, targets.Count);
            foreach (ApertureSolarTarget target in targets)
            {
                Assert.Equal(1.0, target.SampledAreaFraction, 9);
                Assert.All(target.AnalysisCells, x => Assert.InRange(x.Area, 0.0, 0.25 + 1e-9));
            }

            Assert.Contains(targets.SelectMany(x => x.AnalysisCells), x => Math.Abs(x.Area - 0.25) < 1e-9);

            // The fixture's openings are 1.0 m and 1.6667 m by 2.5 m, so the recommendation is the
            // design-grade 0.25 m — and the default 0.5 m grid is genuinely coarser than it.
            double recommended = Analytical.SolarCalculator.Query.RecommendedGridSize(targets);
            Assert.Equal(0.25, recommended, 12);
            Assert.True(Analytical.SolarCalculator.Query.CoarserThanRecommended(0.5, recommended, out string message));
            output.WriteLine(message);
        }

        [Fact]
        public void The_Recommendation_Does_Not_Depend_On_Or_Change_The_Supplied_Grid()
        {
            // The same model geometry sampled on two different grids must yield the same
            // recommendation — it is a property of the apertures and the design-grade guidance,
            // not of the grid the targets happened to be built at.
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MultiAzimuth.sam");
            AnalyticalModel analyticalModel = SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);

            List<ApertureSolarTarget> coarse = analyticalModel.ApertureSolarTargets(null, 0.5);
            List<ApertureSolarTarget> fine = analyticalModel.ApertureSolarTargets(null, 0.25);

            Assert.Equal(Analytical.SolarCalculator.Query.RecommendedGridSize(coarse), Analytical.SolarCalculator.Query.RecommendedGridSize(fine), 12);

            // And the calculation itself still runs at the supplied grid: the targets built at
            // 0.5 m keep 0.5 m cells (never larger than a full 0.25 m2 cell) after the
            // recommendation has been computed.
            double recommended = Analytical.SolarCalculator.Query.RecommendedGridSize(coarse);
            Assert.All(coarse.SelectMany(x => x.AnalysisCells), x => Assert.InRange(x.Area, 0.0, 0.25 + 1e-9));
            Assert.Equal(0.25, recommended, 12);
        }
    }
}
