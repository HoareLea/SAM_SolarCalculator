// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using SAM.Core;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather.SolarCalculator;
using Innovative.SolarCalculator;
using Innovative.Geometry;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Stage 3 tests: Tregenza patch sets, Perez 1990 conventions, and the per-cell
    /// component-aware sky/horizon/ground visibility. Includes the golden-value freezes of the
    /// legacy (compatibility) and corrected (physical) radiation conventions — two systems that
    /// must never be mixed.
    /// </summary>
    public class PerezSkyTests
    {
        private readonly ITestOutputHelper output;

        public PerezSkyTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static Face3D WindowFace()
        {
            // South-facing (outward normal (0,-1,0)), 2 m x 1 m, sill at z = 1.
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 1),
                new Point3D(2, 0, 1),
                new Point3D(2, 0, 2),
                new Point3D(0, 0, 2),
            }));
        }

        private static SolarTimes NoonSummer()
        {
            return Geometry.SolarCalculator.Create.SolarTimes(TestHelpers.London(), new DateTime(2018, 6, 21, 12, 0, 0));
        }

        private static Plane OutwardPlane(Vector3D normal)
        {
            return new Plane(new Point3D(0, 0, 0), normal);
        }

        [Fact]
        public void Tregenza145_PatchSet()
        {
            List<SkyPatch> patches = Geometry.SolarCalculator.Query.SkyPatchDirections(SkyPatchSubdivision.Tregenza145);
            Assert.NotNull(patches);
            Assert.Equal(145, patches.Count);
            Assert.Equal(2 * Math.PI, patches.Sum(x => x.SolidAngle), 9);
            Assert.Equal(30, patches.Count(x => x.IsHorizonBand));
            Assert.All(patches, p => Assert.True(p.Direction.Z >= -1e-9));
            Assert.All(patches, p => Assert.True(Math.Abs(p.Direction.Length - 1.0) < 1e-9));

            // Ground mirror: same count, all below horizon, same total solid angle.
            List<SkyPatch> ground = Geometry.SolarCalculator.Query.GroundPatchDirections(SkyPatchSubdivision.Tregenza145);
            Assert.Equal(145, ground.Count);
            Assert.All(ground, p => Assert.True(p.Direction.Z <= 1e-9));
            Assert.Equal(2 * Math.PI, ground.Sum(x => x.SolidAngle), 9);

            List<SkyPatch> reinhart = Geometry.SolarCalculator.Query.SkyPatchDirections(SkyPatchSubdivision.Reinhart577);
            Assert.Equal(577, reinhart.Count);
            Assert.Equal(2 * Math.PI, reinhart.Sum(x => x.SolidAngle), 9);
        }

        [Fact]
        public void Legacy_Isotropic_GoldenValues_Frozen()
        {
            // Golden freeze of the LEGACY convention (inward-normal tilt, +90 deg solar azimuth
            // rotation): kept byte-for-byte for compatibility. These values were measured on the
            // released code (summer noon, London) and must never drift.
            SolarTimes solarTimes = NoonSummer();
            double dni = 800, dhi = 200, ghi = 700;

            // Down (roof, inward) normal -> tilt 180: full sky, direct from high sun.
            Radiation roof = Geometry.SolarCalculator.Create.Radiation(solarTimes, 180, 180, dni, dhi, ghi);
            Assert.Equal(705.9, roof.DirectNormal, 1);
            Assert.Equal(200.0, roof.DiffuseHorizontal, 6);
            Assert.Equal(0.0, roof.GlobalHorizontal, 6);

            // Up normal -> tilt 0: legacy treats the receiving side as down-facing.
            Radiation up = Geometry.SolarCalculator.Create.Radiation(solarTimes, 0, 0, dni, dhi, ghi);
            Assert.Equal(0.0, up.DirectNormal, 6);
            Assert.Equal(0.0, up.DiffuseHorizontal, 6);
            Assert.Equal(140.0, up.GlobalHorizontal, 6);

            // Vertical south-out normal (saz 180): legacy gives it the east orientation's beam
            // (the +90 deg rotation), nearly grazing at noon.
            Radiation south = Geometry.SolarCalculator.Create.Radiation(solarTimes, 90, 180, dni, dhi, ghi);
            Assert.Equal(7.0, south.DirectNormal, 0);
            Assert.Equal(100.0, south.DiffuseHorizontal, 6);
            Assert.Equal(70.0, south.GlobalHorizontal, 6);

            // Vertical west-out normal (saz 270): legacy assigns the south orientation's beam.
            Radiation west = Geometry.SolarCalculator.Create.Radiation(solarTimes, 90, 270, dni, dhi, ghi);
            Assert.Equal(376.4, west.DirectNormal, 1);
            Assert.Equal(100.0, west.DiffuseHorizontal, 6);
            Assert.Equal(70.0, west.GlobalHorizontal, 6);
        }

        [Fact]
        public void Corrected_Physical_GoldenValues_Frozen()
        {
            // Golden freeze of the CORRECTED path (Plane-based, outward normal, unrotated NOAA
            // azimuth, true DNI). Summer noon, London: elevation 61.95 deg, azimuth 178 deg.
            SolarTimes solarTimes = NoonSummer();
            double dni = 800, dhi = 200, ghi = 700;

            // South vertical (outward normal (0,-1,0)): beam = DNI * cos(elev) * cos(az-180).
            Radiation south = Geometry.SolarCalculator.Create.Radiation(solarTimes, OutwardPlane(new Vector3D(0, -1, 0)), dni, dhi, ghi, SkyModel.PerezAnisotropic);
            double elevationRad = System.Convert.ToDouble(solarTimes.SolarElevation.Radians);
            double azimuthRad = System.Convert.ToDouble(solarTimes.SolarAzimuth.Radians);
            double expectedBeam = dni * Math.Cos(elevationRad) * Math.Cos(azimuthRad - Math.PI);
            Assert.Equal(expectedBeam, south.DirectNormal, 6);
            Assert.Equal(376.4, south.DirectNormal, 1);
            // Perez diffuse for clear-ish sky (epsilon ~ 4.6, F1 ~ 0.63): well above the isotropic
            // 0.5 * DHI = 100 because of the circumsolar term. Frozen value from the implementation.
            Assert.Equal(131.1, south.DiffuseHorizontal, 1);
            // Vertical: ground half-exposed.
            Assert.Equal(0.5 * ghi * 0.2, south.GlobalHorizontal, 6);

            // Roof (up normal): beam = DNI * sin(elev); full sky; no ground.
            Radiation roof = Geometry.SolarCalculator.Create.Radiation(solarTimes, OutwardPlane(new Vector3D(0, 0, 1)), dni, dhi, ghi, SkyModel.PerezAnisotropic);
            Assert.Equal(dni * Math.Sin(elevationRad), roof.DirectNormal, 6);
            Assert.Equal(dhi, roof.DiffuseHorizontal, 0);
            Assert.Equal(0.0, roof.GlobalHorizontal, 6);

            // North vertical at noon: no beam; sky and ground halves.
            Radiation north = Geometry.SolarCalculator.Create.Radiation(solarTimes, OutwardPlane(new Vector3D(0, 1, 0)), dni, dhi, ghi, SkyModel.PerezAnisotropic);
            Assert.Equal(0.0, north.DirectNormal, 6);
            Assert.True(north.DiffuseHorizontal > 0);
            Assert.True(north.GlobalHorizontal > 0);
        }

        [Fact]
        public void ViewFactorMultipliers_Are_Relative_Not_Absolute()
        {
            // Freezes the semantics of skyViewFactorMultiplier / groundViewFactorMultiplier on the
            // Plane-based Create.Radiation overload: they are RELATIVE multipliers applied on top of
            // the analytic unobstructed form factor, not absolute view factors. On a vertical surface
            // the analytic unobstructed sky/ground fraction is (1+cosB)/2 = (1-cosB)/2 = 0.5, so
            // multiplier 1.0 must reproduce the full 0.5 * DHI / 0.5 * GHI * albedo contribution, and
            // multiplier 0.5 must halve it again (0.25 * DHI / 0.25 * GHI * albedo) -- NOT reproduce
            // the multiplier-1.0 result, which is what passing an already-absolute ~0.5 view factor
            // (e.g. from SkyVisibilityCache.SkyViewFactor) would incorrectly do.
            SolarTimes solarTimes = NoonSummer();
            Plane south = OutwardPlane(new Vector3D(0, -1, 0));
            double dni = 0; // isolate diffuse/ground: no beam contribution to reason about
            double dhi = 200;
            double ghi = 200;
            double albedo = 0.2;

            Radiation unobstructed = Geometry.SolarCalculator.Create.Radiation(solarTimes, south, dni, dhi, ghi, SkyModel.Isotropic, skyViewFactorMultiplier: 1.0, groundViewFactorMultiplier: 1.0, albedo: albedo);
            Radiation halved = Geometry.SolarCalculator.Create.Radiation(solarTimes, south, dni, dhi, ghi, SkyModel.Isotropic, skyViewFactorMultiplier: 0.5, groundViewFactorMultiplier: 0.5, albedo: albedo);

            double analyticVerticalFraction = 0.5; // (1 + cosB)/2 for a vertical surface, cosB = 0
            Assert.Equal(dhi * analyticVerticalFraction, unobstructed.DiffuseHorizontal, 6);
            Assert.Equal(ghi * albedo * analyticVerticalFraction, unobstructed.GlobalHorizontal, 6);

            Assert.Equal(unobstructed.DiffuseHorizontal * 0.5, halved.DiffuseHorizontal, 6);
            Assert.Equal(unobstructed.GlobalHorizontal * 0.5, halved.GlobalHorizontal, 6);

            // And explicitly NOT the same: a multiplier of 0.5 must not be a no-op relative to 1.0,
            // which is the failure mode of mistaking it for an already-absolute view factor.
            Assert.NotEqual(unobstructed.DiffuseHorizontal, halved.DiffuseHorizontal);
            Assert.NotEqual(unobstructed.GlobalHorizontal, halved.GlobalHorizontal);
        }

        [Fact]
        public void Perez_Overcast_Agrees_With_Isotropic_On_Horizontal()
        {
            // Overcast (epsilon ~ 1): DNI ~ 0. On a horizontal surface Perez and isotropic must agree.
            SolarTimes solarTimes = NoonSummer();

            double dni = 0;
            double dhi = 200;
            double ghi = 200;

            Plane roof = OutwardPlane(new Vector3D(0, 0, 1));
            Radiation isotropic = Geometry.SolarCalculator.Create.Radiation(solarTimes, roof, dni, dhi, ghi, SkyModel.Isotropic);
            Radiation perez = Geometry.SolarCalculator.Create.Radiation(solarTimes, roof, dni, dhi, ghi, SkyModel.PerezAnisotropic);

            Assert.NotNull(isotropic);
            Assert.NotNull(perez);
            output.WriteLine($"overcast horizontal: isotropic diffuse={isotropic.DiffuseHorizontal:0.##} perez diffuse={perez.DiffuseHorizontal:0.##}");
            Assert.Equal(isotropic.DiffuseHorizontal, perez.DiffuseHorizontal, 1); // well within 5%
        }

        [Fact]
        public void Perez_ClearSky_VerticalSouth_Diverges_From_Isotropic()
        {
            // Clear sky (epsilon > 6) at summer noon, sun due south: circumsolar brightening must
            // push the south vertical diffuse well above the isotropic estimate.
            SolarTimes solarTimes = NoonSummer();
            Assert.True(System.Convert.ToDouble(solarTimes.SolarAzimuth.Degrees) > 150); // sun in the south

            double dni = 800;
            double dhi = 80;
            double ghi = 800 * Math.Sin(System.Convert.ToDouble(solarTimes.SolarElevation.Radians)) + 80;

            bool ok = Geometry.SolarCalculator.Query.TryGetPerezCoefficients(dni, dhi, System.Convert.ToDouble(solarTimes.SolarElevation.Degrees), solarTimes.ForDate.DayOfYear, out double f1, out double f2, out double epsilon, out double delta);
            Assert.True(ok);
            output.WriteLine($"clear sky: epsilon={epsilon:0.##} delta={delta:0.###} F1={f1:0.###} F2={f2:0.###}");
            Assert.True(epsilon > 6.0, $"expected clear-sky epsilon > 6, got {epsilon:0.##}");

            Plane south = OutwardPlane(new Vector3D(0, -1, 0));
            Radiation isotropic = Geometry.SolarCalculator.Create.Radiation(solarTimes, south, dni, dhi, ghi, SkyModel.Isotropic);
            Radiation perez = Geometry.SolarCalculator.Create.Radiation(solarTimes, south, dni, dhi, ghi, SkyModel.PerezAnisotropic);

            output.WriteLine($"clear vertical south: isotropic diffuse={isotropic.DiffuseHorizontal:0.##} perez diffuse={perez.DiffuseHorizontal:0.##}");
            double difference = Math.Abs(perez.DiffuseHorizontal - isotropic.DiffuseHorizontal) / isotropic.DiffuseHorizontal;
            Assert.True(difference > 0.10, $"expected >10% clear-sky vertical difference, got {difference:0.###}");

            // Beam on the surface must be physically correct: DNI * cos(incidence), with the sun
            // essentially due south (azimuth ~178 deg) at solar noon.
            double elevationRad = System.Convert.ToDouble(solarTimes.SolarElevation.Radians);
            double azimuthRad = System.Convert.ToDouble(solarTimes.SolarAzimuth.Radians);
            double cosTheta = Math.Cos(elevationRad) * Math.Cos(azimuthRad - Math.PI);
            Assert.Equal(dni * cosTheta, perez.DirectNormal, 6);
        }

        [Fact]
        public void Perez_Beam_Orientation_Is_Physical()
        {
            // Guard for the convention fix: at summer noon a SOUTH vertical surface gets strong beam
            // and an EAST vertical surface gets little (sun due south). The legacy formula (with its
            // +90 deg rotation) swaps these; the Perez overload must not.
            SolarTimes solarTimes = NoonSummer();
            double dni = 800, dhi = 80, ghi = 600;

            Radiation south = Geometry.SolarCalculator.Create.Radiation(solarTimes, OutwardPlane(new Vector3D(0, -1, 0)), dni, dhi, ghi, SkyModel.PerezAnisotropic);
            Radiation east = Geometry.SolarCalculator.Create.Radiation(solarTimes, OutwardPlane(new Vector3D(1, 0, 0)), dni, dhi, ghi, SkyModel.PerezAnisotropic);
            Radiation north = Geometry.SolarCalculator.Create.Radiation(solarTimes, OutwardPlane(new Vector3D(0, 1, 0)), dni, dhi, ghi, SkyModel.PerezAnisotropic);
            Radiation roof = Geometry.SolarCalculator.Create.Radiation(solarTimes, OutwardPlane(new Vector3D(0, 0, 1)), dni, dhi, ghi, SkyModel.PerezAnisotropic);

            Assert.True(south.DirectNormal > 300, $"south beam {south.DirectNormal:0.#}");
            // Sun essentially due south: an east facade is at grazing incidence (beam ~ 0 but not
            // exactly, as the sun is ~2 deg off the south point at noon) and the north gets none.
            Assert.True(east.DirectNormal < 30, $"east beam {east.DirectNormal:0.#}");
            Assert.True(south.DirectNormal > 10 * east.DirectNormal);
            Assert.Equal(0, north.DirectNormal, 6);
            Assert.True(roof.DirectNormal > 600, $"roof beam {roof.DirectNormal:0.#}");

            // Ground-reflected: up-facing roof sees no ground; vertical sees half.
            Assert.Equal(0, roof.GlobalHorizontal, 6);
            Assert.True(south.GlobalHorizontal > 0);
        }

        [Fact]
        public void SkyVisibility_Unobstructed_Vertical_SVF_Half()
        {
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(WindowFace(), 0.5);
            SkyVisibilityCache cache = Weather.SolarCalculator.Create.SkyVisibilityCache(new List<LinkedFace3D>(), cells, 0.5);
            Assert.NotNull(cache);

            for (int c = 0; c < cache.CellCount; c++)
            {
                output.WriteLine($"cell {c}: SVF={cache.SkyViewFactor(c):0.####} HVF={cache.HorizonViewFactor(c):0.####} GVF={cache.GroundViewFactor(c):0.####}");
                Assert.Equal(0.5, cache.SkyViewFactor(c), 2);       // vertical unobstructed
                Assert.Equal(1.0, cache.HorizonViewFactor(c), 6);   // full horizon band visible
                Assert.Equal(0.5, cache.GroundViewFactor(c), 2);    // vertical unobstructed ground
            }
        }

        [Fact]
        public void SkyVisibility_Enclosed_Cell_SVF_Zero()
        {
            // The hood enclosure from the cache tests: nothing of the sky is visible.
            List<LinkedFace3D> enclosure = new List<LinkedFace3D>
            {
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D> { new Point3D(-1, -0.5, 0), new Point3D(3, -0.5, 0), new Point3D(3, -0.5, 3), new Point3D(-1, -0.5, 3) }))),
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D> { new Point3D(-1, 0, 3), new Point3D(3, 0, 3), new Point3D(3, -0.5, 3), new Point3D(-1, -0.5, 3) }))),
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D> { new Point3D(-1, -0.5, 0), new Point3D(3, -0.5, 0), new Point3D(3, 0, 0), new Point3D(-1, 0, 0) }))),
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D> { new Point3D(-1, -0.5, 0), new Point3D(-1, 0, 0), new Point3D(-1, 0, 3), new Point3D(-1, -0.5, 3) }))),
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D> { new Point3D(3, 0, 0), new Point3D(3, -0.5, 0), new Point3D(3, -0.5, 3), new Point3D(3, 0, 3) }))),
            };

            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(WindowFace(), 0.5);
            SkyVisibilityCache cache = Weather.SolarCalculator.Create.SkyVisibilityCache(enclosure, cells, 0.5);

            for (int c = 0; c < cache.CellCount; c++)
            {
                output.WriteLine($"enclosed cell {c}: SVF={cache.SkyViewFactor(c):0.####} HVF={cache.HorizonViewFactor(c):0.####} GVF={cache.GroundViewFactor(c):0.####}");
                Assert.Equal(0.0, cache.SkyViewFactor(c), 3);
                Assert.Equal(0.0, cache.HorizonViewFactor(c), 6);
                Assert.Equal(0.0, cache.GroundViewFactor(c), 3);
            }
        }

        [Fact]
        public void SkyVisibility_HorizonObstruction_Drops_HVF_Before_SVF()
        {
            // A 4 m wall 2 m in front of the window, wide enough to cover all oblique horizon-band
            // rays: blocks the horizon band (6 deg patches) but leaves the upper sky visible.
            List<LinkedFace3D> occluders = new List<LinkedFace3D>
            {
                new LinkedFace3D(Guid.NewGuid(), new Face3D(new Polygon3D(new List<Point3D>
                {
                    new Point3D(-20, -2, 0), new Point3D(20, -2, 0), new Point3D(20, -2, 4), new Point3D(-20, -2, 4),
                }))),
            };

            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(WindowFace(), 0.5);
            SkyVisibilityCache cache = Weather.SolarCalculator.Create.SkyVisibilityCache(occluders, cells, 0.5);

            for (int c = 0; c < cache.CellCount; c++)
            {
                double svf = cache.SkyViewFactor(c);
                double hvf = cache.HorizonViewFactor(c);
                output.WriteLine($"horizon-obstructed cell {c}: SVF={svf:0.####} HVF={hvf:0.####}");
                Assert.True(hvf < 0.05, $"horizon band should be blocked, HVF={hvf:0.####}");
                Assert.True(svf > 0.05, $"upper sky should remain visible, SVF={svf:0.####}");
                Assert.True(svf < 0.45, $"SVF should drop below the unobstructed 0.5, SVF={svf:0.####}");
            }
        }

        [Fact]
        public void SkyVisibility_Json_RoundTrip()
        {
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(WindowFace(), 0.5);
            SkyVisibilityCache cache = Weather.SolarCalculator.Create.SkyVisibilityCache(new List<LinkedFace3D>(), cells, 0.5);

            SkyVisibilityCache roundTripped = new SkyVisibilityCache(cache.ToJsonObject());
            Assert.Equal(cache.GetIdentity(), roundTripped.GetIdentity());
            for (int c = 0; c < cache.CellCount; c++)
            {
                Assert.Equal(cache.SkyViewFactor(c), roundTripped.SkyViewFactor(c), 12);
                Assert.Equal(cache.HorizonViewFactor(c), roundTripped.HorizonViewFactor(c), 12);
                Assert.Equal(cache.GroundViewFactor(c), roundTripped.GroundViewFactor(c), 12);
            }
        }
    }
}
