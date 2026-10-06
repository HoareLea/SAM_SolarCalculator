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
using SAM.Weather;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The GridSize prerequisite: a run that cannot be performed has to say so, and say why.
    ///
    /// THE DEFECT. Stage 10.2 found that a grid size below roughly 0.032 m produced NOTHING, in
    /// silence. Every candidate sample cell fell below the geometry area tolerance, so every
    /// aperture was skipped for having no cells, the target list came back empty, the solar context
    /// returned null, and the Grasshopper message that followed blamed the aperture — "check that it
    /// is an external sun-exposed aperture of THIS model" — when the real cause was a number typed
    /// into a completely different input.
    ///
    /// THE LIMIT IS ARITHMETIC, NOT AN OBSERVATION. An interior sample cell is a full gridSize
    /// square of area gridSize^2, and Query.AnalysisCells discards any piece below tolerance_Area.
    /// So the whole grid collapses exactly when gridSize &lt; sqrt(tolerance_Area). With SAM's
    /// default area tolerance of 1e-3 m^2 that is 0.0316... m. The 0.032 m from the Stage 10.2
    /// observation is that number rounded, and these tests derive it rather than repeat it.
    ///
    /// TWO DIFFERENT FAULTS, TWO DIFFERENT MESSAGES. A grid size below the limit is a bad number and
    /// is fixed by typing a different one — it is universal, and no model of any shape can be
    /// sampled below it. An opening below the area tolerance is a MODELLING problem and is fixed in
    /// the model. Collapsing them into one message would send the engineer to the wrong place.
    /// </summary>
    public class GridSizeValidityTests
    {
        private readonly ITestOutputHelper output;

        public GridSizeValidityTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>SAM's default area tolerance, the thing the whole limit derives from.</summary>
        private const double AreaTolerance = SAM.Core.Tolerance.MacroDistance;

        /// <summary>A square, axis-aligned vertical face of a given side, for the cell-level tests.</summary>
        private static Face3D Square(double side)
        {
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(side, 0, 0),
                new Point3D(side, 0, side),
                new Point3D(0, 0, side),
            }));
        }

        // ------------------------------------------------------------------ the limit ----

        [Fact]
        public void The_Minimum_Grid_Size_Is_The_Square_Root_Of_The_Area_Tolerance()
        {
            // Derived, not measured: a full cell is gridSize^2 and must clear tolerance_Area.
            Assert.Equal(Math.Sqrt(AreaTolerance), Analytical.SolarCalculator.Query.MinimumGridSize(AreaTolerance), 12);

            // Which, for SAM's default tolerance, is the ~0.032 m the Stage 10.2 study ran into.
            double minimum = Analytical.SolarCalculator.Query.MinimumGridSize();
            Assert.Equal(0.0316227766, minimum, 9);

            output.WriteLine($"area tolerance {AreaTolerance:0.######} m2 -> minimum grid size {minimum:0.########} m");

            // A different tolerance moves it, which is what makes it a rule rather than a constant.
            Assert.Equal(0.01, Analytical.SolarCalculator.Query.MinimumGridSize(1e-4), 12);
        }

        [Fact]
        public void The_Limit_Is_Exactly_Where_Analysis_Cells_Stop_Being_Produced()
        {
            // The claim tested against the thing it is a claim about. A 1 m square face, sampled
            // just above and just below the derived limit: above it produces cells, below it
            // produces none. If AnalysisCells ever changes its rejection rule, this fails.
            Face3D face3D = Square(1.0);
            double minimum = Analytical.SolarCalculator.Query.MinimumGridSize(AreaTolerance);

            List<AnalysisCell> above = SAM.Geometry.SolarCalculator.Query.AnalysisCells(face3D, minimum * 1.02, AreaTolerance);
            List<AnalysisCell> below = SAM.Geometry.SolarCalculator.Query.AnalysisCells(face3D, minimum * 0.98, AreaTolerance);

            Assert.NotNull(above);
            Assert.NotEmpty(above);

            // Below the limit every interior cell is discarded, so nothing survives.
            Assert.True(below == null || below.Count == 0,
                $"a grid of {minimum * 0.98:0.#####} m still produced {below?.Count ?? 0} cells; the derived limit is wrong");

            output.WriteLine($"grid {minimum * 1.02:0.#####} m -> {above.Count} cells; grid {minimum * 0.98:0.#####} m -> {below?.Count ?? 0} cells");
        }

        // ------------------------------------------------------------- the classification ----

        [Fact]
        public void Grid_Sizes_Are_Classified_And_Every_Refusal_Says_Why()
        {
            // Clearly valid.
            foreach (double gridSize in new double[] { 1.0, 0.5, 0.25, 0.1, 0.05, 0.04 })
            {
                Assert.Equal(GridSizeValidity.Valid, Analytical.SolarCalculator.Query.GridSizeValidity(gridSize, out string message));
                Assert.Null(message);
                Assert.True(Analytical.SolarCalculator.Query.IsValidGridSize(gridSize));
            }

            // Just above the limit — still valid, and this is the boundary that matters.
            double minimum = Analytical.SolarCalculator.Query.MinimumGridSize();
            Assert.Equal(GridSizeValidity.Valid, Analytical.SolarCalculator.Query.GridSizeValidity(minimum, out string _));
            Assert.Equal(GridSizeValidity.Valid, Analytical.SolarCalculator.Query.GridSizeValidity(minimum * 1.0001, out string _));

            // Just below — refused, and refused for the RIGHT reason.
            Assert.Equal(GridSizeValidity.BelowAreaTolerance, Analytical.SolarCalculator.Query.GridSizeValidity(minimum * 0.9999, out string tight));
            Assert.NotNull(tight);

            foreach (double gridSize in new double[] { 0.03, 0.01, 0.001, 1e-9 })
            {
                Assert.Equal(GridSizeValidity.BelowAreaTolerance, Analytical.SolarCalculator.Query.GridSizeValidity(gridSize, out string message));

                // The message must carry the number to type instead, and say the limit is not the
                // model's fault — otherwise the reader goes looking at the geometry.
                Assert.Contains("0.0316", message);
                Assert.Contains("area tolerance", message);
                Assert.Contains("not of the model", message);
                Assert.False(Analytical.SolarCalculator.Query.IsValidGridSize(gridSize));
            }

            // Zero and negative are a different fault: not a length at all.
            foreach (double gridSize in new double[] { 0.0, -0.5, -1e-9 })
            {
                Assert.Equal(GridSizeValidity.NotPositive, Analytical.SolarCalculator.Query.GridSizeValidity(gridSize, out string message));
                Assert.Contains("greater than zero", message);
            }

            // NaN and infinity, which a Grasshopper slider or an expression can genuinely produce.
            foreach (double gridSize in new double[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                Assert.Equal(GridSizeValidity.NotANumber, Analytical.SolarCalculator.Query.GridSizeValidity(gridSize, out string message));
                Assert.Contains("not a number", message);
            }

            output.WriteLine(tight);
        }

        // ------------------------------------------------- never silent, on a real model ----

        [Fact]
        public void A_Too_Fine_Grid_Size_Explains_Itself_Instead_Of_Returning_An_Empty_List()
        {
            // The defect, end to end, on the real controlled model.
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MultiAzimuth.sam");
            Assert.True(File.Exists(path), $"Test fixture missing: {path}");

            AnalyticalModel analyticalModel = SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);
            Assert.NotNull(analyticalModel);

            // A usable grid size works and says nothing.
            List<ApertureSolarTarget> good = analyticalModel.ApertureSolarTargets(null, 0.5, out string goodMessage);
            Assert.Equal(10, good.Count);
            Assert.Null(goodMessage);

            // A grid size below the limit returns null WITH A REASON, rather than an empty list.
            List<ApertureSolarTarget> tooFine = analyticalModel.ApertureSolarTargets(null, 0.01, out string tooFineMessage);
            Assert.Null(tooFine);
            Assert.NotNull(tooFineMessage);
            Assert.Contains("0.0316", tooFineMessage);
            output.WriteLine(tooFineMessage);

            // And the whole shading setup — the path the Grasshopper components actually take —
            // now names the cause instead of blaming the aperture.
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            int year = weatherData.WeatherYears.First(x => x != null).Year;
            Guid apertureGuid = good[0].ApertureGuid;

            ApertureShadingSetup setup = Analytical.SolarCalculator.Create.ApertureShadingSetup(
                analyticalModel, apertureGuid, year, out string setupMessage,
                null, null, null, null, null, 0.01, 2.0);

            Assert.Null(setup);
            Assert.NotNull(setupMessage);
            Assert.Contains("0.0316", setupMessage);

            // Specifically: it must NOT send the engineer looking at the aperture.
            Assert.DoesNotContain("external sun-exposed aperture", setupMessage);
            output.WriteLine(setupMessage);
        }

        [Fact]
        public void A_Valid_Grid_Size_Still_Builds_A_Setup_And_Says_Nothing()
        {
            // The diagnostic path must not have made the success path chattier or different.
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MultiAzimuth.sam");
            AnalyticalModel analyticalModel = SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            int year = weatherData.WeatherYears.First(x => x != null).Year;

            Guid apertureGuid = analyticalModel.ApertureSolarTargets(null, 0.5)[0].ApertureGuid;

            ApertureShadingSetup setup = Analytical.SolarCalculator.Create.ApertureShadingSetup(
                analyticalModel, apertureGuid, year, out string message,
                null, null, null, null, new List<Guid> { apertureGuid }, 0.5, 2.0);

            Assert.NotNull(setup);
            Assert.Null(message);
            Assert.Equal(apertureGuid, setup.Target.ApertureGuid);

            // No diagnostic reason exists when nothing failed.
            Assert.Null(Analytical.SolarCalculator.Create.ApertureSolarContextFailureReason(
                analyticalModel, new List<Guid> { apertureGuid }, 0.5));
        }

        [Fact]
        public void An_Unknown_Aperture_Is_Told_Apart_From_A_Bad_Grid_Size()
        {
            // Both used to produce the same null and the same sentence. They are different problems.
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MultiAzimuth.sam");
            AnalyticalModel analyticalModel = SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            int year = weatherData.WeatherYears.First(x => x != null).Year;

            ApertureShadingSetup setup = Analytical.SolarCalculator.Create.ApertureShadingSetup(
                analyticalModel, Guid.NewGuid(), year, out string message,
                null, null, null, null, null, 0.5, 2.0);

            Assert.Null(setup);
            Assert.NotNull(message);

            // This one IS about the aperture, and says so — and says nothing about the tolerance.
            Assert.Contains("not one of the", message);
            Assert.DoesNotContain("area tolerance", message);
            output.WriteLine(message);
        }

        // ------------------------------------------------------------ small apertures ----

        [Fact]
        public void A_Small_But_Valid_Aperture_Is_Sampled_Rather_Than_Refused()
        {
            // The limit is on the GRID, not on the opening. A 0.2 m square opening is far smaller
            // than the default grid and still yields a sample, because the candidate cell is clipped
            // to the opening and 0.04 m2 clears the tolerance comfortably.
            List<AnalysisCell> cells = SAM.Geometry.SolarCalculator.Query.AnalysisCells(Square(0.2), 0.5, AreaTolerance);

            Assert.NotNull(cells);
            Assert.NotEmpty(cells);
            output.WriteLine($"0.2 m square opening at a 0.5 m grid -> {cells.Count} sample(s), total area {cells.Sum(x => x.Area):0.####} m2");

            // A 0.1 m opening (0.01 m2) too — ten times the tolerance.
            Assert.NotEmpty(SAM.Geometry.SolarCalculator.Query.AnalysisCells(Square(0.1), 0.5, AreaTolerance));
        }

        [Fact]
        public void An_Aperture_Below_The_Area_Tolerance_Gets_Its_Own_Message()
        {
            // A 20 x 20 mm opening is 4e-4 m2, below the 1e-3 m2 tolerance, and can never be sampled
            // at ANY grid size. Telling that engineer to change the grid size would waste their time:
            // the fix is in the model, or there is no fix.
            Assert.Empty(SAM.Geometry.SolarCalculator.Query.AnalysisCells(Square(0.02), 0.5, AreaTolerance) ?? new List<AnalysisCell>());

            string message = Analytical.SolarCalculator.Query.EmptyAnalysisCellsMessage(0.02 * 0.02, 0.5, AreaTolerance);
            Assert.Contains("cannot be sampled at ANY analysis grid size", message);
            Assert.Contains("Check the opening in the model", message);
            output.WriteLine(message);

            // An opening that is big enough, but produced nothing for a geometric reason, gets the
            // other message — which does suggest a finer grid, because that can genuinely help.
            string sliver = Analytical.SolarCalculator.Query.EmptyAnalysisCellsMessage(2.0, 0.5, AreaTolerance);
            Assert.Contains("narrow sliver", sliver);
            Assert.DoesNotContain("cannot be sampled at ANY", sliver);
            output.WriteLine(sliver);
        }

        [Fact]
        public void Cells_Are_Produced_Right_Down_To_The_Limit_On_A_Normal_Aperture()
        {
            // The rule has to hold across the whole usable range, not just at one point — otherwise
            // "0.0316 m or coarser" is advice that fails somewhere in the middle.
            Face3D face3D = Square(1.0);

            foreach (double gridSize in new double[] { 1.0, 0.5, 0.25, 0.1, 0.05, 0.04, 0.035, 0.0317 })
            {
                Assert.True(Analytical.SolarCalculator.Query.IsValidGridSize(gridSize));

                List<AnalysisCell> cells = SAM.Geometry.SolarCalculator.Query.AnalysisCells(face3D, gridSize, AreaTolerance);
                Assert.NotNull(cells);
                Assert.NotEmpty(cells);

                double covered = cells.Sum(x => x.Area);
                output.WriteLine($"grid {gridSize,-7:0.#####} m -> {cells.Count,5} samples, area {covered:0.######} m2 ({100.0 * covered:0.##} % of the opening)");
            }
        }

        [Fact]
        public void The_Sampled_Area_Loses_The_Edge_Strip_When_The_Grid_Does_Not_Divide_The_Opening()
        {
            // A REAL EFFECT, found by this test and measured rather than asserted away.
            //
            // AnalysisCells lays a lattice from the opening's bounding-box minimum and clips each
            // cell to the opening. When the grid does not divide the opening exactly, the last
            // column and row are REMAINDER STRIPS of width r = span mod gridSize. Those strips are
            // kept like any other cell — unless their clipped area falls below tolerance_Area, in
            // which case they are discarded like any other degenerate sliver, and the opening is
            // then sampled slightly SMALLER than it is.
            //
            // The condition is r x gridSize < tolerance_Area, i.e. r < tolerance_Area / gridSize.
            // At the default 0.5 m grid that means a strip narrower than 2 mm — genuinely
            // negligible. Near the 0.0316 m limit it means a strip up to a full cell wide, so the
            // loss grows as the grid is refined. That is the opposite of what a reader expects from
            // "finer is more accurate", which is exactly why it is measured here and stated in the
            // assumptions register rather than left to be discovered on a project.
            //
            // It is NOT a defect in the physics. Area-weighting is consistent: every reported
            // percentage is a ratio over the same cell set and is unaffected. What moves is an
            // absolute ENERGY total, in proportion to the sampled area.
            Face3D face3D = Square(1.0);

            double worstLoss = 0;
            double worstGrid = double.NaN;

            foreach (double gridSize in new double[] { 0.5, 0.25, 0.2, 0.1, 0.05, 0.04, 0.035, 0.033, 0.0317 })
            {
                List<AnalysisCell> cells = SAM.Geometry.SolarCalculator.Query.AnalysisCells(face3D, gridSize, AreaTolerance);
                double covered = cells.Sum(x => x.Area);
                double loss = 100.0 * (1.0 - covered);

                double remainder = 1.0 - Math.Floor(1.0 / gridSize) * gridSize;
                bool stripDropped = remainder > SAM.Core.Tolerance.Distance && remainder * gridSize < AreaTolerance;

                output.WriteLine($"grid {gridSize,-7:0.#####} m: remainder {remainder,-8:0.#####} m, strip {(stripDropped ? "DROPPED" : "kept   ")}, sampled {covered:0.######} m2, loss {loss,6:0.###} %");

                if (loss > worstLoss)
                {
                    worstLoss = loss;
                    worstGrid = gridSize;
                }

                // Whatever else is true, the sampled area never EXCEEDS the opening — an
                // over-count would be a real error, an under-count is the tolerance working.
                Assert.True(covered <= 1.0 + 1e-9);

                // And the predicted behaviour matches the measured one: the loss is there exactly
                // when the remainder strip is too thin to clear the area tolerance.
                if (!stripDropped)
                {
                    Assert.Equal(1.0, covered, 6);
                }
            }

            output.WriteLine($"worst sampled-area loss over the tested range: {worstLoss:0.###} % at gridSize {worstGrid:0.#####} m");

            // At the DEFAULT grid size and every ordinary refinement of it, there is no loss at all
            // on an opening that the grid divides. This is the case that matters in practice.
            foreach (double gridSize in new double[] { 0.5, 0.25, 0.2, 0.1, 0.05 })
            {
                Assert.Equal(1.0, SAM.Geometry.SolarCalculator.Query.AnalysisCells(face3D, gridSize, AreaTolerance).Sum(x => x.Area), 6);
            }
        }

        [Fact]
        public void The_Controlled_Model_Is_Fully_Sampled_At_The_Grid_Sizes_Stage_11_Uses()
        {
            // The effect above, checked where it actually matters: the real fixture, at the grid
            // sizes the validation studies and the recommended settings use. If any of these lost
            // coverage, every absolute energy in Stage 11 would carry a quiet bias.
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MultiAzimuth.sam");
            AnalyticalModel analyticalModel = SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);
            Assert.NotNull(analyticalModel);

            // The fixture's openings are 1.0 m and 1.6667 m across by 2.5 m up. The grid sizes below
            // divide both, so nothing is lost — and Stage 11's absolute energies are unbiased at
            // every one of them. 0.05 m is deliberately EXCLUDED and checked separately below,
            // because it does not divide 1.6667 m and the loss there is real.
            foreach (double gridSize in new double[] { 1.0, 0.5, 0.25, 0.2, 0.1 })
            {
                List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(null, gridSize);
                Assert.Equal(10, targets.Count);

                double worst = 0;
                foreach (ApertureSolarTarget target in targets)
                {
                    worst = Math.Max(worst, Math.Abs(1.0 - target.SampledAreaFraction));
                }

                output.WriteLine($"grid {gridSize,-5:0.###} m: worst sampled-area loss across 10 apertures {100.0 * worst:0.####} %");

                Assert.True(worst < 1e-9,
                    $"at gridSize {gridSize} the fixture loses {100.0 * worst:0.####} % of its aperture area to the tolerance, which would bias every absolute energy in the Stage 11 studies");
            }

            // 0.05 m: the 1.6667 m wide openings leave a 0.0167 m strip whose area (8.3e-4 m2) is
            // below the 1e-3 m2 tolerance, so it is dropped and those openings are sampled ~1 % small.
            // Recorded as a measured number rather than hidden, and it is why the recommended
            // settings say to prefer a grid that divides the opening.
            List<ApertureSolarTarget> fine = analyticalModel.ApertureSolarTargets(null, 0.05);
            double worstFine = 0;
            foreach (ApertureSolarTarget target in fine)
            {
                worstFine = Math.Max(worstFine, 1.0 - target.SampledAreaFraction);
            }

            output.WriteLine($"grid 0.05  m: worst sampled-area loss across 10 apertures {100.0 * worstFine:0.####} % (the 1.6667 m openings; 1.0 m openings are unaffected)");
            Assert.InRange(100.0 * worstFine, 0.5, 1.5);
        }
    }
}
