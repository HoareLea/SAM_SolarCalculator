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
    /// The controlled multi-orientation model, run through the whole Grasshopper chain in code.
    ///
    /// MultiAzimuth.sam is the model Stage 10.1 was opened on: one space, six panels, ten apertures
    /// built with SAMAnalytical.AddAperturesByAzimuths, weather embedded. It is deliberately
    /// synthetic — four cardinal orientations, several apertures per orientation — which is exactly
    /// what makes it a good regression fixture: the answers per orientation are distinguishable, and
    /// apertures that share an orientation must agree with each other while differing from the rest.
    ///
    /// MultiApertureShadingTests proves the cell-space contract on a synthetic cache. This file
    /// proves the same property through the REAL entry points a Grasshopper user reaches —
    /// ApertureSolarTargets, ApertureShadingSetup, ShadingPotentialField, the optimiser and the
    /// verifier — on a real exported model, because a contract that holds in a unit fixture and
    /// fails through Create.ApertureSolarContext would have shipped.
    /// </summary>
    public class MultiAzimuthModelTests
    {
        private readonly ITestOutputHelper output;

        public MultiAzimuthModelTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const double GridSize = 0.5;
        private const double SunAngleStep = 2.0;

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

        // ------------------------------------------------------------------ the targets ----

        [Fact]
        public void The_Model_Presents_Ten_Sun_Exposed_Apertures_Across_Four_Orientations()
        {
            AnalyticalModel analyticalModel = Load();

            List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(null, GridSize);
            Assert.Equal(10, targets.Count);

            // Every target must have a usable outward frame, or nothing downstream means anything:
            // the local frame is what devices are built in and what azimuth is read from.
            foreach (ApertureSolarTarget target in targets)
            {
                Assert.NotNull(target.Plane);

                Vector3D outward = target.OutwardNormal;
                Assert.NotNull(outward);
                Assert.True(outward.IsValid());
                Assert.Equal(1.0, outward.Length, 6);

                // Vertical windows: the outward normal has no vertical component.
                Assert.Equal(90.0, target.Tilt, 3);
                Assert.Equal(0.0, outward.Z, 6);

                Assert.True(target.GrossArea > 0);
                Assert.True(target.CellCount > 0);
                Assert.NotEqual(Guid.Empty, target.ApertureGuid);
            }

            // Four cardinal orientations, all present.
            List<double> azimuths = targets.ConvertAll(x => Math.Round(x.Azimuth, 3));
            foreach (double cardinal in new double[] { 0.0, 90.0, 180.0, 270.0 })
            {
                Assert.Contains(cardinal, azimuths);
            }

            Assert.Equal(4, new HashSet<double>(azimuths).Count);

            // Distinct apertures, so nothing downstream can conflate two of them.
            Assert.Equal(10, new HashSet<Guid>(targets.ConvertAll(x => x.ApertureGuid)).Count);

            foreach (ApertureSolarTarget target in targets)
            {
                output.WriteLine($"{target.ApertureGuid} az {target.Azimuth,5:0} area {target.GrossArea,5:0.00} m2 samples {target.CellCount}");
            }
        }

        [Fact]
        public void The_Shared_Cell_Space_Covers_Every_Aperture_In_Order()
        {
            AnalyticalModel analyticalModel = Load();
            int year = WeatherYear(analyticalModel);

            ApertureSolarContext context = Analytical.SolarCalculator.Create.ApertureSolarContext(
                analyticalModel, year, gridSize: GridSize, sunAngleStep: SunAngleStep);

            Assert.NotNull(context);
            Assert.Equal(10, context.TargetCount);

            // This is the condition that used to break everything downstream: with ten apertures the
            // shared cell space is far larger than any one of them.
            int expectedOffset = 0;
            foreach (ApertureSolarTarget target in context.Targets)
            {
                Assert.Equal(expectedOffset, context.CellIndexOffset(target.ApertureGuid));
                Assert.True(target.CellCount < context.Cells.Count);
                expectedOffset += target.CellCount;
            }

            Assert.Equal(expectedOffset, context.Cells.Count);
            Assert.Equal(context.Cells.Count, context.SolarVisibilityCache.CellCount);

            output.WriteLine($"{context.TargetCount} apertures, {context.Cells.Count} sample points, " +
                $"{context.SolarVisibilityCache.BinCount} sun groups");
        }

        // ----------------------------------------------- the chain, on every orientation ----

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Every_Orientation_Produces_A_Field_A_Device_And_A_Verified_Result()
        {
            // The end-to-end reproduction of the manual test that failed: ApertureSolarTargets ->
            // ShadingPotentialField -> RationaliseShading -> VerifyShading, for all ten apertures of
            // a ten-aperture model. Before Stage 10.1 every one of these reported "no shading
            // recommended" without measuring anything, and the verification returned null.
            AnalyticalModel analyticalModel = Load();
            int year = WeatherYear(analyticalModel);

            List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(null, GridSize);
            Dictionary<double, string> summaryPerAzimuth = new Dictionary<double, string>();

            foreach (ApertureSolarTarget target in targets)
            {
                ApertureShadingSetup setup = Analytical.SolarCalculator.Create.ApertureShadingSetup(
                    analyticalModel, target.ApertureGuid, year, gridSize: GridSize, sunAngleStep: SunAngleStep);

                Assert.NotNull(setup);
                Assert.Equal(target.ApertureGuid, setup.Target.ApertureGuid);

                ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(setup.Target, 1.0, 0.5, 0.0, 0.3, 0.3, 0.1);
                ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(
                    setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability, volume, setup.CellIndexOffset);

                Assert.NotNull(field);
                Assert.Equal(target.ApertureGuid, field.ApertureGuid);

                List<OptimisedShadingResult> results = Optimise.ShadingDevice(
                    setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability, setup.Context.ContextOccluders,
                    new ShadingObjective(1.0, 0.1), field, new string[] { "Overhang" }, 40, setup.CellIndexOffset);

                Assert.NotNull(results);
                Assert.NotEmpty(results);

                OptimisedShadingResult best = results[0];

                // Whatever the answer is, it was MEASURED. That is the distinction the defect erased.
                Assert.NotEqual(ShadingOptimisationTermination.EvaluationFailed, best.Termination);
                Assert.False(double.IsNaN(best.ObjectiveScore));
                Assert.False(double.IsNaN(best.AdmittedDirectEnergy));
                Assert.Equal(target.ApertureGuid, best.ApertureGuid);

                // "No shading" is a legitimate outcome here and is not a failure; either way the
                // recommendation verifies.
                IShadingTypology recommended = best.RecommendsNoShading ? new NoShading() : best.Typology();
                Assert.NotNull(recommended);

                ShadingPerformance verified = Analytical.SolarCalculator.Create.ShadingPerformance(
                    setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability,
                    setup.Context.ContextOccluders, recommended, setup.CellIndexOffset);

                Assert.NotNull(verified);
                Assert.Equal(target.ApertureGuid, verified.ApertureGuid);
                Assert.False(double.IsNaN(verified.AdmittedDirectEnergy));

                if (!best.RecommendsNoShading)
                {
                    // The verifier must reproduce what the optimiser reported for the same design.
                    Assert.Equal(best.DirectSolarIntercepted, verified.DirectSolarIntercepted, 9);
                    Assert.Equal(best.UnwantedSolarIntercepted, verified.UnwantedSolarIntercepted, 9);
                    Assert.Equal(best.WantedSolarBlocked, verified.WantedSolarBlocked, 9);
                }

                string summary = Analytical.SolarCalculator.Query.DesignSummary(best, target.Azimuth);
                string status = Analytical.SolarCalculator.Query.StatusText(Analytical.SolarCalculator.Query.DesignStatus(best));
                output.WriteLine($"{status,-13} {summary}");

                // Apertures of the SAME orientation are the same window as far as the sun is
                // concerned, so they must reach the same design. Apertures of different
                // orientations must not.
                double azimuth = Math.Round(target.Azimuth, 3);
                if (summaryPerAzimuth.TryGetValue(azimuth, out string existing))
                {
                    Assert.Equal(existing, summary);
                }
                else
                {
                    summaryPerAzimuth[azimuth] = summary;
                }
            }

            Assert.Equal(4, summaryPerAzimuth.Count);
            Assert.Equal(4, new HashSet<string>(summaryPerAzimuth.Values).Count);
        }

        [Fact]
        public void One_Aperture_Of_Ten_Measures_Exactly_As_That_Aperture_Alone()
        {
            // Gate A of the Stage 10.1 brief, as a test: if batch and individual disagree the cause
            // is data matching; if they agree, the earlier failure was never about Grasshopper trees
            // at all. They agree — to the last decimal place — which is what identifies the defect
            // as the shared cell space rather than the canvas.
            AnalyticalModel batchModel = Load();
            int year = WeatherYear(batchModel);

            List<ApertureSolarTarget> targets = batchModel.ApertureSolarTargets(null, GridSize);

            // One representative aperture per cardinal orientation.
            List<ApertureSolarTarget> representatives = new List<ApertureSolarTarget>();
            foreach (double cardinal in new double[] { 0.0, 90.0, 180.0, 270.0 })
            {
                ApertureSolarTarget target = targets.Find(x => Math.Abs(x.Azimuth - cardinal) < 1e-6);
                Assert.NotNull(target);
                representatives.Add(target);
            }

            foreach (ApertureSolarTarget target in representatives)
            {
                // BATCH: the whole model's apertures share one cell space, as Grasshopper builds it.
                ApertureShadingSetup batch = Analytical.SolarCalculator.Create.ApertureShadingSetup(
                    batchModel, target.ApertureGuid, year, gridSize: GridSize, sunAngleStep: SunAngleStep);

                // INDIVIDUAL: a context scoped to this aperture alone, on a fresh copy of the model
                // so the two calculations cannot share stored state.
                AnalyticalModel singleModel = Load();
                ApertureShadingSetup single = Analytical.SolarCalculator.Create.ApertureShadingSetup(
                    singleModel, target.ApertureGuid, year, null, null, null, null,
                    new List<Guid> { target.ApertureGuid }, GridSize, SunAngleStep);

                Assert.NotNull(batch);
                Assert.NotNull(single);
                Assert.True(batch.CellIndexOffset >= 0);
                Assert.Equal(0, single.CellIndexOffset);
                Assert.True(batch.Context.SolarVisibilityCache.CellCount > single.Context.SolarVisibilityCache.CellCount,
                    "the batch cache must be the larger one, or this proves nothing");

                IShadingTypology device = new Overhang(0.5, 0.0, 0.1);

                ShadingPerformance batchPerformance = Analytical.SolarCalculator.Create.ShadingPerformance(
                    batch.Target, batch.Context.SolarVisibilityCache, batch.Desirability,
                    batch.Context.ContextOccluders, device, batch.CellIndexOffset);

                ShadingPerformance singlePerformance = Analytical.SolarCalculator.Create.ShadingPerformance(
                    single.Target, single.Context.SolarVisibilityCache, single.Desirability,
                    single.Context.ContextOccluders, device, single.CellIndexOffset);

                Assert.NotNull(batchPerformance);
                Assert.NotNull(singlePerformance);

                Assert.Equal(singlePerformance.AdmittedDirectEnergy, batchPerformance.AdmittedDirectEnergy, 9);
                Assert.Equal(singlePerformance.AdmittedUnwantedEnergy, batchPerformance.AdmittedUnwantedEnergy, 9);
                Assert.Equal(singlePerformance.AdmittedWantedEnergy, batchPerformance.AdmittedWantedEnergy, 9);
                Assert.Equal(singlePerformance.DirectSolarIntercepted, batchPerformance.DirectSolarIntercepted, 9);
                Assert.Equal(singlePerformance.UnwantedSolarIntercepted, batchPerformance.UnwantedSolarIntercepted, 9);
                Assert.Equal(singlePerformance.WantedSolarBlocked, batchPerformance.WantedSolarBlocked, 9);

                output.WriteLine($"az {target.Azimuth,5:0}: batch (offset {batch.CellIndexOffset}, " +
                    $"{batch.Context.SolarVisibilityCache.CellCount} shared cells) == alone " +
                    $"({single.Context.SolarVisibilityCache.CellCount} cells): " +
                    $"{batchPerformance.DirectSolarIntercepted:0.###} kWh intercepted");
            }
        }

        [Fact]
        public void A_Device_Sized_For_One_Orientation_Is_Refused_On_Another()
        {
            // No cross-aperture pairing. The device carries its aperture, and the check that
            // VerifyShading performs is asserted here on the real model rather than only described
            // in the component.
            AnalyticalModel analyticalModel = Load();
            int year = WeatherYear(analyticalModel);

            List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(null, GridSize);
            ApertureSolarTarget south = targets.Find(x => Math.Abs(x.Azimuth - 180.0) < 1e-6);
            ApertureSolarTarget north = targets.Find(x => Math.Abs(x.Azimuth - 0.0) < 1e-6);
            Assert.NotNull(south);
            Assert.NotNull(north);

            ApertureShadingSetup setup = Analytical.SolarCalculator.Create.ApertureShadingSetup(
                analyticalModel, south.ApertureGuid, year, gridSize: GridSize, sunAngleStep: SunAngleStep);

            List<OptimisedShadingResult> results = Optimise.ShadingDevice(
                setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability, setup.Context.ContextOccluders,
                new ShadingObjective(1.0, 0.1), null, new string[] { "Overhang" }, 24, setup.CellIndexOffset);

            Assert.NotEmpty(results);

            ShadingDevice southDevice = new ShadingDevice(results[0].ApertureGuid, results[0].Typology());

            // This is the comparison SAMAnalytical.VerifyShading makes before it measures anything.
            Assert.Equal(south.ApertureGuid, southDevice.ApertureGuid);
            Assert.NotEqual(north.ApertureGuid, southDevice.ApertureGuid);

            // And it survives the round-trip a saved Grasshopper file puts it through.
            ShadingDevice reloaded = Core.Create.IJSAMObject<ShadingDevice>(southDevice.ToJsonObject().ToJsonString());
            Assert.Equal(south.ApertureGuid, reloaded.ApertureGuid);
            Assert.NotEqual(north.ApertureGuid, reloaded.ApertureGuid);
        }
    }
}
