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
using SAM.Core.SolarCalculator;
using SAM.Weather;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Stage 10.2 Gates D, E and F: making the reported numbers add up, and making their names say
    /// what they are.
    ///
    /// THE DEFECTS THESE COVER, all found by reading the canvas rather than the code:
    ///
    ///   F. A north window reported 61.3 kWh admitted, 46.0 kWh unwanted intercepted and 0.0 kWh
    ///      wanted blocked, and 15.3 kWh appeared to have vanished. It had not: under the default
    ///      seasonal brief the spring and autumn beam is in NEITHER period, and nothing showed it.
    ///
    ///   D. "benefit 5504 kWh" on the potential field read as an energy saving. It is a sum over
    ///      voxels of a value every voxel along a ray receives, so it counts the same solar many
    ///      times over and is roughly two orders of magnitude larger than anything a device could
    ///      save on that window.
    ///
    ///   E. "captures 90 % of the benefit" read as "blocks 90 % of the unwanted solar". It is a
    ///      share of the field's positive potential, and the shape it describes was never traced.
    /// </summary>
    public class SolarAccountingTests
    {
        private readonly ITestOutputHelper output;

        public SolarAccountingTests(ITestOutputHelper output)
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
            return weatherData.WeatherYears.First(x => x != null).Year;
        }

        // ------------------------------------------------- F: unwanted / wanted / neutral ----

        [Fact]
        public void The_North_Window_That_Looked_Like_Missing_Energy_Reconciles_Exactly()
        {
            // The manual case, reproduced number for number: 61.3 kWh baseline, 46 kWh unwanted,
            // nothing wanted. The residual is real solar in a real part of the year, and it is now
            // named and reported instead of being left for the reader to notice.
            AnalyticalModel model = Load();
            int year = WeatherYear(model);
            Guid apertureGuid = model.ApertureSolarTargets(null, 0.5)
                .First(x => Math.Abs(x.Azimuth) < 1e-6).ApertureGuid;

            ApertureShadingSetup setup = Analytical.SolarCalculator.Create.ApertureShadingSetup(
                model, apertureGuid, year, null, null, null, null, new List<Guid> { apertureGuid }, 0.1, 2.0);

            ShadingPerformance performance = Analytical.SolarCalculator.Create.ShadingPerformance(
                setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability,
                setup.Context.ContextOccluders, new VerticalFins(0.05, 6, 0.0), setup.CellIndexOffset);

            Assert.NotNull(performance);
            output.WriteLine(Analytical.SolarCalculator.Query.AccountingSummary(performance));

            // Both balances close exactly, by construction rather than by luck.
            Assert.Equal(
                performance.AdmittedDirectEnergy,
                performance.AdmittedUnwantedEnergy + performance.AdmittedWantedEnergy + performance.AdmittedNeutralEnergy, 9);

            Assert.Equal(
                performance.DirectSolarIntercepted,
                performance.UnwantedSolarIntercepted + performance.WantedSolarBlocked + performance.NeutralSolarIntercepted, 9);

            // And the residual is the substantial, real quantity the manual test was missing — not a
            // rounding crumb. On this window it is the spring/autumn beam.
            Assert.True(performance.AdmittedNeutralEnergy > 10.0,
                $"the neutral residual should be the ~15 kWh the manual test could not account for, but it is {performance.AdmittedNeutralEnergy:0.##}");

            Assert.Equal(0.0, performance.AdmittedWantedEnergy, 9);
            Assert.True(performance.AdmittedUnwantedEnergy > 0);
        }

        [Fact]
        public void The_Residual_Is_A_Physical_Energy_Whenever_The_Weights_Are_Within_Unit_Magnitude()
        {
            // THE GENERAL TREATMENT, which the brief asks for explicitly and which must not be
            // assumed for continuous or custom strategies.
            //
            // Unwanted and wanted are WEIGHTED sums, |w| x energy over the hours of each sign — not
            // slices of a partition. Written as a residual the identity always closes; what varies is
            // whether the residual is a physical energy. It is, and is non-negative, exactly when
            // every applied weight lay within [-1, 1]. That is MEASURED from the weights actually
            // used rather than declared by the strategy, so a strategy that misdescribes itself
            // cannot make the accounting quietly wrong.
            OptimisationFixture.Scenario seasonal = OptimisationFixture.SouthSeasonal();

            // SeasonalDesirability uses weights of exactly +1, -1 and 0.
            Assert.Equal(1.0, seasonal.Desirability.MaximumWeightMagnitude, 9);
            Assert.True(seasonal.Desirability.WeightsWithinUnitMagnitude);

            // So the year totals partition, and the neutral part is non-negative.
            Assert.Equal(
                seasonal.Desirability.TotalDirectEnergy,
                seasonal.Desirability.TotalUnwantedEnergy + seasonal.Desirability.TotalWantedEnergy + seasonal.Desirability.TotalNeutralEnergy, 9);

            Assert.True(seasonal.Desirability.TotalNeutralEnergy >= -1e-9);

            output.WriteLine($"seasonal brief: {seasonal.Desirability.TotalDirectEnergy:0.###} = " +
                $"{seasonal.Desirability.TotalUnwantedEnergy:0.###} unwanted + {seasonal.Desirability.TotalWantedEnergy:0.###} wanted + " +
                $"{seasonal.Desirability.TotalNeutralEnergy:0.###} neither kWh/m2 (max |weight| {seasonal.Desirability.MaximumWeightMagnitude})");

            // A brief covering the whole year with weight 1 leaves NOTHING neutral, which is the
            // other end of the same rule.
            OptimisationFixture.Scenario allUnwanted = OptimisationFixture.SouthAllUnwanted();
            Assert.Equal(0.0, allUnwanted.Desirability.TotalNeutralEnergy, 6);
        }

        [Fact]
        public void A_Strategy_Weighting_Beyond_Unit_Magnitude_Says_So_Instead_Of_Pretending_To_Partition()
        {
            // The case the general treatment has to be honest about. A weight of 2 claims twice the
            // beam that physically arrives in those hours, so the residual goes NEGATIVE. That is a
            // true statement about the brief and it is surfaced, not clamped to zero — a clamped
            // residual would let an over-weighted brief report a partition it does not have.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            ApertureDesirability doubled = Analytical.SolarCalculator.Create.ApertureDesirability(
                scenario.Target, scenario.BaseCache,
                new ScaledSeasonalDesirability(2.0),
                TestHelpers.SolarSymmetricWeatherData(OptimisationFixture.Year, TestHelpers.London(), 30.0, 900.0, 0.2));

            Assert.NotNull(doubled);
            Assert.Equal(2.0, doubled.MaximumWeightMagnitude, 9);
            Assert.False(doubled.WeightsWithinUnitMagnitude);

            // The identity still closes — it is a definition — but the residual is no longer an
            // energy, and the recorded weight magnitude is what tells a reader that.
            Assert.Equal(
                doubled.TotalDirectEnergy,
                doubled.TotalUnwantedEnergy + doubled.TotalWantedEnergy + doubled.TotalNeutralEnergy, 9);

            Assert.True(doubled.TotalNeutralEnergy < 0);

            output.WriteLine($"weights x2: residual {doubled.TotalNeutralEnergy:0.###} kWh/m2 — negative, because the brief claims more beam than arrives");
        }

        [Fact]
        public void A_Desirability_Loaded_From_An_Older_File_Does_Not_Claim_To_Know()
        {
            // Schema 1 did not record the weight magnitude. Round-tripping through JSON that lacks it
            // must report "unknown" rather than defaulting to "yes, this partitions" — the whole
            // point of measuring it is not to assume it.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            System.Text.Json.Nodes.JsonObject jObject = scenario.Desirability.ToJsonObject();
            Assert.True(jObject.ContainsKey("MaximumWeightMagnitude"));

            // Round trip WITH it: the answer survives.
            ApertureDesirability restored = new ApertureDesirability(jObject);
            Assert.Equal(scenario.Desirability.MaximumWeightMagnitude, restored.MaximumWeightMagnitude, 9);
            Assert.True(restored.WeightsWithinUnitMagnitude);
            Assert.Equal(scenario.Desirability.TotalNeutralEnergy, restored.TotalNeutralEnergy, 9);

            // Round trip WITHOUT it, as a schema 1 file would be: unknown, not assumed.
            jObject.Remove("MaximumWeightMagnitude");
            ApertureDesirability legacy = new ApertureDesirability(jObject);
            Assert.True(double.IsNaN(legacy.MaximumWeightMagnitude));
            Assert.Null(legacy.WeightsWithinUnitMagnitude);

            // The energies themselves are unaffected: only the claim about them is withheld.
            Assert.Equal(scenario.Desirability.TotalNeutralEnergy, legacy.TotalNeutralEnergy, 9);
        }

        [Fact]
        public void The_Optimised_Result_Reports_The_Same_Three_Way_Split_As_The_Verifier()
        {
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            OptimisedShadingResult result = Optimise.Overhang(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                new ShadingObjective(1.0, 0.1), null, 40);

            Assert.False(result.RecommendsNoShading);

            ShadingPerformance verified = Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context, result.Typology());

            Assert.Equal(verified.AdmittedNeutralEnergy, result.AdmittedNeutralEnergy, 9);
            Assert.Equal(verified.NeutralSolarIntercepted, result.NeutralSolarIntercepted, 9);

            Assert.Equal(
                result.DirectSolarIntercepted,
                result.UnwantedSolarIntercepted + result.WantedSolarBlocked + result.NeutralSolarIntercepted, 9);
        }

        [Fact]
        public void The_Design_Summary_Names_Energies_For_What_They_Are()
        {
            // "46 kWh benefit" reads as a saving. It is the unwanted beam intercepted — a narrower
            // claim, and the only one the measurement supports.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            OptimisedShadingResult result = Optimise.Overhang(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                new ShadingObjective(1.0, 0.1), null, 40);

            string summary = Analytical.SolarCalculator.Query.DesignSummary(result, scenario.Target.Azimuth);
            output.WriteLine(summary);

            Assert.Contains("kWh unwanted solar intercepted", summary);
            Assert.Contains("kWh wanted solar blocked", summary);
            Assert.DoesNotContain("benefit", summary);
            Assert.DoesNotContain("harm", summary);
        }

        // ----------------------------------------------------- D: field potential is not kWh ----

        [Fact]
        public void The_Field_Total_Is_Not_An_Energy_Saving_And_Is_Far_Larger_Than_One()
        {
            // The measurement behind the terminology change. If the summed potential were a saving,
            // it would have to be bounded by the unwanted solar the window admits at all. It is not
            // — by a large multiple — because one ray is counted at every voxel it passes through.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(scenario.Target, 1.0, 0.5, 0.0, 0.3, 0.3, 0.1);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(
                scenario.Target, scenario.BaseCache, scenario.Desirability, volume);

            Assert.NotNull(field);

            // The hard physical ceiling on any device's benefit on this window.
            ShadingPerformance baseline = Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context, new NoShading());

            double ceiling = baseline.AdmittedUnwantedEnergy;
            double potential = field.PositiveTotal();

            output.WriteLine($"summed positive potential {potential:0.#} against an absolute ceiling of {ceiling:0.#} kWh of admitted unwanted solar — a factor of {potential / ceiling:0.#}");

            Assert.True(potential > 5.0 * ceiling,
                "if the summed potential were within reach of the physical ceiling, calling it a saving would be defensible; it is not");

            // A real device, optimised, lands below the ceiling — as it must.
            OptimisedShadingResult best = Optimise.Overhang(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context,
                new ShadingObjective(1.0, 0.1), field, 40);

            Assert.True(best.UnwantedSolarIntercepted <= ceiling + 1e-9);
            Assert.True(best.UnwantedSolarIntercepted < potential);
        }

        [Fact]
        public void The_Raw_Field_Values_Are_Still_Available_Numerically()
        {
            // The terminology changed; the numbers did not. Nothing was rounded, rescaled or hidden.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(scenario.Target, 1.0, 0.5, 0.0, 0.3, 0.3, 0.1);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(
                scenario.Target, scenario.BaseCache, scenario.Desirability, volume);

            Assert.NotNull(field.UnwantedEnergyPerVoxel);
            Assert.NotNull(field.WantedEnergyPerVoxel);
            Assert.Equal(volume.VoxelCount, field.UnwantedEnergyPerVoxel.Length);

            Assert.False(double.IsNaN(field.PositiveTotal()));
            Assert.False(double.IsNaN(field.NegativeTotal()));
            Assert.False(double.IsNaN(field.MaxScore()));
            Assert.False(double.IsNaN(field.MinScore()));

            // A PER-VOXEL value genuinely is kWh: the beam a piece of material there would intercept
            // over the year. It is only the SUM that is not an energy, which is why the per-voxel
            // outputs keep their unit and the totals lost theirs.
            Assert.True(field.MaxScore() <= field.PositiveTotal() + 1e-9);
            Assert.True(field.NegativeTotal() <= 0);
        }

        // -------------------------------------------- E: captured potential, not captured energy ----

        [Fact]
        public void Captured_Potential_Fraction_Is_A_Share_Of_The_Field_Not_Of_The_Solar()
        {
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(scenario.Target, 1.0, 0.5, 0.0, 0.3, 0.3, 0.1);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(
                scenario.Target, scenario.BaseCache, scenario.Desirability, volume);

            IdealShadingResult ideal = Analytical.SolarCalculator.Create.IdealShadingResult(
                field, ShadingThresholdMethod.CumulativeCapture, 0.9);

            Assert.NotNull(ideal);

            // It does what its name says: 90 % of the field's POSITIVE POTENTIAL.
            Assert.True(ideal.CapturedPotentialFraction >= 0.9);
            Assert.True(ideal.CapturedPotentialFraction <= 1.0 + 1e-9);

            // And that is emphatically NOT 90 % of the unwanted solar. The Stage 7 shape is display
            // geometry: measured as an occluder it intercepts materially less than its "capture"
            // suggests, which is the whole reason the name changed.
            ShadingPerformance baseline = Analytical.SolarCalculator.Create.ShadingPerformance(
                scenario.Target, scenario.BaseCache, scenario.Desirability, scenario.Context, new NoShading());

            output.WriteLine($"captured potential fraction {100 * ideal.CapturedPotentialFraction:0.#} % over {ideal.SelectedVoxelCount} voxels; " +
                $"the window admits {baseline.AdmittedUnwantedEnergy:0.#} kWh of unwanted solar in total");

            Assert.True(baseline.AdmittedUnwantedEnergy > 0);
        }

        [Fact]
        public void A_Saved_Ideal_Result_From_Schema_One_Keeps_Its_Captured_Fraction()
        {
            // The property was renamed pre-release, so the JSON key changed with it. A file written
            // before the rename must still load its number rather than coming back NaN.
            OptimisationFixture.Scenario scenario = OptimisationFixture.SouthSeasonal();

            ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(scenario.Target, 1.0, 0.5, 0.0, 0.3, 0.3, 0.1);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(
                scenario.Target, scenario.BaseCache, scenario.Desirability, volume);

            IdealShadingResult ideal = Analytical.SolarCalculator.Create.IdealShadingResult(
                field, ShadingThresholdMethod.CumulativeCapture, 0.9);

            System.Text.Json.Nodes.JsonObject jObject = ideal.ToJsonObject();
            Assert.True(jObject.ContainsKey("CapturedPotentialFraction"));

            // Current schema round-trips.
            Assert.Equal(ideal.CapturedPotentialFraction, new IdealShadingResult(jObject).CapturedPotentialFraction, 12);

            // Rewritten as a schema 1 file would have been.
            double value = jObject["CapturedPotentialFraction"].GetValue<double>();
            jObject.Remove("CapturedPotentialFraction");
            jObject["CapturedBenefitFraction"] = value;
            jObject["SchemaVersion"] = 1;

            IdealShadingResult legacy = new IdealShadingResult(jObject);
            Assert.Equal(ideal.CapturedPotentialFraction, legacy.CapturedPotentialFraction, 12);
            Assert.False(double.IsNaN(legacy.CapturedPotentialFraction));
        }

        /// <summary>
        /// Summer unwanted / winter wanted, with every weight scaled — a brief that claims more beam
        /// than physically arrives, so the neutral residual has to go negative.
        /// </summary>
        private class ScaledSeasonalDesirability : IDesirabilityStrategy
        {
            private readonly double scale;
            private readonly SeasonalDesirability inner;

            public ScaledSeasonalDesirability(double scale)
            {
                this.scale = scale;
                inner = new SeasonalDesirability(
                    new AnalysisPeriod(OptimisationFixture.Year, 6, 1, 8, 31),
                    new AnalysisPeriod(OptimisationFixture.Year, 11, 1, 2, 28));
            }

            public double Weight(DateTime dateTime, WeatherHour weatherHour, ApertureSolarTarget target)
            {
                return scale * inner.Weight(dateTime, weatherHour, target);
            }

            public bool FromJsonObject(System.Text.Json.Nodes.JsonObject jObject) { return true; }

            public System.Text.Json.Nodes.JsonObject ToJsonObject() { return new System.Text.Json.Nodes.JsonObject(); }
        }
    }
}
