// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using SAM.Analytical.SolarCalculator;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// I1 (deterministic scheme identity) and I2 (per-scheme element identity): the two identity
    /// contracts the whole comparison stands on. In particular, three identical overhangs on three
    /// apertures MUST produce three distinct element GUIDs — that is the P1 regression guard, and
    /// it fails on today's typology-level ElementGuid before the scheme-scoped re-identification.
    /// </summary>
    public class ShadingSchemeTests
    {
        private static ApertureSolarTarget Target(int ordinal)
        {
            return new ApertureSolarTarget(
                new Guid("aaaaaaa1-0000-0000-0000-00000000000" + ordinal),
                new Guid("bbbbbbb1-0000-0000-0000-00000000000" + ordinal),
                SyntheticTargets.Face(SyntheticTargets.South, new Point3D(2.0 * ordinal, 0, 5), 1.0, 2.0),
                Geometry.SolarCalculator.Query.AnalysisCells(SyntheticTargets.Face(SyntheticTargets.South, new Point3D(2.0 * ordinal, 0, 5), 1.0, 2.0), 0.5));
        }

        private static ShadingScheme OverhangScheme(Guid a, Guid b, Guid c, double depth, string name = "Overhang")
        {
            return new ShadingScheme(
                name, "RationaliseShading", new List<Guid> { a, b, c }, new List<Guid> { new Guid("bbbbbbb1-0000-0000-0000-000000000001") },
                new List<ShadingDevice>
                {
                    new ShadingDevice(a, new Overhang(depth)),
                    new ShadingDevice(b, new Overhang(depth)),
                    new ShadingDevice(c, new Overhang(depth)),
                },
                new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());
        }

        private static ShadingScheme OverhangScheme(IEnumerable<Guid> apertures, double depth, string name = "Overhang")
        {
            List<ShadingDevice> devices = new List<ShadingDevice>();
            foreach (Guid aperture in apertures)
            {
                devices.Add(new ShadingDevice(aperture, new Overhang(depth)));
            }

            return new ShadingScheme(
                name, "RationaliseShading", apertures, new List<Guid> { new Guid("bbbbbbb1-0000-0000-0000-000000000001") },
                devices, new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());
        }

        // ------------------------------------------------------------------------ I1 ----

        [Fact]
        public void SchemeGuid_Is_Independent_Of_Input_Order()
        {
            Guid a = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
            Guid b = new Guid("aaaaaaa1-0000-0000-0000-000000000002");
            Guid c = new Guid("aaaaaaa1-0000-0000-0000-000000000003");

            ShadingScheme forward = OverhangScheme(new List<Guid> { a, b, c }, 0.5);
            ShadingScheme shuffled = OverhangScheme(new List<Guid> { c, a, b }, 0.5);

            Assert.Equal(forward.SchemeGuid, shuffled.SchemeGuid);
        }

        [Fact]
        public void SchemeGuid_Changes_When_Any_Parameter_Aperture_Or_Name_Changes()
        {
            Guid a = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
            Guid b = new Guid("aaaaaaa1-0000-0000-0000-000000000002");
            Guid c = new Guid("aaaaaaa1-0000-0000-0000-000000000003");

            ShadingScheme baseline = OverhangScheme(new List<Guid> { a, b, c }, 0.5);
            ShadingScheme deeper = OverhangScheme(new List<Guid> { a, b, c }, 0.7);
            ShadingScheme differentName = OverhangScheme(new List<Guid> { a, b, c }, 0.5, "Overhang renamed");
            ShadingScheme differentScope = OverhangScheme(new List<Guid> { a, b, new Guid("aaaaaaa1-0000-0000-0000-000000000004") }, 0.5);

            Assert.NotEqual(baseline.SchemeGuid, deeper.SchemeGuid);
            Assert.NotEqual(baseline.SchemeGuid, differentName.SchemeGuid);
            Assert.NotEqual(baseline.SchemeGuid, differentScope.SchemeGuid);
        }

        [Fact]
        public void SchemeGuid_Covers_MountingOffset_Through_ParameterNames()
        {
            Guid a = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
            Guid b = new Guid("aaaaaaa1-0000-0000-0000-000000000002");
            Guid c = new Guid("aaaaaaa1-0000-0000-0000-000000000003");

            List<GroupedShadingDevice> devicesAtZero = new List<GroupedShadingDevice>
            {
                new GroupedShadingDevice(new Guid("ccccccc1-0000-0000-0000-000000000001"), new Guid("bbbbbbb1-0000-0000-0000-000000000001"), new List<Guid> { a, b, c }, new RetractableAwning(2.1, 15.0, 0.0, 0.15, 0.0, 0.0), AwningSpecification.Dakar),
            };
            List<GroupedShadingDevice> devicesAtHalf = new List<GroupedShadingDevice>
            {
                new GroupedShadingDevice(new Guid("ccccccc1-0000-0000-0000-000000000001"), new Guid("bbbbbbb1-0000-0000-0000-000000000001"), new List<Guid> { a, b, c }, new RetractableAwning(2.1, 15.0, 0.0, 0.15, 0.0, 0.5), AwningSpecification.Dakar),
            };

            ShadingScheme atZero = new ShadingScheme(
                "Grouped Dakar Retractable Awning", "RationaliseAwningGroup", new List<Guid> { a, b, c }, new List<Guid> { new Guid("bbbbbbb1-0000-0000-0000-000000000001") },
                new List<ShadingDevice>(), devicesAtZero, new List<ApertureShadingGroup>(), ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());
            ShadingScheme atHalf = new ShadingScheme(
                "Grouped Dakar Retractable Awning", "RationaliseAwningGroup", new List<Guid> { a, b, c }, new List<Guid> { new Guid("bbbbbbb1-0000-0000-0000-000000000001") },
                new List<ShadingDevice>(), devicesAtHalf, new List<ApertureShadingGroup>(), ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());

            // This guards the §5.5 claim that ParameterNames iteration picks MountingOffset up: if
            // the parameter were ever removed from the declared set, the identity would silently
            // stop covering the mounting condition and this test would fail.
            Assert.NotEqual(atZero.SchemeGuid, atHalf.SchemeGuid);
        }

        [Fact]
        public void SchemeGuid_Is_Stable_Across_Two_Builds()
        {
            Guid a = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
            Guid b = new Guid("aaaaaaa1-0000-0000-0000-000000000002");
            Guid c = new Guid("aaaaaaa1-0000-0000-0000-000000000003");

            Assert.Equal(
                OverhangScheme(new List<Guid> { a, b, c }, 0.5).SchemeGuid,
                OverhangScheme(new List<Guid> { a, b, c }, 0.5).SchemeGuid);
        }

        // ------------------------------------------------------------------------ I2 ----

        [Fact]
        public void Three_Identical_Overhangs_Produce_Three_Distinct_Element_Guids()
        {
            // P1 regression guard. The typology-level ElementGuid hashes family + parameters +
            // ordinal only, so these three devices would carry ONE element Guid each... and all
            // three would be THE SAME Guid. The scheme must re-identify them per placement.
            Guid a = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
            Guid b = new Guid("aaaaaaa1-0000-0000-0000-000000000002");
            Guid c = new Guid("aaaaaaa1-0000-0000-0000-000000000003");

            ShadingScheme scheme = OverhangScheme(new List<Guid> { a, b, c }, 0.5);
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget> { Target(1), Target(2), Target(3) };

            List<ShadingElement> elements = scheme.SchemeElements(targets);
            Assert.Equal(3, elements.Count);
            Assert.Equal(3, elements.Select(x => x.Guid).Distinct().Count());

            // And the raw typology GUIDs really do collide - the test only means something because
            // it fails on the un-wrapped identity.
            Overhang overhang = new Overhang(0.5);
            Guid raw = overhang.ShadingElements(targets[0])[0].Guid;
            Assert.Equal(raw, new Overhang(0.5).ShadingElements(targets[0])[0].Guid);
            Assert.Equal(raw, new Overhang(0.5).ShadingElements(targets[1])[0].Guid);

            // Element owners map each scheme element to its placement.
            Dictionary<Guid, Guid> owners = scheme.ElementOwners(targets);
            Assert.Equal(3, owners.Count);
            Assert.Contains(a, owners.Values);
            Assert.Contains(b, owners.Values);
            Assert.Contains(c, owners.Values);
        }

        [Fact]
        public void Mixed_Scheme_Assembles_Enumerates_And_RoundTrips()
        {
            Guid a = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
            Guid b = new Guid("aaaaaaa1-0000-0000-0000-000000000002");
            Guid c = new Guid("aaaaaaa1-0000-0000-0000-000000000003");

            ShadingScheme scheme = new ShadingScheme(
                "Mixed", "RationaliseShading", new List<Guid> { a, b, c }, new List<Guid> { new Guid("bbbbbbb1-0000-0000-0000-000000000001") },
                new List<ShadingDevice> { new ShadingDevice(a, new Overhang(0.5)), new ShadingDevice(b, new NoShading()) },
                new List<GroupedShadingDevice>
                {
                    new GroupedShadingDevice(new Guid("ccccccc1-0000-0000-0000-000000000001"), new Guid("bbbbbbb1-0000-0000-0000-000000000001"), new List<Guid> { a, b, c }, new RetractableAwning(2.1, 15.0, 0.0, 0.15), AwningSpecification.Dakar),
                },
                new List<ApertureShadingGroup>(), ShadingDesignStatus.Warning,
                new List<string> { "warning one" }, null, new List<string> { "diagnostic" });

            Assert.Equal(2, scheme.PhysicalDeviceCount); // overhang + awning; NoShading not counted
            Assert.Equal(new List<string> { "Overhang", "RetractableAwning" }, scheme.TypologyNames);
            Assert.Equal("Dakar", scheme.ProductPreset);

            // JSON round-trip.
            ShadingScheme roundTripped = new ShadingScheme(scheme.ToJsonObject());
            Assert.Equal(scheme.SchemeGuid, roundTripped.SchemeGuid);
            Assert.Equal(scheme.Name, roundTripped.Name);
            Assert.Equal(scheme.PhysicalDeviceCount, roundTripped.PhysicalDeviceCount);
            Assert.Equal(scheme.TypologyNames, roundTripped.TypologyNames);
            Assert.Equal(scheme.ApertureGuids, roundTripped.ApertureGuids);
            Assert.Equal(scheme.Warnings, roundTripped.Warnings);
            Assert.Equal(scheme.DesignDiagnostics, roundTripped.DesignDiagnostics);
            Assert.Equal(2, roundTripped.Devices.Count);
            Assert.Single(roundTripped.GroupedDevices);
        }

        [Fact]
        public void No_Shade_Scheme_Preserves_Scope_With_Zero_Devices()
        {
            Guid a = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
            Guid b = new Guid("aaaaaaa1-0000-0000-0000-000000000002");

            ShadingScheme scheme = new ShadingScheme(
                "No Shade", "Baseline", new List<Guid> { a, b }, new List<Guid>(),
                new List<ShadingDevice>(), new List<GroupedShadingDevice>(), new List<ApertureShadingGroup>(),
                ShadingDesignStatus.NoShading, new List<string>(), null, new List<string>());

            Assert.True(scheme.IsNoShade);
            Assert.Equal(0, scheme.PhysicalDeviceCount);
            Assert.Equal(2, scheme.ApertureGuids.Count);
            Assert.Empty(scheme.TypologyNames);
            Assert.Equal(ShadingDesignStatus.NoShading, scheme.Status);
        }

        [Fact]
        public void Grouped_Devices_Stay_Paired_With_Their_Own_Groups_Under_Reversed_Input_Order()
        {
            // Two grouped devices over two groups of DIFFERENT widths, supplied with the group list
            // in the opposite order to the device list. A device rebuilt against another device's
            // group frame produces the wrong canopy; the pairing must follow GroupGuid, never input
            // order.
            Guid a = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
            Guid b = new Guid("aaaaaaa1-0000-0000-0000-000000000002");
            Guid c = new Guid("aaaaaaa1-0000-0000-0000-000000000003");

            ApertureSolarTarget targetA = Target(1);
            ApertureSolarTarget targetB = Target(2);
            ApertureSolarTarget targetC = Target(3);
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget> { targetA, targetB, targetC };

            Guid wideGroupGuid = new Guid("ccccccc1-0000-0000-0000-000000000009"); // sorts LAST
            Guid narrowGroupGuid = new Guid("ccccccc1-0000-0000-0000-000000000001"); // sorts FIRST

            // The wide group spans A + B (width 2.0); the narrow group spans C alone (width 1.0).
            ApertureShadingGroup wideGroup = new ApertureShadingGroup(
                wideGroupGuid, new Guid("bbbbbbb1-0000-0000-0000-000000000001"),
                new List<ApertureSolarTarget> { targetA, targetB }, targetA.Plane, 0.0, 2.0, 1.0);
            ApertureShadingGroup narrowGroup = new ApertureShadingGroup(
                narrowGroupGuid, new Guid("bbbbbbb1-0000-0000-0000-000000000001"),
                new List<ApertureSolarTarget> { targetC }, targetC.Plane, 0.0, 1.0, 1.0);

            GroupedShadingDevice wideDevice = new GroupedShadingDevice(
                wideGroupGuid, new Guid("bbbbbbb1-0000-0000-0000-000000000001"), new List<Guid> { a, b }, new RetractableAwning(2.1, 15.0, 0.0, 0.15), AwningSpecification.Dakar);
            GroupedShadingDevice narrowDevice = new GroupedShadingDevice(
                narrowGroupGuid, new Guid("bbbbbbb1-0000-0000-0000-000000000001"), new List<Guid> { c }, new RetractableAwning(2.1, 15.0, 0.0, 0.15), AwningSpecification.Dakar);

            // Device list: wide first. Group list: REVERSED (narrow first). Pre-fix, the devices
            // were sorted by GroupGuid while the groups kept input order, pairing the wide device
            // with the narrow group's frame.
            ShadingScheme scheme = new ShadingScheme(
                "Two Awnings", "RationaliseAwningGroup", new List<Guid> { a, b, c }, new List<Guid> { new Guid("bbbbbbb1-0000-0000-0000-000000000001") },
                new List<ShadingDevice>(), new List<GroupedShadingDevice> { wideDevice, narrowDevice },
                new List<ApertureShadingGroup> { narrowGroup, wideGroup },
                ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());

            Assert.Equal(2, scheme.GroupedDevices.Count);
            Assert.Equal(2, scheme.Groups.Count);

            // Each device resolves to ITS OWN group, identified by GroupGuid.
            ApertureShadingGroup resolvedWide = scheme.Group(wideDevice);
            ApertureShadingGroup resolvedNarrow = scheme.Group(narrowDevice);
            Assert.Equal(wideGroupGuid, resolvedWide.GroupGuid);
            Assert.Equal(narrowGroupGuid, resolvedNarrow.GroupGuid);
            Assert.Equal(2.0, resolvedWide.Width, 9);
            Assert.Equal(1.0, resolvedNarrow.Width, 9);

            // The built geometry follows the pairing: the wide device's canopy spans the wide
            // group's extent, the narrow device's canopy the narrow group's extent.
            List<ShadingElement> elements = scheme.SchemeElements(targets);
            Assert.Equal(2, elements.Count);
            ShadingElement wideCanopy = elements.First(x => x.Area > 4.0);
            ShadingElement narrowCanopy = elements.First(x => x.Area <= 4.0);

            double wideExpected = (2.0 + 2.0 * 0.15) * (2.1 / Math.Cos(15.0 * Math.PI / 180.0));
            double narrowExpected = (1.0 + 2.0 * 0.15) * (2.1 / Math.Cos(15.0 * Math.PI / 180.0));
            Assert.Equal(wideExpected, wideCanopy.Area, 6);
            Assert.Equal(narrowExpected, narrowCanopy.Area, 6);

            // Element owners point at the right placements.
            Dictionary<Guid, Guid> owners = scheme.ElementOwners(targets);
            Assert.Contains(wideGroupGuid, owners.Values);
            Assert.Contains(narrowGroupGuid, owners.Values);
        }

        [Fact]
        public void Returned_Collections_Are_Copies()
        {
            Guid a = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
            ShadingScheme scheme = OverhangScheme(new List<Guid> { a }, 0.5);

            scheme.ApertureGuids.Clear();
            scheme.Devices.Clear();
            scheme.Warnings.Add("mutated");
            scheme.TypologyNames.Clear();

            Assert.Single(scheme.ApertureGuids);
            Assert.Single(scheme.Devices);
            Assert.Empty(scheme.Warnings);
            Assert.Equal(new List<string> { "Overhang" }, scheme.TypologyNames);
        }

        // -------------------------------------------------------------- JSON canonical ----

        [Fact]
        public void Reordering_Json_Arrays_Does_Not_Change_SchemeGuid()
        {
            Guid a = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
            Guid b = new Guid("aaaaaaa1-0000-0000-0000-000000000002");
            Guid c = new Guid("aaaaaaa1-0000-0000-0000-000000000003");

            ShadingScheme scheme = new ShadingScheme(
                "Mixed", "RationaliseShading", new List<Guid> { a, b, c }, new List<Guid> { new Guid("bbbbbbb1-0000-0000-0000-000000000001") },
                new List<ShadingDevice> { new ShadingDevice(a, new Overhang(0.5)), new ShadingDevice(b, new NoShading()) },
                new List<GroupedShadingDevice>
                {
                    new GroupedShadingDevice(new Guid("ccccccc1-0000-0000-0000-000000000001"), new Guid("bbbbbbb1-0000-0000-0000-000000000001"), new List<Guid> { a, b, c }, new RetractableAwning(2.1, 15.0, 0.0, 0.15), AwningSpecification.Dakar),
                },
                new List<ApertureShadingGroup>(), ShadingDesignStatus.Warning,
                new List<string> { "warning one" }, null, new List<string> { "diagnostic" });

            JsonObject json = scheme.ToJsonObject();
            ReverseJsonArray(json, "ApertureGuids");
            ReverseJsonArray(json, "PanelGuids");
            ReverseJsonArray(json, "Devices");
            ReverseJsonArray(json, "GroupedDevices");
            ReverseJsonArray(json, "Groups");

            ShadingScheme reordered = new ShadingScheme(json);
            Assert.Equal(scheme.SchemeGuid, reordered.SchemeGuid);
            Assert.Equal(scheme.ApertureGuids, reordered.ApertureGuids);
            Assert.Equal(scheme.TypologyNames, reordered.TypologyNames);
            Assert.Equal(scheme.PhysicalDeviceCount, reordered.PhysicalDeviceCount);
        }

        [Fact]
        public void Grouped_Device_Group_Alignment_Survives_Json_Reordering()
        {
            // Two grouped devices over two groups of DIFFERENT widths, with the JSON arrays reversed
            // after serialisation. The pairing must follow GroupGuid, never the array order, so a
            // device can never be rebuilt against another device's frame.
            Guid a = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
            Guid b = new Guid("aaaaaaa1-0000-0000-0000-000000000002");
            Guid c = new Guid("aaaaaaa1-0000-0000-0000-000000000003");

            ApertureSolarTarget targetA = Target(1);
            ApertureSolarTarget targetB = Target(2);
            ApertureSolarTarget targetC = Target(3);
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget> { targetA, targetB, targetC };

            Guid wideGroupGuid = new Guid("ccccccc1-0000-0000-0000-000000000009");
            Guid narrowGroupGuid = new Guid("ccccccc1-0000-0000-0000-000000000001");

            ApertureShadingGroup wideGroup = new ApertureShadingGroup(
                wideGroupGuid, new Guid("bbbbbbb1-0000-0000-0000-000000000001"),
                new List<ApertureSolarTarget> { targetA, targetB }, targetA.Plane, 0.0, 2.0, 1.0);
            ApertureShadingGroup narrowGroup = new ApertureShadingGroup(
                narrowGroupGuid, new Guid("bbbbbbb1-0000-0000-0000-000000000001"),
                new List<ApertureSolarTarget> { targetC }, targetC.Plane, 0.0, 1.0, 1.0);

            GroupedShadingDevice wideDevice = new GroupedShadingDevice(
                wideGroupGuid, new Guid("bbbbbbb1-0000-0000-0000-000000000001"), new List<Guid> { a, b }, new RetractableAwning(2.1, 15.0, 0.0, 0.15), AwningSpecification.Dakar);
            GroupedShadingDevice narrowDevice = new GroupedShadingDevice(
                narrowGroupGuid, new Guid("bbbbbbb1-0000-0000-0000-000000000001"), new List<Guid> { c }, new RetractableAwning(2.1, 15.0, 0.0, 0.15), AwningSpecification.Dakar);

            ShadingScheme scheme = new ShadingScheme(
                "Two Awnings", "RationaliseAwningGroup", new List<Guid> { a, b, c }, new List<Guid> { new Guid("bbbbbbb1-0000-0000-0000-000000000001") },
                new List<ShadingDevice>(), new List<GroupedShadingDevice> { wideDevice, narrowDevice },
                new List<ApertureShadingGroup> { narrowGroup, wideGroup },
                ShadingDesignStatus.Ok, new List<string>(), null, new List<string>());

            JsonObject json = scheme.ToJsonObject();
            ReverseJsonArray(json, "GroupedDevices");
            ReverseJsonArray(json, "Groups");

            ShadingScheme reordered = new ShadingScheme(json);
            Assert.Equal(scheme.SchemeGuid, reordered.SchemeGuid);

            ApertureShadingGroup resolvedWide = reordered.Group(reordered.GroupedDevices.First(x => x.GroupGuid == wideGroupGuid));
            ApertureShadingGroup resolvedNarrow = reordered.Group(reordered.GroupedDevices.First(x => x.GroupGuid == narrowGroupGuid));
            Assert.Equal(wideGroupGuid, resolvedWide.GroupGuid);
            Assert.Equal(narrowGroupGuid, resolvedNarrow.GroupGuid);
            Assert.Equal(2.0, resolvedWide.Width, 9);
            Assert.Equal(1.0, resolvedNarrow.Width, 9);

            List<ShadingElement> elements = reordered.SchemeElements(targets);
            Assert.Equal(2, elements.Count);
            Assert.True(elements.Any(x => x.Area > 4.0));
            Assert.True(elements.Any(x => x.Area <= 4.0));
        }

        [Fact]
        public void Malformed_Device_Entry_Is_Refused_Without_Null_Reference_Exception()
        {
            Guid a = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
            ShadingScheme scheme = OverhangScheme(new List<Guid> { a }, 0.5);

            JsonObject json = scheme.ToJsonObject();
            JsonArray devices = json["Devices"] as JsonArray;
            devices[0] = new JsonObject { ["_type"] = "Not.A.ShadingDevice" };

            // A malformed declared device must fail the read explicitly, never leave a null entry in
            // a collection that assumes a valid device (which ComputeSchemeGuid would dereference).
            ShadingScheme target = new ShadingScheme(new JsonObject());
            Assert.False(target.FromJsonObject(json));
        }

        [Fact]
        public void Corrupted_Json_Through_Standard_Loader_Is_Inert()
        {
            // The real production loading path (Core.Create.IJSAMObject<T>) constructs via the
            // ShadingScheme(JsonObject) constructor, which cannot propagate FromJsonObject's bool.
            // A failed reconstruction must therefore leave the object completely inert rather than a
            // plausible "Overhang" with no devices and an empty identity.
            Guid a = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
            ShadingScheme scheme = OverhangScheme(new List<Guid> { a }, 0.5);

            JsonObject json = scheme.ToJsonObject();
            ((JsonArray)json["Devices"])[0] = new JsonObject { ["_type"] = "Not.A.ShadingDevice" };

            ShadingScheme loaded = SAM.Core.Create.IJSAMObject<ShadingScheme>(json);

            Assert.NotNull(loaded);
            Assert.Equal(Guid.Empty, loaded.SchemeGuid);
            Assert.Null(loaded.Name);
            Assert.Empty(loaded.Devices);
            Assert.Empty(loaded.GroupedDevices);
            Assert.Empty(loaded.Groups);
            Assert.Empty(loaded.ApertureGuids);
            Assert.Empty(loaded.PanelGuids);
        }

        [Fact]
        public void Failed_ReRead_Clears_Stale_Identity()
        {
            Guid a = new Guid("aaaaaaa1-0000-0000-0000-000000000001");
            ShadingScheme scheme = OverhangScheme(new List<Guid> { a }, 0.5);

            Guid originalGuid = scheme.SchemeGuid;
            Assert.NotEqual(Guid.Empty, originalGuid);

            JsonObject json = scheme.ToJsonObject();
            ((JsonArray)json["Devices"])[0] = new JsonObject { ["_type"] = "Not.A.ShadingDevice" };

            bool result = scheme.FromJsonObject(json);

            Assert.False(result);
            Assert.Equal(Guid.Empty, scheme.SchemeGuid);
            Assert.NotEqual(originalGuid, scheme.SchemeGuid);
            Assert.Null(scheme.Name);
            Assert.Empty(scheme.Devices);
            Assert.Empty(scheme.GroupedDevices);
            Assert.Empty(scheme.Groups);
            Assert.Empty(scheme.ApertureGuids);
            Assert.Empty(scheme.PanelGuids);
        }

        private static void ReverseJsonArray(JsonObject json, string name)
        {
            if (!(json[name] is JsonArray array))
            {
                return;
            }

            List<JsonNode> items = new List<JsonNode>();
            foreach (JsonNode node in array)
            {
                items.Add(node?.DeepClone());
            }
            items.Reverse();

            JsonArray reversed = new JsonArray();
            foreach (JsonNode node in items)
            {
                reversed.Add(node);
            }

            json[name] = reversed;
        }
    }
}
