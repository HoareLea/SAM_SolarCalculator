// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
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
    /// One shared awning measured against every aperture of its group, on a shared cell space.
    ///
    /// The defect class these tests pin: the single-aperture path builds a device around ONE
    /// target, so a naive "group" implementation that called it per member would rebuild the
    /// canopy centred on each window, charge the fabric three times, and credit three different
    /// element sets. The grouped path must build once and measure every member against the SAME
    /// element Guids, with the correct shared-cache offsets, and charge material once.
    /// </summary>
    public class AwningGroupPerformanceTests
    {
        private readonly ITestOutputHelper output;

        public AwningGroupPerformanceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const int Year = 2018;
        private const double GridSize = 0.5;
        private static readonly Guid PanelGuid = new Guid("dddd4444-0000-0000-0000-000000000001");

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
            public List<ApertureSolarTarget> Targets;
            public SolarVisibilityCache SharedCache;
            public List<int> Offsets;
            public List<ApertureDesirability> Desirabilities;
            public List<LinkedFace3D> Context;
            public ApertureShadingGroup Group;
        }

        /// <summary>
        /// Three adjacent south-facing apertures sharing one cell space — the Kołobrzeg layout in
        /// miniature: one tall window beside two shorter ones, all with the SAME head level.
        /// </summary>
        private static Scenario Build(double extensionBeyondJambs)
        {
            const double head = 3.25;
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>
            {
                Aperture(new Guid("eeee5555-0000-0000-0000-000000000001"), 0.0, 0.9, head - 2.25, 2.25),
                Aperture(new Guid("eeee5555-0000-0000-0000-000000000002"), 0.9, 1.2, head - 1.39, 1.39),
                Aperture(new Guid("eeee5555-0000-0000-0000-000000000003"), 2.1, 0.6, head - 1.39, 1.39),
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
                Targets = targets,
                SharedCache = sharedCache,
                Offsets = offsets,
                Desirabilities = desirabilities,
                Context = context,
                Group = group,
            };
        }

        [Fact]
        public void One_Shared_Device_Is_Measured_Against_Every_Member_With_Correct_Identity()
        {
            Scenario scenario = Build(0.15);

            // Zero penalties: any awning that blocks ANY unwanted solar is worth building, which
            // guarantees a measurable recommendation to exercise the shared-element contract.
            GroupedAwningResult result = Optimise.RetractableAwningGroup(
                scenario.Group, scenario.SharedCache, scenario.Desirabilities, scenario.Context,
                new ShadingObjective(0.0, 0.0), AwningSpecification.Dakar,
                projection: 2.6, tiltDegrees: 15.0, riseAboveHead: 0.0, extensionBeyondJambs: 0.15, valanceDepth: 0.0);

            Assert.NotNull(result);
            Assert.Equal(ShadingDesignStatus.Ok, result.Status);

            // The locked tilt bypasses the angle search entirely: the reported angle is the lock.
            Assert.Equal(15.0, result.TiltDegrees, 9);
            Assert.Equal(2.6, result.Projection, 9);

            GroupedShadingPerformance performance = result.Performance;
            Assert.NotNull(performance);

            List<ShadingElement> sharedElements = result.Device.ShadingElements(result.Group);
            Assert.Single(sharedElements);
            Guid canopyGuid = sharedElements[0].Guid;

            // THE SHARED-ELEMENT CONTRACT: every member's per-element credits carry the SAME
            // element Guids — one physical canopy — and each performance keeps its own aperture.
            Assert.Equal(3, performance.PerAperture.Count);
            double sumIntercepted = 0, sumAdmitted = 0, sumUnwanted = 0, sumAdmittedUnwanted = 0, sumBlockedWanted = 0, sumAdmittedWanted = 0;

            for (int i = 0; i < scenario.Targets.Count; i++)
            {
                ShadingPerformance member = performance.PerAperture[i];
                Assert.Equal(scenario.Targets[i].ApertureGuid, member.ApertureGuid);
                Assert.Contains(canopyGuid, member.EnergyPerElement.Keys);

                sumIntercepted += member.DirectSolarIntercepted;
                sumAdmitted += member.AdmittedDirectEnergy;
                sumUnwanted += member.UnwantedSolarIntercepted;
                sumAdmittedUnwanted += member.AdmittedUnwantedEnergy;
                sumBlockedWanted += member.WantedSolarBlocked;
                sumAdmittedWanted += member.AdmittedWantedEnergy;
            }

            // Group energies are the sums of the members.
            Assert.Equal(sumAdmitted, performance.AdmittedDirectEnergy, 9);
            Assert.Equal(sumAdmittedUnwanted, performance.AdmittedUnwantedEnergy, 9);
            Assert.Equal(sumAdmittedWanted, performance.AdmittedWantedEnergy, 9);
            Assert.Equal(sumIntercepted, performance.DirectSolarIntercepted, 9);
            Assert.Equal(sumUnwanted, performance.UnwantedSolarIntercepted, 9);
            Assert.Equal(sumBlockedWanted, performance.WantedSolarBlocked, 9);

            // Group percentages come from the SUMMED energies, not averaged percentages.
            Assert.Equal(sumIntercepted / sumAdmitted, performance.DirectShadingEfficiency, 9);
            Assert.Equal(sumUnwanted / sumAdmittedUnwanted, performance.UnwantedSolarBlocked, 9);
            Assert.Equal((sumAdmittedWanted - sumBlockedWanted) / sumAdmittedWanted, performance.WantedSolarRetained, 9);

            // The shared canopy genuinely shades all three members.
            Assert.True(sumIntercepted > 0, "the shared canopy must intercept solar on the group");

            output.WriteLine($"shared canopy {canopyGuid}: admitted {sumAdmitted:0.###} kWh, intercepted {sumIntercepted:0.###} kWh, " +
                $"unwanted blocked {100 * performance.UnwantedSolarBlocked:0.#} %, wanted retained {100 * performance.WantedSolarRetained:0.#} %");
        }

        [Fact]
        public void Material_Is_Charged_Once_Per_Physical_Awning()
        {
            Scenario scenario = Build(0.15);

            GroupedAwningResult result = Optimise.RetractableAwningGroup(
                scenario.Group, scenario.SharedCache, scenario.Desirabilities, scenario.Context,
                new ShadingObjective(0.0, 0.0), AwningSpecification.Dakar,
                projection: 2.6, tiltDegrees: 15.0, riseAboveHead: 0.0, extensionBeyondJambs: 0.15, valanceDepth: 0.0);

            GroupedShadingPerformance performance = result.Performance;

            // One shared device area over the SUM of the member gross areas.
            double expectedFraction = performance.SharedDeviceArea / scenario.Group.TotalGrossArea;
            Assert.Equal(expectedFraction, performance.MaterialFraction, 9);

            // Never the sum of per-window charges: each member's own fraction uses the SAME shared
            // area over its own (smaller) gross area, so summing them would over-charge.
            double sumOfMemberFractions = performance.PerAperture.Sum(x => x.MaterialFraction);
            Assert.True(performance.MaterialFraction < sumOfMemberFractions,
                $"group material fraction {performance.MaterialFraction} must be charged once, not summed ({sumOfMemberFractions})");

            // The objective's cost term uses the group fraction (the penalty μ scales it in Score,
            // not in Cost).
            Assert.Equal(performance.MaterialFraction * performance.AdmittedDirectEnergy, new ShadingObjective(1.0, 0.1).Cost(performance), 9);

            output.WriteLine($"shared device area {performance.SharedDeviceArea:0.###} m2, group fraction {performance.MaterialFraction:0.###}, " +
                $"sum of member fractions {sumOfMemberFractions:0.###}");
        }

        [Fact]
        public void A_Valance_Is_Part_Of_The_Same_Shared_Device()
        {
            Scenario scenario = Build(0.15);

            GroupedAwningResult result = Optimise.RetractableAwningGroup(
                scenario.Group, scenario.SharedCache, scenario.Desirabilities, scenario.Context,
                new ShadingObjective(0.0, 0.0), AwningSpecification.Dakar,
                projection: 2.6, tiltDegrees: 15.0, riseAboveHead: 0.0, extensionBeyondJambs: 0.15, valanceDepth: 0.21);

            Assert.Equal(ShadingDesignStatus.Ok, result.Status);

            List<ShadingElement> elements = result.Device.ShadingElements(result.Group);
            Assert.Equal(2, elements.Count);

            HashSet<Guid> guids = new HashSet<Guid>(elements.Select(x => x.Guid));
            Assert.Equal(2, guids.Count);

            foreach (ShadingPerformance member in result.Performance.PerAperture)
            {
                foreach (Guid guid in guids)
                {
                    Assert.Contains(guid, member.EnergyPerElement.Keys);
                }
            }
        }

        [Fact]
        public void The_Null_Device_Is_A_Measured_No_Shade_Answer()
        {
            Scenario scenario = Build(0.15);

            // An absurd material penalty: nothing can beat building nothing.
            GroupedAwningResult result = Optimise.RetractableAwningGroup(
                scenario.Group, scenario.SharedCache, scenario.Desirabilities, scenario.Context,
                new ShadingObjective(1.0, 1000.0), AwningSpecification.Dakar,
                projection: 2.6, tiltDegrees: 15.0, riseAboveHead: 0.0, extensionBeyondJambs: 0.15, valanceDepth: 0.0);

            Assert.Equal(ShadingDesignStatus.NoShading, result.Status);
            Assert.True(result.Device.IsNoShading);
            Assert.NotNull(result.BestCandidateDevice);
            Assert.False(result.BestCandidateDevice.IsNoShading);

            // The null device is MEASURED, not asserted: a real baseline, nothing intercepted.
            GroupedShadingPerformance performance = result.Performance;
            Assert.NotNull(performance);
            Assert.Equal("NoShading", performance.TypologyName);
            Assert.True(performance.AdmittedDirectEnergy > 0);
            Assert.Equal(0.0, performance.DirectSolarIntercepted, 9);
            Assert.Equal(0.0, performance.MaterialFraction, 9);
        }

        [Fact]
        public void A_Group_Nothing_Could_Measure_Is_Not_Reported_As_No_Shade()
        {
            Scenario scenario = Build(0.15);

            // Corrupt the shared-cache offsets: no member can be addressed, so nothing is measurable.
            ApertureShadingGroup broken = new ApertureShadingGroup(scenario.Group);
            broken.SetCellIndexOffsets(new List<int> { -1, -1, -1 });

            GroupedAwningResult result = Optimise.RetractableAwningGroup(
                broken, scenario.SharedCache, scenario.Desirabilities, scenario.Context,
                new ShadingObjective(1.0, 0.1), AwningSpecification.Dakar,
                projection: 2.6, tiltDegrees: 15.0, riseAboveHead: 0.0, extensionBeyondJambs: 0.15, valanceDepth: 0.0);

            Assert.Equal(ShadingDesignStatus.NotEvaluated, result.Status);
            Assert.Null(result.Device);
            Assert.Null(result.Performance);
            Assert.Contains("Not evaluated", result.DesignSummary);
        }

        [Fact]
        public void Group_Result_Survives_A_Json_Round_Trip()
        {
            Scenario scenario = Build(0.15);

            GroupedAwningResult result = Optimise.RetractableAwningGroup(
                scenario.Group, scenario.SharedCache, scenario.Desirabilities, scenario.Context,
                new ShadingObjective(1.0, 0.1), AwningSpecification.Dakar,
                projection: 2.6, tiltDegrees: 15.0, riseAboveHead: 0.0, extensionBeyondJambs: 0.15, valanceDepth: 0.0);

            GroupedAwningResult reloaded = Core.Create.IJSAMObject<GroupedAwningResult>(result.ToJsonObject().ToJsonString());

            Assert.NotNull(reloaded);
            Assert.Equal(result.Group.GroupGuid, reloaded.Group.GroupGuid);
            Assert.Equal(result.Status, reloaded.Status);
            Assert.Equal(result.Projection, reloaded.Projection, 9);
            Assert.Equal(result.TiltDegrees, reloaded.TiltDegrees, 9);
            Assert.Equal(result.Width, reloaded.Width, 9);
            Assert.Equal(result.Performance.AdmittedDirectEnergy, reloaded.Performance.AdmittedDirectEnergy, 9);
            Assert.Equal(result.Performance.MaterialFraction, reloaded.Performance.MaterialFraction, 9);
            Assert.Equal(result.Performance.PerAperture.Count, reloaded.Performance.PerAperture.Count);
        }

        [Fact]
        public void Mounting_Offset_Produces_A_Distinct_Device_And_Report()
        {
            Scenario scenario = Build(0.15);

            GroupedAwningResult atFacade = Optimise.RetractableAwningGroup(
                scenario.Group, scenario.SharedCache, scenario.Desirabilities, scenario.Context,
                new ShadingObjective(0.0, 0.0), AwningSpecification.Dakar,
                projection: 2.6, tiltDegrees: 15.0, riseAboveHead: 0.0, extensionBeyondJambs: 0.15, valanceDepth: 0.0, maximumEvaluations: 400, mountingOffset: 0.0);

            GroupedAwningResult recessed = Optimise.RetractableAwningGroup(
                scenario.Group, scenario.SharedCache, scenario.Desirabilities, scenario.Context,
                new ShadingObjective(0.0, 0.0), AwningSpecification.Dakar,
                projection: 2.6, tiltDegrees: 15.0, riseAboveHead: 0.0, extensionBeyondJambs: 0.15, valanceDepth: 0.0, maximumEvaluations: 400, mountingOffset: 0.35);

            Assert.NotNull(atFacade.Device);
            Assert.NotNull(recessed.Device);

            // The reported mounting offset is the offset the geometry was actually built from.
            Assert.Equal(0.0, atFacade.MountingOffset, 9);
            Assert.Equal(0.35, recessed.MountingOffset, 9);

            // Distinct geometry identity: the element GUID scheme hashes the mounting offset, so two
            // otherwise identical awnings at different offsets never share an element identity and
            // can never reuse each other's geometry.
            List<ShadingElement> facadeElements = atFacade.Device.ShadingElements(atFacade.Group);
            List<ShadingElement> recessedElements = recessed.Device.ShadingElements(recessed.Group);
            Assert.Single(facadeElements);
            Assert.Single(recessedElements);
            Assert.NotEqual(facadeElements[0].Guid, recessedElements[0].Guid);

            // The design summary records where the awning was mounted.
            Assert.Contains("MountingOffset 0.35", recessed.DesignSummary);
            Assert.Contains("MountingOffset 0", atFacade.DesignSummary);
        }

        [Fact]
        public void Group_Result_Json_Round_Trip_Preserves_Mounting_Offset()
        {
            Scenario scenario = Build(0.15);

            // Zero penalties guarantee a measurable awning recommendation, so the recommended device
            // carries a RetractableAwning typology whose MountingOffset must survive the round trip.
            GroupedAwningResult result = Optimise.RetractableAwningGroup(
                scenario.Group, scenario.SharedCache, scenario.Desirabilities, scenario.Context,
                new ShadingObjective(0.0, 0.0), AwningSpecification.Dakar,
                projection: 2.6, tiltDegrees: 15.0, riseAboveHead: 0.0, extensionBeyondJambs: 0.15, valanceDepth: 0.0, maximumEvaluations: 400, mountingOffset: 0.35);

            Assert.Equal(ShadingDesignStatus.Ok, result.Status);

            GroupedAwningResult reloaded = Core.Create.IJSAMObject<GroupedAwningResult>(result.ToJsonObject().ToJsonString());

            Assert.NotNull(reloaded);
            Assert.Equal(0.35, reloaded.MountingOffset, 12);
            Assert.Equal(0.35, reloaded.Device.Typology.GetParameter("MountingOffset"), 12);
        }
    }
}
