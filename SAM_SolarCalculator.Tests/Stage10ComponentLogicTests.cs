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
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Stage 10 tests: the behaviour the Grasshopper components promise, tested where it actually
    /// lives.
    ///
    /// The components themselves are deliberately thin — they read wires, call one of these
    /// methods, and write wires — because a Grasshopper component cannot be instantiated outside
    /// Rhino and anything hidden inside one would be untestable. Everything a user could get wrong
    /// (which input wins, what the default is, when work is reused, when a number is unavailable,
    /// when a device is finer than the analysis can see) is therefore decided in code that runs
    /// here.
    ///
    /// What is NOT covered, and needs the manual checklist: parameter visibility, the preview,
    /// and Grasshopper's own casting.
    /// </summary>
    public class Stage10ComponentLogicTests
    {
        private readonly ITestOutputHelper output;

        public Stage10ComponentLogicTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        private static AnalyticalModel Load(string fileName)
        {
            string path = Path.Combine(FixturesDirectory, fileName);
            Assert.True(File.Exists(path), $"Test fixture missing: {path}");

            List<AnalyticalModel> analyticalModels = SAM.Core.Convert.ToSAM<AnalyticalModel>(path);
            AnalyticalModel analyticalModel = analyticalModels?.FirstOrDefault(x => x != null);
            Assert.NotNull(analyticalModel);
            return analyticalModel;
        }

        private static int WeatherYearOf(AnalyticalModel analyticalModel)
        {
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            Assert.NotNull(weatherData);
            return weatherData.WeatherYears.First(x => x != null).Year;
        }

        // ------------------------------------------------------------------ hour precedence ----

        [Fact]
        public void ExplicitHours_Override_AnalysisPeriod_And_Say_So()
        {
            AnalysisPeriod summer = new AnalysisPeriod(2018, 6, 1, 8, 31);
            List<int> hours = new List<int> { 10, 11, 12 };

            AnalysisPeriod resolved = Core.SolarCalculator.Create.ResolvedAnalysisPeriod(2018, summer, hours, out bool overrode);

            Assert.True(overrode, "the component must be able to tell the user the period was overridden");
            Assert.Equal(hours, resolved.HoursOfYear());
        }

        [Fact]
        public void AnalysisPeriod_Used_When_No_Hours_Are_Supplied()
        {
            AnalysisPeriod summer = new AnalysisPeriod(2018, 6, 1, 8, 31);

            AnalysisPeriod resolved = Core.SolarCalculator.Create.ResolvedAnalysisPeriod(2018, summer, null, out bool overrode);
            Assert.False(overrode);
            Assert.Equal(summer.HoursOfYear().Count, resolved.HoursOfYear().Count);

            // An empty list is not a request for anything: it must not override either.
            resolved = Core.SolarCalculator.Create.ResolvedAnalysisPeriod(2018, summer, new List<int>(), out overrode);
            Assert.False(overrode);
            Assert.Equal(summer.HoursOfYear().Count, resolved.HoursOfYear().Count);
        }

        [Fact]
        public void FullYear_Is_The_Default_When_Nothing_Is_Supplied()
        {
            AnalysisPeriod resolved = Core.SolarCalculator.Create.ResolvedAnalysisPeriod(2018, null, null, out bool overrode);

            Assert.False(overrode);
            Assert.Equal(8760, resolved.HoursOfYear().Count);
        }

        // -------------------------------------------------------------- resolution warning ----

        [Fact]
        public void Element_Spacing_Below_The_Grid_Is_Reported_As_Unreliable()
        {
            // 2 m x 1 m window: louvres spread over the 1 m height.
            ApertureSolarTarget target = OptimisationFixture.SouthSeasonal().Target;

            // 11 blades over 1 m is a 0.1 m pitch against a 0.25 m grid: the analysis cannot see
            // the gaps at all. This is the Stage 9 case that reported 100 % blocked and 100 %
            // retained at the same time.
            ShadingResolutionState state = Analytical.SolarCalculator.Query.ShadingResolution(
                new HorizontalLouvres(0.3, 11), target, 0.25, out string message, out double pitch);

            Assert.Equal(ShadingResolutionState.BelowResolutionLimit, state);
            Assert.Contains("at or below the analysis grid", message);
            Assert.Contains("at most one sample point per gap", message);

            // The message has to say what to DO, and both routes out are stated: space the elements
            // further apart, or refine the grid to the size that would resolve this spacing.
            Assert.Contains("reduce GridSize to at most 0.05 m", message);
            Assert.Equal(0.1, pitch, 6);
            output.WriteLine(message);
        }

        [Fact]
        public void Element_Spacing_Near_The_Grid_Is_Reported_As_Near_The_Limit()
        {
            ApertureSolarTarget target = OptimisationFixture.SouthSeasonal().Target;

            // 4 blades over 1 m is a 0.333 m pitch: above the 0.25 m grid, but inside twice it.
            ShadingResolutionState state = Analytical.SolarCalculator.Query.ShadingResolution(
                new HorizontalLouvres(0.3, 4), target, 0.25, out string message, out double pitch);

            Assert.Equal(ShadingResolutionState.NearResolutionLimit, state);
            Assert.Contains("below the minimum feature size", message);
            Assert.InRange(pitch, 0.3, 0.34);
            output.WriteLine(message);
        }

        [Fact]
        public void A_Single_Element_And_A_Coarse_Array_Have_Nothing_To_Report()
        {
            ApertureSolarTarget target = OptimisationFixture.SouthSeasonal().Target;

            // An overhang has no repeated element, so it has no pitch to resolve.
            Assert.Equal(ShadingResolutionState.Resolved, Analytical.SolarCalculator.Query.ShadingResolution(new Overhang(0.5), target, 0.25, out string overhangMessage, out double overhangPitch));
            Assert.Null(overhangMessage);
            Assert.True(double.IsNaN(overhangPitch));

            // Two blades over 1 m is a 1 m pitch against a 0.25 m grid: comfortably resolved.
            Assert.Equal(ShadingResolutionState.Resolved, Analytical.SolarCalculator.Query.ShadingResolution(new HorizontalLouvres(0.3, 2), target, 0.25, out string louvreMessage, out double _));
            Assert.Null(louvreMessage);
        }

        [Fact]
        public void Fin_Spacing_Is_Measured_Across_The_Window_Not_Up_It()
        {
            // The south window is 2 m across and 1 m tall. Fins run ACROSS, so 5 fins is a 0.5 m
            // pitch (resolved at a 0.25 m grid); 5 louvres would be a 0.25 m pitch (not resolved).
            ApertureSolarTarget target = OptimisationFixture.SouthSeasonal().Target;

            Assert.Equal(ShadingResolutionState.Resolved, Analytical.SolarCalculator.Query.ShadingResolution(new VerticalFins(0.3, 5), target, 0.25, out string _, out double finPitch));
            Assert.Equal(0.5, finPitch, 6);

            Assert.Equal(ShadingResolutionState.BelowResolutionLimit, Analytical.SolarCalculator.Query.ShadingResolution(new HorizontalLouvres(0.3, 5), target, 0.25, out string _, out double louvrePitch));
            Assert.Equal(0.25, louvrePitch, 6);
        }

        // ------------------------------------------------------------------- the brief ----

        [Fact]
        public void The_Default_Brief_Is_Summer_Unwanted_Winter_Wanted_And_Flips_South_Of_The_Equator()
        {
            SeasonalDesirability northern = Analytical.SolarCalculator.Create.DefaultDesirabilityStrategy(2018, TestHelpers.London()) as SeasonalDesirability;
            Assert.NotNull(northern);
            Assert.Equal(6, northern.UnwantedPeriod.StartMonth);
            Assert.Equal(12, northern.WantedPeriod.StartMonth);

            SeasonalDesirability southern = Analytical.SolarCalculator.Create.DefaultDesirabilityStrategy(2018, new Location("Sydney", 151.2, -33.9, 3.0)) as SeasonalDesirability;
            Assert.NotNull(southern);
            Assert.Equal(12, southern.UnwantedPeriod.StartMonth);
            Assert.Equal(6, southern.WantedPeriod.StartMonth);
        }

        // ------------------------------------------------- setup, reuse and invalidation ----

        [Fact]
        public void Solar_Context_Defaults_To_Every_Aperture_Reuses_Its_Work_And_Invalidates_On_Change()
        {
            AnalyticalModel analyticalModel = Load("ModelB-NoShadeSolarSimulation.sam");
            int year = WeatherYearOf(analyticalModel);

            // Default selection: every external sun-exposed aperture, exactly as ApertureSolarTargets.
            List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(null, 0.5);
            Assert.NotEmpty(targets);

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            ApertureSolarContext first = Analytical.SolarCalculator.Create.ApertureSolarContext(analyticalModel, year, gridSize: 0.5);
            stopwatch.Stop();
            long buildMs = stopwatch.ElapsedMilliseconds;

            Assert.NotNull(first);
            Assert.False(first.ReusedPreviousCalculation, "the first run has nothing to reuse");
            Assert.Equal(targets.Count, first.TargetCount);

            // Cell offsets address the shared cell space in target order.
            int expectedOffset = 0;
            foreach (ApertureSolarTarget target in first.Targets)
            {
                Assert.Equal(expectedOffset, first.CellIndexOffset(target.ApertureGuid));
                expectedOffset += target.CellCount;
            }

            Assert.Equal(expectedOffset, first.Cells.Count);
            Assert.Equal(-1, first.CellIndexOffset(Guid.NewGuid()));

            stopwatch.Restart();
            ApertureSolarContext second = Analytical.SolarCalculator.Create.ApertureSolarContext(analyticalModel, year, gridSize: 0.5);
            stopwatch.Stop();
            Assert.True(second.ReusedPreviousCalculation, "identical inputs must reuse the previous calculation");
            output.WriteLine($"build {buildMs} ms; reuse {stopwatch.ElapsedMilliseconds} ms");

            // _recalculate_ = true forces the work to be redone.
            ApertureSolarContext forced = Analytical.SolarCalculator.Create.ApertureSolarContext(analyticalModel, year, gridSize: 0.5, recalculate: true);
            Assert.False(forced.ReusedPreviousCalculation);

            // A finer grid is a different analysis and cannot reuse the previous one.
            ApertureSolarContext finer = Analytical.SolarCalculator.Create.ApertureSolarContext(analyticalModel, year, gridSize: 0.75);
            Assert.False(finer.ReusedPreviousCalculation);
        }

        [Fact]
        public void Different_Geometry_Never_Reuses_Another_Models_Calculation()
        {
            AnalyticalModel unshaded = Load("ModelB-NoShadeSolarSimulation.sam");
            int year = WeatherYearOf(unshaded);

            ApertureSolarContext context = Analytical.SolarCalculator.Create.ApertureSolarContext(unshaded, year, gridSize: 0.75);
            Assert.False(context.ReusedPreviousCalculation);

            // Hand the finished calculation to a model with DIFFERENT geometry — the case a user
            // creates by editing the building and re-running. It must be rejected, not reused.
            AnalyticalModel shaded = Load("ModelB-WithShadeSolarSimulation.sam");
            shaded.SetValue(AnalyticalModelParameter.SolarModel, unshaded.GetValue<Geometry.SolarCalculator.SolarModel>(AnalyticalModelParameter.SolarModel));

            // The same weather either way, so geometry is the only thing that differs.
            WeatherData weatherData = unshaded.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            ApertureSolarContext shadedContext = Analytical.SolarCalculator.Create.ApertureSolarContext(shaded, year, weatherData, gridSize: 0.75);
            Assert.NotNull(shadedContext);
            Assert.False(shadedContext.ReusedPreviousCalculation, "different geometry must force a recalculation");
        }

        [Fact]
        public void Supplied_Weather_Wins_Over_The_Models_Own()
        {
            AnalyticalModel analyticalModel = Load("ModelB-NoShadeSolarSimulation.sam");
            int year = WeatherYearOf(analyticalModel);

            // A synthetic year that is unmistakably not the model's own weather.
            WeatherData supplied = TestHelpers.SyntheticWeatherData(year, analyticalModel.Location, dt =>
            {
                bool day = dt.Hour >= 9 && dt.Hour <= 15;
                return Tuple.Create(day ? 800.0 : 0.0, day ? 100.0 : 0.0, 0.0);
            });

            ApertureSolarContext context = Analytical.SolarCalculator.Create.ApertureSolarContext(analyticalModel, year, supplied, gridSize: 0.75);
            Assert.NotNull(context);
            Assert.Same(supplied, context.WeatherData);

            // With nothing supplied the model's own weather is used instead.
            ApertureSolarContext fromModel = Analytical.SolarCalculator.Create.ApertureSolarContext(analyticalModel, year, gridSize: 0.75);
            Assert.NotSame(supplied, fromModel.WeatherData);
            Assert.Same(analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData), fromModel.WeatherData);
        }

        [Fact]
        public void No_Weather_Anywhere_Produces_No_Context_Rather_Than_A_Guess()
        {
            AnalyticalModel analyticalModel = Load("ModelB-NoShadeSolarSimulation.sam");
            int year = WeatherYearOf(analyticalModel);

            AnalyticalModel weatherless = new AnalyticalModel(analyticalModel);
            Assert.True(weatherless.RemoveValue(AnalyticalModelParameter.WeatherData));
            Assert.Null(weatherless.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData));

            Assert.Null(Analytical.SolarCalculator.Create.ApertureSolarContext(weatherless, year, gridSize: 0.75));
        }

        [Fact]
        public void An_Aperture_Outside_The_Model_Cannot_Be_Prepared()
        {
            AnalyticalModel analyticalModel = Load("ModelB-NoShadeSolarSimulation.sam");
            int year = WeatherYearOf(analyticalModel);

            Assert.Null(Analytical.SolarCalculator.Create.ApertureShadingSetup(analyticalModel, Guid.NewGuid(), year, gridSize: 0.75));
        }

        // ------------------------------------------------------- metrics the nodes report ----

        [Fact]
        public void A_Verified_Device_Reproduces_The_Numbers_The_Optimiser_Reported()
        {
            // RationaliseShading hands VerifyShading a device rebuilt from the winning parameters.
            // If the rebuild were not faithful the two nodes would disagree on the same design,
            // which is the one thing an engineer must be able to rely on.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            OptimisedShadingResult optimised = Optimise.Overhang(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                new ShadingObjective(1.0, 0.1), null, maximumEvaluations: 12);

            Assert.NotNull(optimised);

            IShadingTypology rebuilt = optimised.Typology();
            Assert.NotNull(rebuilt);
            Assert.Equal(optimised.TypologyName, rebuilt.Name);

            ShadingPerformance verified = Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context, rebuilt);

            Assert.NotNull(verified);
            Assert.Equal(optimised.DirectSolarIntercepted, verified.DirectSolarIntercepted, 9);
            Assert.Equal(optimised.UnwantedSolarIntercepted, verified.UnwantedSolarIntercepted, 9);
            Assert.Equal(optimised.WantedSolarBlocked, verified.WantedSolarBlocked, 9);
            Assert.Equal(optimised.AdmittedDirectEnergy, verified.AdmittedDirectEnergy, 9);
            Assert.Equal(optimised.MaterialFraction, verified.MaterialFraction, 9);

            output.WriteLine($"optimised {optimised.TypologyName} depth {optimised.GetParameter("Depth"):0.###} m, " +
                $"benefit {optimised.Benefit:0.##} kWh, harm {optimised.Harm:0.##} kWh, score {optimised.ObjectiveScore:0.##} kWh");
        }

        [Fact]
        public void A_Percentage_With_No_Denominator_Stays_Unavailable()
        {
            // Everything unwanted: there is no wanted solar, so 'wanted solar retained' has no
            // denominator. It must come back unavailable, never as a flattering 100 %.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthAllUnwanted();

            ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context, new Overhang(0.5));

            Assert.NotNull(performance);
            Assert.True(double.IsNaN(performance.WantedSolarRetained), "no wanted solar means the metric is unavailable, not 100 %");
            Assert.False(double.IsNaN(performance.UnwantedSolarBlocked));

            // And the reporting conversion the components use must not invent a number either.
            Assert.True(double.IsNaN(Percentage(performance.WantedSolarRetained)));
            Assert.Equal(100.0 * performance.UnwantedSolarBlocked, Percentage(performance.UnwantedSolarBlocked), 9);
        }

        /// <summary>Mirrors the components' own ratio-to-percentage conversion.</summary>
        private static double Percentage(double ratio)
        {
            return double.IsNaN(ratio) ? double.NaN : 100.0 * ratio;
        }

        // ------------------------------------------------------------- durable wrappers ----

        [Fact]
        public void The_Wired_Objects_Survive_A_Save_And_Reload()
        {
            // A Grasshopper file stores each wired object as its SAM JSON and rebuilds it on
            // reload. Anything that does not round-trip would come back empty in a saved script.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            ApertureSolarTarget target = RoundTrip(scenario.Target);
            Assert.Equal(scenario.Target.ApertureGuid, target.ApertureGuid);
            Assert.Equal(scenario.Target.CellCount, target.CellCount);
            Assert.Equal(scenario.Target.Azimuth, target.Azimuth, 9);
            Assert.Equal(scenario.Target.GrossArea, target.GrossArea, 9);

            ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(scenario.Target, 0.6, 0.2, 0.0, 0.2, 0.2, 0.2);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(scenario.Target, scenario.BaseCache, scenario.Desirability, volume);
            Assert.NotNull(field);

            ShadingPotentialField reloadedField = RoundTrip(field);
            Assert.Equal(field.PositiveTotal(), reloadedField.PositiveTotal(), 9);
            Assert.Equal(field.NegativeTotal(), reloadedField.NegativeTotal(), 9);
            Assert.Equal(field.Volume.VoxelCount, reloadedField.Volume.VoxelCount);

            IdealShadingResult ideal = Analytical.SolarCalculator.Create.IdealShadingResult(field, ShadingThresholdMethod.CumulativeCapture, 0.9);
            IdealShadingResult reloadedIdeal = RoundTrip(ideal);
            Assert.Equal(ideal.SelectedVoxelCount, reloadedIdeal.SelectedVoxelCount);
            Assert.Equal(ideal.CapturedPotentialFraction, reloadedIdeal.CapturedPotentialFraction, 9);
            Assert.Equal(ideal.Threshold, reloadedIdeal.Threshold, 9);

            IShadingTypology device = new HorizontalLouvres(0.35, 3, 15.0);
            IShadingTypology reloadedDevice = RoundTrip(device);
            Assert.Equal(device.Name, reloadedDevice.Name);
            foreach (string name in device.ParameterNames)
            {
                Assert.Equal(device.GetParameter(name), reloadedDevice.GetParameter(name), 9);
            }
        }

        private static T RoundTrip<T>(T jSAMObject) where T : IJSAMObject
        {
            T result = Core.Create.IJSAMObject<T>(jSAMObject.ToJsonObject().ToJsonString());
            Assert.NotNull(result);
            return result;
        }
    }
}
