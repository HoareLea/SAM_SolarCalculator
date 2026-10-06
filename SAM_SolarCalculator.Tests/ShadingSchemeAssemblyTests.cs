// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SAM.Analytical.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// SAM.Analytical.SolarCalculator.Create.ShadingSchemes: one scheme per conventional family covering the whole scope, one
    /// scheme for the whole grouped-awning run, and the No Shade baseline. The assembly must never
    /// substitute another family's device, must name the aperture a family is missing, and must be
    /// independent of input order (I12).
    /// </summary>
    public class ShadingSchemeAssemblyTests
    {
        private static readonly Guid ApertureA = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
        private static readonly Guid ApertureB = new Guid("aaaaaaa1-0000-0000-0000-000000000002");
        private static readonly Guid ApertureC = new Guid("aaaaaaa1-0000-0000-0000-000000000003");

        private static List<ApertureSolarTarget> Targets()
        {
            return new List<ApertureSolarTarget>
            {
                Target(ApertureA, 0),
                Target(ApertureB, 1),
                Target(ApertureC, 2),
            };
        }

        private static ApertureSolarTarget Target(Guid guid, int ordinal)
        {
            SAM.Geometry.Spatial.Face3D face = SyntheticTargets.Face(SyntheticTargets.South, new Point3D(2.0 * ordinal, 0, 5), 1.0, 2.0);
            return new ApertureSolarTarget(guid, new Guid("bbbbbbb1-0000-0000-0000-000000000001"), face, Geometry.SolarCalculator.Query.AnalysisCells(face, 0.5));
        }

        private static OptimisedShadingResult Result(Guid aperture, string family, double score, double benefit, double harm, double cost, ShadingOptimisationTermination termination = ShadingOptimisationTermination.StepBelowGranularity, bool noShading = false)
        {
            OptimisedShadingResult result = new OptimisedShadingResult();
            result.ApertureGuid = aperture;
            result.TypologyName = family;
            result.SetParameters(
                new List<string> { "Depth" }, new Dictionary<string, double> { { "Depth", 0.5 } },
                new Dictionary<string, double>(), new List<ShadingParameter> { new ShadingParameter("Depth", 0.05, 3.0, 0.01) });
            result.Objective = new ShadingObjective(1.0, 0.1);
            result.SetRun(100, 10, 1.0, termination, noShading, 0.0, 10, 10);
            result.SetProvenance(aperture, "SeasonalDesirability", 0.5, 2.0, 30.0, 2018, "ctx", "tgt", "tbl", new List<Guid>());
            result.SetPerformance(Performance(aperture, family, benefit, harm, cost), new ShadingObjective(1.0, 0.1));
            return result;
        }

        private static ShadingPerformance Performance(Guid aperture, string family, double benefit, double harm, double cost)
        {
            double admittedDirect = 100.0;
            double admittedUnwanted = 60.0;
            double admittedWanted = 20.0;
            double materialFraction = cost / admittedDirect; // cost = fraction x reference
            return new ShadingPerformance(
                aperture, family,
                admittedDirect, admittedUnwanted, admittedWanted,
                benefit + harm, benefit, harm,
                0.0, materialFraction, new Dictionary<Guid, double>(), new Dictionary<Guid, string>());
        }

        // -------------------------------------------------------------- families ----

        [Fact]
        public void Per_Aperture_Results_Group_By_Family_Into_One_Scheme_Per_Family()
        {
            List<OptimisedShadingResult> results = new List<OptimisedShadingResult>();
            foreach (Guid aperture in new List<Guid> { ApertureA, ApertureB, ApertureC })
            {
                results.Add(Result(aperture, "Overhang", 20.0, 30.0, 5.0, 5.0));
                results.Add(Result(aperture, "RetractableAwning", 15.0, 25.0, 5.0, 5.0));
            }

            List<ShadingScheme> schemes = SAM.Analytical.SolarCalculator.Create.ShadingSchemes(Targets(), results, null, true, out string message);
            Assert.Null(message);
            Assert.NotNull(schemes);

            // No Shade baseline + Overhang + VerticalFins.
            Assert.Equal(3, schemes.Count);
            Assert.Contains(schemes, x => x.Name == "No Shade");
            Assert.Contains(schemes, x => x.Name == "Overhang");
            Assert.Contains(schemes, x => x.Name == "RetractableAwning");

            foreach (ShadingScheme scheme in schemes.Where(x => x.Name != "No Shade"))
            {
                Assert.Equal(3, scheme.Devices.Count);
                Assert.Equal(ShadingDesignStatus.Ok, scheme.Status);
            }

            // (30 - 1.0 x 5 - 0.1 x 5) x 3 apertures = 73.5; the awning family's design score is its own.
            Assert.Equal(73.5, schemes.First(x => x.Name == "Overhang").DesignTimeScore, 9);
            Assert.Equal(58.5, schemes.First(x => x.Name == "RetractableAwning").DesignTimeScore, 9);
        }

        [Fact]
        public void RecommendsNoShading_Keeps_The_Null_Device_And_Records_The_Best_Candidate()
        {
            List<OptimisedShadingResult> results = new List<OptimisedShadingResult>
            {
                Result(ApertureA, "Overhang", 20.0, 30.0, 5.0, 5.0),
                Result(ApertureB, "Overhang", 20.0, 30.0, 5.0, 5.0),
                Result(ApertureC, "Overhang", -1.0, 1.0, 2.0, 1.0, ShadingOptimisationTermination.NoBeneficialCandidate, noShading: true),
            };

            List<ShadingScheme> schemes = SAM.Analytical.SolarCalculator.Create.ShadingSchemes(Targets(), results, null, true, out string message);
            Assert.Null(message);

            ShadingScheme overhang = schemes.First(x => x.Name == "Overhang");
            Assert.Equal(ShadingDesignStatus.NoShading, overhang.Status);
            Assert.Equal(2, overhang.PhysicalDeviceCount);
            Assert.True(overhang.Devices.First(x => x.ApertureGuid == ApertureC).IsNoShading);
            Assert.Contains(overhang.DesignDiagnostics, x => x.Contains("best candidate") && x.Contains(ApertureC.ToString()));
        }

        [Fact]
        public void A_Missing_Family_Result_Makes_The_Scheme_NotEvaluated_And_Nothing_Is_Substituted()
        {
            List<OptimisedShadingResult> results = new List<OptimisedShadingResult>
            {
                Result(ApertureA, "Overhang", 20.0, 30.0, 5.0, 5.0),
                // ApertureB missing for Overhang.
                Result(ApertureC, "Overhang", 20.0, 30.0, 5.0, 5.0),
            };

            List<ShadingScheme> schemes = SAM.Analytical.SolarCalculator.Create.ShadingSchemes(Targets(), results, null, true, out string message);
            Assert.Null(message);

            ShadingScheme overhang = schemes.First(x => x.Name == "Overhang");
            Assert.Equal(ShadingDesignStatus.NotEvaluated, overhang.Status);
            Assert.Contains(overhang.DesignDiagnostics, x => x.Contains(ApertureB.ToString()) && x.Contains("No Overhang result"));

            // No other family's device was substituted: the scheme carries no device for B at all.
            Assert.Equal(2, overhang.Devices.Count);
            Assert.DoesNotContain(overhang.Devices, x => x.ApertureGuid == ApertureB);
        }

        [Fact]
        public void EvaluationFailed_Makes_The_Scheme_NotEvaluated()
        {
            List<OptimisedShadingResult> results = new List<OptimisedShadingResult>
            {
                Result(ApertureA, "Overhang", 20.0, 30.0, 5.0, 5.0),
                Result(ApertureB, "Overhang", 20.0, 30.0, 5.0, 5.0),
                Result(ApertureC, "Overhang", double.NaN, double.NaN, double.NaN, double.NaN, ShadingOptimisationTermination.EvaluationFailed),
            };

            List<ShadingScheme> schemes = SAM.Analytical.SolarCalculator.Create.ShadingSchemes(Targets(), results, null, true, out string message);
            ShadingScheme overhang = schemes.First(x => x.Name == "Overhang");
            Assert.Equal(ShadingDesignStatus.NotEvaluated, overhang.Status);
            Assert.Contains(overhang.DesignDiagnostics, x => x.Contains("evaluation failed") && x.Contains(ApertureC.ToString()));
        }

        [Fact]
        public void An_All_No_Shade_Family_Is_Emitted_As_A_Distinct_Row()
        {
            List<OptimisedShadingResult> results = new List<OptimisedShadingResult>();
            foreach (Guid aperture in new List<Guid> { ApertureA, ApertureB, ApertureC })
            {
                results.Add(Result(aperture, "EggCrate", -1.0, 1.0, 2.0, 1.0, ShadingOptimisationTermination.NoBeneficialCandidate, noShading: true));
            }

            List<ShadingScheme> schemes = SAM.Analytical.SolarCalculator.Create.ShadingSchemes(Targets(), results, null, true, out string message);
            Assert.Null(message);

            ShadingScheme eggCrate = schemes.First(x => x.Name == "EggCrate (all no shade)");
            Assert.Equal(ShadingDesignStatus.NoShading, eggCrate.Status);
            Assert.Equal(0, eggCrate.PhysicalDeviceCount);

            // It is a DIFFERENT engineering statement from the baseline: a different SchemeGuid and
            // a different design method.
            ShadingScheme noShade = schemes.First(x => x.Name == "No Shade");
            Assert.NotEqual(noShade.SchemeGuid, eggCrate.SchemeGuid);
            Assert.Equal("RationaliseShading", eggCrate.DesignMethod);
            Assert.Equal("Baseline", noShade.DesignMethod);
        }

        [Fact]
        public void Input_Order_Does_Not_Change_The_Schemes()
        {
            List<OptimisedShadingResult> results = new List<OptimisedShadingResult>();
            foreach (Guid aperture in new List<Guid> { ApertureA, ApertureB, ApertureC })
            {
                results.Add(Result(aperture, "Overhang", 20.0, 30.0, 5.0, 5.0));
                results.Add(Result(aperture, "RetractableAwning", 15.0, 25.0, 5.0, 5.0));
            }

            List<OptimisedShadingResult> shuffled = new List<OptimisedShadingResult>(results);
            shuffled.Reverse();

            List<ShadingScheme> forward = SAM.Analytical.SolarCalculator.Create.ShadingSchemes(Targets(), results, null, true, out string _);
            List<ShadingScheme> backward = SAM.Analytical.SolarCalculator.Create.ShadingSchemes(Targets(), shuffled, null, true, out string _);

            Assert.Equal(forward.Select(x => x.SchemeGuid).OrderBy(x => x), backward.Select(x => x.SchemeGuid).OrderBy(x => x));
            Assert.Equal(forward.Select(x => x.Name).OrderBy(x => x), backward.Select(x => x.Name).OrderBy(x => x));
        }

        [Fact]
        public void A_Duplicated_Target_Is_Refused()
        {
            List<ApertureSolarTarget> duplicated = new List<ApertureSolarTarget>(Targets()) { Target(ApertureA, 0) };
            List<ShadingScheme> schemes = SAM.Analytical.SolarCalculator.Create.ShadingSchemes(duplicated, new List<OptimisedShadingResult>(), null, true, out string message);
            Assert.Null(schemes);
            Assert.Contains("duplicate", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Design_Time_Components_Are_Summed_Per_Aperture()
        {
            List<OptimisedShadingResult> results = new List<OptimisedShadingResult>();
            foreach (Guid aperture in new List<Guid> { ApertureA, ApertureB, ApertureC })
            {
                results.Add(Result(aperture, "Overhang", 20.0, 30.0, 5.0, 5.0));
            }

            List<ShadingScheme> schemes = SAM.Analytical.SolarCalculator.Create.ShadingSchemes(Targets(), results, null, true, out string _);
            ShadingScheme overhang = schemes.First(x => x.Name == "Overhang");
            Assert.Equal(73.5, overhang.DesignTimeScore, 9);
            Assert.Equal(90.0, overhang.DesignTimeBenefit, 9);
            Assert.Equal(15.0, overhang.DesignTimeHarm, 9);
            Assert.Equal(15.0, overhang.DesignTimeCost, 9);
            Assert.Equal(300, overhang.DesignEvaluations);
        }

        // --------------------------------------------------------- grouped awning ----

        private static GroupedAwningResult GroupResult(Guid groupGuid, IEnumerable<Guid> members, ShadingDesignStatus status, string specificationName = "Dakar")
        {
            ApertureShadingGroup group = new ApertureShadingGroup(
                groupGuid, new Guid("bbbbbbb1-0000-0000-0000-000000000001"),
                members.Select(x => Target(x, 0)), SyntheticTargets.Face(SyntheticTargets.South, new Point3D(0, 0, 5)).GetPlane(),
                0.0, 2.7, 2.25);

            GroupedShadingDevice device = new GroupedShadingDevice(
                groupGuid, new Guid("bbbbbbb1-0000-0000-0000-000000000001"), members,
                status == ShadingDesignStatus.NoShading ? (IShadingTypology)new NoShading() : new RetractableAwning(2.1, 15.0, 0.0, 0.15),
                status == ShadingDesignStatus.NoShading ? null : AwningSpecification.Dakar);

            return new GroupedAwningResult(
                group, device, null, null, status, "summary", new List<string>(), new List<AwningSearchCandidate>(), 42);
        }

        [Fact]
        public void All_Groups_From_One_Run_Form_One_Scheme()
        {
            Guid groupOne = new Guid("ccccccc1-0000-0000-0000-000000000001");
            Guid groupTwo = new Guid("ccccccc1-0000-0000-0000-000000000002");

            List<GroupedAwningResult> grouped = new List<GroupedAwningResult>
            {
                GroupResult(groupOne, new List<Guid> { ApertureA, ApertureB }, ShadingDesignStatus.Ok),
                GroupResult(groupTwo, new List<Guid> { ApertureC }, ShadingDesignStatus.Ok),
            };

            List<ShadingScheme> schemes = SAM.Analytical.SolarCalculator.Create.ShadingSchemes(Targets(), null, grouped, true, out string message);
            Assert.Null(message);

            ShadingScheme awning = schemes.First(x => x.DesignMethod == "RationaliseAwningGroup");
            Assert.Equal("Grouped Dakar Retractable Awning", awning.Name);
            Assert.Equal(2, awning.GroupedDevices.Count);
            Assert.Equal(2, awning.PhysicalDeviceCount);
            Assert.Equal(ShadingDesignStatus.Ok, awning.Status);
            Assert.Equal("Dakar", awning.ProductPreset);
            Assert.Equal(84, awning.DesignEvaluations);
        }

        [Fact]
        public void Two_Groups_Sharing_An_Aperture_Are_NotEvaluated()
        {
            Guid groupOne = new Guid("ccccccc1-0000-0000-0000-000000000001");
            Guid groupTwo = new Guid("ccccccc1-0000-0000-0000-000000000002");

            List<GroupedAwningResult> grouped = new List<GroupedAwningResult>
            {
                GroupResult(groupOne, new List<Guid> { ApertureA, ApertureB }, ShadingDesignStatus.Ok),
                GroupResult(groupTwo, new List<Guid> { ApertureB, ApertureC }, ShadingDesignStatus.Ok), // B shared
            };

            List<ShadingScheme> schemes = SAM.Analytical.SolarCalculator.Create.ShadingSchemes(Targets(), null, grouped, true, out string message);
            Assert.Null(message);

            ShadingScheme awning = schemes.First(x => x.DesignMethod == "RationaliseAwningGroup");
            Assert.Equal(ShadingDesignStatus.NotEvaluated, awning.Status);
            Assert.Contains(awning.DesignDiagnostics, x => x.Contains(ApertureB.ToString()) && x.Contains("two grouped awnings"));
        }

        [Fact]
        public void A_Group_Not_Covering_The_Scope_Is_NotEvaluated()
        {
            List<GroupedAwningResult> grouped = new List<GroupedAwningResult>
            {
                GroupResult(new Guid("ccccccc1-0000-0000-0000-000000000001"), new List<Guid> { ApertureA }, ShadingDesignStatus.Ok),
            };

            List<ShadingScheme> schemes = SAM.Analytical.SolarCalculator.Create.ShadingSchemes(Targets(), null, grouped, true, out string message);
            ShadingScheme awning = schemes.First(x => x.DesignMethod == "RationaliseAwningGroup");
            Assert.Equal(ShadingDesignStatus.NotEvaluated, awning.Status);
            Assert.Contains(awning.DesignDiagnostics, x => x.Contains("covered by no grouped awning"));
        }

        [Fact]
        public void No_Shade_Baseline_Is_Emitted_When_Requested_And_Suppressed_When_Not()
        {
            List<ShadingScheme> with = SAM.Analytical.SolarCalculator.Create.ShadingSchemes(Targets(), null, null, true, out string _);
            Assert.Contains(with, x => x.Name == "No Shade");
            Assert.Single(with);

            List<ShadingScheme> without = SAM.Analytical.SolarCalculator.Create.ShadingSchemes(Targets(), null, null, false, out string _);
            Assert.Empty(without);
        }
    }
}
