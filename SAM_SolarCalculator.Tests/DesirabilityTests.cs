// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical.SolarCalculator;
using SAM.Core;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Stage 5 tests: the desirability framework — sign convention, energy weighting, HOY
    /// selections, zero-energy behaviour, strategy substitution and JSON round-trips.
    /// </summary>
    public class DesirabilityTests
    {
        private readonly ITestOutputHelper output;

        public DesirabilityTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static readonly int Year = 2018;

        private static Location London()
        {
            return TestHelpers.London();
        }

        /// <summary>A south-facing 1 m2 single-cell target at z = 5 (no model machinery).</summary>
        private static ApertureSolarTarget SouthTarget(double gridSize = 1.0)
        {
            Face3D face = SyntheticTargets.Face(SyntheticTargets.South, new Point3D(0, 0, 5), 1.0);
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(face, gridSize);
            return new ApertureSolarTarget(Guid.NewGuid(), Guid.NewGuid(), face, cells);
        }

        private static SolarVisibilityCache Cache(ApertureSolarTarget target, double sunAngleStep = 2.0, double shift = 30.0)
        {
            return Weather.SolarCalculator.Create.SolarVisibilityCache(
                London(), Year, sunAngleStep, new List<LinkedFace3D>(), target.AnalysisCells,
                cellSize: 1.0, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: shift);
        }

        private static WeatherData SymmetricWeather(double shift = 30.0, double peak = 900.0)
        {
            return TestHelpers.SolarSymmetricWeatherData(Year, London(), shift, peak, 0.2);
        }

        /// <summary>Independent hand-evaluation of one hour's aperture-plane beam energy, kWh/m2.</summary>
        private static double ExpectedHourEnergy(WeatherData weatherData, Location location, DateTime dateTime, double shiftMinutes, Vector3D outward)
        {
            WeatherHour weatherHour = weatherData.GetWeatherHour(dateTime);
            double beamHorizontal = Math.Max(0.0, weatherHour.GlobalSolarRadiation - weatherHour.DiffuseSolarRadiation);

            DateTime sunTime = dateTime.AddMinutes(shiftMinutes);
            Assert.True(Geometry.SolarCalculator.Query.TryGetSunAngles(location, sunTime, out double elevationDegrees, out double azimuthDegrees));

            double elevation = elevationDegrees * Math.PI / 180.0;
            double azimuth = azimuthDegrees * Math.PI / 180.0;
            Vector3D toward = new Vector3D(Math.Cos(elevation) * Math.Sin(azimuth), Math.Cos(elevation) * Math.Cos(azimuth), Math.Sin(elevation));
            double cosThetaI = Math.Max(0.0, outward.Unit.DotProduct(toward));
            double dni = beamHorizontal / Math.Max(Math.Sin(elevation), Math.Sin(5.0 * Math.PI / 180.0));
            return dni * cosThetaI / 1000.0;
        }

        [Fact]
        public void AllUnwanted_Period_Gives_Positive_Block_Weighting()
        {
            ApertureSolarTarget target = SouthTarget();
            SolarVisibilityCache cache = Cache(target);
            WeatherData weatherData = SymmetricWeather();

            SeasonalDesirability strategy = new SeasonalDesirability(new AnalysisPeriod(Year), null);
            ApertureDesirability result = Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, strategy, weatherData);

            Assert.NotNull(result);
            Assert.True(result.TotalUnwantedEnergy > 0);
            Assert.Equal(0.0, result.TotalWantedEnergy);
            Assert.Equal(result.TotalUnwantedEnergy, result.TotalDirectEnergy, 10);
            Assert.Equal(result.TotalUnwantedEnergy, result.NetDesirability, 10);
            Assert.True(result.EvaluatedHours > 3000, $"expected most daylight hours evaluated, got {result.EvaluatedHours}");
        }

        [Fact]
        public void AllWanted_Period_Gives_Preservation_Penalty()
        {
            ApertureSolarTarget target = SouthTarget();
            SolarVisibilityCache cache = Cache(target);
            WeatherData weatherData = SymmetricWeather();

            SeasonalDesirability strategy = new SeasonalDesirability(null, new AnalysisPeriod(Year));
            ApertureDesirability result = Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, strategy, weatherData);

            Assert.NotNull(result);
            Assert.True(result.TotalWantedEnergy > 0);
            Assert.Equal(0.0, result.TotalUnwantedEnergy);
            Assert.Equal(result.TotalWantedEnergy, result.TotalDirectEnergy, 10);
            Assert.Equal(-result.TotalWantedEnergy, result.NetDesirability, 10);
        }

        [Fact]
        public void Mixed_Periods_Split_Energy_Consistently()
        {
            ApertureSolarTarget target = SouthTarget();
            SolarVisibilityCache cache = Cache(target);
            WeatherData weatherData = SymmetricWeather();

            // Unwanted: meteorological summer. Wanted: heating season wrapping the year end.
            SeasonalDesirability strategy = new SeasonalDesirability(
                new AnalysisPeriod(Year, 6, 1, 8, 31),
                new AnalysisPeriod(Year, 11, 1, 2, 28));
            ApertureDesirability result = Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, strategy, weatherData);

            Assert.NotNull(result);
            Assert.True(result.TotalUnwantedEnergy > 0);
            Assert.True(result.TotalWantedEnergy > 0);

            // With this weather DNI is constant, so the facade incidence decides: low winter sun
            // strikes a vertical south facade near-perpendicularly, high summer sun obliquely. The
            // wanted heating season therefore carries MORE facade beam energy than the unwanted
            // summer period — the physical reason south facades are winter solar-gain assets.
            Assert.True(result.TotalWantedEnergy > result.TotalUnwantedEnergy,
                $"wanted {result.TotalWantedEnergy:0.#} should exceed unwanted {result.TotalUnwantedEnergy:0.#} on a south vertical facade");

            // Conservation: weighted hours land in exactly one bucket each; the remainder is the
            // neutral spring/autumn energy, which is real and positive here.
            double neutral = result.TotalDirectEnergy - result.TotalUnwantedEnergy - result.TotalWantedEnergy;
            Assert.True(neutral > 0, $"expected positive neutral (spring/autumn) energy, got {neutral}");
            Assert.Equal(result.TotalUnwantedEnergy - result.TotalWantedEnergy, result.NetDesirability, 10);
        }

        [Fact]
        public void Explicit_HOY_Selection_Weights_Exactly_Those_Hours()
        {
            ApertureSolarTarget target = SouthTarget();
            SolarVisibilityCache cache = Cache(target);
            WeatherData weatherData = SymmetricWeather();
            Location location = London();

            DateTime h0 = new DateTime(Year, 6, 21, 12, 0, 0);
            int hoy0 = (int)(h0 - new DateTime(Year, 1, 1)).TotalHours;

            SeasonalDesirability strategy = new SeasonalDesirability(new AnalysisPeriod(Year, new[] { hoy0 }), null);
            ApertureDesirability result = Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, strategy, weatherData);

            double expected = ExpectedHourEnergy(weatherData, location, h0, 30.0, target.OutwardNormal);
            output.WriteLine($"single-hour unwanted energy: expected {expected:0.######} kWh/m2, got {result.TotalUnwantedEnergy:0.######}");

            Assert.Equal(expected, result.TotalUnwantedEnergy, 9);
            Assert.Equal(0.0, result.TotalWantedEnergy);

            // Exactly one sun group carries the unwanted energy.
            double[] unwanted = result.UnwantedEnergyPerGroup;
            int nonZero = unwanted.Count(x => x > 0);
            Assert.Equal(1, nonZero);
        }

        [Fact]
        public void HighEnergy_Hour_Dominates_LowEnergy_Hours()
        {
            ApertureSolarTarget target = SouthTarget();
            SolarVisibilityCache cache = Cache(target);
            Location location = London();

            // Two near-solstice noon hours on consecutive days (near-identical sun positions);
            // the second carries TEN times the irradiance. Hour-counting would weight them 1:1;
            // energy weighting must give ~10:1.
            DateTime h0 = new DateTime(Year, 6, 21, 12, 0, 0);
            DateTime h1 = new DateTime(Year, 6, 22, 12, 0, 0);
            int hoy0 = (int)(h0 - new DateTime(Year, 1, 1)).TotalHours;
            int hoy1 = (int)(h1 - new DateTime(Year, 1, 1)).TotalHours;

            WeatherData weatherData = TestHelpers.SyntheticWeatherData(Year, location, dateTime =>
            {
                DateTime sunTime = dateTime.AddMinutes(30.0);
                if (!Geometry.SolarCalculator.Query.TryGetSunAngles(location, sunTime, out double altitude, out _) || altitude <= 0)
                {
                    return Tuple.Create(0.0, 0.0, 0.0);
                }

                double global = 900.0 * Math.Sin(altitude * Math.PI / 180.0);
                if (dateTime == h1)
                {
                    global *= 10.0;
                }

                return Tuple.Create(global, global * 0.2, 0.0);
            });

            SeasonalDesirability strategy = new SeasonalDesirability(new AnalysisPeriod(Year, new[] { hoy0, hoy1 }), null);
            ApertureDesirability result = Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, strategy, weatherData);

            double e0 = ExpectedHourEnergy(weatherData, location, h0, 30.0, target.OutwardNormal);
            double e1 = ExpectedHourEnergy(weatherData, location, h1, 30.0, target.OutwardNormal);
            output.WriteLine($"hour energies: {e0:0.####} vs {e1:0.####} kWh/m2 (ratio {e1 / e0:0.##})");

            Assert.Equal(e0 + e1, result.TotalUnwantedEnergy, 8);
            Assert.InRange(e1 / e0, 9.0, 11.0);
            Assert.True(e1 > 5.0 * e0, "energy weighting must dominate, not hour counting");
        }

        [Fact]
        public void Zero_Direct_Energy_Gives_Zero_Contribution()
        {
            ApertureSolarTarget target = SouthTarget();
            SolarVisibilityCache cache = Cache(target);

            // Fully diffuse sky all year: beam horizontal is exactly zero everywhere.
            WeatherData weatherData = TestHelpers.SyntheticWeatherData(Year, London(), dateTime =>
            {
                DateTime sunTime = dateTime.AddMinutes(30.0);
                if (!Geometry.SolarCalculator.Query.TryGetSunAngles(London(), sunTime, out double altitude, out _) || altitude <= 0)
                {
                    return Tuple.Create(0.0, 0.0, 0.0);
                }

                double global = 400.0 * Math.Sin(altitude * Math.PI / 180.0);
                return Tuple.Create(global, global, 0.0); // GHI == DHI -> no beam
            });

            SeasonalDesirability strategy = new SeasonalDesirability(new AnalysisPeriod(Year), null);
            ApertureDesirability result = Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, strategy, weatherData);

            Assert.NotNull(result);
            Assert.True(result.EvaluatedHours > 3000, "hours ARE evaluated — they just carry no beam energy");
            Assert.Equal(0.0, result.TotalDirectEnergy);
            Assert.Equal(0.0, result.TotalUnwantedEnergy);
            Assert.Equal(0.0, result.TotalWantedEnergy);
        }

        [Fact]
        public void BackFacing_Sun_Gives_Zero_Contribution()
        {
            // A down-facing aperture can never receive direct beam.
            Face3D face = SyntheticTargets.Face(new Vector3D(0, 0, -1), new Point3D(0, 0, 5), 1.0);
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(face, 1.0);
            ApertureSolarTarget target = new ApertureSolarTarget(Guid.NewGuid(), Guid.NewGuid(), face, cells);

            SolarVisibilityCache cache = Cache(target);
            WeatherData weatherData = SymmetricWeather();

            SeasonalDesirability strategy = new SeasonalDesirability(new AnalysisPeriod(Year), null);
            ApertureDesirability result = Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, strategy, weatherData);

            Assert.NotNull(result);
            Assert.Equal(0.0, result.TotalDirectEnergy);
            Assert.Equal(0.0, result.TotalUnwantedEnergy);
        }

        [Fact]
        public void Strategy_Substitution_Is_Genuinely_Pluggable()
        {
            ApertureSolarTarget target = SouthTarget();
            SolarVisibilityCache cache = Cache(target);
            WeatherData weatherData = SymmetricWeather();

            // A test-local strategy the production code has never seen: constant +1.
            ApertureDesirability constant = Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, new ConstantWeightDesirability(1.0), weatherData);
            ApertureDesirability seasonal = Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, new SeasonalDesirability(new AnalysisPeriod(Year), null), weatherData);

            Assert.Equal(seasonal.TotalUnwantedEnergy, constant.TotalUnwantedEnergy, 9);
            Assert.Equal(seasonal.TotalDirectEnergy, constant.TotalDirectEnergy, 9);

            // Composite with a single component at blend 1.0 equals that component alone.
            CompositeDesirability composite = new CompositeDesirability(
                new IDesirabilityStrategy[] { new SeasonalDesirability(new AnalysisPeriod(Year), null) },
                new double[] { 1.0 });
            ApertureDesirability blended = Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, composite, weatherData);
            Assert.Equal(seasonal.TotalUnwantedEnergy, blended.TotalUnwantedEnergy, 9);

            // Externally supplied per-hour weights (the future TAS-load hook) reproduce an
            // explicit-HOY seasonal definition exactly.
            DateTime h0 = new DateTime(Year, 6, 21, 12, 0, 0);
            int hoy0 = (int)(h0 - new DateTime(Year, 1, 1)).TotalHours;
            ApertureDesirability seasonalHoy = Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, new SeasonalDesirability(new AnalysisPeriod(Year, new[] { hoy0 }), null), weatherData);
            ApertureDesirability external = Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, new ExternalDesirability(Year, new Dictionary<int, double> { { hoy0, 1.0 } }), weatherData);
            Assert.Equal(seasonalHoy.TotalUnwantedEnergy, external.TotalUnwantedEnergy, 9);
        }

        [Fact]
        public void Seasonal_Strategy_Zero_Outside_Periods_And_Unwanted_Wins_Overlap()
        {
            SeasonalDesirability strategy = new SeasonalDesirability(
                new AnalysisPeriod(Year, 6, 1, 8, 31),
                new AnalysisPeriod(Year, 7, 15, 9, 15)); // overlaps the unwanted period 15 Jul - 31 Aug

            WeatherHour weatherHour = new WeatherHour();

            // Outside both: exactly zero.
            Assert.Equal(0.0, strategy.Weight(new DateTime(Year, 3, 10, 12, 0, 0), weatherHour, null));
            // Unwanted only: +1.
            Assert.Equal(1.0, strategy.Weight(new DateTime(Year, 6, 21, 12, 0, 0), weatherHour, null));
            // Wanted only: -1.
            Assert.Equal(-1.0, strategy.Weight(new DateTime(Year, 9, 10, 12, 0, 0), weatherHour, null));
            // Overlap: unwanted wins.
            Assert.Equal(1.0, strategy.Weight(new DateTime(Year, 8, 1, 12, 0, 0), weatherHour, null));
        }

        [Fact]
        public void Temperature_Strategy_Follows_Balance_Temperature()
        {
            TemperatureDesirability strategy = new TemperatureDesirability(15.5, 5.0);

            WeatherHour cold = new WeatherHour();
            cold[WeatherDataType.DryBulbTemperature] = 5.0;
            WeatherHour warm = new WeatherHour();
            warm[WeatherDataType.DryBulbTemperature] = 25.0;
            WeatherHour missing = new WeatherHour();

            Assert.True(strategy.Weight(DateTime.Now, cold, null) < 0);
            Assert.True(strategy.Weight(DateTime.Now, warm, null) > 0);
            Assert.Equal(0.0, strategy.Weight(DateTime.Now, missing, null));

            // Clamped to [-1, 1].
            WeatherHour hot = new WeatherHour();
            hot[WeatherDataType.DryBulbTemperature] = 40.0;
            Assert.Equal(1.0, strategy.Weight(DateTime.Now, hot, null));

            // Majority sign across the year: cold January days negative, warm July afternoons positive.
            int januaryNegative = 0;
            int julyPositive = 0;
            int januaryCount = 0;
            int julyCount = 0;
            for (int day = 1; day <= 31; day++)
            {
                for (int hour = 8; hour <= 17; hour++)
                {
                    januaryCount++;
                    WeatherHour januaryHour = new WeatherHour();
                    januaryHour[WeatherDataType.DryBulbTemperature] = 5.0;
                    if (strategy.Weight(new DateTime(Year, 1, day, hour, 0, 0), januaryHour, null) < 0)
                    {
                        januaryNegative++;
                    }

                    julyCount++;
                    WeatherHour julyHour = new WeatherHour();
                    julyHour[WeatherDataType.DryBulbTemperature] = 25.0;
                    if (strategy.Weight(new DateTime(Year, 7, day, hour, 0, 0), julyHour, null) > 0)
                    {
                        julyPositive++;
                    }
                }
            }

            Assert.True(januaryNegative > januaryCount / 2, "majority of January daylight hours should be wanted (negative)");
            Assert.True(julyPositive > julyCount / 2, "majority of July daylight hours should be unwanted (positive)");
        }

        [Fact]
        public void Json_RoundTrips_Preserve_Strategies_And_Result()
        {
            ApertureSolarTarget target = SouthTarget();
            SolarVisibilityCache cache = Cache(target);
            WeatherData weatherData = SymmetricWeather();

            ApertureDesirability result = Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, new SeasonalDesirability(new AnalysisPeriod(Year, 6, 1, 8, 31), new AnalysisPeriod(Year, 11, 1, 2, 28)), weatherData);
            ApertureDesirability result_Restored = new ApertureDesirability(result.ToJsonObject());
            Assert.Equal(result.TotalUnwantedEnergy, result_Restored.TotalUnwantedEnergy, 12);
            Assert.Equal(result.TotalWantedEnergy, result_Restored.TotalWantedEnergy, 12);
            Assert.Equal(result.GroupCount, result_Restored.GroupCount);
            Assert.Equal(result.DesirabilityStrategyName, result_Restored.DesirabilityStrategyName);

            SeasonalDesirability seasonal = new SeasonalDesirability(new AnalysisPeriod(Year, 6, 1, 8, 31), new AnalysisPeriod(Year, 11, 1, 2, 28), 0.8, 0.6);
            SeasonalDesirability seasonal_Restored = new SeasonalDesirability(seasonal.ToJsonObject());
            WeatherHour any = new WeatherHour();
            Assert.Equal(seasonal.Weight(new DateTime(Year, 7, 1, 12, 0, 0), any, null), seasonal_Restored.Weight(new DateTime(Year, 7, 1, 12, 0, 0), any, null));
            Assert.Equal(seasonal.Weight(new DateTime(Year, 1, 1, 12, 0, 0), any, null), seasonal_Restored.Weight(new DateTime(Year, 1, 1, 12, 0, 0), any, null));

            CompositeDesirability composite = new CompositeDesirability(
                new IDesirabilityStrategy[] { seasonal, new TemperatureDesirability(18.0, 2.0) },
                new double[] { 0.7, 0.3 });
            CompositeDesirability composite_Restored = new CompositeDesirability(composite.ToJsonObject());
            Assert.Equal(2, composite_Restored.Strategies.Count);
            Assert.Equal(0.7, composite_Restored.BlendWeights[0], 12);

            ExternalDesirability external = new ExternalDesirability(Year, new Dictionary<int, double> { { 100, 0.5 }, { 4000, -1.0 } });
            ExternalDesirability external_Restored = new ExternalDesirability(external.ToJsonObject());
            Assert.Equal(0.5, external_Restored.WeightByHourOfYear[100], 12);
            Assert.Equal(-1.0, external_Restored.WeightByHourOfYear[4000], 12);
        }

        /// <summary>Test-local strategy proving the framework accepts implementations it has never seen.</summary>
        private class ConstantWeightDesirability : IDesirabilityStrategy
        {
            private readonly double weight;

            public ConstantWeightDesirability(double weight)
            {
                this.weight = weight;
            }

            public double Weight(DateTime dateTime, WeatherHour weatherHour, ApertureSolarTarget target)
            {
                return weight;
            }

            public bool FromJsonObject(System.Text.Json.Nodes.JsonObject jObject)
            {
                return false;
            }

            public System.Text.Json.Nodes.JsonObject ToJsonObject()
            {
                return null;
            }
        }
    }
}
