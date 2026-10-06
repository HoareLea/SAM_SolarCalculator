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
using SAM.Geometry.Spatial;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// T1 — evaluated-hour conservation across sun-position time shifts, and the named
    /// <see cref="SunTimeConvention"/> paths.
    ///
    /// The contract under test: for any shift, EVERY period hour must be accounted for exactly once
    /// as evaluated, below-horizon or missing-weather; the number of evaluated hours must equal the
    /// independently computed number of hours whose shifted sun clears the minimum horizon angle;
    /// and no hour may be lost to a sun-group (bin) miss.
    /// </summary>
    public class TimelineConventionTests
    {
        private readonly ITestOutputHelper output;

        public TimelineConventionTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const int Year = 2018;
        private const int HoursInYear = 8760;
        private const double MinHorizonAngle = Core.Tolerance.Angle;   // 0.0349066 rad = 2 deg

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

        /// <summary>
        /// Independent expectation: hours of the year whose sun position at (hour + shift) is at or
        /// above the minimum horizon angle. Computed straight from SolarTimes, without touching any
        /// cache or bin machinery.
        /// </summary>
        private static int ExpectedAboveHorizonHours(Location location, double shiftInMinutes)
        {
            double minAltitudeDegrees = MinHorizonAngle * 180.0 / Math.PI;
            DateTime start = new DateTime(Year, 1, 1);

            int count = 0;
            for (int h = 0; h < HoursInYear; h++)
            {
                DateTime sunTime = start.AddHours(h).AddMinutes(shiftInMinutes);
                Innovative.SolarCalculator.SolarTimes solarTimes = Geometry.SolarCalculator.Create.SolarTimes(location, sunTime);
                double altitude = System.Convert.ToDouble(solarTimes.SolarElevation.Radians) * 180.0 / Math.PI;
                if (altitude >= minAltitudeDegrees)
                {
                    count++;
                }
            }

            return count;
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void T1_EvaluatedHours_Conserved_Across_TimeShifts()
        {
            Location location = TestHelpers.London();

            // A single free-standing south-facing cell with no context: the study is about the
            // TIMELINE, so nothing must be able to remove an hour for a geometric reason.
            List<AnalysisCell> cells = SyntheticTargets.Cell(SyntheticTargets.South);
            List<Vector3D> normals = SyntheticTargets.Normals(cells, SyntheticTargets.South);
            List<LinkedFace3D> noOccluders = new List<LinkedFace3D>();

            // Full-year weather, every hour populated (so MissingWeatherHours can only be non-zero
            // through a real defect) with a physically shaped profile: GHI proportional to
            // sin(elevation) on the UNSHIFTED timeline, so the same file serves every shift and the
            // reported dropped-energy share is a real twilight number rather than an artefact of a
            // rectangular day profile.
            WeatherData weatherData = TestHelpers.SolarSymmetricWeatherData(Year, location, 0.0, 900.0, 0.25);

            AnalysisPeriod period = new AnalysisPeriod(Year);
            Assert.Equal(HoursInYear, period.HoursOfYear().Count);

            output.WriteLine("shift |  expected | evaluated |  missedBin | belowHorizon | missingWeather |  sum | droppedGHI%");
            output.WriteLine("------+-----------+-----------+------------+--------------+----------------+------+------------");

            foreach (double shift in new[] { -60.0, -30.0, 0.0, 30.0, 60.0 })
            {
                SolarVisibilityCache solarCache = Weather.SolarCalculator.Create.SolarVisibilityCache(
                    location, Year, 2.0, noOccluders, cells, 1.0, MinHorizonAngle, shift);
                SkyVisibilityCache skyCache = Weather.SolarCalculator.Create.SkyVisibilityCache(noOccluders, cells, 1.0);
                Assert.NotNull(solarCache);
                Assert.Equal(shift, solarCache.SunPositionShiftInMinutes);

                CachedIrradianceResult result = Analytical.SolarCalculator.Query.CachedIrradiance(
                    solarCache, skyCache, weatherData, period, normals, SkyModel.PerezAnisotropic, 0.2);
                Assert.NotNull(result);

                int expected = ExpectedAboveHorizonHours(location, shift);
                int sum = result.EvaluatedHours + result.MissedBinHours + result.BelowHorizonHours + result.MissingWeatherHours;

                // Energy actually dropped by the horizon gate, as a share of the annual global
                // horizontal total: this is what "silently losing valid hours" would look like.
                double droppedGlobal = 0;
                double totalGlobal = 0;
                DateTime start = new DateTime(Year, 1, 1);
                double minAltitudeDegrees = MinHorizonAngle * 180.0 / Math.PI;
                for (int h = 0; h < HoursInYear; h++)
                {
                    DateTime dateTime = start.AddHours(h);
                    double global = weatherData.GetWeatherHour(dateTime).GlobalSolarRadiation;
                    if (double.IsNaN(global))
                    {
                        continue;
                    }

                    totalGlobal += global;
                    Geometry.SolarCalculator.Query.TryGetSunAngles(location, dateTime.AddMinutes(shift), out double altitude, out _);
                    if (altitude < minAltitudeDegrees)
                    {
                        droppedGlobal += global;
                    }
                }

                double droppedPercent = 100.0 * droppedGlobal / totalGlobal;
                output.WriteLine($"{shift,5:+0;-0;0} | {expected,9} | {result.EvaluatedHours,9} | {result.MissedBinHours,10} | {result.BelowHorizonHours,12} | {result.MissingWeatherHours,14} | {sum,4} | {droppedPercent,10:0.00}");

                // 1. Nothing is lost: every period hour is accounted for exactly once.
                Assert.Equal(HoursInYear, sum);

                // 2. No hour may be dropped because its sun position fell outside the cached groups.
                //    Bin construction and bin lookup share one angle source, so this is exact.
                Assert.Equal(0, result.MissedBinHours);

                // 3. No hour may be dropped for missing weather (every hour is populated).
                Assert.Equal(0, result.MissingWeatherHours);

                // 4. The evaluated count must equal the independent above-horizon count EXACTLY.
                Assert.Equal(expected, result.EvaluatedHours);

                // 5. Sanity: London sees roughly 4000-4700 hours above 2 deg.
                Assert.InRange(result.EvaluatedHours, 4000, 4700);

                // 6. The horizon gate is the ONLY route by which a valid hour's energy leaves the
                //    integration, and what it removes must stay marginal (twilight only). The bound
                //    is 3 % here rather than the 2 % applied to real EPW weather because this
                //    synthetic year is cloudless on every single day, which over-weights the
                //    low-sun hours relative to any real climate file (see
                //    T1_Horizon_Gate_Energy_Loss_On_Real_Weather_Is_Marginal for the real number).
                Assert.True(droppedPercent < 3.0, $"shift {shift}: horizon gate drops {droppedPercent:0.00} % of annual GHI");
            }
        }

        [Fact]
        public void T1_Horizon_Gate_Energy_Loss_On_Real_Weather_Is_Marginal()
        {
            // The same measurement against the real EPW-imported weather attached to ModelB, which
            // is the number that belongs in the documentation. Hours whose sun is below the 2 deg
            // minimum horizon angle are skipped whole — their diffuse and ground-reflected energy is
            // not integrated. This is the released engine's convention (Modify.Simulate applies the
            // same gate); it is recorded here as a bounded, measured limitation rather than assumed
            // negligible.
            AnalyticalModel analyticalModel = Load("ModelB-NoShadeSolarSimulation.sam");
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            Location location = weatherData.Location ?? analyticalModel.Location;
            int year = WeatherYear(analyticalModel);

            double minAltitudeDegrees = MinHorizonAngle * 180.0 / Math.PI;
            DateTime start = new DateTime(year, 1, 1);
            int hours = DateTime.IsLeapYear(year) ? 8784 : 8760;

            foreach (double shift in new[] { 0.0, 30.0 })
            {
                double total = 0;
                double dropped = 0;
                double droppedDiffuse = 0;
                double totalDiffuse = 0;
                int droppedHoursWithEnergy = 0;
                for (int h = 0; h < hours; h++)
                {
                    DateTime dateTime = start.AddHours(h);
                    WeatherHour weatherHour = weatherData.GetWeatherHour(dateTime);
                    if (weatherHour == null)
                    {
                        continue;
                    }

                    double global = weatherHour.GlobalSolarRadiation;
                    double diffuse = weatherHour.DiffuseSolarRadiation;
                    if (double.IsNaN(global) || double.IsNaN(diffuse))
                    {
                        continue;
                    }

                    total += global;
                    totalDiffuse += diffuse;

                    Geometry.SolarCalculator.Query.TryGetSunAngles(location, dateTime.AddMinutes(shift), out double altitude, out _);
                    if (altitude < minAltitudeDegrees)
                    {
                        dropped += global;
                        droppedDiffuse += diffuse;
                        if (global > 0)
                        {
                            droppedHoursWithEnergy++;
                        }
                    }
                }

                output.WriteLine($"ModelB EPW weather, shift {shift,+3:+0;-0;0} min: horizon gate (2 deg) drops {100.0 * dropped / total:0.00} % of annual GHI and {100.0 * droppedDiffuse / totalDiffuse:0.00} % of annual DHI, over {droppedHoursWithEnergy} hours carrying energy");
                Assert.True(100.0 * dropped / total < 2.0);
                Assert.True(100.0 * droppedDiffuse / totalDiffuse < 4.0);
            }
        }

        [Fact]
        public void T1_Named_Conventions_Map_To_The_Documented_Minutes()
        {
            Assert.Equal(0.0, SunTimeConvention.OnTheHour.TimeShiftInMinutes());
            Assert.Equal(30.0, SunTimeConvention.IntervalStart.TimeShiftInMinutes());
            Assert.Equal(-30.0, SunTimeConvention.IntervalEnd.TimeShiftInMinutes());
            Assert.True(double.IsNaN(SunTimeConvention.Undefined.TimeShiftInMinutes()));

            // IntervalStart and IntervalEnd are one hour apart: they label the SAME physical
            // interval from opposite ends, so a SAM timestamp read as IntervalEnd samples the sun a
            // full hour before the same timestamp read as IntervalStart.
            Assert.Equal(60.0, SunTimeConvention.IntervalStart.TimeShiftInMinutes() - SunTimeConvention.IntervalEnd.TimeShiftInMinutes());
        }

        [Fact]
        public void T1_Named_Convention_Path_Equals_Explicit_Minutes_Path()
        {
            // The named overload must be nothing but a lookup in front of the explicit-minutes one:
            // same results, same recorded shift, same convention read back off the result.
            AnalyticalModel byConvention = Load("ModelB-NoShadeSolarSimulation.sam");
            AnalyticalModel byMinutes = Load("ModelB-NoShadeSolarSimulation.sam");

            // One aperture only — the equivalence is about the timeline, not the model size.
            Guid apertureGuid = byConvention.ApertureSolarTargets(null, 0.5)[0].ApertureGuid;
            List<Guid> selection = new List<Guid> { apertureGuid };

            int year = WeatherYear(byConvention);
            AnalysisPeriod period = new AnalysisPeriod(year);

            List<ApertureIrradianceResult> a = byConvention.SimulateApertures(
                period, out _, null, selection, 0.5, SkyModel.PerezAnisotropic, 2.0, false, 0.2, SunTimeConvention.IntervalEnd);
            List<ApertureIrradianceResult> b = byMinutes.SimulateApertures(
                period, out _, null, selection, 0.5, SkyModel.PerezAnisotropic, 2.0, false, 0.2, -30.0);

            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Single(a);
            Assert.Single(b);
            Assert.Equal(a[0].TotalEnergy, b[0].TotalEnergy, 9);
            Assert.Equal(-30.0, a[0].TimeShiftInMinutes);
            Assert.Equal(SunTimeConvention.IntervalEnd, a[0].SunTimeConvention);
            Assert.Equal(SunTimeConvention.IntervalEnd, b[0].SunTimeConvention);

            // And the DEFAULT is the EPW/SAM weather convention, not the TAS compatibility one.
            AnalyticalModel byDefault = Load("ModelB-NoShadeSolarSimulation.sam");
            List<ApertureIrradianceResult> c = byDefault.SimulateApertures(period, out _, null, selection, 0.5);
            Assert.Equal(30.0, c[0].TimeShiftInMinutes);
            Assert.Equal(SunTimeConvention.IntervalStart, c[0].SunTimeConvention);
            output.WriteLine($"one aperture, annual kWh: IntervalStart(+30)={c[0].TotalEnergy:0.###} IntervalEnd(-30)={a[0].TotalEnergy:0.###}");

            // The two conventions are a real, measurable hour apart — not a cosmetic label.
            Assert.NotEqual(c[0].TotalEnergy, a[0].TotalEnergy, 3);
        }

        [Fact]
        public void T1_Convention_Change_Invalidates_The_Previous_Calculation()
        {
            // A convention change must rebuild rather than silently reuse groups built on the other
            // timeline (this is the failure mode B1 was about).
            AnalyticalModel analyticalModel = Load("ModelB-NoShadeSolarSimulation.sam");
            Guid apertureGuid = analyticalModel.ApertureSolarTargets(null, 0.5)[0].ApertureGuid;
            List<Guid> selection = new List<Guid> { apertureGuid };
            AnalysisPeriod period = new AnalysisPeriod(WeatherYear(analyticalModel));

            analyticalModel.SimulateApertures(period, out bool reusedFirst, null, selection, 0.5, SkyModel.PerezAnisotropic, 2.0, false, 0.2, SunTimeConvention.IntervalStart);
            Assert.False(reusedFirst);

            analyticalModel.SimulateApertures(period, out bool reusedSame, null, selection, 0.5, SkyModel.PerezAnisotropic, 2.0, false, 0.2, SunTimeConvention.IntervalStart);
            Assert.True(reusedSame);

            analyticalModel.SimulateApertures(period, out bool reusedOther, null, selection, 0.5, SkyModel.PerezAnisotropic, 2.0, false, 0.2, SunTimeConvention.IntervalEnd);
            Assert.False(reusedOther);
        }

        [Fact]
        public void B6_Genuine_DNI_In_DirectSolarRadiation_Does_Not_Change_The_Aperture_Result()
        {
            // B6 regression. Weather carrying GHI, DHI *and* a populated DirectSolarRadiation field.
            // Two variants: one where the field holds a genuine DNI (what an EPW field-14 reader
            // would produce) and one where it holds an absurd sentinel. The aperture workflow must
            // derive beam-horizontal as (GHI - DHI) and never read the field, so all three runs must
            // agree bit-for-bit — in particular the genuine DNI must never be divided by
            // sin(elevation) a second time.
            Location location = TestHelpers.London();
            List<AnalysisCell> cells = SyntheticTargets.Cell(SyntheticTargets.South);
            List<Vector3D> normals = SyntheticTargets.Normals(cells, SyntheticTargets.South);
            List<LinkedFace3D> noOccluders = new List<LinkedFace3D>();

            Func<DateTime, Tuple<double, double>> globalDiffuse = dateTime =>
            {
                if (!Geometry.SolarCalculator.Query.TryGetSunAngles(location, dateTime.AddMinutes(30), out double altitude, out _) || altitude <= 0)
                {
                    return Tuple.Create(0.0, 0.0);
                }

                double global = 850.0 * Math.Sin(altitude * Math.PI / 180.0);
                return Tuple.Create(global, global * 0.18);
            };

            // Genuine DNI = beam-horizontal / sin(elevation) — numerically much larger than the
            // beam-horizontal it was derived from, so consuming it by mistake is unmissable.
            Func<DateTime, double> genuineDni = dateTime =>
            {
                Tuple<double, double> values = globalDiffuse(dateTime);
                double beamHorizontal = values.Item1 - values.Item2;
                if (beamHorizontal <= 0)
                {
                    return 0.0;
                }

                Geometry.SolarCalculator.Query.TryGetSunAngles(location, dateTime.AddMinutes(30), out double altitude, out _);
                return beamHorizontal / Math.Max(Math.Sin(altitude * Math.PI / 180.0), Math.Sin(5.0 * Math.PI / 180.0));
            };

            WeatherData withoutField = TestHelpers.SyntheticWeatherData(Year, location, dateTime =>
            {
                Tuple<double, double> values = globalDiffuse(dateTime);
                return Tuple.Create(values.Item1, values.Item2, 0.0);
            });
            WeatherData withGenuineDni = TestHelpers.SyntheticWeatherData_WithDirectField(Year, location, globalDiffuse, genuineDni);
            WeatherData withSentinel = TestHelpers.SyntheticWeatherData_WithDirectField(Year, location, globalDiffuse, dateTime => 9999.0);

            // The fixtures really do differ where it matters.
            DateTime probe = new DateTime(Year, 6, 21, 12, 0, 0);
            Assert.True(double.IsNaN(withoutField.GetWeatherHour(probe).DirectSolarRadiation));
            Assert.True(withGenuineDni.GetWeatherHour(probe).DirectSolarRadiation > 0);
            Assert.Equal(9999.0, withSentinel.GetWeatherHour(probe).DirectSolarRadiation);
            Assert.Equal(withoutField.GetWeatherHour(probe).GlobalSolarRadiation, withGenuineDni.GetWeatherHour(probe).GlobalSolarRadiation, 12);
            Assert.Equal(withoutField.GetWeatherHour(probe).DiffuseSolarRadiation, withGenuineDni.GetWeatherHour(probe).DiffuseSolarRadiation, 12);

            AnalysisPeriod period = new AnalysisPeriod(Year);
            SolarVisibilityCache solarCache = Weather.SolarCalculator.Create.SolarVisibilityCache(location, Year, 2.0, noOccluders, cells, 1.0, MinHorizonAngle, 30.0);
            SkyVisibilityCache skyCache = Weather.SolarCalculator.Create.SkyVisibilityCache(noOccluders, cells, 1.0);

            CachedIrradianceResult a = Analytical.SolarCalculator.Query.CachedIrradiance(solarCache, skyCache, withoutField, period, normals, SkyModel.PerezAnisotropic, 0.2);
            CachedIrradianceResult b = Analytical.SolarCalculator.Query.CachedIrradiance(solarCache, skyCache, withGenuineDni, period, normals, SkyModel.PerezAnisotropic, 0.2);
            CachedIrradianceResult c = Analytical.SolarCalculator.Query.CachedIrradiance(solarCache, skyCache, withSentinel, period, normals, SkyModel.PerezAnisotropic, 0.2);

            output.WriteLine($"south 1 m2 annual kWh/m2 — no direct field: direct={a.Direct[0]:0.####} diffuse={a.Diffuse[0]:0.####} ground={a.GroundReflected[0]:0.####}");
            output.WriteLine($"                        genuine DNI field: direct={b.Direct[0]:0.####} diffuse={b.Diffuse[0]:0.####} ground={b.GroundReflected[0]:0.####}");
            output.WriteLine($"                      sentinel 9999 field: direct={c.Direct[0]:0.####} diffuse={c.Diffuse[0]:0.####} ground={c.GroundReflected[0]:0.####}");

            Assert.Equal(a.EvaluatedHours, b.EvaluatedHours);
            Assert.Equal(a.EvaluatedHours, c.EvaluatedHours);
            for (int i = 0; i < a.CellCount; i++)
            {
                Assert.Equal(a.Direct[i], b.Direct[i], 12);
                Assert.Equal(a.Diffuse[i], b.Diffuse[i], 12);
                Assert.Equal(a.GroundReflected[i], b.GroundReflected[i], 12);
                Assert.Equal(a.Direct[i], c.Direct[i], 12);
                Assert.Equal(a.Diffuse[i], c.Diffuse[i], 12);
                Assert.Equal(a.GroundReflected[i], c.GroundReflected[i], 12);
            }

            Assert.True(a.Direct[0] > 0, "the regression is only meaningful if there IS direct energy to get wrong");
        }

        [Fact]
        public void B6_Legacy_Radiation_Path_Still_Consumes_The_Direct_Field_Unchanged()
        {
            // The counterpart guarantee: legacy behaviour is NOT altered globally. The released
            // Weather.SolarCalculator.Create.Radiation path still feeds
            // WeatherHour.CalculatedDirectSolarRadiation() into the legacy isotropic formula, so a
            // populated DirectSolarRadiation field still changes its answer exactly as before.
            Location location = TestHelpers.London();
            DateTime dateTime = new DateTime(Year, 6, 21, 12, 0, 0);

            WeatherData withoutField = TestHelpers.SyntheticWeatherData(Year, location, dt => Tuple.Create(700.0, 200.0, 0.0));
            WeatherData withField = TestHelpers.SyntheticWeatherData_WithDirectField(Year, location, dt => Tuple.Create(700.0, 200.0), dt => 123.0);

            Plane plane = new Plane(new Point3D(0, 0, 0), new Vector3D(0, -1, 0));
            Radiation legacyWithout = Weather.SolarCalculator.Create.Radiation(withoutField, dateTime, plane);
            Radiation legacyWith = Weather.SolarCalculator.Create.Radiation(withField, dateTime, plane);

            Assert.NotNull(legacyWithout);
            Assert.NotNull(legacyWith);

            // Legacy reads the field when present (500 = 700-200 without it, 123 with it), so the
            // beam term scales by exactly 123/500.
            Assert.Equal(legacyWithout.DirectNormal * 123.0 / 500.0, legacyWith.DirectNormal, 9);
            output.WriteLine($"legacy beam W/m2: derived(500)={legacyWithout.DirectNormal:0.####} field(123)={legacyWith.DirectNormal:0.####}");
        }

        private static int WeatherYear(AnalyticalModel analyticalModel)
        {
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            return weatherData.WeatherYears.First(x => x != null).Year;
        }
    }
}
