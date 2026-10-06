// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
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
    /// T2 — east/west symmetry under genuinely AM/PM symmetric weather, on unobstructed vertical
    /// N/E/S/W surfaces.
    ///
    /// Why the weather is built from the sun position rather than from clock time: a clock-time
    /// profile is only AM/PM symmetric about 12:00, while the sun is symmetric about SOLAR noon,
    /// which drifts by up to +-16 minutes over the year (equation of time). Driving the synthetic
    /// radiation from sin(elevation) at the instant the analysis samples the sun makes the series
    /// symmetric on the same timeline the analysis integrates on, so any residual east/west
    /// difference is a defect in the analysis and not a property of the calendar.
    ///
    /// The suite is written to FAIL on: a timestamp-midpoint (sampling offset) error, a rotation of
    /// the solar azimuth, an east/west mirroring, and a wrong sun-vector direction. Each of those is
    /// exercised with an explicit detection-power check rather than merely asserted against.
    /// </summary>
    public class OrientationSymmetryTests
    {
        private readonly ITestOutputHelper output;

        public OrientationSymmetryTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const int Year = 2018;
        private const double MinHorizonAngle = Core.Tolerance.Angle;

        private sealed class Orientation
        {
            public string Name;
            public Vector3D Outward;
            public List<AnalysisCell> Cells;
            public List<Vector3D> Normals;
        }

        private static Orientation Make(string name, Vector3D outward)
        {
            List<AnalysisCell> cells = SyntheticTargets.Cell(outward);
            return new Orientation
            {
                Name = name,
                Outward = outward,
                Cells = cells,
                Normals = SyntheticTargets.Normals(cells, outward),
            };
        }

        /// <summary>Annual per-m2 total for one unobstructed vertical surface, kWh/m2.</summary>
        private static CachedIrradianceResult Run(Location location, Orientation orientation, WeatherData weatherData, double sunShiftInMinutes, AnalysisPeriod period = null)
        {
            List<LinkedFace3D> noOccluders = new List<LinkedFace3D>();
            SolarVisibilityCache solarCache = Weather.SolarCalculator.Create.SolarVisibilityCache(
                location, Year, 2.0, noOccluders, orientation.Cells, 1.0, MinHorizonAngle, sunShiftInMinutes);
            SkyVisibilityCache skyCache = Weather.SolarCalculator.Create.SkyVisibilityCache(noOccluders, orientation.Cells, 1.0);
            return Analytical.SolarCalculator.Query.CachedIrradiance(
                solarCache, skyCache, weatherData, period ?? new AnalysisPeriod(Year), orientation.Normals, SkyModel.PerezAnisotropic, 0.2);
        }

        private static double Total(CachedIrradianceResult result)
        {
            return result.Direct[0] + result.Diffuse[0] + result.GroundReflected[0];
        }

        [Fact]
        public void T2_EastWest_Agree_Under_Symmetric_Weather()
        {
            Location location = TestHelpers.London();

            Orientation north = Make("north", SyntheticTargets.North);
            Orientation east = Make("east", SyntheticTargets.East);
            Orientation south = Make("south", SyntheticTargets.South);
            Orientation west = Make("west", SyntheticTargets.West);

            // Symmetric in solar time on the shift-0 timeline, and integrated on that same timeline.
            const double shift = 0.0;
            WeatherData weatherData = TestHelpers.SolarSymmetricWeatherData(Year, location, shift, 900.0, 0.2);

            CachedIrradianceResult n = Run(location, north, weatherData, shift);
            CachedIrradianceResult e = Run(location, east, weatherData, shift);
            CachedIrradianceResult s = Run(location, south, weatherData, shift);
            CachedIrradianceResult w = Run(location, west, weatherData, shift);

            output.WriteLine("unobstructed 1 m2 vertical surfaces, symmetric synthetic year, kWh/m2:");
            foreach (Tuple<string, CachedIrradianceResult> pair in new[]
            {
                Tuple.Create("north", n), Tuple.Create("east", e), Tuple.Create("south", s), Tuple.Create("west", w),
            })
            {
                output.WriteLine($"  {pair.Item1,-5}: direct={pair.Item2.Direct[0],8:0.###} diffuse={pair.Item2.Diffuse[0],7:0.###} ground={pair.Item2.GroundReflected[0],7:0.###} total={Total(pair.Item2),8:0.###} sunlitHours={pair.Item2.SunlitHours[0],6:0}");
            }

            double directAsymmetry = Asymmetry(e.Direct[0], w.Direct[0]);
            double totalAsymmetry = Asymmetry(Total(e), Total(w));
            double sunlitAsymmetry = Asymmetry(e.SunlitHours[0], w.SunlitHours[0]);
            output.WriteLine($"east/west asymmetry: direct={100.0 * directAsymmetry:0.0000} % total={100.0 * totalAsymmetry:0.0000} % sunlitHours={100.0 * sunlitAsymmetry:0.0000} %");

            // Gate. The only physical residual is that the hourly sample grid is not centred on
            // solar noon (the offset sweeps with the equation of time and largely averages out over
            // a year). Measured on this build: ~0.1 %. The gate is set an order of magnitude below
            // the smallest defect signal measured in the detection-power tests below, and is NOT to
            // be relaxed to make a change pass.
            Assert.True(directAsymmetry < 0.01, $"east/west DIRECT asymmetry {100.0 * directAsymmetry:0.0000} % exceeds the 1 % gate");
            Assert.True(totalAsymmetry < 0.01, $"east/west TOTAL asymmetry {100.0 * totalAsymmetry:0.0000} % exceeds the 1 % gate");

            // The sunlit-HOUR count is REPORTED but deliberately NOT used as a symmetry gate, and
            // the reason is worth stating because it is not obvious: that count is a binary
            // in-front-of/behind decision quantised onto whole hours, so it measures where the
            // hourly sample grid sits relative to solar noon and nothing else. It is completely
            // independent of how the weather is paired with the sun, which is the defect class this
            // test exists to catch — the detection-power test below measures it directly and finds
            // a -30 min sampling error moves this metric to 0.38 % (i.e. it goes DOWN, while the
            // energy asymmetry goes to 59 %). Gating on it would add no protection and would only
            // make the suite fragile. The energy metrics above are the gates.
            Assert.True(sunlitAsymmetry < 0.05, $"east/west sunlit-hour asymmetry {100.0 * sunlitAsymmetry:0.0000} % is far beyond whole-hour quantisation and indicates a real orientation defect");

            // Orientation ordering: a +-90 deg rotation of the solar azimuth would map south onto
            // east or west and break this outright.
            Assert.True(Total(s) > Total(e) * 1.2, $"south {Total(s):0.#} must clearly exceed east {Total(e):0.#}");
            Assert.True(Total(s) > Total(w) * 1.2, $"south {Total(s):0.#} must clearly exceed west {Total(w):0.#}");
            Assert.True(Total(e) > Total(n) * 1.2, $"east {Total(e):0.#} must clearly exceed north {Total(n):0.#}");
            Assert.True(n.Direct[0] < 0.2 * e.Direct[0], "a north facade at 51.5 N must receive far less beam than an east one");
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void T2_Detects_A_Timestamp_Midpoint_Error()
        {
            // Detection power for the exact defect class B2 is about. The weather is symmetric on
            // the shift-0 timeline; integrating it while sampling the sun 30 or 60 minutes away
            // makes the series asymmetric relative to the sun, and east/west must diverge by far
            // more than the gate above.
            Location location = TestHelpers.London();
            Orientation east = Make("east", SyntheticTargets.East);
            Orientation west = Make("west", SyntheticTargets.West);
            WeatherData weatherData = TestHelpers.SolarSymmetricWeatherData(Year, location, 0.0, 900.0, 0.2);

            CachedIrradianceResult referenceEast = Run(location, east, weatherData, 0.0);
            CachedIrradianceResult referenceWest = Run(location, west, weatherData, 0.0);
            double baseline = Asymmetry(referenceEast.Direct[0], referenceWest.Direct[0]);
            double baselineSunlit = Asymmetry(referenceEast.SunlitHours[0], referenceWest.SunlitHours[0]);

            output.WriteLine("sampling-offset detection power (weather symmetric on the 0 min timeline):");
            output.WriteLine($"  sun sampled at +0 min : east/west direct asymmetry {100.0 * baseline:0.0000} % sunlitHours {100.0 * baselineSunlit:0.0000} %  (reference)");

            foreach (double wrongShift in new[] { -60.0, -30.0, 30.0, 60.0 })
            {
                CachedIrradianceResult resultEast = Run(location, east, weatherData, wrongShift);
                CachedIrradianceResult resultWest = Run(location, west, weatherData, wrongShift);
                double e = resultEast.Direct[0];
                double w = resultWest.Direct[0];
                double asymmetry = Asymmetry(e, w);
                double sunlitAsymmetry = Asymmetry(resultEast.SunlitHours[0], resultWest.SunlitHours[0]);
                output.WriteLine($"  sun sampled at {wrongShift,+3:+0;-0;0} min : east/west direct asymmetry {100.0 * asymmetry:0.0000} % sunlitHours {100.0 * sunlitAsymmetry:0.0000} %  (east={e:0.##} west={w:0.##})");

                // Measured fact, pinned so it cannot quietly become the basis of a future gate: the
                // sunlit-HOUR count has NO detection power for a sampling-offset error. A whole-hour
                // shift maps the hourly sample grid onto itself, leaving the count exactly
                // unchanged; a half-hour shift merely re-centres the grid on solar noon, so the
                // count asymmetry goes DOWN even as the energy asymmetry goes to ~59 %.
                if (Math.Abs(wrongShift) >= 60.0)
                {
                    Assert.Equal(baselineSunlit, sunlitAsymmetry, 6);
                }
                else
                {
                    Assert.True(sunlitAsymmetry < baselineSunlit, $"expected the half-hour grid to be MORE hour-symmetric, not less: {100.0 * sunlitAsymmetry:0.0000} % vs {100.0 * baselineSunlit:0.0000} %");
                }

                // A half-hour mis-sampling must show up at least an order of magnitude above the
                // symmetric baseline, otherwise the T2 gate proves nothing.
                Assert.True(asymmetry > 10.0 * baseline, $"a {wrongShift:0} min sampling error is not detectable: asymmetry {100.0 * asymmetry:0.0000} % vs baseline {100.0 * baseline:0.0000} %");
                Assert.True(asymmetry > 0.01, $"a {wrongShift:0} min sampling error must exceed the T2 gate, got {100.0 * asymmetry:0.0000} %");

                // Sign check: sampling the sun LATER than the weather moves energy to the west.
                if (wrongShift > 0)
                {
                    Assert.True(w > e, $"sampling the sun {wrongShift:0} min late must favour the west facade");
                }
                else
                {
                    Assert.True(e > w, $"sampling the sun {wrongShift:0} min early must favour the east facade");
                }
            }
        }

        [Fact]
        public void T2_Detects_EastWest_Mirroring()
        {
            // A symmetric test cannot see a mirrored east/west by construction, so the mirroring
            // guard needs deliberately asymmetric weather: a morning-heavy year must give the east
            // facade decisively more beam than the west.
            Location location = TestHelpers.London();
            Orientation east = Make("east", SyntheticTargets.East);
            Orientation west = Make("west", SyntheticTargets.West);

            WeatherData morningHeavy = TestHelpers.SyntheticWeatherData(Year, location, dateTime =>
            {
                if (!Geometry.SolarCalculator.Query.TryGetSunAngles(location, dateTime, out double altitude, out double azimuth) || altitude <= 0)
                {
                    return Tuple.Create(0.0, 0.0, 0.0);
                }

                // Clear all morning (sun east of south), overcast all afternoon.
                double global = 900.0 * Math.Sin(altitude * Math.PI / 180.0) * (azimuth < 180.0 ? 1.0 : 0.25);
                return Tuple.Create(global, global * 0.2, 0.0);
            });

            double e = Run(location, east, morningHeavy, 0.0).Direct[0];
            double w = Run(location, west, morningHeavy, 0.0).Direct[0];
            output.WriteLine($"morning-heavy year, direct kWh/m2: east={e:0.###} west={w:0.###} (ratio {e / w:0.###})");
            Assert.True(e > 2.0 * w, $"morning-heavy weather must strongly favour east: east={e:0.###} west={w:0.###}");

            // And the mirror image, so the test cannot pass by a constant bias in one direction.
            WeatherData afternoonHeavy = TestHelpers.SyntheticWeatherData(Year, location, dateTime =>
            {
                if (!Geometry.SolarCalculator.Query.TryGetSunAngles(location, dateTime, out double altitude, out double azimuth) || altitude <= 0)
                {
                    return Tuple.Create(0.0, 0.0, 0.0);
                }

                double global = 900.0 * Math.Sin(altitude * Math.PI / 180.0) * (azimuth >= 180.0 ? 1.0 : 0.25);
                return Tuple.Create(global, global * 0.2, 0.0);
            });

            double e2 = Run(location, east, afternoonHeavy, 0.0).Direct[0];
            double w2 = Run(location, west, afternoonHeavy, 0.0).Direct[0];
            output.WriteLine($"afternoon-heavy year, direct kWh/m2: east={e2:0.###} west={w2:0.###} (ratio {w2 / e2:0.###})");
            Assert.True(w2 > 2.0 * e2, $"afternoon-heavy weather must strongly favour west: east={e2:0.###} west={w2:0.###}");
        }

        [Fact]
        public void T2_Sun_Vector_Direction_And_Azimuth_Are_Physical()
        {
            // Direct guards on the sun vector itself, independent of any integration: at London a
            // summer morning sun is in the east and a summer afternoon sun in the west, the sun
            // culminates in the south, and the cached lit bit follows the sun rather than its mirror.
            Location location = TestHelpers.London();

            Geometry.SolarCalculator.Query.TryGetSunAngles(location, new DateTime(Year, 6, 21, 7, 0, 0), out double altitudeMorning, out double azimuthMorning);
            Geometry.SolarCalculator.Query.TryGetSunAngles(location, new DateTime(Year, 6, 21, 12, 0, 0), out double altitudeNoon, out double azimuthNoon);
            Geometry.SolarCalculator.Query.TryGetSunAngles(location, new DateTime(Year, 6, 21, 17, 0, 0), out double altitudeEvening, out double azimuthEvening);
            output.WriteLine($"21 Jun London: 07:00 alt={altitudeMorning:0.##} az={azimuthMorning:0.##}; 12:00 alt={altitudeNoon:0.##} az={azimuthNoon:0.##}; 17:00 alt={altitudeEvening:0.##} az={azimuthEvening:0.##}");

            Assert.InRange(azimuthMorning, 45.0, 110.0);    // east-north-east
            Assert.InRange(azimuthNoon, 170.0, 190.0);      // due south at solar noon
            Assert.InRange(azimuthEvening, 250.0, 315.0);   // west-north-west
            Assert.True(altitudeNoon > altitudeMorning && altitudeNoon > altitudeEvening);

            // Sun vector convention: points FROM the sun TOWARD the surface, so it has a negative Z
            // when the sun is up and a negative X component when the sun is in the east.
            Vector3D morning = Geometry.SolarCalculator.Create.SunDirection(altitudeMorning, azimuthMorning);
            Assert.True(morning.Z < 0, "sun-direction Z must be negative while the sun is above the horizon");
            Assert.True(morning.X < 0, "an easterly sun must travel in the -X direction");

            // The cache must light the EAST cell and not the WEST cell at that morning hour.
            Orientation east = Make("east", SyntheticTargets.East);
            Orientation west = Make("west", SyntheticTargets.West);
            WeatherData weatherData = TestHelpers.SolarSymmetricWeatherData(Year, location, 0.0, 900.0, 0.2);
            AnalysisPeriod morningHour = new AnalysisPeriod(Year, 6, 21, 6, 21, 7, 7);

            CachedIrradianceResult e = Run(location, east, weatherData, 0.0, morningHour);
            CachedIrradianceResult w = Run(location, west, weatherData, 0.0, morningHour);
            output.WriteLine($"21 Jun 07:00 single hour: east sunlit={e.SunlitHours[0]:0} direct={e.Direct[0]:0.####}; west sunlit={w.SunlitHours[0]:0} direct={w.Direct[0]:0.####}");
            Assert.Equal(1, e.EvaluatedHours);
            Assert.Equal(1.0, e.SunlitHours[0]);
            Assert.Equal(0.0, w.SunlitHours[0]);
            Assert.True(e.Direct[0] > 0);
            Assert.Equal(0.0, w.Direct[0]);
        }

        private static double Asymmetry(double a, double b)
        {
            double mean = 0.5 * (a + b);
            return mean <= 0 ? 0 : Math.Abs(a - b) / mean;
        }
    }
}
