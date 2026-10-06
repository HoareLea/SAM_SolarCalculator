// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SAM.Analytical.SolarCalculator;
using SAM.Core.SolarCalculator;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The load-bearing test of the PR: whole-scheme verification measures the COMPLETE element set
    /// against every member aperture, so an overhang over window A that overhangs window B is
    /// credited on B. A sum of independent per-window verifications cannot see that.
    ///
    /// THE MUTATION CHECK (§15.3) is assertion 3 below: the "naive sum" path IS the mutated
    /// implementation (each member verified against its own device alone). If assertions 1 and 3 do
    /// not fail against it, the test is not testing what it claims.
    /// </summary>
    public class SchemeVerificationTests
    {
        private const int Year = 2018;

        // Two coplanar south-facing apertures side by side on one wall, 1.0 m x 2.0 m each, a
        // 0.2 m gap apart, heads aligned. B sits to the east of A.
        private static ApertureSolarTarget Target(int ordinal, double centreX)
        {
            SAM.Geometry.Spatial.Face3D face = new SAM.Geometry.Spatial.Face3D(new SAM.Geometry.Spatial.Polygon3D(new List<SAM.Geometry.Spatial.Point3D>
            {
                new SAM.Geometry.Spatial.Point3D(centreX - 0.5, 0, 1), new SAM.Geometry.Spatial.Point3D(centreX + 0.5, 0, 1),
                new SAM.Geometry.Spatial.Point3D(centreX + 0.5, 0, 3), new SAM.Geometry.Spatial.Point3D(centreX - 0.5, 0, 3),
            }));
            return new ApertureSolarTarget(
                new Guid("eeeeeee1-0000-0000-0000-00000000000" + ordinal),
                new Guid("fffffff1-0000-0000-0000-000000000001"),
                face, Geometry.SolarCalculator.Query.AnalysisCells(face, 0.5));
        }

        private class Fixture
        {
            public List<ApertureSolarTarget> Targets;
            public ApertureSolarContext Context;
            public List<ApertureDesirability> Desirabilities;
            public ShadingAnalysisSignature Signature;
            public IDesirabilityStrategy Strategy;
        }

        private static Fixture Build(params ApertureSolarTarget[] targets)
        {
            WeatherData weatherData = TestHelpers.SolarSymmetricWeatherData(Year, TestHelpers.London(), 30.0);
            IDesirabilityStrategy strategy = new SeasonalDesirability(new AnalysisPeriod(Year, 6, 1, 8, 31), new AnalysisPeriod(Year, 11, 1, 2, 28));

            List<AnalysisCell> cells = new List<AnalysisCell>();
            List<Vector3D> normals = new List<Vector3D>();
            foreach (ApertureSolarTarget target in targets)
            {
                cells.AddRange(target.AnalysisCells);
                normals.AddRange(target.AnalysisCells.ConvertAll(x => new Vector3D(target.OutwardNormal)));
            }

            SolarVisibilityCache cache = Weather.SolarCalculator.Create.SolarVisibilityCache(
                TestHelpers.London(), Year, 2.0, new List<SAM.Geometry.Object.Spatial.LinkedFace3D>(), cells,
                cellSize: 0.5, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);

            string contextHash = Geometry.SolarCalculator.Query.GeometryHash(new List<SAM.Geometry.Object.Spatial.LinkedFace3D>(), Core.Tolerance.Distance);
            string targetHash = Geometry.SolarCalculator.Query.TargetHash(cells, Core.Tolerance.Distance);

            ApertureSolarContext context = new ApertureSolarContext(
                new List<ApertureSolarTarget>(targets), cells, normals,
                new List<SAM.Geometry.Object.Spatial.LinkedFace3D>(), cache, null,
                weatherData, TestHelpers.London(), Year, 0.5, 2.0, 30.0, contextHash, targetHash, false);

            List<ApertureDesirability> desirabilities = new List<ApertureDesirability>();
            foreach (ApertureSolarTarget target in targets)
            {
                desirabilities.Add(Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, strategy, weatherData));
            }

            ShadingAnalysisSignature signature = Analytical.SolarCalculator.Create.ShadingAnalysisSignature(context, strategy);
            return new Fixture { Targets = new List<ApertureSolarTarget>(targets), Context = context, Desirabilities = desirabilities, Signature = signature, Strategy = strategy };
        }

        private static ShadingScheme Scheme(string name, Dictionary<Guid, IShadingTypology> devices, List<Guid> scope, string designMethod = "RationaliseShading")
        {
            List<ShadingDevice> shadingDevices = new List<ShadingDevice>();
            foreach (KeyValuePair<Guid, IShadingTypology> pair in devices)
            {
                shadingDevices.Add(new ShadingDevice(pair.Key, pair.Value));
            }

            return new ShadingScheme(
                name, designMethod, scope, new List<Guid> { new Guid("fffffff1-0000-0000-0000-000000000001") },
                shadingDevices, new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());
        }

        private static VerifiedShadingSchemeResult Verify(ShadingScheme scheme, Fixture fixture, List<ApertureSolarTarget> targets, out string message)
        {
            return scheme.VerifiedShadingSchemeResult(fixture.Context, targets, fixture.Desirabilities, fixture.Signature, out message);
        }

        [Fact]
        public void Cross_Shading_Is_Measured_Not_Assumed()
        {
            ApertureSolarTarget a = Target(1, 0.0);
            ApertureSolarTarget b = Target(2, 1.2);
            Fixture fixture = Build(a, b);
            List<ApertureSolarTarget> scope = new List<ApertureSolarTarget> { a, b };
            List<Guid> scopeGuids = new List<Guid> { a.ApertureGuid, b.ApertureGuid };

            // Scheme S: an overhang over A deep enough, and extended far enough, to overhang B.
            ShadingScheme s = Scheme("Cross", new Dictionary<Guid, IShadingTypology>
            {
                { a.ApertureGuid, new Overhang(3.0, 0.0, 1.0) },
                { b.ApertureGuid, new NoShading() },
            }, scopeGuids);

            VerifiedShadingSchemeResult verified = Verify(s, fixture, scope, out string message);
            Assert.Null(message);
            Assert.NotNull(verified);

            // 1. The scheme verification of S reports intercepted energy on B.
            double onB = verified.Performance.Performance(b.ApertureGuid).DirectSolarIntercepted;
            Assert.True(onB > 1e-9, $"the overhang over A must intercept beam reaching B; got {onB}");

            // 2. Independent verification of B against its own null device reports exactly 0.
            ShadingScheme bAlone = Scheme("BAlone", new Dictionary<Guid, IShadingTypology> { { b.ApertureGuid, new NoShading() } }, new List<Guid> { b.ApertureGuid });
            VerifiedShadingSchemeResult bVerified = Verify(bAlone, fixture, new List<ApertureSolarTarget> { b }, out string bMessage);
            Assert.Null(bMessage);
            Assert.Equal(0.0, bVerified.Performance.DirectSolarIntercepted, 12);

            // 3. THE MUTATION CHECK. The naive sum (each member against its own device) is what the
            //    mutated implementation computes. The scheme total must exceed it by more than 1e-9:
            //    if the test passes against the mutation, it is not testing what it claims.
            ShadingScheme aAlone = Scheme("AAlone", new Dictionary<Guid, IShadingTypology> { { a.ApertureGuid, new Overhang(3.0, 0.0, 1.0) } }, new List<Guid> { a.ApertureGuid });
            VerifiedShadingSchemeResult aVerified = Verify(aAlone, fixture, new List<ApertureSolarTarget> { a }, out string aMessage);
            Assert.Null(aMessage);

            double naiveSum = aVerified.Performance.DirectSolarIntercepted + 0.0; // B contributes 0 alone
            Assert.True(verified.Performance.DirectSolarIntercepted > naiveSum + 1e-9,
                $"scheme total {verified.Performance.DirectSolarIntercepted} must exceed the naive per-window sum {naiveSum}");

            // 4. The interception on B is ATTRIBUTED to the scheme's elements — not residual.
            Assert.Equal(0.0, verified.Performance.UnattributedInterceptedEnergy, 12);

            // 5. I9: per-element energies plus the residual reconcile on every member and the scheme.
            foreach (ShadingPerformance member in verified.Performance.PerAperture)
            {
                Assert.Equal(member.DirectSolarIntercepted, member.ReconciledInterceptedEnergy, 9);
            }
        }

        [Fact]
        public void Conservation_Identities_Hold_On_A_Multi_Aperture_Scheme()
        {
            ApertureSolarTarget a = Target(1, 0.0);
            ApertureSolarTarget b = Target(2, 1.2);
            Fixture fixture = Build(a, b);

            ShadingScheme s = Scheme("Cross", new Dictionary<Guid, IShadingTypology>
            {
                { a.ApertureGuid, new Overhang(1.0) },
                { b.ApertureGuid, new Overhang(0.5) },
            }, new List<Guid> { a.ApertureGuid, b.ApertureGuid });

            VerifiedShadingSchemeResult verified = Verify(s, fixture, new List<ApertureSolarTarget> { a, b }, out string message);
            Assert.Null(message);

            ShadingSchemePerformance performance = verified.Performance;

            // I4 and I5 at scheme level.
            Assert.Equal(performance.AdmittedDirectEnergy,
                performance.AdmittedUnwantedEnergy + performance.AdmittedWantedEnergy + performance.AdmittedNeutralEnergy, 9);
            Assert.Equal(performance.DirectSolarIntercepted,
                performance.UnwantedSolarIntercepted + performance.WantedSolarBlocked + performance.NeutralSolarIntercepted, 9);

            // I9 on every member.
            foreach (ShadingPerformance member in performance.PerAperture)
            {
                Assert.Equal(member.DirectSolarIntercepted, member.ReconciledInterceptedEnergy, 9);
            }

            // The scheme baseline equals the unshaded state: verify the No Shade scheme and compare.
            ShadingScheme noShade = Scheme("No Shade", new Dictionary<Guid, IShadingTypology>
            {
                { a.ApertureGuid, new NoShading() },
                { b.ApertureGuid, new NoShading() },
            }, new List<Guid> { a.ApertureGuid, b.ApertureGuid }, "Baseline");

            VerifiedShadingSchemeResult noShadeVerified = Verify(noShade, fixture, new List<ApertureSolarTarget> { a, b }, out string noShadeMessage);
            Assert.Null(noShadeMessage);

            // I10: the No Shade score is COMPUTED as zero, and its baseline is real.
            Assert.Equal(0.0, noShadeVerified.Performance.UnwantedSolarIntercepted, 12);
            Assert.Equal(0.0, noShadeVerified.Performance.WantedSolarBlocked, 12);
            Assert.Equal(0.0, noShadeVerified.Performance.DirectSolarIntercepted, 12);
            Assert.True(noShadeVerified.Performance.AdmittedDirectEnergy > 0);

            // I8: every scheme's admitted baseline equals the No Shade scheme's.
            Assert.Equal(noShadeVerified.Performance.AdmittedDirectEnergy, performance.AdmittedDirectEnergy, 9);
            Assert.Equal(noShadeVerified.Performance.AdmittedUnwantedEnergy, performance.AdmittedUnwantedEnergy, 9);
            Assert.Equal(noShadeVerified.Performance.AdmittedWantedEnergy, performance.AdmittedWantedEnergy, 9);
        }

        // ------------------------------------------------------------ scope refusals ----

        [Fact]
        public void A_Scope_Mismatch_Is_Refused_With_A_Message_Naming_The_Guid()
        {
            ApertureSolarTarget a = Target(1, 0.0);
            ApertureSolarTarget b = Target(2, 1.2);
            ApertureSolarTarget c = Target(3, 2.4);
            Fixture fixture = Build(a, b, c);

            ShadingScheme s = Scheme("TwoOnly", new Dictionary<Guid, IShadingTypology>
            {
                { a.ApertureGuid, new Overhang(1.0) },
                { b.ApertureGuid, new Overhang(1.0) },
            }, new List<Guid> { a.ApertureGuid, b.ApertureGuid });

            // Targets cover a, b AND c — the scheme scope is {a, b}.
            VerifiedShadingSchemeResult verified = Verify(s, fixture, new List<ApertureSolarTarget> { a, b, c }, out string message);
            Assert.Null(verified);
            Assert.Contains(c.ApertureGuid.ToString(), message);
        }

        [Fact]
        public void A_Duplicated_Target_Is_Refused()
        {
            ApertureSolarTarget a = Target(1, 0.0);
            Fixture fixture = Build(a);

            ShadingScheme s = Scheme("Single", new Dictionary<Guid, IShadingTypology> { { a.ApertureGuid, new Overhang(1.0) } }, new List<Guid> { a.ApertureGuid });
            VerifiedShadingSchemeResult verified = Verify(s, fixture, new List<ApertureSolarTarget> { a, a }, out string message);
            Assert.Null(verified);
            Assert.Contains("twice", message);
        }

        [Fact]
        public void A_Device_Outside_The_Scope_Is_Refused()
        {
            ApertureSolarTarget a = Target(1, 0.0);
            ApertureSolarTarget b = Target(2, 1.2);
            Fixture fixture = Build(a, b);

            // The scheme declares scope {a} but carries a device for b.
            List<ShadingDevice> devices = new List<ShadingDevice>
            {
                new ShadingDevice(a.ApertureGuid, new Overhang(1.0)),
                new ShadingDevice(b.ApertureGuid, new Overhang(1.0)),
            };

            ShadingScheme s = new ShadingScheme(
                "Broken", "RationaliseShading", new List<Guid> { a.ApertureGuid }, new List<Guid> { new Guid("fffffff1-0000-0000-0000-000000000001") },
                devices, new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());

            VerifiedShadingSchemeResult verified = Verify(s, fixture, new List<ApertureSolarTarget> { a }, out string message);
            Assert.Null(verified);
            Assert.Contains(b.ApertureGuid.ToString(), message);
        }

        [Fact]
        public void Verification_Is_Deterministic_And_Input_Order_Independent()
        {
            ApertureSolarTarget a = Target(1, 0.0);
            ApertureSolarTarget b = Target(2, 1.2);
            Fixture fixture = Build(a, b);

            ShadingScheme s = Scheme("Cross", new Dictionary<Guid, IShadingTypology>
            {
                { a.ApertureGuid, new Overhang(1.0) },
                { b.ApertureGuid, new Overhang(0.5) },
            }, new List<Guid> { a.ApertureGuid, b.ApertureGuid });

            VerifiedShadingSchemeResult first = Verify(s, fixture, new List<ApertureSolarTarget> { a, b }, out string _);
            VerifiedShadingSchemeResult shuffled = Verify(s, fixture, new List<ApertureSolarTarget> { b, a }, out string _);

            Assert.Equal(first.Performance.AdmittedDirectEnergy, shuffled.Performance.AdmittedDirectEnergy, 9);
            Assert.Equal(first.Performance.DirectSolarIntercepted, shuffled.Performance.DirectSolarIntercepted, 9);
            Assert.Equal(first.Performance.UnwantedSolarIntercepted, shuffled.Performance.UnwantedSolarIntercepted, 9);
            Assert.Equal(first.ToJsonObject().ToJsonString(), shuffled.ToJsonObject().ToJsonString());
        }
    }
}
