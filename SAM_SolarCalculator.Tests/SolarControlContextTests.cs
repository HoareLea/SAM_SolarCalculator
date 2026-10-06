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
    /// The context-visibility integration into <see cref="SolarControlProfile"/>: the aperture-sun
    /// gate additionally requires direct sun to actually reach the aperture through the
    /// surroundings, read from a <see cref="SolarVisibilityCache"/>, while every approved control,
    /// shading-operation and energy semantic is preserved.
    ///
    /// Everything is asserted as a RELATIONSHIP or against a value the test computes itself, the
    /// same discipline as <see cref="SolarControlProfileTests"/>.
    /// </summary>
    public class SolarControlContextTests
    {
        private readonly ITestOutputHelper output;

        public SolarControlContextTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const int Year = 2018;
        private const double Shift = 30.0;
        private const double MinHorizonAngle = 0.0349066; // radians (~2 deg), the production default.

        private static Location London()
        {
            return TestHelpers.London();
        }

        private static ApertureSolarTarget Target(Vector3D outward, double gridSize = 0.5, double size = 1.0)
        {
            Face3D face = SyntheticTargets.Face(outward, new Point3D(0, 0, 5), size);
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(face, gridSize);
            return new ApertureSolarTarget(Guid.NewGuid(), Guid.NewGuid(), face, cells);
        }

        private static int HourOfYear(DateTime dateTime)
        {
            return (int)(dateTime - new DateTime(dateTime.Year, 1, 1)).TotalHours;
        }

        private static readonly object padlock = new object();
        private static readonly Dictionary<int, double[]> sinAltitudeByYear = new Dictionary<int, double[]>();

        /// <summary>
        /// sin(solar altitude) at every hour of the year, clamped at zero below the horizon, on the
        /// same sampling shift the tests analyse on. Memoised: evaluating the sun 8760 times is the
        /// slow part of building synthetic weather.
        /// </summary>
        private static double[] SinAltitude(int year, Location location)
        {
            lock (padlock)
            {
                if (sinAltitudeByYear.TryGetValue(year, out double[] existing))
                {
                    return existing;
                }

                DateTime start = new DateTime(year, 1, 1);
                double[] result = new double[DateTime.IsLeapYear(year) ? 8784 : 8760];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = Geometry.SolarCalculator.Query.TryGetSunAngles(location, start.AddHours(i).AddMinutes(Shift), out double altitude, out _) && altitude > 0
                        ? Math.Sin(altitude * Math.PI / 180.0)
                        : 0.0;
                }

                sinAltitudeByYear[year] = result;
                return result;
            }
        }

        /// <summary>
        /// Synthetic weather whose radiation is a pure function of the sun position at
        /// (timestamp + shift), with NO diffuse, so the direct normal irradiance is exactly
        /// <paramref name="directNormal"/> whenever the sun is up.
        /// </summary>
        private static WeatherData SyntheticWeather(int year, Location location, double directNormal = 900.0)
        {
            double[] sinAltitude = SinAltitude(year, location);

            WeatherData weatherData = new WeatherData("Synthetic", "Synthetic context weather", location.Latitude, location.Longitude, location.Elevation);
            weatherData.SetValue(WeatherDataParameter.TimeZone, "UTC+00:00");

            DateTime start = new DateTime(year, 1, 1);
            for (int i = 0; i < sinAltitude.Length; i++)
            {
                weatherData.Add(start.AddHours(i), new Dictionary<string, double>
                {
                    { WeatherDataType.GlobalSolarRadiation.ToString(), directNormal * sinAltitude[i] },
                    { WeatherDataType.DiffuseSolarRadiation.ToString(), 0.0 },
                });
            }

            return weatherData;
        }

        private static SolarVisibilityCache Cache(ApertureSolarTarget target, List<LinkedFace3D> occluders, int year = Year, double shiftInMinutes = Shift)
        {
            return Weather.SolarCalculator.Create.SolarVisibilityCache(
                London(), year, 2.0, occluders, target.AnalysisCells,
                cellSize: 1.0, minHorizonAngle: MinHorizonAngle, sunPositionShiftInMinutes: shiftInMinutes);
        }

        /// <summary>A closed box around the south-facing window: no sun reaches it from any direction.</summary>
        private static List<LinkedFace3D> FullShadeOccluders()
        {
            return new List<LinkedFace3D>
            {
                new LinkedFace3D(Guid.NewGuid(), SyntheticTargets.Face(SyntheticTargets.North, new Point3D(0, -3, 5), 20.0, 20.0)), // front (south)
                new LinkedFace3D(Guid.NewGuid(), SyntheticTargets.Face(SyntheticTargets.South, new Point3D(0, 3, 5), 20.0, 20.0)),  // back (north)
                new LinkedFace3D(Guid.NewGuid(), SyntheticTargets.Face(SyntheticTargets.West, new Point3D(3, 0, 5), 20.0, 20.0)),   // east side
                new LinkedFace3D(Guid.NewGuid(), SyntheticTargets.Face(SyntheticTargets.East, new Point3D(-3, 0, 5), 20.0, 20.0)),  // west side
                new LinkedFace3D(Guid.NewGuid(), SyntheticTargets.Face(SyntheticTargets.Up, new Point3D(0, 0, 15), 20.0, 20.0)),    // roof
                new LinkedFace3D(Guid.NewGuid(), SyntheticTargets.Face(SyntheticTargets.Up, new Point3D(0, 0, -5), 20.0, 20.0)),    // floor
            };
        }

        /// <summary>
        /// A horizontal overhang above a south-facing window that shades HIGH sun (summer noon) but
        /// lets the LOW winter sun pass under it — visibility varies with the sun position.
        /// </summary>
        private static List<LinkedFace3D> OverhangOccluders()
        {
            return new List<LinkedFace3D>
            {
                new LinkedFace3D(Guid.NewGuid(), SyntheticTargets.Face(SyntheticTargets.Up, new Point3D(0, -1.5, 6.2), 4.0, 3.0)),
            };
        }

        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        private static AnalyticalModel Load(string fileName)
        {
            string path = Path.Combine(FixturesDirectory, fileName);
            Assert.True(File.Exists(path), $"Test fixture missing: {path}");

            AnalyticalModel analyticalModel = SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);
            Assert.NotNull(analyticalModel);
            return analyticalModel;
        }

        private static int WeatherYearOf(AnalyticalModel analyticalModel)
        {
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            Assert.NotNull(weatherData);
            return weatherData.WeatherYears.First(x => x != null).Year;
        }

        // --------------------------------------------------- no context / full visibility ----------

        [Fact]
        public void Full_Visibility_Matches_The_Weather_Only_Path_Up_To_The_Min_Horizon_Band()
        {
            ApertureSolarTarget target = Target(SyntheticTargets.South);
            WeatherData weatherData = SyntheticWeather(Year, London());
            SolarControlSettings settings = new SolarControlSettings(200.0);

            SolarControlProfile noContext = target.SolarControlProfile(weatherData, settings, Year);
            SolarControlProfile fullVisibility = target.SolarControlProfile(weatherData, settings, Cache(target, new List<LinkedFace3D>()), 0, Year);

            Assert.NotNull(noContext);
            Assert.NotNull(fullVisibility);

            // Daylight is a property of the site and never changes.
            Assert.Equal(noContext.DaylightHoursOfYear, fullVisibility.DaylightHoursOfYear);
            Assert.Equal(noContext.Year, fullVisibility.Year);
            Assert.Equal(noContext.TimeShiftInMinutes, fullVisibility.TimeShiftInMinutes, 12);

            // With no occluders every lit bin is visible, so the context path can only remove the
            // hours the sun bins do not cover — those below the cache's minimum horizon angle.
            HashSet<int> noContextSun = new HashSet<int>(noContext.ApertureSunHoursOfYear);
            HashSet<int> contextSun = new HashSet<int>(fullVisibility.ApertureSunHoursOfYear);
            Assert.Empty(fullVisibility.ApertureSunHoursOfYear.Except(noContext.ApertureSunHoursOfYear));
            Assert.Empty(fullVisibility.ShadeDemandHoursOfYear.Except(noContext.ShadeDemandHoursOfYear));

            double minHorizonDegrees = Cache(target, new List<LinkedFace3D>()).MinHorizonAngle * 180.0 / Math.PI;
            List<ApertureSolarHour> hours = target.ApertureSolarHours(weatherData, Year, Shift);

            foreach (int hourOfYear in noContextSun.Except(contextSun))
            {
                ApertureSolarHour hour = hours.First(x => x.HourOfYear == hourOfYear);
                Assert.True(hour.SolarElevation < minHorizonDegrees,
                    $"removed hour {hourOfYear} has elevation {hour.SolarElevation:0.###} deg, above the min-horizon gate {minHorizonDegrees:0.###} deg");
            }

            // And every no-context aperture-sun hour above the gate is retained: the two paths agree
            // everywhere the cache has an opinion.
            foreach (ApertureSolarHour hour in hours)
            {
                if (noContextSun.Contains(hour.HourOfYear) && hour.SolarElevation >= minHorizonDegrees)
                {
                    Assert.Contains(hour.HourOfYear, contextSun);
                }
            }

            output.WriteLine($"no-context {noContext.ApertureSunHours} sun hours vs full-visibility {fullVisibility.ApertureSunHours} (min-horizon gate {minHorizonDegrees:0.###} deg)");
        }

        // --------------------------------------------------------------- full context shade ----------

        [Fact]
        public void A_Fully_Shaded_Aperture_Has_No_Aperture_Sun_And_No_Demand()
        {
            ApertureSolarTarget target = Target(SyntheticTargets.South);
            WeatherData weatherData = SyntheticWeather(Year, London());
            SolarControlSettings settings = new SolarControlSettings(200.0);

            SolarControlProfile noContext = target.SolarControlProfile(weatherData, settings, Year);
            SolarControlProfile shaded = target.SolarControlProfile(weatherData, settings, Cache(target, FullShadeOccluders()), 0, Year);

            Assert.NotNull(noContext);
            Assert.NotNull(shaded);
            Assert.True(noContext.ApertureSunHours > 100, "the fixture must have sun for the shade to mean anything");
            Assert.True(noContext.ShadeDemandHours > 0);

            // The enclosing box removes every aperture-sun hour, and with it every demand hour:
            // context-shaded solar is not available direct solar.
            Assert.Equal(0, shaded.ApertureSunHours);
            Assert.Equal(0, shaded.ShadeDemandHours);

            output.WriteLine($"no-context {noContext.ApertureSunHours} sun / {noContext.ShadeDemandHours} demand; full-shade {shaded.ApertureSunHours} sun / {shaded.ShadeDemandHours} demand");
        }

        // ------------------------------------------------------- partial / time-varying context ----

        [Fact]
        public void An_Overhang_Removes_High_Summer_Sun_And_Keeps_The_Low_Winter_Sun()
        {
            ApertureSolarTarget target = Target(SyntheticTargets.South, 0.5, 2.0); // 2x2 m window, 16 cells
            WeatherData weatherData = SyntheticWeather(Year, London());
            SolarControlSettings settings = new SolarControlSettings(200.0);

            SolarControlProfile noContext = target.SolarControlProfile(weatherData, settings, Year);
            SolarControlProfile shaded = target.SolarControlProfile(weatherData, settings, Cache(target, OverhangOccluders()), 0, Year);

            Assert.NotNull(noContext);
            Assert.NotNull(shaded);

            int summerNoon = HourOfYear(new DateTime(Year, 6, 21, 12, 0, 0));
            int winterNoon = HourOfYear(new DateTime(Year, 12, 21, 12, 0, 0));

            // Both are aperture-sun hours with no context (sun in front, non-zero beam).
            Assert.Contains(summerNoon, noContext.ApertureSunHoursOfYear);
            Assert.Contains(winterNoon, noContext.ApertureSunHoursOfYear);

            // The overhang shades the HIGH summer sun but lets the LOW winter sun pass underneath:
            // visibility changes with the sun position, and the control profile must follow it.
            Assert.DoesNotContain(summerNoon, shaded.ApertureSunHoursOfYear);
            Assert.Contains(winterNoon, shaded.ApertureSunHoursOfYear);

            // Partial, not full: some sun remains, but strictly less than with no context.
            Assert.True(shaded.ApertureSunHours > 0);
            Assert.True(shaded.ApertureSunHours < noContext.ApertureSunHours);
            Assert.Empty(shaded.ApertureSunHoursOfYear.Except(noContext.ApertureSunHoursOfYear));

            output.WriteLine($"summer noon HOY {summerNoon} (shaded out), winter noon HOY {winterNoon} (kept): {noContext.ApertureSunHours} -> {shaded.ApertureSunHours} sun hours");
        }

        // -------------------------------------------------------------------- timeline alignment ----

        [Fact]
        public void A_Cache_On_A_Different_Timeline_Is_Refused_Not_Approximated()
        {
            ApertureSolarTarget target = Target(SyntheticTargets.South);
            WeatherData weatherData = SyntheticWeather(Year, London());
            SolarControlSettings settings = new SolarControlSettings(200.0);

            SolarVisibilityCache matching = Cache(target, new List<LinkedFace3D>());

            // Matching year and shift: accepted, and the profile records the cache's own timeline.
            SolarControlProfile profile = target.SolarControlProfile(weatherData, settings, matching, 0, Year);
            Assert.NotNull(profile);
            Assert.Equal(matching.Year, profile.Year);
            Assert.Equal(matching.SunPositionShiftInMinutes, profile.TimeShiftInMinutes, 12);

            // A different SUN-POSITION SHIFT than the cache is refused.
            Assert.Null(target.SolarControlProfile(weatherData, settings, matching, 0, Year, null, SunTimeConvention.OnTheHour));

            // A different YEAR than the cache is refused.
            SolarVisibilityCache otherYear = Cache(target, new List<LinkedFace3D>(), 2019);
            Assert.Null(target.SolarControlProfile(weatherData, settings, otherYear, 0, Year));
        }

        // ------------------------------------------------------------------ operation semantics ----

        [Fact]
        public void Context_Visibility_Changes_The_Schedule_Not_The_Geometric_Energy()
        {
            ApertureSolarTarget target = Target(SyntheticTargets.South, 0.5, 2.0);
            WeatherData weatherData = SyntheticWeather(Year, London());
            SolarControlSettings settings = new SolarControlSettings(200.0);

            List<LinkedFace3D> context = OverhangOccluders();
            SolarVisibilityCache cache = Cache(target, context);

            SolarControlProfile noContext = target.SolarControlProfile(weatherData, settings, Year);
            SolarControlProfile withContext = target.SolarControlProfile(weatherData, settings, cache, 0, Year);

            Assert.True(withContext.ShadeDemandHours < noContext.ShadeDemandHours, "context shade must remove demand hours");

            RetractableAwning awning = new RetractableAwning(1.6, 28.0);
            ShadingOperationProfile a = Analytical.SolarCalculator.Create.ShadingOperationProfile(target, noContext, cache, context, awning, weatherData);
            ShadingOperationProfile b = Analytical.SolarCalculator.Create.ShadingOperationProfile(target, withContext, cache, context, awning, weatherData);

            Assert.NotNull(a);
            Assert.NotNull(b);

            // The PR #20 full-year geometric quantities never read the schedule: identical geometry,
            // cache and weather give identical direct interception and identical attribution, however
            // the context gating reshaped the hours.
            Assert.True(a.ControlledDirectSolarIntercepted > 0, "the awning must intercept beam for the equality to be meaningful");
            Assert.Equal(a.ControlledDirectSolarIntercepted, b.ControlledDirectSolarIntercepted, 9);
            Assert.Equal(a.CanopyAttributedEnergy, b.CanopyAttributedEnergy, 9);
            Assert.Equal(a.EnergyPerElement.Keys.OrderBy(x => x), b.EnergyPerElement.Keys.OrderBy(x => x));
            foreach (Guid guid in a.EnergyPerElement.Keys)
            {
                Assert.Equal(a.EnergyPerElement[guid], b.EnergyPerElement[guid], 9);
            }

            output.WriteLine($"demand: no-context {noContext.ShadeDemandHours} h -> context {withContext.ShadeDemandHours} h; geometric direct intercepted {a.ControlledDirectSolarIntercepted:0.###} kWh (unchanged)");
        }

        // ------------------------------------------------------ production context reuse (fixture) ----

        [Fact]
        public void The_Context_Overload_Reuses_The_Production_Aperture_Context()
        {
            AnalyticalModel analyticalModel = Load("ModelB-NoShadeSolarSimulation.sam");
            int year = WeatherYearOf(analyticalModel);

            ApertureSolarContext context = Analytical.SolarCalculator.Create.ApertureSolarContext(analyticalModel, year, gridSize: 0.5);
            Assert.NotNull(context);
            Assert.NotEmpty(context.Targets);

            ApertureSolarTarget target = context.Targets.First();
            WeatherData weatherData = context.WeatherData;
            SolarControlSettings settings = new SolarControlSettings(200.0);

            SolarControlProfile noContext = target.SolarControlProfile(weatherData, settings, context.Year);
            SolarControlProfile withContext = context.SolarControlProfile(target.ApertureGuid, settings);

            Assert.NotNull(noContext);
            Assert.NotNull(withContext);

            // Same target, same timeline; daylight is a site property and never changes.
            Assert.Equal(noContext.Year, withContext.Year);
            Assert.Equal(noContext.TimeShiftInMinutes, withContext.TimeShiftInMinutes, 12);
            Assert.Equal(noContext.DaylightHoursOfYear, withContext.DaylightHoursOfYear);

            // The production cache can only shade the aperture, never add sun.
            Assert.Empty(withContext.ApertureSunHoursOfYear.Except(noContext.ApertureSunHoursOfYear));
            Assert.Empty(withContext.ShadeDemandHoursOfYear.Except(noContext.ShadeDemandHoursOfYear));

            output.WriteLine($"ModelB: {noContext.ApertureSunHours} -> {withContext.ApertureSunHours} aperture-sun hours through the production context");
        }
    }
}
