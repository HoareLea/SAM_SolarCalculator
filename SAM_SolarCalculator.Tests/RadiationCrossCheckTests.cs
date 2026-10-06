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
    /// T3 — the corrected standalone radiation path
    /// (<c>Geometry.SolarCalculator.Create.Radiation(SolarTimes, Plane, ..., SkyModel)</c>) checked
    /// against the corrected cached path
    /// (<c>Analytical.SolarCalculator.Query.CachedIrradiance</c>), plus a third, INDEPENDENT
    /// implementation of Perez 1990 written directly from the paper inside this test file.
    ///
    /// The legacy overload is deliberately never used as the physical reference: it assumes an
    /// inward-normal tilt and rotates the solar azimuth by +90 deg, and is frozen for compatibility
    /// only.
    ///
    /// Three-way agreement covers: DNI reconstruction from beam-horizontal, incidence angle,
    /// the direct term, Perez F1/F2 composition, the isotropic-diffuse, circumsolar, horizon-
    /// brightening and ground-reflected terms, and the W/m2 -> kWh/m2 unit conversion.
    /// </summary>
    public class RadiationCrossCheckTests
    {
        private readonly ITestOutputHelper output;

        public RadiationCrossCheckTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const int Year = 2018;
        private const double MinHorizonAngle = Core.Tolerance.Angle;
        private const double Albedo = 0.2;

        // Representative hours: winter/equinox/summer, morning/noon/afternoon.
        private static readonly DateTime[] ProbeHours =
        {
            new DateTime(Year, 3, 21, 9, 0, 0),
            new DateTime(Year, 3, 21, 12, 0, 0),
            new DateTime(Year, 6, 21, 8, 0, 0),
            new DateTime(Year, 6, 21, 12, 0, 0),
            new DateTime(Year, 6, 21, 16, 0, 0),
            new DateTime(Year, 9, 21, 15, 0, 0),
            new DateTime(Year, 12, 21, 12, 0, 0),
        };

        // Sky conditions as (globalHorizontal, diffuseHorizontal) W/m2 pairs, spanning the Perez
        // clearness bins from fully overcast (epsilon ~ 1) to very clear (epsilon > 6).
        private static readonly Tuple<string, double, double>[] SkyConditions =
        {
            Tuple.Create("overcast", 120.0, 120.0),
            Tuple.Create("hazy", 400.0, 260.0),
            Tuple.Create("partly-clear", 600.0, 220.0),
            Tuple.Create("clear", 780.0, 95.0),
        };

        private static readonly Tuple<string, Vector3D>[] Orientations =
        {
            Tuple.Create("north", SyntheticTargets.North),
            Tuple.Create("east", SyntheticTargets.East),
            Tuple.Create("south", SyntheticTargets.South),
            Tuple.Create("west", SyntheticTargets.West),
            Tuple.Create("roof", SyntheticTargets.Up),
        };

        [Fact]
        [Trait("Category", "LongRunning")]
        public void T3_Standalone_Plane_Radiation_Matches_CachedIrradiance()
        {
            Location location = TestHelpers.London();
            List<LinkedFace3D> noOccluders = new List<LinkedFace3D>();

            double worstDirect = 0;
            double worstDiffuse = 0;
            double worstGround = 0;
            int compared = 0;

            output.WriteLine("hour                 sky           orient  cached W/m2 (dir/dif/gnd)          standalone W/m2 (dir/dif/gnd)       max abs delta");

            foreach (Tuple<string, double, double> sky in SkyConditions)
            {
                // One weather file per sky condition: the probe hours carry the condition's values,
                // every other hour is dark. Sun sampling shift 0, so the weather hour and the sun
                // instant are the same instant and the standalone call can use it directly.
                WeatherData weatherData = TestHelpers.SyntheticWeatherData(Year, location, dateTime =>
                    Array.IndexOf(ProbeHours, dateTime) >= 0 ? Tuple.Create(sky.Item2, sky.Item3, 0.0) : Tuple.Create(0.0, 0.0, 0.0));

                foreach (Tuple<string, Vector3D> orientation in Orientations)
                {
                    List<AnalysisCell> cells = SyntheticTargets.Cell(orientation.Item2);
                    List<Vector3D> normals = SyntheticTargets.Normals(cells, orientation.Item2);
                    Plane plane = new Plane(new Point3D(0, 0, 0), orientation.Item2.Unit);

                    SolarVisibilityCache solarCache = Weather.SolarCalculator.Create.SolarVisibilityCache(
                        location, Year, 2.0, noOccluders, cells, 1.0, MinHorizonAngle, 0.0);

                    foreach (DateTime probe in ProbeHours)
                    {
                        Geometry.SolarCalculator.Query.TryGetSunAngles(location, probe, out double elevationDegrees, out _);
                        if (elevationDegrees < MinHorizonAngle * 180.0 / Math.PI)
                        {
                            continue;   // the cached path would (correctly) skip this hour
                        }

                        // Cached path, one-hour period. skyVisibilityCache is deliberately null so
                        // the cached path uses its ANALYTIC unobstructed view factors ((1+cosB)/2 and
                        // (1-cosB)/2) and the comparison isolates the physics from the Tregenza
                        // ray-cast discretisation (which is measured separately below).
                        AnalysisPeriod period = new AnalysisPeriod(Year, probe.Month, probe.Day, probe.Month, probe.Day, probe.Hour, probe.Hour);
                        CachedIrradianceResult cached = Analytical.SolarCalculator.Query.CachedIrradiance(
                            solarCache, null, weatherData, period, normals, SkyModel.PerezAnisotropic, Albedo);
                        Assert.NotNull(cached);
                        Assert.Equal(1, cached.EvaluatedHours);
                        Assert.Equal(0, cached.MissedBinHours);

                        // Standalone path, fed the SAME weather values. DNI is reconstructed here,
                        // independently of the engine, from beam-horizontal and the solar elevation.
                        double beamHorizontal = Math.Max(0.0, sky.Item2 - sky.Item3);
                        double dni = beamHorizontal / Math.Max(Math.Sin(elevationDegrees * Math.PI / 180.0), Math.Sin(5.0 * Math.PI / 180.0));
                        Innovative.SolarCalculator.SolarTimes solarTimes = Geometry.SolarCalculator.Create.SolarTimes(location, probe);
                        Radiation standalone = Geometry.SolarCalculator.Create.Radiation(
                            solarTimes, plane, dni, sky.Item3, sky.Item2, SkyModel.PerezAnisotropic, 1.0, 1.0, Albedo);
                        Assert.NotNull(standalone);

                        // kWh/m2 over one hour -> W/m2.
                        double cachedDirect = cached.Direct[0] * 1000.0;
                        double cachedDiffuse = cached.Diffuse[0] * 1000.0;
                        double cachedGround = cached.GroundReflected[0] * 1000.0;

                        double dDirect = Math.Abs(cachedDirect - standalone.DirectNormal);
                        double dDiffuse = Math.Abs(cachedDiffuse - standalone.DiffuseHorizontal);
                        double dGround = Math.Abs(cachedGround - standalone.GlobalHorizontal);

                        worstDirect = Math.Max(worstDirect, dDirect);
                        worstDiffuse = Math.Max(worstDiffuse, dDiffuse);
                        worstGround = Math.Max(worstGround, dGround);
                        compared++;

                        if (compared <= 12)
                        {
                            output.WriteLine($"{probe:yyyy-MM-dd HH:mm}  {sky.Item1,-12}  {orientation.Item1,-6}  {cachedDirect,8:0.####}/{cachedDiffuse,8:0.####}/{cachedGround,8:0.####}  {standalone.DirectNormal,8:0.####}/{standalone.DiffuseHorizontal,8:0.####}/{standalone.GlobalHorizontal,8:0.####}  {Math.Max(dDirect, Math.Max(dDiffuse, dGround)):0.###E+00}");
                        }

                        // The two implementations must agree to floating-point noise: same
                        // conventions, same DNI reconstruction, same Perez composition, same units.
                        Assert.Equal(standalone.DirectNormal, cachedDirect, 9);
                        Assert.Equal(standalone.DiffuseHorizontal, cachedDiffuse, 9);
                        Assert.Equal(standalone.GlobalHorizontal, cachedGround, 9);
                    }
                }
            }

            output.WriteLine($"compared {compared} (hour x sky x orientation) cases; worst abs delta W/m2: direct={worstDirect:0.###E+00} diffuse={worstDiffuse:0.###E+00} ground={worstGround:0.###E+00}");
            Assert.True(compared >= 100, $"expected a broad sweep, only compared {compared} cases");
            Assert.True(worstDirect < 1e-9 && worstDiffuse < 1e-9 && worstGround < 1e-9);
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void T3_Both_Corrected_Paths_Match_An_Independent_Perez_Implementation()
        {
            // Neither of the two corrected paths is an independent check of the Perez formulation
            // itself: they share Query.TryGetPerezCoefficients. This test re-derives the whole
            // component set from Perez et al. 1990 (Solar Energy 44(5), 271-289) inside the test,
            // with its own clearness/brightness/coefficient table, and requires all three to agree.
            Location location = TestHelpers.London();
            List<LinkedFace3D> noOccluders = new List<LinkedFace3D>();

            double worst = 0;
            int compared = 0;

            output.WriteLine("hour                 sky           orient   cached      standalone  independent   (total W/m2)");

            foreach (Tuple<string, double, double> sky in SkyConditions)
            {
                WeatherData weatherData = TestHelpers.SyntheticWeatherData(Year, location, dateTime =>
                    Array.IndexOf(ProbeHours, dateTime) >= 0 ? Tuple.Create(sky.Item2, sky.Item3, 0.0) : Tuple.Create(0.0, 0.0, 0.0));

                foreach (Tuple<string, Vector3D> orientation in Orientations)
                {
                    List<AnalysisCell> cells = SyntheticTargets.Cell(orientation.Item2);
                    List<Vector3D> normals = SyntheticTargets.Normals(cells, orientation.Item2);
                    Plane plane = new Plane(new Point3D(0, 0, 0), orientation.Item2.Unit);
                    SolarVisibilityCache solarCache = Weather.SolarCalculator.Create.SolarVisibilityCache(
                        location, Year, 2.0, noOccluders, cells, 1.0, MinHorizonAngle, 0.0);

                    foreach (DateTime probe in ProbeHours)
                    {
                        Geometry.SolarCalculator.Query.TryGetSunAngles(location, probe, out double elevationDegrees, out double azimuthDegrees);
                        if (elevationDegrees < MinHorizonAngle * 180.0 / Math.PI)
                        {
                            continue;
                        }

                        AnalysisPeriod period = new AnalysisPeriod(Year, probe.Month, probe.Day, probe.Month, probe.Day, probe.Hour, probe.Hour);
                        CachedIrradianceResult cached = Analytical.SolarCalculator.Query.CachedIrradiance(
                            solarCache, null, weatherData, period, normals, SkyModel.PerezAnisotropic, Albedo);

                        double beamHorizontal = Math.Max(0.0, sky.Item2 - sky.Item3);
                        double dni = beamHorizontal / Math.Max(Math.Sin(elevationDegrees * Math.PI / 180.0), Math.Sin(5.0 * Math.PI / 180.0));
                        Radiation standalone = Geometry.SolarCalculator.Create.Radiation(
                            Geometry.SolarCalculator.Create.SolarTimes(location, probe), plane, dni, sky.Item3, sky.Item2, SkyModel.PerezAnisotropic, 1.0, 1.0, Albedo);

                        IndependentPerez(orientation.Item2.Unit, elevationDegrees, azimuthDegrees, probe.DayOfYear, dni, sky.Item3, sky.Item2, Albedo,
                            out double beam, out double diffuse, out double ground);

                        double cachedTotal = (cached.Direct[0] + cached.Diffuse[0] + cached.GroundReflected[0]) * 1000.0;
                        double standaloneTotal = standalone.DirectNormal + standalone.DiffuseHorizontal + standalone.GlobalHorizontal;
                        double independentTotal = beam + diffuse + ground;

                        worst = Math.Max(worst, Math.Max(Math.Abs(cachedTotal - independentTotal), Math.Abs(standaloneTotal - independentTotal)));
                        compared++;

                        if (compared <= 12)
                        {
                            output.WriteLine($"{probe:yyyy-MM-dd HH:mm}  {sky.Item1,-12}  {orientation.Item1,-6}  {cachedTotal,10:0.####}  {standaloneTotal,10:0.####}  {independentTotal,10:0.####}");
                        }

                        Assert.Equal(beam, standalone.DirectNormal, 9);
                        Assert.Equal(diffuse, standalone.DiffuseHorizontal, 9);
                        Assert.Equal(ground, standalone.GlobalHorizontal, 9);
                        Assert.Equal(beam, cached.Direct[0] * 1000.0, 9);
                        Assert.Equal(diffuse, cached.Diffuse[0] * 1000.0, 9);
                        Assert.Equal(ground, cached.GroundReflected[0] * 1000.0, 9);
                    }
                }
            }

            output.WriteLine($"compared {compared} cases against the independent Perez implementation; worst abs delta {worst:0.###E+00} W/m2");
            Assert.True(compared >= 100);
            Assert.True(worst < 1e-9);
        }

        [Fact]
        public void T3_Tregenza_ViewFactors_Track_The_Analytic_Ones()
        {
            // The one place the cached path legitimately differs from the analytic standalone one:
            // with a SkyVisibilityCache attached, the isotropic-diffuse and ground terms are weighted
            // by ray-cast Tregenza-145 view factors instead of the closed-form (1 +- cos B)/2. The
            // discretisation error is measured here rather than assumed.
            Location location = TestHelpers.London();
            List<LinkedFace3D> noOccluders = new List<LinkedFace3D>();
            DateTime probe = new DateTime(Year, 6, 21, 12, 0, 0);
            AnalysisPeriod period = new AnalysisPeriod(Year, 6, 21, 6, 21, 12, 12);

            WeatherData weatherData = TestHelpers.SyntheticWeatherData(Year, location, dateTime =>
                dateTime == probe ? Tuple.Create(700.0, 200.0, 0.0) : Tuple.Create(0.0, 0.0, 0.0));

            foreach (Tuple<string, Vector3D> orientation in Orientations)
            {
                List<AnalysisCell> cells = SyntheticTargets.Cell(orientation.Item2);
                List<Vector3D> normals = SyntheticTargets.Normals(cells, orientation.Item2);
                SolarVisibilityCache solarCache = Weather.SolarCalculator.Create.SolarVisibilityCache(
                    location, Year, 2.0, noOccluders, cells, 1.0, MinHorizonAngle, 0.0);
                SkyVisibilityCache skyCache = Weather.SolarCalculator.Create.SkyVisibilityCache(noOccluders, cells, 1.0);

                CachedIrradianceResult analytic = Analytical.SolarCalculator.Query.CachedIrradiance(solarCache, null, weatherData, period, normals, SkyModel.PerezAnisotropic, Albedo);
                CachedIrradianceResult tregenza = Analytical.SolarCalculator.Query.CachedIrradiance(solarCache, skyCache, weatherData, period, normals, SkyModel.PerezAnisotropic, Albedo);

                double cosBeta = orientation.Item2.Unit.Z;
                double analyticSvf = (1.0 + cosBeta) / 2.0;
                double analyticGvf = (1.0 - cosBeta) / 2.0;
                double diffuseDelta = analytic.Diffuse[0] == 0 ? 0 : Math.Abs(tregenza.Diffuse[0] - analytic.Diffuse[0]) / analytic.Diffuse[0];
                double groundDelta = analytic.GroundReflected[0] == 0 ? Math.Abs(tregenza.GroundReflected[0]) : Math.Abs(tregenza.GroundReflected[0] - analytic.GroundReflected[0]) / analytic.GroundReflected[0];

                output.WriteLine($"{orientation.Item1,-6}: SVF analytic={analyticSvf:0.####} Tregenza={skyCache.SkyViewFactor(0):0.####}; GVF analytic={analyticGvf:0.####} Tregenza={skyCache.GroundViewFactor(0):0.####}; HVF={skyCache.HorizonViewFactor(0):0.####}; diffuse delta={100.0 * diffuseDelta:0.###} % ground delta={100.0 * groundDelta:0.###} %");

                // Direct beam never goes through the sky cache: it must be identical.
                Assert.Equal(analytic.Direct[0], tregenza.Direct[0], 12);

                // The 145-patch quadrature must reproduce the closed-form view factors to ~1 %.
                Assert.True(Math.Abs(skyCache.SkyViewFactor(0) - analyticSvf) < 0.01, $"{orientation.Item1}: SVF {skyCache.SkyViewFactor(0):0.####} vs analytic {analyticSvf:0.####}");
                Assert.True(Math.Abs(skyCache.GroundViewFactor(0) - analyticGvf) < 0.01, $"{orientation.Item1}: GVF {skyCache.GroundViewFactor(0):0.####} vs analytic {analyticGvf:0.####}");
                Assert.True(diffuseDelta < 0.02, $"{orientation.Item1}: diffuse delta {100.0 * diffuseDelta:0.###} %");
                Assert.True(groundDelta < 0.02, $"{orientation.Item1}: ground delta {100.0 * groundDelta:0.###} %");
            }
        }

        [Fact]
        public void T3_Legacy_Overload_Is_Not_The_Reference_And_Still_Disagrees()
        {
            // Explicit statement of the compatibility boundary: the legacy (tilt, surfaceAzimuth)
            // overload keeps its historical inward-normal / +90 deg-azimuth conventions, so it does
            // NOT agree with the corrected path on a tilted surface — by design. This test exists so
            // that "they disagree" is a recorded, deliberate fact rather than a surprise.
            Location location = TestHelpers.London();
            Innovative.SolarCalculator.SolarTimes solarTimes = Geometry.SolarCalculator.Create.SolarTimes(location, new DateTime(Year, 6, 21, 12, 0, 0));
            double dni = 800, dhi = 200, ghi = 700;

            Plane southPlane = new Plane(new Point3D(0, 0, 0), new Vector3D(0, -1, 0));
            Radiation corrected = Geometry.SolarCalculator.Create.Radiation(solarTimes, southPlane, dni, dhi, ghi, SkyModel.Isotropic);
            Radiation legacy = Geometry.SolarCalculator.Create.Radiation(solarTimes, 90.0, 180.0, dni, dhi, ghi);

            output.WriteLine($"summer noon south vertical, beam W/m2: corrected(outward normal)={corrected.DirectNormal:0.##} legacy(tilt 90, azimuth 180)={legacy.DirectNormal:0.##}");
            Assert.True(corrected.DirectNormal > 300);
            Assert.True(legacy.DirectNormal < 30);
            Assert.True(corrected.DirectNormal > 10 * legacy.DirectNormal);
        }

        /// <summary>
        /// Perez et al. 1990 written out here from the paper, independently of the engine:
        /// clearness bins, brightness, the F1/F2 coefficient table, and the full component
        /// composition on an arbitrarily oriented surface. Angles in DEGREES on the way in.
        /// </summary>
        private static void IndependentPerez(Vector3D outwardNormal, double solarElevationDegrees, double solarAzimuthDegrees, int dayOfYear, double dni, double dhi, double ghi, double albedo, out double beam, out double diffuse, out double ground)
        {
            // Perez 1990 Table 1 (f11 f12 f13 f21 f22 f23) per clearness bin.
            double[][] table =
            {
                new[] { -0.008, 0.588, -0.062, -0.060, 0.072, -0.022 },
                new[] { 0.130, 0.683, -0.151, -0.019, 0.066, -0.029 },
                new[] { 0.330, 0.487, -0.221, 0.055, -0.064, -0.026 },
                new[] { 0.568, 0.187, -0.295, 0.109, -0.152, -0.014 },
                new[] { 0.873, -0.392, -0.362, 0.226, -0.462, 0.001 },
                new[] { 1.132, -1.237, -0.412, 0.288, -0.823, 0.056 },
                new[] { 1.060, -1.600, -0.359, 0.264, -1.127, 0.131 },
                new[] { 0.678, -0.327, -0.250, 0.156, -1.377, 0.251 },
            };
            double[] upperBounds = { 1.065, 1.230, 1.500, 1.950, 2.800, 4.500, 6.200, double.MaxValue };

            double elevation = solarElevationDegrees * Math.PI / 180.0;
            double azimuth = solarAzimuthDegrees * Math.PI / 180.0;
            double zenith = (90.0 - solarElevationDegrees) * Math.PI / 180.0;

            // Surface geometry from the outward normal.
            Vector3D n = outwardNormal.Unit;
            double cosTilt = Math.Max(-1.0, Math.Min(1.0, n.Z));
            double sinTilt = Math.Sin(Math.Acos(cosTilt));

            // Sun unit vector, compass azimuth clockwise from north (+Y).
            double sx = Math.Cos(elevation) * Math.Sin(azimuth);
            double sy = Math.Cos(elevation) * Math.Cos(azimuth);
            double sz = Math.Sin(elevation);
            double cosIncidence = n.X * sx + n.Y * sy + n.Z * sz;

            // Clearness (eq. 3), kappa = 1.041 with zenith in radians.
            double dhiSafe = Math.Max(dhi, 1e-9);
            double kappaZ3 = 1.041 * zenith * zenith * zenith;
            double epsilon = ((dhiSafe + Math.Max(dni, 0.0)) / dhiSafe + kappaZ3) / (1.0 + kappaZ3);

            // Brightness (eq. 4): Kasten-Young air mass, eccentricity-corrected I0n.
            double airMass = 1.0 / (Math.Sin(elevation) + 0.50572 * Math.Pow(96.07995 - (90.0 - solarElevationDegrees), -1.6364));
            double i0n = 1367.0 * (1.0 + 0.033 * Math.Cos(2.0 * Math.PI * dayOfYear / 365.0));
            double delta = airMass * Math.Max(dhi, 0.0) / i0n;

            int bin = 0;
            while (bin < upperBounds.Length - 1 && epsilon > upperBounds[bin])
            {
                bin++;
            }

            double f1 = Math.Max(0.0, table[bin][0] + table[bin][1] * delta + table[bin][2] * zenith);
            double f2 = table[bin][3] + table[bin][4] * delta + table[bin][5] * zenith;

            double a = Math.Max(0.0, cosIncidence);
            double b = Math.Max(Math.Cos(85.0 * Math.PI / 180.0), Math.Cos(zenith));

            beam = dni * Math.Max(0.0, cosIncidence);
            diffuse = Math.Max(0.0, dhi * ((1.0 - f1) * (1.0 + cosTilt) / 2.0 + f1 * a / b + f2 * sinTilt));
            ground = Math.Max(0.0, ghi * albedo * (1.0 - cosTilt) / 2.0);
        }
    }
}
