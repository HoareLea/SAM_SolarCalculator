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
    /// Scheme material accounting: each physical element is charged exactly once per scheme. A
    /// shared canopy over three apertures counts once; three separate identical overhangs count
    /// three times (this fails on today's typology-level element GUIDs — the second P1 guard);
    /// overlapping devices both count in full; and the per-member MaterialFraction values from the
    /// shared-elements overload are NEVER used at scheme level (Trap 1).
    /// </summary>
    public class SchemeMaterialAccountingTests
    {
        private static ApertureSolarTarget Target(Guid guid, double centreX, double width = 1.0, double height = 2.0)
        {
            SAM.Geometry.Spatial.Face3D face = SyntheticTargets.Face(SyntheticTargets.South, new Point3D(centreX, 0, 5), width, height);
            return new ApertureSolarTarget(guid, new Guid("fffffff1-0000-0000-0000-000000000001"), face, Geometry.SolarCalculator.Query.AnalysisCells(face, 0.5));
        }

        private static ShadingSchemePerformance BuildPerformance(List<ApertureSolarTarget> targets, ShadingScheme scheme)
        {
            List<ShadingPerformance> members = new List<ShadingPerformance>();
            foreach (ApertureSolarTarget target in targets)
            {
                // A per-member performance whose material fraction is deliberately the meaningless
                // per-member value (whole scheme area / one window) — the scheme performance must
                // discard it.
                members.Add(new ShadingPerformance(
                    target.ApertureGuid, scheme.Name,
                    100.0, 60.0, 20.0, 40.0, 30.0, 5.0, 0.0,
                    99.0, // nonsense per-member material fraction, must be ignored
                    new Dictionary<Guid, double>(), new Dictionary<Guid, string>()));
            }

            List<ShadingElement> elements = scheme.SchemeElements(targets);
            double deviceArea = 0;
            bool available = true;
            foreach (ShadingElement element in elements)
            {
                double area = element.Area;
                if (double.IsNaN(area) || double.IsInfinity(area) || area < 0)
                {
                    available = false;
                }
                else
                {
                    deviceArea += area;
                }
            }

            double gross = targets.Sum(x => x.GrossArea);
            return new ShadingSchemePerformance(members, deviceArea, available, gross, scheme.ElementOwners(targets));
        }

        [Fact]
        public void A_Grouped_Shared_Canopy_Is_Charged_Once()
        {
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>
            {
                Target(new Guid("eeeeeee1-0000-0000-0000-000000000001"), 0.0),
                Target(new Guid("eeeeeee1-0000-0000-0000-000000000002"), 1.2),
                Target(new Guid("eeeeeee1-0000-0000-0000-000000000003"), 2.4),
            };

            Guid groupGuid = new Guid("ccccccc1-0000-0000-0000-000000000001");
            ApertureShadingGroup group = new ApertureShadingGroup(
                groupGuid, new Guid("fffffff1-0000-0000-0000-000000000001"), targets,
                targets[0].Plane, 0.0, 3.4, 1.0);

            GroupedShadingDevice device = new GroupedShadingDevice(
                groupGuid, new Guid("fffffff1-0000-0000-0000-000000000001"),
                targets.Select(x => x.ApertureGuid), new RetractableAwning(2.1, 15.0, 0.0, 0.15), AwningSpecification.Dakar);

            ShadingScheme scheme = new ShadingScheme(
                "Grouped Dakar Retractable Awning", "RationaliseAwningGroup", targets.Select(x => x.ApertureGuid), new List<Guid> { new Guid("fffffff1-0000-0000-0000-000000000001") },
                new List<ShadingDevice>(), new List<GroupedShadingDevice> { device }, new List<ApertureShadingGroup> { group },
                ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());

            ShadingSchemePerformance performance = BuildPerformance(targets, scheme);

            List<ShadingElement> elements = scheme.SchemeElements(targets);
            double canopyArea = elements.Sum(x => x.Area);

            // The canopy area is counted ONCE, not once per aperture.
            Assert.Equal(canopyArea, performance.PhysicalDeviceArea, 9);
            Assert.Equal(3, performance.PerAperture.Count);
            Assert.Equal(3, targets.Count);

            // The material fraction is the canopy over the SUM of the gross areas, and is NOT the
            // mean or sum of the per-member values (Trap 1).
            double expectedFraction = canopyArea / targets.Sum(x => x.GrossArea);
            Assert.Equal(expectedFraction, performance.MaterialFraction, 9);
            Assert.NotEqual(99.0, performance.MaterialFraction);
            Assert.NotEqual(99.0 * 3, performance.MaterialFraction);
        }

        [Fact]
        public void Three_Identical_Overhangs_Are_Charged_Three_Times()
        {
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>
            {
                Target(new Guid("eeeeeee1-0000-0000-0000-000000000001"), 0.0),
                Target(new Guid("eeeeeee1-0000-0000-0000-000000000002"), 1.2),
                Target(new Guid("eeeeeee1-0000-0000-0000-000000000003"), 2.4),
            };

            ShadingScheme scheme = new ShadingScheme(
                "Overhang", "RationaliseShading", targets.Select(x => x.ApertureGuid), new List<Guid> { new Guid("fffffff1-0000-0000-0000-000000000001") },
                targets.Select(x => new ShadingDevice(x.ApertureGuid, new Overhang(0.5))),
                new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());

            ShadingSchemePerformance performance = BuildPerformance(targets, scheme);

            // One plate (0.5 deep x 1.0 wide) per window: three plates. This fails on the typology
            // level GUIDs if the scheme-scoped re-identification is dropped, because the distinct
            // element set would collapse to one plate.
            Assert.Equal(1.5, performance.PhysicalDeviceArea, 9);
        }

        [Fact]
        public void Two_Overlapping_Devices_Both_Count_In_Full()
        {
            // Two separate devices over the SAME aperture: an overhang and an awning. Material is
            // what is bought and hung — never a geometric union of the shadows.
            ApertureSolarTarget target = Target(new Guid("eeeeeee1-0000-0000-0000-000000000001"), 0.0);

            ShadingScheme scheme = new ShadingScheme(
                "Double", "RationaliseShading", new List<Guid> { target.ApertureGuid }, new List<Guid> { new Guid("fffffff1-0000-0000-0000-000000000001") },
                new List<ShadingDevice>
                {
                    new ShadingDevice(target.ApertureGuid, new Overhang(0.5)),
                    new ShadingDevice(target.ApertureGuid, new RetractableAwning(2.1, 15.0)),
                },
                new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());

            ShadingSchemePerformance performance = BuildPerformance(new List<ApertureSolarTarget> { target }, scheme);

            double overhangArea = new Overhang(0.5).ShadingElements(target).Sum(x => x.Area);
            double awningArea = new RetractableAwning(2.1, 15.0).ShadingElements(target).Sum(x => x.Area);

            Assert.Equal(overhangArea + awningArea, performance.PhysicalDeviceArea, 9);
            Assert.True(performance.PhysicalDeviceArea > Math.Max(overhangArea, awningArea), "the union area would collapse the two devices");
        }

        [Fact]
        public void MaterialFraction_Is_Area_Over_Total_Gross_And_Not_The_Member_Values()
        {
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>
            {
                Target(new Guid("eeeeeee1-0000-0000-0000-000000000001"), 0.0),
                Target(new Guid("eeeeeee1-0000-0000-0000-000000000002"), 1.2),
            };

            ShadingScheme scheme = new ShadingScheme(
                "Overhang", "RationaliseShading", targets.Select(x => x.ApertureGuid), new List<Guid> { new Guid("fffffff1-0000-0000-0000-000000000001") },
                targets.Select(x => new ShadingDevice(x.ApertureGuid, new Overhang(0.5))),
                new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());

            ShadingSchemePerformance performance = BuildPerformance(targets, scheme);

            double deviceArea = 2 * 0.5 * 1.0; // two plates
            double gross = targets.Sum(x => x.GrossArea);
            Assert.Equal(deviceArea / gross, performance.MaterialFraction, 9);
        }

        [Fact]
        public void Unmeasurable_Element_Area_Makes_Material_Unavailable()
        {
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>
            {
                Target(new Guid("eeeeeee1-0000-0000-0000-000000000001"), 0.0),
            };

            // A scheme with no geometry at all but a positive material penalty: the performance is
            // built with materialAvailable false.
            ShadingSchemePerformance performance = new ShadingSchemePerformance(
                new List<ShadingPerformance>
                {
                    new ShadingPerformance(targets[0].ApertureGuid, "Broken", 100.0, 60.0, 20.0, 40.0, 30.0, 5.0, 0.0, 99.0,
                        new Dictionary<Guid, double>(), new Dictionary<Guid, string>()),
                },
                double.NaN, false, targets.Sum(x => x.GrossArea), new Dictionary<Guid, Guid>());

            Assert.False(performance.MaterialAvailable);
            Assert.True(double.IsNaN(performance.MaterialFraction));
        }
    }
}
