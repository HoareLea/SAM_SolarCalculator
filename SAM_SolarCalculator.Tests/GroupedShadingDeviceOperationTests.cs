// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.Spatial;
using SAM.Weather;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The Grasshopper hand-off SAMAnalytical.RationaliseAwningGroup.groupedShadingDevices →
    /// SAMAnalytical.ShadingOperation._shadingDevice, tested where the behaviour actually lives:
    /// the Create.GroupedShadingOperationProfile overload that takes a GroupedShadingDevice.
    ///
    /// THE CONTRACT UNDER TEST. The device defines the physical group — the member aperture scope,
    /// the left-to-right member order and the deterministic group identity all come from the
    /// device, never from whatever targets happen to be wired. The wired targets must BE the
    /// member group (matched by aperture Guid, in any order); the group frame is re-established
    /// through the one deterministic grouping algorithm and its Guid must equal the device's
    /// GroupGuid; and one control profile per member is enforced by the existing agreement gate.
    /// Anything else is refused with the differing Guids named — never silently re-grouped,
    /// averaged or measured against a different scope.
    ///
    /// Everything runs against the committed Kołobrzeg fixture (three adjacent apertures on one
    /// WSW wall) through the same entry points the components call, and the expensive solar
    /// context is built once and shared by every test in the class.
    /// </summary>
    public class GroupedShadingDeviceOperationTests
    {
        private readonly ITestOutputHelper output;

        public GroupedShadingDeviceOperationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const double Extension = 0.15;

        private static readonly object padlock = new object();
        private static ApertureSolarContext context;

        private static List<Guid> StudiedGuids()
        {
            return new List<Guid>
            {
                KolobrzegFixture.TallApertureGuid,
                KolobrzegFixture.MidApertureGuid,
                KolobrzegFixture.SmallApertureGuid,
            };
        }

        /// <summary>The shared solar context over the three studied apertures, built once.</summary>
        private static ApertureSolarContext Context()
        {
            lock (padlock)
            {
                if (context != null)
                {
                    return context;
                }

                AnalyticalModel analyticalModel = KolobrzegFixture.Model();
                int year = KolobrzegFixture.Year(analyticalModel);
                WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);

                context = Analytical.SolarCalculator.Create.ApertureSolarContext(
                    analyticalModel, year, weatherData, StudiedGuids(), KolobrzegFixture.HistoricalGridSize, 2.0);

                Assert.NotNull(context);
                return context;
            }
        }

        /// <summary>The targets exactly as a Grasshopper wire would carry them: fresh objects out of ApertureSolarTargets, distinct from the context's own.</summary>
        private static List<ApertureSolarTarget> WiredTargets(IEnumerable<Guid> guids)
        {
            return KolobrzegFixture.Model().ApertureSolarTargets(guids, KolobrzegFixture.HistoricalGridSize);
        }

        /// <summary>The deterministic three-member group, re-established from the context's own targets.</summary>
        private static ApertureShadingGroup Group(ApertureSolarContext solarContext)
        {
            return solarContext.Targets.ApertureShadingGroups(AwningSpecification.Dakar, Extension).Single();
        }

        private static RetractableAwning Awning()
        {
            return new RetractableAwning(1.6, 28.0, 0.0, Extension, 0.21);
        }

        /// <summary>The device, built exactly as Optimise.RetractableAwningGroup builds it: the group's identity, the awning and the product preset.</summary>
        private static GroupedShadingDevice Device(ApertureShadingGroup group)
        {
            return new GroupedShadingDevice(group.GroupGuid, group.PanelGuid, group.ApertureGuids, Awning(), AwningSpecification.Dakar);
        }

        private static List<SolarControlProfile> Profiles(IEnumerable<ApertureSolarTarget> targets, WeatherData weatherData, int year)
        {
            List<SolarControlProfile> result = new List<SolarControlProfile>();
            foreach (ApertureSolarTarget target in targets)
            {
                SolarControlProfile profile = target.SolarControlProfile(weatherData, new SolarControlSettings(200.0), year);
                Assert.NotNull(profile);
                result.Add(profile);
            }

            return result;
        }

        // ------------------------------------------------------------ the valid hand-off ----

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Grouped_Device_Handoff_Succeeds_For_The_Correct_Aperture_Group()
        {
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            ApertureSolarContext solarContext = Context();

            List<ApertureSolarTarget> wired = WiredTargets(StudiedGuids());
            GroupedShadingDevice device = Device(Group(solarContext));
            List<SolarControlProfile> profiles = Profiles(wired, weatherData, year);

            GroupedShadingOperationProfile operation = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                device, wired, profiles, solarContext, out string message);

            Assert.NotNull(operation);
            Assert.Null(message);

            // The physical-device identity is preserved: the result answers for the group the
            // device was designed for, on its host panel.
            Assert.Equal(device.GroupGuid, operation.GroupGuid);
            Assert.Equal(device.PanelGuid, operation.PanelGuid);

            // One physical device over three members: every member keeps its own diagnostic,
            // attributable to its aperture.
            Assert.Equal(3, operation.Members.Count);
            foreach (SolarControlProfile profile in profiles)
            {
                ShadingOperationProfile member = operation.Member(profile.ApertureGuid);
                Assert.NotNull(member);
                Assert.Equal(profile.ShadeOnHoursOfYear, member.DeployedHoursOfYear);
            }

            // The headline describes the shared physical device: the union of the member
            // schedules, never one member's. No wind limit here, so nothing is retracted.
            List<int> demand = new List<int>();
            foreach (SolarControlProfile profile in profiles)
            {
                demand.AddRange(profile.ShadeDemandHoursOfYear);
            }

            Assert.Equal(new HashSet<int>(demand), new HashSet<int>(operation.DeviceDemandHoursOfYear));
            Assert.Equal(operation.DeviceDemandHoursOfYear, operation.DeviceDeployedHoursOfYear);
            Assert.Empty(operation.DeviceWindRetractedHoursOfYear);
            Assert.Equal(operation.DeviceDemandHours, operation.DeviceDeployedHours + operation.DeviceWindRetractedHours);

            output.WriteLine(operation.ToString());
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Grouped_Device_Handoff_Does_Not_Care_About_Member_Order()
        {
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            ApertureSolarContext solarContext = Context();

            List<ApertureSolarTarget> wired = WiredTargets(StudiedGuids());
            GroupedShadingDevice device = Device(Group(solarContext));

            List<SolarControlProfile> inOrder = Profiles(wired, weatherData, year);

            // The SAME members and profiles, both lists reversed: matching is by aperture Guid,
            // never by position.
            List<ApertureSolarTarget> reversedTargets = new List<ApertureSolarTarget>(wired);
            reversedTargets.Reverse();
            List<SolarControlProfile> reversedProfiles = new List<SolarControlProfile>(inOrder);
            reversedProfiles.Reverse();

            GroupedShadingOperationProfile a = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                device, wired, inOrder, solarContext, out string messageA);
            GroupedShadingOperationProfile b = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                device, reversedTargets, reversedProfiles, solarContext, out string messageB);

            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Null(messageA);
            Assert.Null(messageB);

            Assert.Equal(a.DeviceDemandHoursOfYear, b.DeviceDemandHoursOfYear);
            Assert.Equal(a.DeviceDeployedHoursOfYear, b.DeviceDeployedHoursOfYear);
            Assert.Equal(a.DeviceWindRetractedHoursOfYear, b.DeviceWindRetractedHoursOfYear);
            Assert.Equal(a.CanopyEffectiveHoursOfYear, b.CanopyEffectiveHoursOfYear);
            Assert.Equal(a.ValanceEffectiveHoursOfYear, b.ValanceEffectiveHoursOfYear);
            Assert.Equal(a.ControlledUnwantedSolarIntercepted, b.ControlledUnwantedSolarIntercepted, 12);
            Assert.Equal(a.ControlledDirectSolarIntercepted, b.ControlledDirectSolarIntercepted, 12);
            Assert.Equal(a.CanopyAttributedEnergy, b.CanopyAttributedEnergy, 12);
            Assert.Equal(a.ValanceAttributedEnergy, b.ValanceAttributedEnergy, 12);
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Grouped_Device_Handoff_Matches_The_Explicit_Group_Path()
        {
            // The regression anchor: the device hand-off must produce the SAME deployment and
            // energy figures as the pre-existing valid path — the explicit ApertureShadingGroup
            // overload the multi-target component branch has always used.
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            ApertureSolarContext solarContext = Context();

            List<ApertureSolarTarget> wired = WiredTargets(StudiedGuids());
            ApertureShadingGroup group = Group(solarContext);
            RetractableAwning awning = Awning();
            GroupedShadingDevice device = Device(group);
            List<SolarControlProfile> profiles = Profiles(wired, weatherData, year);

            GroupedShadingOperationProfile viaDevice = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                device, wired, profiles, solarContext, out string message);

            List<int> offsets = new List<int>();
            foreach (Guid guid in group.ApertureGuids)
            {
                offsets.Add(solarContext.CellIndexOffset(guid));
            }

            GroupedShadingOperationProfile viaGroup = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                group, profiles, solarContext.SolarVisibilityCache, solarContext.ContextOccluders,
                awning, weatherData, out string groupMessage, offsets);

            Assert.NotNull(viaDevice);
            Assert.NotNull(viaGroup);
            Assert.Null(message);
            Assert.Null(groupMessage);
            Assert.Equal(viaGroup.GroupGuid, viaDevice.GroupGuid);

            Assert.Equal(viaGroup.DeviceDemandHoursOfYear, viaDevice.DeviceDemandHoursOfYear);
            Assert.Equal(viaGroup.DeviceDeployedHoursOfYear, viaDevice.DeviceDeployedHoursOfYear);
            Assert.Equal(viaGroup.DeviceWindRetractedHoursOfYear, viaDevice.DeviceWindRetractedHoursOfYear);
            Assert.Equal(viaGroup.CanopyEffectiveHoursOfYear, viaDevice.CanopyEffectiveHoursOfYear);
            Assert.Equal(viaGroup.ValanceEffectiveHoursOfYear, viaDevice.ValanceEffectiveHoursOfYear);

            Assert.Equal(viaGroup.ControlledUnwantedSolarIntercepted, viaDevice.ControlledUnwantedSolarIntercepted, 9);
            Assert.Equal(viaGroup.ControlledDirectSolarIntercepted, viaDevice.ControlledDirectSolarIntercepted, 9);
            Assert.Equal(viaGroup.UncontrolledUnwantedSolarIntercepted, viaDevice.UncontrolledUnwantedSolarIntercepted, 9);
            Assert.Equal(viaGroup.CanopyAttributedEnergy, viaDevice.CanopyAttributedEnergy, 9);
            Assert.Equal(viaGroup.ValanceAttributedEnergy, viaDevice.ValanceAttributedEnergy, 9);

            Assert.Equal(viaGroup.Members.Count, viaDevice.Members.Count);
            foreach (ShadingOperationProfile member in viaGroup.Members)
            {
                ShadingOperationProfile byDevice = viaDevice.Member(member.ApertureGuid);
                Assert.NotNull(byDevice);
                Assert.Equal(member.DeployedHoursOfYear, byDevice.DeployedHoursOfYear);
                Assert.Equal(member.WindRetractedHoursOfYear, byDevice.WindRetractedHoursOfYear);
                Assert.Equal(member.ControlledUnwantedSolarIntercepted, byDevice.ControlledUnwantedSolarIntercepted, 9);
            }
        }

        // ----------------------------------------------------------------- the refusals ----

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Grouped_Device_Handoff_Refuses_A_Missing_Member_And_A_Duplicate()
        {
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            ApertureSolarContext solarContext = Context();

            GroupedShadingDevice device = Device(Group(solarContext));

            // MISSING: two of the three members wired. The refusal names the missing member.
            List<ApertureSolarTarget> twoMembers = WiredTargets(new List<Guid> { KolobrzegFixture.TallApertureGuid, KolobrzegFixture.MidApertureGuid });
            List<SolarControlProfile> twoProfiles = Profiles(twoMembers, weatherData, year);

            GroupedShadingOperationProfile refused = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                device, twoMembers, twoProfiles, solarContext, out string message);

            Assert.Null(refused);
            Assert.Contains(KolobrzegFixture.SmallApertureGuid.ToString(), message);
            output.WriteLine($"missing member: {message}");

            // DUPLICATE: the right members plus one member twice. The device measures each window
            // exactly once, so a duplicated wire is refused, not double-counted.
            List<ApertureSolarTarget> duplicated = WiredTargets(StudiedGuids());
            duplicated.Add(duplicated[0]);
            List<SolarControlProfile> profiles = Profiles(WiredTargets(StudiedGuids()), weatherData, year);

            refused = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                device, duplicated, profiles, solarContext, out message);

            Assert.Null(refused);
            Assert.Contains("twice", message);
            output.WriteLine($"duplicate: {message}");
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Grouped_Device_Handoff_Refuses_A_Device_That_Lists_A_Member_Twice()
        {
            // The device defines the scope, so a malformed device — one that lists the same
            // member aperture twice — is refused BEFORE the set-based scope gate, which a
            // duplicated member would otherwise slip past to be double-counted downstream.
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            ApertureSolarContext solarContext = Context();

            ApertureShadingGroup group = Group(solarContext);
            List<Guid> duplicatedMembers = new List<Guid>(group.ApertureGuids) { group.ApertureGuids[0] };
            GroupedShadingDevice malformed = new GroupedShadingDevice(group.GroupGuid, group.PanelGuid, duplicatedMembers, Awning(), AwningSpecification.Dakar);

            List<ApertureSolarTarget> wired = WiredTargets(StudiedGuids());
            List<SolarControlProfile> profiles = Profiles(wired, weatherData, year);

            GroupedShadingOperationProfile refused = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                malformed, wired, profiles, solarContext, out string message);

            Assert.Null(refused);
            Assert.Contains("twice", message);
            Assert.Contains(group.ApertureGuids[0].ToString(), message);
            output.WriteLine($"duplicate member on the device: {message}");
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Grouped_Device_Handoff_Refuses_An_Additional_Or_Unrelated_Aperture()
        {
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            ApertureSolarContext solarContext = Context();

            List<ApertureSolarTarget> wired = WiredTargets(StudiedGuids());
            GroupedShadingDevice device = Device(Group(solarContext));
            List<SolarControlProfile> profiles = Profiles(wired, weatherData, year);

            // An aperture from a DIFFERENT model — the unrelated window a user adds when the
            // device selection and the target selection drift apart.
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModelB-NoShadeSolarSimulation.sam");
            AnalyticalModel otherModel = SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);
            Assert.NotNull(otherModel);
            ApertureSolarTarget outsider = otherModel.ApertureSolarTargets(null, KolobrzegFixture.HistoricalGridSize).First();

            List<ApertureSolarTarget> withOutsider = new List<ApertureSolarTarget>(wired) { outsider };

            GroupedShadingOperationProfile refused = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                device, withOutsider, profiles, solarContext, out string message);

            Assert.Null(refused);
            Assert.Contains(outsider.ApertureGuid.ToString(), message);
            Assert.Contains("not in the device", message);
            output.WriteLine($"additional aperture: {message}");
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Grouped_Device_Handoff_Refuses_A_Missing_Or_Mismatched_Control_Profile()
        {
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            ApertureSolarContext solarContext = Context();

            List<ApertureSolarTarget> wired = WiredTargets(StudiedGuids());
            GroupedShadingDevice device = Device(Group(solarContext));
            List<SolarControlProfile> profiles = Profiles(wired, weatherData, year);

            // MISSING: one profile short. The agreement gate's count check refuses.
            GroupedShadingOperationProfile refused = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                device, wired, profiles.GetRange(0, 2), solarContext, out string message);

            Assert.Null(refused);
            Assert.Contains("one control profile per member aperture", message);
            output.WriteLine($"missing profile: {message}");

            // MISMATCHED: a profile built for an aperture outside the group. The membership check
            // refuses — a schedule from outside the device is never merged.
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ModelB-NoShadeSolarSimulation.sam");
            AnalyticalModel otherModel = SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);
            Assert.NotNull(otherModel);
            ApertureSolarTarget outsider = otherModel.ApertureSolarTargets(null, KolobrzegFixture.HistoricalGridSize).First();
            SolarControlProfile outsiderProfile = outsider.SolarControlProfile(weatherData, new SolarControlSettings(200.0), year);
            Assert.NotNull(outsiderProfile);

            List<SolarControlProfile> mismatched = new List<SolarControlProfile>(profiles);
            mismatched[2] = outsiderProfile;

            refused = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                device, wired, mismatched, solarContext, out message);

            Assert.Null(refused);
            Assert.Contains("does not belong to any member aperture", message);
            output.WriteLine($"mismatched profile: {message}");
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Grouped_Device_Handoff_Never_Rebuilds_A_Different_Group()
        {
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            ApertureSolarContext solarContext = Context();

            List<ApertureSolarTarget> wired = WiredTargets(StudiedGuids());
            ApertureShadingGroup group = Group(solarContext);
            List<SolarControlProfile> profiles = Profiles(wired, weatherData, year);

            // A device whose claimed identity does not match the group its members re-establish:
            // right apertures, wrong group Guid. Refused — the physical device is never silently
            // re-attributed to a different group.
            GroupedShadingDevice impostor = new GroupedShadingDevice(Guid.NewGuid(), group.PanelGuid, group.ApertureGuids, Awning(), AwningSpecification.Dakar);

            GroupedShadingOperationProfile refused = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                impostor, wired, profiles, solarContext, out string message);

            Assert.Null(refused);
            Assert.Contains("not the group this device was designed for", message);
            output.WriteLine($"identity mismatch: {message}");
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Grouped_Device_Handoff_For_A_Subgroup_Measures_Exactly_That_Subgroup()
        {
            // The device defines the scope — not the wall, and not the wired selection the grouping
            // algorithm would make of it. A device designed for two of the three studied apertures
            // measures those two, even though all three could share one awning.
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            ApertureSolarContext solarContext = Context();

            List<Guid> subgroupGuids = new List<Guid> { KolobrzegFixture.TallApertureGuid, KolobrzegFixture.MidApertureGuid };
            List<ApertureSolarTarget> subgroupTargets = subgroupGuids.ConvertAll(x => solarContext.Target(x));
            ApertureShadingGroup subgroup = subgroupTargets.ApertureShadingGroups(AwningSpecification.Dakar, Extension).Single();

            GroupedShadingDevice device = new GroupedShadingDevice(subgroup.GroupGuid, subgroup.PanelGuid, subgroup.ApertureGuids, Awning(), AwningSpecification.Dakar);

            List<ApertureSolarTarget> wired = WiredTargets(subgroupGuids);
            List<SolarControlProfile> profiles = Profiles(wired, weatherData, year);

            GroupedShadingOperationProfile operation = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                device, wired, profiles, solarContext, out string message);

            Assert.NotNull(operation);
            Assert.Null(message);
            Assert.Equal(device.GroupGuid, operation.GroupGuid);
            Assert.Equal(2, operation.Members.Count);
            Assert.NotNull(operation.Member(KolobrzegFixture.TallApertureGuid));
            Assert.NotNull(operation.Member(KolobrzegFixture.MidApertureGuid));
            Assert.Null(operation.Member(KolobrzegFixture.SmallApertureGuid));

            output.WriteLine(operation.ToString());
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Grouped_Device_With_No_Shading_Typology_Is_Refused_As_Before()
        {
            // The null device means "build nothing over this group". The grouped operation path
            // has always supported RetractableAwning only, and the hand-off keeps that boundary:
            // the refusal comes from the same gate the explicit group path uses.
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            ApertureSolarContext solarContext = Context();

            List<ApertureSolarTarget> wired = WiredTargets(StudiedGuids());
            ApertureShadingGroup group = Group(solarContext);
            List<SolarControlProfile> profiles = Profiles(wired, weatherData, year);

            GroupedShadingDevice noShade = new GroupedShadingDevice(group.GroupGuid, group.PanelGuid, group.ApertureGuids, new NoShading(), AwningSpecification.Dakar);

            GroupedShadingOperationProfile refused = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                noShade, wired, profiles, solarContext, out string message);

            Assert.Null(refused);
            Assert.Contains("RetractableAwning", message);
            output.WriteLine($"null device: {message}");
        }

        // -------------------------------------- the recorded grouping criteria ----

        private const int SyntheticYear = 2018;
        private const double SyntheticShift = 30.0;
        private static readonly Guid SyntheticPanelGuid = new Guid("dddd4444-0000-0000-0000-000000000001");

        /// <summary>A south-facing synthetic aperture on the shared synthetic panel, heads aligned.</summary>
        private static ApertureSolarTarget SyntheticAperture(Guid apertureGuid, double x0, double width)
        {
            Face3D face3D = new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(x0, 0, 1.0),
                new Point3D(x0 + width, 0, 1.0),
                new Point3D(x0 + width, 0, 2.4),
                new Point3D(x0, 0, 2.4),
            }));

            return new ApertureSolarTarget(apertureGuid, SyntheticPanelGuid, face3D, Geometry.SolarCalculator.Query.AnalysisCells(face3D, 0.5));
        }

        [Fact]
        public void Grouping_Criteria_Survive_Copy_And_Json_Round_Trip()
        {
            GroupedShadingDevice device = new GroupedShadingDevice(
                new Guid("aaaa1111-0000-0000-0000-000000000001"), SyntheticPanelGuid,
                new List<Guid> { Guid.NewGuid(), Guid.NewGuid() },
                new RetractableAwning(2.1, 15.0, 0.0, Extension), AwningSpecification.Dakar,
                0.35, 0.005);

            Assert.Equal(0.35, device.MaximumGap, 12);
            Assert.Equal(0.005, device.HeadTolerance, 12);

            GroupedShadingDevice copy = new GroupedShadingDevice(device);
            Assert.Equal(0.35, copy.MaximumGap, 12);
            Assert.Equal(0.005, copy.HeadTolerance, 12);

            // The Grasshopper save/reload path: SAM JSON out and back, through the typed reader.
            GroupedShadingDevice restored = Core.Create.IJSAMObject<GroupedShadingDevice>(device.ToJsonObject().ToJsonString());
            Assert.NotNull(restored);
            Assert.Equal(device.GroupGuid, restored.GroupGuid);
            Assert.Equal(0.35, restored.MaximumGap, 12);
            Assert.Equal(0.005, restored.HeadTolerance, 12);
        }

        [Fact]
        public void A_Device_Without_Recorded_Criteria_Claims_The_Algorithm_Defaults()
        {
            // The five-parameter constructor — every caller written before the criteria were
            // carried — claims the ApertureShadingGroups defaults.
            GroupedShadingDevice legacy = new GroupedShadingDevice(
                new Guid("aaaa1111-0000-0000-0000-000000000002"), SyntheticPanelGuid,
                new List<Guid> { Guid.NewGuid() }, new NoShading(), AwningSpecification.Dakar);

            Assert.Equal(0.20, legacy.MaximumGap, 12);
            Assert.Equal(0.02, legacy.HeadTolerance, 12);

            // A pre-criteria FILE: JSON without the two keys deserialises to the same defaults,
            // so an old save behaves exactly as it did before the criteria were recorded.
            JsonObject jObject = legacy.ToJsonObject();
            Assert.True(jObject.Remove("MaximumGap"));
            Assert.True(jObject.Remove("HeadTolerance"));

            GroupedShadingDevice fromOldFile = new GroupedShadingDevice(jObject);
            Assert.Equal(0.20, fromOldFile.MaximumGap, 12);
            Assert.Equal(0.02, fromOldFile.HeadTolerance, 12);
        }

        [Fact]
        public void The_Handoff_Reestablishes_The_Group_Under_The_Devices_Own_Criteria()
        {
            // WHITE-BOX: the synthetic solar context is assembled through the internal
            // ApertureSolarContext constructor (visible to this assembly), because the behaviour
            // under test — the criteria the re-establishment runs under — must be observable
            // end-to-end and no committed fixture has apertures gapped between 0.20 and 0.35 m.
            //
            // Three adjacent apertures with 0.30 m gaps: under the 0.20 m default they split into
            // three one-member groups; under the 0.35 m criterion the device records they form ONE
            // group. The same members, the same identity — only the recorded criterion differs.
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>
            {
                SyntheticAperture(new Guid("ffff6666-0000-0000-0000-000000000001"), 0.0, 0.9),
                SyntheticAperture(new Guid("ffff6666-0000-0000-0000-000000000002"), 1.2, 0.9),
                SyntheticAperture(new Guid("ffff6666-0000-0000-0000-000000000003"), 2.4, 0.6),
            };

            // Sanity: the criteria really decide the grouping here.
            Assert.Equal(3, targets.ApertureShadingGroups(AwningSpecification.Dakar, Extension).Count);
            ApertureShadingGroup group = targets.ApertureShadingGroups(AwningSpecification.Dakar, Extension, 0.35, 0.02).Single();

            Core.Location location = TestHelpers.London();
            WeatherData weatherData = TestHelpers.SolarSymmetricWeatherData(SyntheticYear, location, SyntheticShift);

            List<Geometry.SolarCalculator.AnalysisCell> cells = new List<Geometry.SolarCalculator.AnalysisCell>();
            foreach (ApertureSolarTarget target in targets)
            {
                cells.AddRange(target.AnalysisCells);
            }

            List<LinkedFace3D> contextOccluders = new List<LinkedFace3D>();
            Weather.SolarCalculator.SolarVisibilityCache cache = Weather.SolarCalculator.Create.SolarVisibilityCache(
                location, SyntheticYear, 2.0, contextOccluders, cells,
                cellSize: 0.5, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: SyntheticShift);

            ApertureSolarContext syntheticContext = new ApertureSolarContext(
                targets, cells, null, contextOccluders, cache, null, weatherData, location,
                SyntheticYear, 0.5, 2.0, SyntheticShift, null, null, false);

            List<SolarControlProfile> profiles = new List<SolarControlProfile>();
            foreach (ApertureSolarTarget target in targets)
            {
                SolarControlProfile profile = target.SolarControlProfile(weatherData, new SolarControlSettings(200.0), SyntheticYear);
                Assert.NotNull(profile);
                profiles.Add(profile);
            }

            RetractableAwning awning = new RetractableAwning(2.6, 15.0, 0.0, Extension, 0.0);

            // The device that RECORDS the 0.35 m criterion: the hand-off re-establishes exactly
            // its group and measures it.
            GroupedShadingDevice recorded = new GroupedShadingDevice(
                group.GroupGuid, group.PanelGuid, group.ApertureGuids, awning, AwningSpecification.Dakar, 0.35, 0.02);

            GroupedShadingOperationProfile operation = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                recorded, targets, profiles, syntheticContext, out string message);

            Assert.NotNull(operation);
            Assert.Null(message);
            Assert.Equal(recorded.GroupGuid, operation.GroupGuid);
            Assert.Equal(3, operation.Members.Count);

            output.WriteLine(operation.ToString());

            // The SAME device identity without the recorded criterion (the legacy constructor,
            // claiming the 0.20 m default): the members split, so the group cannot be
            // re-established and the hand-off refuses rather than measure a different grouping.
            GroupedShadingDevice legacy = new GroupedShadingDevice(
                group.GroupGuid, group.PanelGuid, group.ApertureGuids, awning, AwningSpecification.Dakar);

            GroupedShadingOperationProfile refused = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                legacy, targets, profiles, syntheticContext, out message);

            Assert.Null(refused);
            Assert.Contains("do not form ONE group", message);
            output.WriteLine($"legacy criteria: {message}");
        }

        [Fact]
        public void NaN_Grouping_Criteria_Are_Normalised_Before_Grouping()
        {
            // Review pin: NaN is not a criterion. Every ">" comparison against it is false, so
            // grouping under NaN would NEVER split on that criterion — while GroupedShadingDevice
            // records the DEFAULT for a NaN, and the hand-off would then refuse a device the
            // algorithm itself produced. The algorithm normalises NaN to the same constants the
            // device records, before a single comparison runs, so the criterion used and the
            // criterion carried are identical by construction.
            List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>
            {
                SyntheticAperture(new Guid("ffff6666-0000-0000-0000-000000000011"), 0.0, 0.9),
                SyntheticAperture(new Guid("ffff6666-0000-0000-0000-000000000012"), 1.2, 0.9),
                SyntheticAperture(new Guid("ffff6666-0000-0000-0000-000000000013"), 2.4, 0.6),
            };

            // 0.30 m gaps: ONE group only above the 0.20 m default. NaN must read as the
            // default — three one-member groups, identical in identity to the explicit default,
            // not one merged group as raw NaN comparisons would produce.
            List<ApertureShadingGroup> underDefault = targets.ApertureShadingGroups(AwningSpecification.Dakar, Extension);
            List<ApertureShadingGroup> underNaN = targets.ApertureShadingGroups(AwningSpecification.Dakar, Extension, double.NaN, double.NaN);

            Assert.Equal(3, underDefault.Count);
            Assert.Equal(
                underDefault.ConvertAll(x => x.GroupGuid),
                underNaN.ConvertAll(x => x.GroupGuid));
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void A_Device_With_Custom_Recorded_Criteria_Measures_The_Same_Group()
        {
            // The criteria are not part of the group identity: when they do not change the
            // grouping (the Kołobrzeg members sit effectively gap-free), a device recording
            // custom criteria re-establishes the SAME group and measures identically.
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            ApertureSolarContext solarContext = Context();

            List<ApertureSolarTarget> wired = WiredTargets(StudiedGuids());
            ApertureShadingGroup group = Group(solarContext);
            List<SolarControlProfile> profiles = Profiles(wired, weatherData, year);

            GroupedShadingDevice custom = new GroupedShadingDevice(
                group.GroupGuid, group.PanelGuid, group.ApertureGuids, Awning(), AwningSpecification.Dakar, 0.5, 0.05);

            GroupedShadingOperationProfile viaCustom = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                custom, wired, profiles, solarContext, out string customMessage);
            GroupedShadingOperationProfile viaDefault = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                Device(group), wired, profiles, solarContext, out string defaultMessage);

            Assert.NotNull(viaCustom);
            Assert.NotNull(viaDefault);
            Assert.Null(customMessage);
            Assert.Null(defaultMessage);
            Assert.Equal(viaDefault.GroupGuid, viaCustom.GroupGuid);
            Assert.Equal(viaDefault.DeviceDeployedHoursOfYear, viaCustom.DeviceDeployedHoursOfYear);
            Assert.Equal(viaDefault.ControlledUnwantedSolarIntercepted, viaCustom.ControlledUnwantedSolarIntercepted, 12);
        }

        // ------------------------------------------- the pre-existing paths, pinned unchanged ----

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Single_Device_And_Bare_Typology_Paths_Are_Unchanged()
        {
            // What the component does with a ShadingDevice: extract its typology and measure one
            // aperture. The hand-off changes nothing about it — a ShadingDevice and its bare
            // typology give the identical single-aperture answer.
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            ApertureSolarContext solarContext = Context();

            ApertureSolarTarget target = solarContext.Target(KolobrzegFixture.TallApertureGuid);
            Assert.NotNull(target);

            SolarControlProfile profile = target.SolarControlProfile(weatherData, new SolarControlSettings(200.0), year);
            Assert.NotNull(profile);

            RetractableAwning awning = Awning();
            ShadingDevice shadingDevice = new ShadingDevice(target.ApertureGuid, awning);
            int offset = solarContext.CellIndexOffset(target.ApertureGuid);

            ShadingOperationProfile viaDevice = Analytical.SolarCalculator.Create.ShadingOperationProfile(
                target, profile, solarContext.SolarVisibilityCache, solarContext.ContextOccluders,
                shadingDevice.Typology, weatherData, offset);
            ShadingOperationProfile viaTypology = Analytical.SolarCalculator.Create.ShadingOperationProfile(
                target, profile, solarContext.SolarVisibilityCache, solarContext.ContextOccluders,
                awning, weatherData, offset);

            Assert.NotNull(viaDevice);
            Assert.NotNull(viaTypology);
            Assert.Equal(viaTypology.DeployedHoursOfYear, viaDevice.DeployedHoursOfYear);
            Assert.Equal(viaTypology.WindRetractedHoursOfYear, viaDevice.WindRetractedHoursOfYear);
            Assert.Equal(viaTypology.ControlledUnwantedSolarIntercepted, viaDevice.ControlledUnwantedSolarIntercepted, 12);
            Assert.Equal(viaTypology.ControlledDirectSolarIntercepted, viaDevice.ControlledDirectSolarIntercepted, 12);
        }

        [Fact]
        public void ShadingScheme_Can_Never_Pass_The_ShadingOperation_Recognition_Chain()
        {
            // The component recognises exactly three kinds on _shadingDevice: ShadingDevice,
            // GroupedShadingDevice and IShadingTypology. A ShadingScheme is none of them — pin the
            // type boundary so no future inheritance change can smuggle a scheme onto the wire.
            // (SelectShadingScheme.selectedScheme → ShadingOperation stays invalid by design;
            // the scheme path is VerifyShading.)
            Assert.False(typeof(ShadingDevice).IsAssignableFrom(typeof(ShadingScheme)));
            Assert.False(typeof(GroupedShadingDevice).IsAssignableFrom(typeof(ShadingScheme)));
            Assert.False(typeof(IShadingTypology).IsAssignableFrom(typeof(ShadingScheme)));
        }

        [Fact]
        public void Existing_Operation_Overloads_Keep_Their_Signatures_And_The_Device_Overload_Is_Additive()
        {
            // §14.1: capability arrives as NEW overloads; the pre-existing entry points — the
            // single-aperture ShadingOperationProfile and the group-level
            // GroupedShadingOperationProfile the multi-target branch calls — keep their shapes.
            MethodInfo[] grouped = typeof(Analytical.SolarCalculator.Create)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "GroupedShadingOperationProfile")
                .ToArray();

            MethodInfo[] byGroup = grouped.Where(method => method.GetParameters().FirstOrDefault()?.ParameterType == typeof(ApertureShadingGroup)).ToArray();
            MethodInfo[] byDevice = grouped.Where(method => method.GetParameters().FirstOrDefault()?.ParameterType == typeof(GroupedShadingDevice)).ToArray();

            Assert.Equal(2, byGroup.Length);
            Assert.Equal(new HashSet<int> { 8, 7 }, new HashSet<int>(byGroup.Select(method => method.GetParameters().Length)));

            // The hand-off overload pair: the device, the wired targets, the profiles, the
            // prepared context — with and without the failure message. No weather parameter:
            // the context's own weather keeps schedule and accounting on one timeline.
            Assert.Equal(2, byDevice.Length);
            Assert.Equal(new HashSet<int> { 5, 4 }, new HashSet<int>(byDevice.Select(method => method.GetParameters().Length)));
            Assert.All(byDevice, method => Assert.Equal(typeof(ApertureSolarContext), method.GetParameters()[3].ParameterType));

            MethodInfo single = typeof(Analytical.SolarCalculator.Create)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == "ShadingOperationProfile");
            Assert.Equal(typeof(ApertureSolarTarget), single.GetParameters()[0].ParameterType);
            Assert.Equal(7, single.GetParameters().Length);
        }

        [Fact]
        public void Grouping_Criteria_Are_Additive_On_The_Device_And_The_Optimiser()
        {
            // The pre-criteria constructor keeps its exact shape; the criteria arrive as a NEW
            // seven-parameter constructor (the RetractableAwning mounting-offset pattern).
            ConstructorInfo five = typeof(GroupedShadingDevice).GetConstructor(new[]
            {
                typeof(Guid), typeof(Guid), typeof(IEnumerable<Guid>), typeof(IShadingTypology), typeof(AwningSpecification),
            });
            Assert.NotNull(five);

            ConstructorInfo seven = typeof(GroupedShadingDevice).GetConstructor(new[]
            {
                typeof(Guid), typeof(Guid), typeof(IEnumerable<Guid>), typeof(IShadingTypology), typeof(AwningSpecification),
                typeof(double), typeof(double),
            });
            Assert.NotNull(seven);

            // The optimiser keeps the pinned 12- and 13-parameter overloads (AwningApiCompatibilityTests)
            // and gains the grouping-criteria overload — trailing maximumGap, then headTolerance.
            MethodInfo[] overloads = typeof(Optimise)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => method.Name == "RetractableAwningGroup")
                .ToArray();

            Assert.NotNull(overloads.SingleOrDefault(method => method.GetParameters().Length == 12));
            Assert.NotNull(overloads.SingleOrDefault(method => method.GetParameters().Length == 13));

            MethodInfo fifteen = overloads.SingleOrDefault(method => method.GetParameters().Length == 15);
            Assert.NotNull(fifteen);
            Assert.Equal(typeof(double), fifteen.GetParameters()[13].ParameterType);
            Assert.Equal(typeof(double), fifteen.GetParameters()[14].ParameterType);
        }
    }
}
