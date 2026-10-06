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
using SAM.Geometry.Spatial;
using SAM.Weather;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Stage 10.2 Gate A: what the analysis grid can and cannot see, measured rather than asserted.
    ///
    /// THE DEFECT THIS FILE EXISTS FOR. Manual testing found that reducing GridSize made the ENERGY
    /// answer converge tightly while the recommended GEOMETRY did not, and that the optimiser kept
    /// choosing element spacing at whatever the grid happened to be — so the resolution warning
    /// never went away no matter how fine the grid got. That is not a warning that needed silencing.
    /// It was the optimiser being allowed to propose devices at exactly the spacing where the
    /// measurement stops working, and the warning correctly saying so every time.
    ///
    /// The root cause was two numbers that should have been one: Create.ShadingParameters capped
    /// element pitch at 1 x GridSize, while Query.ShadingResolution warned below 2 x GridSize. Any
    /// aperture where more elements kept helping drove the search onto its own cap, straight into
    /// the warning band — and because the cap is defined IN GRID SIZES, refining the grid moved the
    /// cap down with it and reproduced the same situation one scale finer.
    ///
    /// These tests pin the measurement the fix is justified by, and the rule that came out of it.
    /// </summary>
    public class ResolutionConvergenceTests
    {
        private readonly ITestOutputHelper output;

        public ResolutionConvergenceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static AnalyticalModel Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MultiAzimuth.sam");
            Assert.True(File.Exists(path), $"Test fixture missing: {path}");

            AnalyticalModel analyticalModel = SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);
            Assert.NotNull(analyticalModel);
            return analyticalModel;
        }

        private static int WeatherYear(AnalyticalModel analyticalModel)
        {
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            Assert.NotNull(weatherData);
            return weatherData.WeatherYears.First(x => x != null).Year;
        }

        /// <summary>One aperture on its own cell space, at a chosen grid.</summary>
        private static ApertureShadingSetup Setup(Guid apertureGuid, int year, double gridSize)
        {
            return Analytical.SolarCalculator.Create.ApertureShadingSetup(
                Load(), apertureGuid, year, null, null, null, null,
                new List<Guid> { apertureGuid }, gridSize, 2.0);
        }

        private static Guid Aperture(AnalyticalModel analyticalModel, double azimuth)
        {
            return analyticalModel.ApertureSolarTargets(null, 0.5)
                .First(x => Math.Abs(x.Azimuth - azimuth) < 1e-6).ApertureGuid;
        }

        private static ShadingPerformance Measure(ApertureShadingSetup setup, IShadingTypology device)
        {
            return Analytical.SolarCalculator.Create.ShadingPerformance(
                setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability,
                setup.Context.ContextOccluders, device, setup.CellIndexOffset);
        }

        /// <summary>
        /// Fins at a chosen absolute PITCH and PHASE.
        ///
        /// The production families space a fixed COUNT across the whole aperture, so pitch and grid
        /// cannot be varied independently and the phase between the two lattices cannot be moved at
        /// all. Both are exactly what has to be varied to separate a sampling artefact from a real
        /// change in the device, so the convergence study builds its own.
        /// </summary>
        private class PhasedFins : ShadingTypology
        {
            public PhasedFins(double depth, double pitch, double phase)
            {
                Define("Depth", depth, 0.01, 3.0);
                Define("Pitch", pitch, 0.001, 10.0);
                Define("Phase", phase, 0.0, 1.0);
            }

            public override string Name { get { return "PhasedFins"; } }

            public override List<ShadingElement> ShadingElements(ApertureSolarTarget target)
            {
                if (!Analytical.SolarCalculator.Query.TryGetApertureLocalBounds(target, out double minX, out double maxX, out double minY, out double maxY))
                {
                    return null;
                }

                double depth = GetParameter("Depth");
                double pitch = GetParameter("Pitch");
                double phase = GetParameter("Phase");
                Plane plane = target.Plane;

                List<ShadingElement> result = new List<ShadingElement>();
                int ordinal = 0;
                for (double x = minX + phase * pitch; x <= maxX + 1e-9; x += pitch)
                {
                    Face3D face3D = new Face3D(new Polygon3D(new List<Point3D>
                    {
                        World(plane, x, minY, 0), World(plane, x, maxY, 0),
                        World(plane, x, maxY, depth), World(plane, x, minY, depth),
                    }));

                    result.Add(new ShadingElement(ElementGuid(ordinal), "Fin " + ordinal, face3D));
                    ordinal++;
                }

                return result;
            }
        }

        // ------------------------------------------------------- the convergence measurement ----

        [Fact]
        [Trait("Category", "LongRunning")]
        public void One_Device_Measured_On_Coarser_Grids_Is_Worst_At_Exactly_One_Cell_Pitch()
        {
            // THE experiment behind Create.MinimumElementPitchInGridSizes. ONE physical device — same
            // pitch, same depth, same phase — measured on a series of grids so that pitch / GridSize
            // takes the values 1.0, 1.25, 1.5, 2.0 and 3.0, each compared against a common fine
            // reference. Because the device never changes, the difference IS the sampling error.
            //
            // A study that instead varies the element COUNT at a fixed grid cannot say this: it
            // changes the device and the sampling together, so a large discrepancy is not
            // attributable to either.
            AnalyticalModel model = Load();
            int year = WeatherYear(model);
            Guid apertureGuid = Aperture(model, 0.0);

            const double Pitch = 0.25;
            const double ReferenceGrid = 0.04;

            ApertureShadingSetup reference = Setup(apertureGuid, year, ReferenceGrid);
            Assert.NotNull(reference);

            output.WriteLine($"fins at {Pitch} m pitch, reference grid {ReferenceGrid} m ({reference.Target.CellCount} samples)");
            output.WriteLine("ratio  grid    phase  blocked%@grid  blocked%@reference   error (percentage points)");

            Dictionary<double, double> worstErrorPerRatio = new Dictionary<double, double>();

            foreach (double ratio in new double[] { 1.0, 1.25, 1.5, 2.0, 3.0 })
            {
                double gridSize = Pitch / ratio;
                ApertureShadingSetup setup = Setup(apertureGuid, year, gridSize);
                Assert.NotNull(setup);

                double worst = 0;
                foreach (double phase in new double[] { 0.0, 0.5 })
                {
                    IShadingTypology device = new PhasedFins(0.15, Pitch, phase);

                    double atGrid = 100.0 * Measure(setup, device).UnwantedSolarBlocked;
                    double atReference = 100.0 * Measure(reference, device).UnwantedSolarBlocked;
                    double error = atGrid - atReference;
                    worst = Math.Max(worst, Math.Abs(error));

                    output.WriteLine($"{ratio,-6:0.##} {gridSize,-7:0.####} {phase,-6:0.#} {atGrid,13:0.###} {atReference,19:0.###} {error,28:+0.###;-0.###;0}");
                }

                worstErrorPerRatio[ratio] = worst;
            }

            // 1. AT ONE CELL PER PITCH THE MEASUREMENT IS A COIN TOSS. There is exactly one sample
            //    per lit/shaded stripe period and, the lattices being commensurate, it sits at the
            //    same place in every period — so moving the device by half a pitch, which barely
            //    changes the physics, moves the reported figure enormously.
            Assert.True(worstErrorPerRatio[1.0] > 15.0,
                $"pitch = GridSize must be measurably unreliable, but the worst error was only {worstErrorPerRatio[1.0]:0.##} pp");

            // 2. AT THE MINIMUM FEATURE SIZE IT IS NOT. Two samples per period is the first ratio at
            //    which the estimator can return anything between "all lit" and "all shaded".
            Assert.True(worstErrorPerRatio[2.0] < 3.0,
                $"pitch = 2 x GridSize should measure closely, but the worst error was {worstErrorPerRatio[2.0]:0.##} pp");

            // 3. AND THE RULE IS AN ORDER-OF-MAGNITUDE ONE, not a fine tuning. Anything else would be
            //    reading precision into a single fixture.
            Assert.True(worstErrorPerRatio[1.0] > 5.0 * worstErrorPerRatio[2.0],
                "the case for the rule is that one cell per pitch is CATEGORICALLY worse, not marginally");

            // 4. THE ERROR DOES NOT FALL MONOTONICALLY WITH THE RATIO, and this is why the rule is
            //    justified by the degeneracy at ratio 1 rather than by picking the flattest point of
            //    a convergence curve — there is no such curve. Every ratio above 1 carries a real
            //    sampling error; only ratio 1 carries a structural one. Recording the fact here so a
            //    later reader does not "improve" the rule to 3 x GridSize expecting it to be better.
            Assert.True(worstErrorPerRatio[3.0] > worstErrorPerRatio[2.0],
                "on this fixture ratio 3 is not an improvement on ratio 2 — if that has changed, the rule's justification needs rereading, not the number bumping");

            foreach (double ratio in new double[] { 1.0, 1.25, 1.5, 2.0, 3.0 })
            {
                output.WriteLine($"worst error at pitch / GridSize = {ratio}: {worstErrorPerRatio[ratio]:0.###} pp");
            }
        }

        [Fact]
        public void Energy_Converges_Even_Where_The_Recommended_Geometry_Does_Not()
        {
            // The other half of the manual observation, and the reason the fix is about geometry
            // bounds rather than about the energy engine: measured on a device the analysis CAN see,
            // halving the grid barely moves the answer. Nothing was wrong with the physics.
            AnalyticalModel model = Load();
            int year = WeatherYear(model);
            Guid apertureGuid = Aperture(model, 0.0);

            // 6 fins over the 1 m width is a 0.2 m pitch: at or above the minimum feature size on
            // every grid below, so it is a device Stage 9 could legitimately propose.
            IShadingTypology device = new VerticalFins(0.15, 6, 0.0);

            double previous = double.NaN;
            foreach (double gridSize in new double[] { 0.1, 0.05, 0.04 })
            {
                ShadingPerformance performance = Measure(Setup(apertureGuid, year, gridSize), device);
                Assert.NotNull(performance);

                double blocked = 100.0 * performance.UnwantedSolarBlocked;
                output.WriteLine($"grid {gridSize:0.###} m: {blocked:0.###}% unwanted blocked, {performance.DirectSolarIntercepted:0.###} kWh intercepted");

                if (!double.IsNaN(previous))
                {
                    Assert.True(Math.Abs(blocked - previous) < 2.0,
                        $"refining the grid moved a resolvable device's answer by {Math.Abs(blocked - previous):0.##} pp");
                }

                previous = blocked;
            }
        }

        // --------------------------------------------------------------- the rule as applied ----

        [Fact]
        [Trait("Category", "LongRunning")]
        public void The_Optimiser_Cannot_Propose_A_Device_Its_Own_Warning_Would_Reject()
        {
            // THE REGRESSION FOR THE DEFECT ITSELF. Whatever the optimiser returns, the resolution
            // check must be satisfied by it — on every orientation, at every grid. Before Stage 10.2
            // this failed at every grid size tested, which is exactly what "the warning never goes
            // away by reducing GridSize" meant.
            AnalyticalModel model = Load();
            int year = WeatherYear(model);
            List<ApertureSolarTarget> targets = model.ApertureSolarTargets(null, 0.5);

            foreach (double azimuth in new double[] { 0.0, 90.0, 180.0, 270.0 })
            {
                Guid apertureGuid = targets.First(x => Math.Abs(x.Azimuth - azimuth) < 1e-6).ApertureGuid;

                foreach (double gridSize in new double[] { 0.5, 0.2 })
                {
                    ApertureShadingSetup setup = Setup(apertureGuid, year, gridSize);
                    Assert.NotNull(setup);

                    List<OptimisedShadingResult> results = Optimise.ShadingDevice(
                        setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability,
                        setup.Context.ContextOccluders, new ShadingObjective(1.0, 0.1), null,
                        Analytical.SolarCalculator.Create.ShadingTypologyNames, 60, setup.CellIndexOffset);

                    Assert.NotNull(results);
                    Assert.NotEmpty(results);

                    foreach (OptimisedShadingResult result in results)
                    {
                        IShadingTypology device = result.Typology();
                        ShadingResolutionState state = Analytical.SolarCalculator.Query.ShadingResolution(
                            device, setup.Target, gridSize, out string message, out double pitch);

                        Assert.True(state == ShadingResolutionState.Resolved,
                            $"az {azimuth} grid {gridSize}: {result.TypologyName} came back at {pitch:0.####} m pitch — {message}");
                    }

                    // And therefore the reported STATUS is never a resolution warning.
                    ShadingResolutionState best = Analytical.SolarCalculator.Query.ShadingResolution(
                        results[0].Typology(), setup.Target, gridSize, out string _, out double bestPitch);

                    ShadingDesignStatus status = Analytical.SolarCalculator.Query.DesignStatus(results[0], best);
                    Assert.NotEqual(ShadingDesignStatus.Warning, status);

                    output.WriteLine($"az {azimuth,3} grid {gridSize}: {Analytical.SolarCalculator.Query.StatusText(status),-9} " +
                        $"{results[0].TypologyName,-18} tightest pitch {bestPitch,7:0.####} m (minimum {Analytical.SolarCalculator.Create.MinimumElementPitchInGridSizes * gridSize:0.###} m)");
                }
            }
        }

        [Fact]
        public void Refining_The_Grid_Buys_A_Finer_Device_Instead_Of_Moving_The_Same_Problem_Down()
        {
            // The cap is defined in grid sizes, so halving the grid halves the permitted pitch. That
            // is the intended behaviour and is what makes "reduce GridSize to justify a finer device"
            // a true instruction. What must NOT come with it is the warning, which is the part
            // Stage 10.2 fixed.
            AnalyticalModel model = Load();
            Guid apertureGuid = Aperture(model, 0.0);
            ApertureSolarTarget target = model.ApertureSolarTargets(new List<Guid> { apertureGuid }, 0.5)[0];

            Analytical.SolarCalculator.Query.TryGetApertureLocalBounds(target, out double minX, out double maxX, out double _, out double _);
            double span = maxX - minX;

            double previousMaximum = 0;
            foreach (double gridSize in new double[] { 0.4, 0.2, 0.1, 0.05 })
            {
                List<ShadingParameter> parameters = Analytical.SolarCalculator.Create.ShadingParameters(
                    new VerticalFins(), double.NaN, target, gridSize);

                double maximum = parameters.Find(x => x.Name == "Count").Maximum;
                double pitch = maximum > 1 ? span / (maximum - 1) : double.NaN;

                output.WriteLine($"grid {gridSize:0.###} m: at most {maximum:0} fins, tightest pitch {pitch:0.####} m = {pitch / gridSize:0.##} x GridSize");

                // A finer grid never permits FEWER elements.
                Assert.True(maximum >= previousMaximum);
                previousMaximum = maximum;

                if (maximum > 1)
                {
                    // And whatever it permits is at or above the minimum feature size...
                    Assert.True(pitch >= Analytical.SolarCalculator.Create.MinimumElementPitchInGridSizes * gridSize - 1e-9);

                    // ...so the device at the cap reports as resolved rather than as a warning. This
                    // single assertion is the whole defect: it was false at every grid size before.
                    Assert.Equal(ShadingResolutionState.Resolved, Analytical.SolarCalculator.Query.ShadingResolution(
                        new VerticalFins(0.2, (int)maximum, 0.0), target, gridSize, out string _, out double _));
                }
            }

            // The cap really did move: a 0.05 m grid must justify more fins than a 0.4 m one, or
            // "refine the grid" would be advice with no effect.
            Assert.True(previousMaximum > 2);
        }

        [Fact]
        public void A_Hand_Typed_Device_Finer_Than_The_Grid_Is_Still_Warned_About()
        {
            // The bound was moved; the warning was NOT suppressed. Nothing caps a device a user types
            // in themselves, and that is the case the reporting side exists for.
            ApertureSolarTarget target = OptimisationFixture.SouthSeasonal().Target;

            // 2 m across, 1 m up. 11 louvres over 1 m is a 0.1 m pitch against a 0.25 m grid.
            Assert.Equal(ShadingResolutionState.BelowResolutionLimit, Analytical.SolarCalculator.Query.ShadingResolution(
                new HorizontalLouvres(0.3, 11), target, 0.25, out string below, out double _));
            Assert.NotNull(below);

            // 4 louvres over 1 m is 0.333 m: above one cell, below two.
            Assert.Equal(ShadingResolutionState.NearResolutionLimit, Analytical.SolarCalculator.Query.ShadingResolution(
                new HorizontalLouvres(0.3, 4), target, 0.25, out string near, out double _));
            Assert.NotNull(near);

            // 3 louvres over 1 m is 0.5 m — exactly the minimum feature size, and inside the rule.
            Assert.Equal(ShadingResolutionState.Resolved, Analytical.SolarCalculator.Query.ShadingResolution(
                new HorizontalLouvres(0.3, 3), target, 0.25, out string resolved, out double resolvedPitch));
            Assert.Null(resolved);
            Assert.Equal(0.5, resolvedPitch, 6);

            output.WriteLine(below);
            output.WriteLine(near);
        }

        [Fact]
        public void The_Bound_And_The_Warning_Are_One_Number()
        {
            // The defect was two constants that disagreed. This asserts they cannot drift apart
            // again: for a range of spans and grids, the tightest device the parameter cap permits is
            // always accepted by the resolution check, with no gap and no overlap.
            ApertureSolarTarget target = OptimisationFixture.SouthSeasonal().Target;
            Analytical.SolarCalculator.Query.TryGetApertureLocalBounds(target, out double _, out double _, out double minY, out double maxY);
            double span = maxY - minY;

            foreach (double gridSize in new double[] { 0.5, 0.3, 0.25, 0.2, 0.15, 0.1, 0.07, 0.05 })
            {
                List<ShadingParameter> parameters = Analytical.SolarCalculator.Create.ShadingParameters(
                    new HorizontalLouvres(), double.NaN, target, gridSize);

                int maximum = (int)parameters.Find(x => x.Name == "Count").Maximum;
                if (maximum <= 1)
                {
                    continue;
                }

                // The device AT the cap is resolved...
                Assert.Equal(ShadingResolutionState.Resolved, Analytical.SolarCalculator.Query.ShadingResolution(
                    new HorizontalLouvres(0.3, maximum), target, gridSize, out string _, out double atCap));

                // ...and one element more — which the cap forbids — is not. No gap between the two
                // rules in either direction.
                Assert.NotEqual(ShadingResolutionState.Resolved, Analytical.SolarCalculator.Query.ShadingResolution(
                    new HorizontalLouvres(0.3, maximum + 1), target, gridSize, out string _, out double justOver));

                output.WriteLine($"grid {gridSize:0.###} m: {maximum} louvres at {atCap:0.####} m resolved; {maximum + 1} at {justOver:0.####} m refused");
            }
        }
    }
}
