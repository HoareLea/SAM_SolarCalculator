// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Globalization;
using Xunit;
using SAM.Analytical.SolarCalculator;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The DIRECT public optimiser refuses what the product forbids, and never reports a parameter
    /// it did not build.
    ///
    /// The defect class these tests pin has two halves, and both are silent.
    ///
    /// First, Optimise.RetractableAwningGroup is public in its own right. Create.AwningGroupResults
    /// validates the fixed projection / tilt / valance before calling it, but a caller reaching the
    /// optimiser directly bypasses that entirely — so the optimiser has to validate for itself
    /// rather than inherit a guarantee from an orchestration layer it cannot see.
    ///
    /// Second, and worse, the consequence of NOT validating is not an exception. ShadingTypology
    /// .Define clamps every parameter into its declared bounds with Math.Min/Math.Max, so a fixed
    /// tilt of 50° builds 40° of geometry, a fixed projection of 5 m builds 3.6 m, and a fixed
    /// valance of 0.5 m builds 0.21 m — while the candidate record, the design summary and the
    /// search history all still say 50, 5 and 0.5. The answer looks complete and is a measurement
    /// of a different awning from the one it names. A NaN tilt is the sharpest case: every ordinary
    /// range comparison against NaN is false, so a bounds test alone waves it through and the
    /// canopy gets built from tan(NaN).
    ///
    /// The contract these tests hold the code to: a request outside the product or outside the
    /// family is REFUSED with a reason, and every parameter that reaches a result is the parameter
    /// the geometry was actually built from.
    /// </summary>
    public class AwningInputValidationTests
    {
        private const int Year = 2018;
        private const double GridSize = 0.5;
        private static readonly Guid PanelGuid = new Guid("bbbb7777-0000-0000-0000-000000000001");

        private static ApertureSolarTarget Aperture(Guid apertureGuid, double x0, double width, double sill, double height)
        {
            Face3D face3D = new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(x0, 0, sill),
                new Point3D(x0 + width, 0, sill),
                new Point3D(x0 + width, 0, sill + height),
                new Point3D(x0, 0, sill + height),
            }));

            return new ApertureSolarTarget(apertureGuid, PanelGuid, face3D, SAM.Geometry.SolarCalculator.Query.AnalysisCells(face3D, GridSize));
        }

        private class Scenario
        {
            public SolarVisibilityCache SharedCache;
            public List<ApertureDesirability> Desirabilities;
            public List<LinkedFace3D> Context;
            public ApertureShadingGroup Group;
        }

        /// <summary>
        /// Three adjacent south-facing apertures on one shared cell space, heads aligned: a 2.70 m
        /// envelope, so with the default 0.15 m side extensions the awning is 3.00 m wide and a
        /// 2.60 m Dakar projection is exactly buildable.
        /// </summary>
        private static Scenario Build(double extensionBeyondJambs = 0.15)
        {
            const double head = 3.25;
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>
            {
                Aperture(new Guid("cccc8888-0000-0000-0000-000000000001"), 0.0, 0.9, head - 2.25, 2.25),
                Aperture(new Guid("cccc8888-0000-0000-0000-000000000002"), 0.9, 1.2, head - 1.39, 1.39),
                Aperture(new Guid("cccc8888-0000-0000-0000-000000000003"), 2.1, 0.6, head - 1.39, 1.39),
            };

            List<AnalysisCell> cells = new List<AnalysisCell>();
            List<int> offsets = new List<int>();
            foreach (ApertureSolarTarget target in targets)
            {
                offsets.Add(cells.Count);
                cells.AddRange(target.AnalysisCells);
            }

            List<LinkedFace3D> context = new List<LinkedFace3D>();
            SolarVisibilityCache sharedCache = Weather.SolarCalculator.Create.SolarVisibilityCache(
                TestHelpers.London(), Year, 2.0, context, cells,
                cellSize: GridSize, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);

            IDesirabilityStrategy strategy = new SeasonalDesirability(
                new AnalysisPeriod(Year, 6, 1, 8, 31), new AnalysisPeriod(Year, 11, 1, 2, 28));
            WeatherData weatherData = TestHelpers.SolarSymmetricWeatherData(Year, TestHelpers.London(), 30.0);

            List<ApertureDesirability> desirabilities = new List<ApertureDesirability>();
            foreach (ApertureSolarTarget target in targets)
            {
                desirabilities.Add(Analytical.SolarCalculator.Create.ApertureDesirability(target, sharedCache, strategy, weatherData));
            }

            List<ApertureShadingGroup> groups = targets.ApertureShadingGroups(AwningSpecification.Dakar, extensionBeyondJambs);
            Assert.Single(groups);
            ApertureShadingGroup group = groups[0];
            group.SetCellIndexOffsets(offsets);

            return new Scenario
            {
                SharedCache = sharedCache,
                Desirabilities = desirabilities,
                Context = context,
                Group = group,
            };
        }

        private static GroupedAwningResult Run(Scenario scenario, double? projection = null, double? tiltDegrees = null, double? valanceDepth = 0.0, double riseAboveHead = 0.0, double extensionBeyondJambs = 0.15, double mountingOffset = 0.0, AwningSpecification specification = null)
        {
            return Optimise.RetractableAwningGroup(
                scenario.Group, scenario.SharedCache, scenario.Desirabilities, scenario.Context,
                new ShadingObjective(0.0, 0.0), specification ?? AwningSpecification.Dakar,
                projection, tiltDegrees, riseAboveHead, extensionBeyondJambs, valanceDepth,
                maximumEvaluations: 400, mountingOffset: mountingOffset);
        }

        /// <summary>A refusal must be NOT EVALUATED with a reason, and must build nothing at all.</summary>
        private static void AssertRefused(GroupedAwningResult result, string expectedFragment)
        {
            Assert.NotNull(result);

            // NOT EVALUATED, never NO SHADE: the analysis did not conclude that building nothing was
            // best, it never ran.
            Assert.Equal(ShadingDesignStatus.NotEvaluated, result.Status);
            Assert.Null(result.Device);
            Assert.Null(result.Performance);

            // The reason has to reach the caller, or a refusal is indistinguishable from a failure.
            Assert.NotEmpty(result.Warnings);
            Assert.Contains(result.Warnings, x => x != null && x.Contains(expectedFragment));
            Assert.Contains(expectedFragment, result.DesignSummary);

            // Nothing was measured, so nothing may be reported as having been measured.
            Assert.Empty(result.EvaluatedCandidates);
            Assert.Equal(0, result.Evaluations);
        }

        // ------------------------------------------------- fixed values the product forbids ----

        [Fact]
        public void Fixed_Projection_Off_The_Product_Lattice_Is_Refused_By_The_Direct_Optimiser()
        {
            // 2.7 m sits between the 2.6 m and 3.1 m nominals. It is not a Dakar awning, and the
            // orchestration layer is not in the call path to say so.
            AssertRefused(Run(Build(), projection: 2.7), "not an allowed Dakar projection");
        }

        [Fact]
        public void Fixed_Projection_Beyond_The_Family_Bounds_Is_Refused_Rather_Than_Clamped()
        {
            GroupedAwningResult result = Run(Build(), projection: 5.0);

            AssertRefused(result, "not an allowed Dakar projection");

            // The point of the test: 5 m must NOT come back as a silently clamped 3.6 m awning.
            Assert.True(double.IsNaN(result.Projection), "a refused request must not report a projection at all");
        }

        [Fact]
        public void Fixed_Tilt_Above_The_Product_Range_Is_Refused_Rather_Than_Clamped()
        {
            GroupedAwningResult result = Run(Build(), projection: 2.6, tiltDegrees: 50.0);

            AssertRefused(result, "outside the Dakar tilt range");

            // ShadingTypology.Define would have clamped 50° to 40° and reported 50°. Neither number
            // may appear: there is no design here.
            Assert.True(double.IsNaN(result.TiltDegrees), "a refused request must not report a tilt at all");
        }

        [Fact]
        public void Fixed_Tilt_Below_The_Product_Range_Is_Refused()
        {
            AssertRefused(Run(Build(), projection: 2.6, tiltDegrees: 4.0), "outside the Dakar tilt range");
        }

        [Fact]
        public void Fixed_Tilt_Of_NaN_Is_Refused_Explicitly()
        {
            // The sharp case. NaN < 5 and NaN > 40 are BOTH false, so a range test alone accepts it
            // and the canopy is built from tan(NaN) — NaN corners fed to the ray engine.
            AssertRefused(Run(Build(), projection: 2.6, tiltDegrees: double.NaN), "not a finite number of degrees");
        }

        [Theory]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Fixed_Tilt_That_Is_Not_Finite_Is_Refused(double tiltDegrees)
        {
            GroupedAwningResult result = Run(Build(), projection: 2.6, tiltDegrees: tiltDegrees);

            Assert.NotNull(result);
            Assert.Equal(ShadingDesignStatus.NotEvaluated, result.Status);
            Assert.Null(result.Device);
        }

        [Fact]
        public void Fixed_Valance_Between_The_Two_Buildable_States_Is_Refused()
        {
            // A Dakar valance is either absent or the standard 0.21 m. 0.15 m is neither, and
            // clamping it into the [0, 0.21] parameter bounds would leave it untouched at 0.15 m —
            // buildable-looking geometry for a valance the product does not make.
            AssertRefused(Run(Build(), projection: 2.6, valanceDepth: 0.15), "valances are either 0 or the standard");
        }

        [Fact]
        public void Fixed_Extension_Beyond_The_Family_Bounds_Is_Refused()
        {
            // The extension is the dangerous one: the awning WIDTH, the product width check and the
            // bracket count are all computed from the requested value, while the canopy would have
            // been built from the value clamped into [0, 1] m.
            AssertRefused(Run(Build(), projection: 2.6, extensionBeyondJambs: 1.5), "outside the RetractableAwning range");
        }

        [Fact]
        public void Fixed_Rise_Beyond_The_Family_Bounds_Is_Refused()
        {
            AssertRefused(Run(Build(), projection: 2.6, riseAboveHead: 2.0), "outside the RetractableAwning range");
        }

        // ------------------------------------------------- mounting offset placement input ----

        [Fact]
        public void A_Negative_Mounting_Offset_Is_Refused_Rather_Than_Clamped()
        {
            // A mounting offset is the outward distance from the aperture plane to the mounting line:
            // a negative value would mean mounting INSIDE the facade, and must be refused — not
            // silently clamped to 0, which would report a recessed mounting that never happened.
            AssertRefused(Run(Build(), projection: 2.6, mountingOffset: -0.35), "mounting offset");
        }

        [Fact]
        public void A_NaN_Mounting_Offset_Is_Refused_Explicitly()
        {
            // The sharp case: every ordinary comparison against NaN is false, so a bounds test alone
            // would wave it through and the canopy would be built at a NaN offset.
            AssertRefused(Run(Build(), projection: 2.6, mountingOffset: double.NaN), "not a finite number of metres");
        }

        [Theory]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void A_Non_Finite_Mounting_Offset_Is_Refused(double mountingOffset)
        {
            GroupedAwningResult result = Run(Build(), projection: 2.6, mountingOffset: mountingOffset);

            Assert.NotNull(result);
            Assert.Equal(ShadingDesignStatus.NotEvaluated, result.Status);
            Assert.Null(result.Device);
            Assert.Null(result.Performance);
        }

        [Fact]
        public void A_Valid_Non_Zero_Mounting_Offset_Is_Accepted_And_Reported()
        {
            // A positive offset is project geometry, not a product limit: it must be accepted and
            // flow through to the reported result unchanged.
            GroupedAwningResult result = Run(Build(), projection: 2.6, tiltDegrees: 15.0, valanceDepth: 0.0, mountingOffset: 0.35);

            Assert.NotNull(result);
            Assert.Equal(ShadingDesignStatus.Ok, result.Status);
            Assert.NotNull(result.Device);
            Assert.Equal(0.35, result.MountingOffset, 9);
            Assert.Contains("MountingOffset 0.35", result.DesignSummary);
        }

        // ---------------------------------------------- old positional call compatibility ----

        [Fact]
        public void Old_Positional_Call_Maps_The_Trailing_Integer_To_MaximumEvaluations()
        {
            // The pre-mounting-offset signature ended in (..., valanceDepth, maximumEvaluations).
            // A caller compiled against it passes 400 positionally and must get
            // maximumEvaluations = 400, never mountingOffset = 400 m.
            Scenario scenario = Build(0.15);

            GroupedAwningResult result = Optimise.RetractableAwningGroup(
                scenario.Group, scenario.SharedCache, scenario.Desirabilities, scenario.Context,
                new ShadingObjective(0.0, 0.0), AwningSpecification.Dakar,
                2.6, 15.0, 0.0, 0.15, (double?)0.0, 400);

            Assert.NotNull(result);
            Assert.Equal(ShadingDesignStatus.Ok, result.Status);
            Assert.Equal(2.6, result.Projection, 9);
            Assert.Equal(15.0, result.TiltDegrees, 9);

            // The trailing 400 must NOT have become a 400 m mounting offset.
            Assert.Equal(0.0, result.MountingOffset, 9);
        }

        [Fact]
        public void A_Fixed_Projection_Too_Long_For_This_Group_Width_Is_Refused()
        {
            // 3.10 m is a real Dakar projection, but it needs a 3.50 m unit and this group is
            // 3.00 m wide. The pair is invalid even though each value is individually legal.
            AssertRefused(Run(Build(), projection: 3.1), "minimum width");
        }

        // ------------------------------------------------------------------ positive control ----

        [Fact]
        public void A_Valid_Locked_Request_Still_Produces_A_Measured_Design()
        {
            // The refusals above must not have made the ordinary path stricter: 2.60 m at 15° on a
            // 3.00 m unit is exactly buildable and must still be measured.
            GroupedAwningResult result = Run(Build(), projection: 2.6, tiltDegrees: 15.0, valanceDepth: 0.0);

            Assert.NotNull(result);
            Assert.Equal(ShadingDesignStatus.Ok, result.Status);
            Assert.NotNull(result.Device);
            Assert.NotNull(result.Performance);
            Assert.Equal(2.6, result.Projection, 9);
            Assert.Equal(15.0, result.TiltDegrees, 9);
            Assert.NotEmpty(result.EvaluatedCandidates);
        }

        [Fact]
        public void A_Free_Search_Over_The_Dakar_Preset_Reports_Only_Buildable_Parameters()
        {
            GroupedAwningResult result = Run(Build());

            Assert.NotNull(result);
            Assert.NotEqual(ShadingDesignStatus.NotEvaluated, result.Status);

            // Dakar and the RetractableAwning family agree, so a free search has nothing to exclude
            // and every candidate must land on the product lattice.
            Assert.NotEmpty(result.EvaluatedCandidates);
            foreach (AwningSearchCandidate candidate in result.EvaluatedCandidates)
            {
                Assert.True(AwningSpecification.Dakar.IsProjectionAllowed(candidate.Projection),
                    string.Format("candidate projection {0} is not a Dakar projection", candidate.Projection));
                Assert.InRange(candidate.TiltDegrees, 5.0, 40.0);
                Assert.True(AwningSpecification.Dakar.IsValidValanceDepth(candidate.ValanceDepth),
                    string.Format("candidate valance {0} is not a Dakar valance state", candidate.ValanceDepth));
            }
        }

        // --------------------------------- a preset wider than the family it is built from ----

        /// <summary>
        /// A specification is DATA with a public constructor, so a caller's own preset need not
        /// agree with the RetractableAwning parameter bounds the way Dakar does. Where it does not,
        /// the searched lattice must be CONFINED to the intersection and say so — never clamped
        /// silently, which would evaluate one geometry and report another.
        /// </summary>
        [Fact]
        public void A_Preset_Wider_Than_The_Family_Is_Confined_And_Reported_Not_Silently_Clamped()
        {
            // Projections: 1.0 m is below the family minimum of 1.6 m but passes the width rule
            // (zero minimum-width allowance), so it reaches the family-bounds check. 2.6 m is fine.
            // Tilt 0°-60° is wider than the family's 5°-40°. The 0.5 m valance is deeper than the
            // family's 0.21 m maximum.
            AwningSpecification wide = new AwningSpecification(
                "Wide", new double[] { 1.0, 2.6 }, 0.0, 60.0, 6.0, 0.0, 0.5, 4.1, 2, 3);

            Scenario scenario = Build();
            GroupedAwningResult result = Optimise.RetractableAwningGroup(
                scenario.Group, scenario.SharedCache, scenario.Desirabilities, scenario.Context,
                new ShadingObjective(0.0, 0.0), wide,
                projection: null, tiltDegrees: null, riseAboveHead: 0.0, extensionBeyondJambs: 0.15, valanceDepth: null);

            Assert.NotNull(result);
            Assert.NotEmpty(result.EvaluatedCandidates);

            // EVERY reported parameter is a parameter the geometry could actually be built from.
            foreach (AwningSearchCandidate candidate in result.EvaluatedCandidates)
            {
                Assert.InRange(candidate.Projection, 1.6, 3.6);
                Assert.InRange(candidate.TiltDegrees, 5.0, 40.0);
                Assert.InRange(candidate.ValanceDepth, 0.0, 0.21);
            }

            // The 1.0 m projection was never evaluated as a silently clamped 1.6 m awning.
            Assert.DoesNotContain(result.EvaluatedCandidates, x => Math.Abs(x.Projection - 1.0) < 1e-9);

            // The 0.5 m valance was never evaluated as a silently clamped 0.21 m one.
            Assert.DoesNotContain(result.EvaluatedCandidates, x => Math.Abs(x.ValanceDepth - 0.5) < 1e-9);

            // And the exclusions are SAID, not hidden: a narrower search than the preset named is a
            // fact the engineer has to be told.
            Assert.Contains(result.Warnings, x => x != null && x.Contains("projection") && x.Contains("was not evaluated"));
            Assert.Contains(result.Warnings, x => x != null && x.Contains("tilt range") && x.Contains("wider than"));
            Assert.Contains(result.Warnings, x => x != null && x.Contains("valance depth") && x.Contains("was not evaluated"));
        }

        /// <summary>
        /// The memoisation key is the BUILT parameter vector, so two requests that clamp onto the
        /// same geometry are one evaluation, not two candidates with the same score and different
        /// reported numbers.
        /// </summary>
        [Fact]
        public void Candidates_Are_Distinct_Geometries_Not_Distinct_Requests()
        {
            AwningSpecification wide = new AwningSpecification(
                "Wide", new double[] { 2.6 }, 0.0, 60.0, 6.0, 0.0, 0.21, 4.1, 2, 3);

            Scenario scenario = Build();
            GroupedAwningResult result = Optimise.RetractableAwningGroup(
                scenario.Group, scenario.SharedCache, scenario.Desirabilities, scenario.Context,
                new ShadingObjective(0.0, 0.0), wide,
                projection: null, tiltDegrees: null, riseAboveHead: 0.0, extensionBeyondJambs: 0.15, valanceDepth: 0.0);

            Assert.NotNull(result);
            Assert.NotEmpty(result.EvaluatedCandidates);

            // The coarse sweep is confined to 5°-40°, so no two candidates may share a tilt: a
            // duplicate would mean the same geometry was measured twice under two names.
            HashSet<string> seen = new HashSet<string>();
            foreach (AwningSearchCandidate candidate in result.EvaluatedCandidates)
            {
                // The preset offers 0°-60°. Anything outside the family range would have been built
                // at 5° or 40° while being reported as the angle that was asked for.
                Assert.InRange(candidate.TiltDegrees, 5.0, 40.0);

                string key = string.Concat(
                    candidate.Projection.ToString("R", CultureInfo.InvariantCulture), "|",
                    candidate.TiltDegrees.ToString("R", CultureInfo.InvariantCulture), "|",
                    candidate.ValanceDepth.ToString("R", CultureInfo.InvariantCulture));

                Assert.True(seen.Add(key), string.Format("the same built geometry was reported twice: {0}", key));
            }
        }

        // ---------------------------------------------- the two layers refuse in one voice ----

        /// <summary>
        /// The orchestration layer and the optimiser must not drift apart: whatever
        /// Create.AwningGroupResults refuses up front, the optimiser refuses too, in the same words.
        /// </summary>
        [Theory]
        [InlineData(2.7, null, 0.0)]
        [InlineData(5.0, null, 0.0)]
        [InlineData(2.6, 50.0, 0.0)]
        [InlineData(2.6, double.NaN, 0.0)]
        [InlineData(2.6, null, 0.15)]
        public void Both_Layers_Refuse_The_Same_Requests(double projection, double? tiltDegrees, double valanceDepth)
        {
            // Through the optimiser, with a real group and a real solar cache.
            GroupedAwningResult result = Run(Build(), projection: projection, tiltDegrees: tiltDegrees, valanceDepth: valanceDepth);

            Assert.NotNull(result);
            Assert.Equal(ShadingDesignStatus.NotEvaluated, result.Status);
            Assert.Null(result.Device);
            Assert.NotEmpty(result.Warnings);
            Assert.NotNull(result.DesignSummary);

            // And through the shared group-independent half that Create.AwningGroupResults calls
            // before it pays for a solar context. Same verdict, same sentence.
            Assert.False(Optimise.ValidAwningInputs(
                AwningSpecification.Dakar, projection, tiltDegrees, 0.0, 0.15, valanceDepth, 0.0, out string message));

            Assert.False(string.IsNullOrWhiteSpace(message));
            Assert.Contains(result.Warnings, x => x == message);
        }
    }
}
