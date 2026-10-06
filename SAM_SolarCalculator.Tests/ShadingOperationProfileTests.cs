// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
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
    /// The retractable-awning operation profile: what the device ACTUALLY DID over the weather
    /// year, from the hour-by-hour control schedule that SolarControlProfileTests pins down.
    ///
    /// Everything here is asserted as a RELATIONSHIP or against a value the test computes itself.
    /// No annual hour count or kWh figure is hard-coded: the fixtures are synthetic weather with
    /// hand-checkable radiation, and the assertions are the invariants the design promises.
    ///
    /// THE THREE CORRECTIONS THESE TESTS ENFORCE:
    ///   A. canopy- and valance-effective hours are NOT mutually exclusive at hour level — the
    ///      exclusivity lives at the (bin, cell) ray, and only there;
    ///   B. first-hit valance energy is ATTRIBUTED energy, never "marginal" — nothing here claims
    ///      what the ray would do without the valance;
    ///   C. the deployed weighting is what the device did, the demand weighting what was wanted,
    ///      and the difference is what the wind retraction costs.
    /// </summary>
    public class ShadingOperationProfileTests
    {
        private readonly ITestOutputHelper output;

        public ShadingOperationProfileTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const int Year = 2018;
        private const double Shift = 30.0;
        private const double GridSize = 0.5;
        private static readonly Guid PanelGuid = new Guid("cccc3333-0000-0000-0000-000000000001");

        // ---------------------------------------------------------------- fixtures ----------

        private static ApertureSolarTarget Target(Vector3D outward, double gridSize = 1.0)
        {
            Face3D face = SyntheticTargets.Face(outward, new Point3D(0, 0, 5), 1.0, 2.0);
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(face, gridSize);
            return new ApertureSolarTarget(Guid.NewGuid(), Guid.NewGuid(), face, cells);
        }

        /// <summary>
        /// Synthetic weather with full control of wind: radiation is a pure function of the sun
        /// position at (timestamp + shift), no diffuse, so the direct normal irradiance is exactly
        /// <paramref name="directNormal"/> whenever the sun is up.
        /// </summary>
        private static WeatherData SyntheticWeather(int year, Location location, double directNormal, Func<DateTime, double> windSpeed)
        {
            double[] sinAltitude = SinAltitude(year, location);

            WeatherData weatherData = new WeatherData("Synthetic", "Synthetic operation-profile weather", location.Latitude, location.Longitude, location.Elevation);
            weatherData.SetValue(WeatherDataParameter.TimeZone, "UTC+00:00");

            DateTime start = new DateTime(year, 1, 1);
            for (int i = 0; i < sinAltitude.Length; i++)
            {
                DateTime dateTime = start.AddHours(i);

                Dictionary<string, double> dictionary = new Dictionary<string, double>
                {
                    { WeatherDataType.GlobalSolarRadiation.ToString(), directNormal * sinAltitude[i] },
                    { WeatherDataType.DiffuseSolarRadiation.ToString(), 0.0 },
                };

                double wind = windSpeed == null ? double.NaN : windSpeed(dateTime);
                if (!double.IsNaN(wind))
                {
                    dictionary[WeatherDataType.WindSpeed.ToString()] = wind;
                }

                weatherData.Add(dateTime, dictionary);
            }

            return weatherData;
        }

        private static WeatherData SyntheticWeather(double directNormal = 900.0, double wind = 2.0)
        {
            return SyntheticWeather(Year, TestHelpers.London(), directNormal, x => wind);
        }

        private static readonly object padlock = new object();
        private static readonly Dictionary<int, double[]> sinAltitudeByYear = new Dictionary<int, double[]>();

        private static double[] SinAltitude(int year, Location location)
        {
            lock (padlock)
            {
                if (sinAltitudeByYear.TryGetValue(year, out double[] existing))
                {
                    return existing;
                }

                DateTime start = new DateTime(year, 1, 1);
                double[] result = new double[DateTime.IsLeapYear(year) ? 8784 : 8760];
                for (int i = 0; i < result.Length; i++)
                {
                    result[i] = SAM.Geometry.SolarCalculator.Query.TryGetSunAngles(location, start.AddHours(i).AddMinutes(Shift), out double altitude, out _) && altitude > 0
                        ? Math.Sin(altitude * Math.PI / 180.0)
                        : 0.0;
                }

                sinAltitudeByYear[year] = result;
                return result;
            }
        }

        private static SolarVisibilityCache Cache(ApertureSolarTarget target, double shiftInMinutes = Shift)
        {
            return Weather.SolarCalculator.Create.SolarVisibilityCache(
                TestHelpers.London(), Year, 2.0, new List<LinkedFace3D>(), target.AnalysisCells,
                cellSize: 1.0, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: shiftInMinutes);
        }

        private static WeatherData Gales()
        {
            return SyntheticWeather(Year, TestHelpers.London(), 900.0, x => x.Hour < 12 ? 2.0 : 20.0);
        }

        // ------------------------------------------------ single aperture: the schedule ----------

        [Fact]
        public void Deployed_Is_A_Subset_Of_Demand_And_Partitions_It_Exactly()
        {
            ApertureSolarTarget target = Target(SyntheticTargets.South);
            WeatherData weatherData = Gales();
            SolarControlSettings settings = new SolarControlSettings(200.0, double.NaN, 10.0);

            SolarControlProfile profile = target.SolarControlProfile(weatherData, settings, Year);
            ShadingOperationProfile operation = Analytical.SolarCalculator.Create.ShadingOperationProfile(
                target, profile, Cache(target), new List<LinkedFace3D>(), new RetractableAwning(1.6, 28.0), weatherData);

            Assert.NotNull(operation);
            Assert.True(profile.HighWindHours > 0, "the fixture must actually bite for the partition to mean anything");

            List<int> demand = profile.ShadeDemandHoursOfYear;
            List<int> deployed = operation.DeployedHoursOfYear;
            List<int> windRetracted = operation.WindRetractedHoursOfYear;

            // Passthrough, not recomputation: the operation profile reports the control profile's sets.
            Assert.Equal(profile.ShadeOnHoursOfYear, deployed);
            Assert.Equal(profile.HighWindHoursOfYear, windRetracted);

            // Deployed is a subset of demand; deployed + wind-retracted = demand, disjointly.
            Assert.Empty(deployed.Except(demand));
            Assert.Empty(windRetracted.Except(demand));
            Assert.Empty(deployed.Intersect(windRetracted));
            Assert.Equal(demand.OrderBy(x => x), deployed.Concat(windRetracted).OrderBy(x => x));

            Assert.True(operation.ShadeUseFraction < 1.0);
            Assert.Equal(operation.ShadeUseFraction, profile.ShadeUseFraction, 12);

            output.WriteLine($"south: requested {profile.ShadeDemandHours} h, deployed {operation.DeployedHours} h, wind-retracted {operation.WindRetractedHours} h");
        }

        [Fact]
        public void No_Wind_Limit_Deploys_The_Full_Request()
        {
            ApertureSolarTarget target = Target(SyntheticTargets.South);
            WeatherData weatherData = SyntheticWeather();

            SolarControlProfile profile = target.SolarControlProfile(weatherData, new SolarControlSettings(200.0), Year);
            ShadingOperationProfile operation = Analytical.SolarCalculator.Create.ShadingOperationProfile(
                target, profile, Cache(target), new List<LinkedFace3D>(), new RetractableAwning(1.6, 28.0), weatherData);

            Assert.NotNull(operation);
            Assert.True(profile.ShadeDemandHours > 0);

            Assert.Equal(profile.ShadeDemandHoursOfYear, operation.DeployedHoursOfYear);
            Assert.Empty(operation.WindRetractedHoursOfYear);
            Assert.Equal(1.0, operation.ShadeUseFraction, 12);
        }

        [Fact]
        public void A_Biting_Wind_Limit_Reduces_Controlled_Interception_Below_The_Always_Deployed_Reference()
        {
            ApertureSolarTarget target = Target(SyntheticTargets.South);
            WeatherData weatherData = Gales();
            SolarControlSettings settings = new SolarControlSettings(200.0, double.NaN, 10.0);

            SolarControlProfile profile = target.SolarControlProfile(weatherData, settings, Year);
            ShadingOperationProfile operation = Analytical.SolarCalculator.Create.ShadingOperationProfile(
                target, profile, Cache(target), new List<LinkedFace3D>(), new RetractableAwning(1.6, 28.0), weatherData);

            Assert.NotNull(operation);

            // The deployed device can never intercept more than the always-deployed reference —
            // deployed hours are a subset of demand hours — and with a biting wind limit it
            // intercepts strictly less.
            Assert.True(operation.ControlledUnwantedSolarIntercepted <= operation.UncontrolledUnwantedSolarIntercepted + 1e-9);
            Assert.True(operation.ControlledUnwantedSolarIntercepted < operation.UncontrolledUnwantedSolarIntercepted,
                "a wind limit that retracts the device must reduce what it intercepts");
            Assert.True(operation.ShadeUseFraction < 1.0);

            output.WriteLine($"controlled {operation.ControlledUnwantedSolarIntercepted:0.###} kWh vs always-deployed {operation.UncontrolledUnwantedSolarIntercepted:0.###} kWh, shade use {100.0 * operation.ShadeUseFraction:0.#} %");
        }

        [Fact]
        public void A_Wind_Limit_Changes_Only_The_Unwanted_Channel_Not_The_Geometric_Attribution()
        {
            // F1 regression pin. The direct channel and the per-element/attribution energies are
            // FULL-YEAR geometric quantities: ShadingPerformance accumulates them from the UNWEIGHTED
            // direct beam, so the deployment schedule never gates them. For identical geometry and
            // weather, changing the wind limit (and with it the deployed hours) must therefore leave
            // ControlledDirectSolarIntercepted, CanopyAttributedEnergy, ValanceAttributedEnergy and
            // EnergyPerElement unchanged, while the unwanted channel — the only operation-weighted
            // figure — shrinks. This is asserted against the same implementation twice, not against
            // a hard-coded number, so it pins the RELATIONSHIP, not a value.
            ApertureSolarTarget target = Target(SyntheticTargets.West, GridSize);
            WeatherData weatherData = Gales();
            SolarVisibilityCache cache = Cache(target);

            SolarControlProfile noLimit = target.SolarControlProfile(weatherData, new SolarControlSettings(200.0), Year);
            SolarControlProfile bitingLimit = target.SolarControlProfile(weatherData, new SolarControlSettings(200.0, double.NaN, 10.0), Year);

            ShadingOperationProfile a = Analytical.SolarCalculator.Create.ShadingOperationProfile(
                target, noLimit, cache, new List<LinkedFace3D>(), WithValance, weatherData);
            ShadingOperationProfile b = Analytical.SolarCalculator.Create.ShadingOperationProfile(
                target, bitingLimit, cache, new List<LinkedFace3D>(), WithValance, weatherData);

            Assert.NotNull(a);
            Assert.NotNull(b);

            // The wind limit must actually bite: fewer deployed hours, more wind-retracted hours.
            Assert.True(b.DeployedHours < a.DeployedHours);
            Assert.True(b.WindRetractedHours > a.WindRetractedHours);

            // Operation-weighted: retracting hours reduces the unwanted energy the control achieves.
            Assert.True(b.ControlledUnwantedSolarIntercepted < a.ControlledUnwantedSolarIntercepted);

            // Geometric / full-year: identical geometry and weather give identical direct interception
            // and identical first-hit attribution, independent of the deployment schedule.
            Assert.Equal(a.ControlledDirectSolarIntercepted, b.ControlledDirectSolarIntercepted, 9);
            Assert.Equal(a.CanopyAttributedEnergy, b.CanopyAttributedEnergy, 9);

            // The west-facade valance is real (see the valance test below), so the equality is a
            // statement about a live element, not two absent ones.
            Assert.True(a.ValanceAttributedEnergy > 0);
            Assert.Equal(a.ValanceAttributedEnergy, b.ValanceAttributedEnergy, 9);

            Assert.Equal(a.EnergyPerElement.Keys.OrderBy(x => x), b.EnergyPerElement.Keys.OrderBy(x => x));
            foreach (Guid guid in a.EnergyPerElement.Keys)
            {
                Assert.Equal(a.EnergyPerElement[guid], b.EnergyPerElement[guid], 9);
            }

            output.WriteLine($"no limit: deployed {a.DeployedHours} h, unwanted {a.ControlledUnwantedSolarIntercepted:0.###} kWh, canopy {a.CanopyAttributedEnergy:0.###} kWh, valance {a.ValanceAttributedEnergy:0.###} kWh");
            output.WriteLine($"10 m/s  : deployed {b.DeployedHours} h, unwanted {b.ControlledUnwantedSolarIntercepted:0.###} kWh, canopy {b.CanopyAttributedEnergy:0.###} kWh, valance {b.ValanceAttributedEnergy:0.###} kWh");
        }

        // ------------------------------------------------------------ canopy and valance ----------

        private static readonly RetractableAwning NoValance = new RetractableAwning(1.6, 28.0, 0.0, 0.0, 0.0);
        private static readonly RetractableAwning WithValance = new RetractableAwning(1.6, 28.0, 0.0, 0.0, 0.21);

        private static ShadingOperationProfile Operate(ApertureSolarTarget target, WeatherData weatherData, RetractableAwning awning)
        {
            SolarControlProfile profile = target.SolarControlProfile(weatherData, new SolarControlSettings(200.0), Year);
            return Analytical.SolarCalculator.Create.ShadingOperationProfile(target, profile, Cache(target), new List<LinkedFace3D>(), awning, weatherData);
        }

        [Fact]
        public void A_Device_Without_A_Valance_Reports_No_Valance_Hours_And_No_Valance_Energy()
        {
            ApertureSolarTarget target = Target(SyntheticTargets.West, GridSize);
            WeatherData weatherData = SyntheticWeather();

            ShadingOperationProfile operation = Operate(target, weatherData, NoValance);

            Assert.NotNull(operation);
            Assert.True(operation.DeployedHours > 0);

            // The valance-less device has nothing to be effective in and no valance Guid in its
            // energy table — the energy table holds exactly the device's own element: the canopy.
            Assert.Empty(operation.ValanceEffectiveHoursOfYear);
            Assert.Equal(0, operation.ValanceEffectiveHours);
            Assert.True(double.IsNaN(operation.ValanceAttributedEnergy));
            Assert.True(double.IsNaN(operation.ValanceEffectiveFraction));

            // The element Guids are derived from the full parameter set, so a rebuild WITH a
            // valance is a different device with different Guids. The correct statement is about
            // THIS device's own elements: exactly one — the canopy — and no valance.
            List<ShadingElement> own = NoValance.ShadingElements(target);
            Assert.Single(own);
            Guid canopyGuid = own[0].Guid;
            Assert.Equal("RetractableAwning_Canopy", own[0].Name);
            Assert.Equal(new List<Guid> { canopyGuid }, operation.EnergyPerElement.Keys.OrderBy(x => x).ToList());

            // The canopy, on the other hand, is real and credited.
            Assert.True(operation.CanopyEffectiveHours > 0, "a deployed awning must be effective in some hours");
        }

        [Fact]
        public void A_Valanced_Awning_On_A_West_Facade_Is_Effective_In_Real_Hours()
        {
            ApertureSolarTarget target = Target(SyntheticTargets.West, GridSize);
            WeatherData weatherData = SyntheticWeather();

            ShadingOperationProfile operation = Operate(target, weatherData, WithValance);

            Assert.NotNull(operation);
            Assert.True(operation.DeployedHours > 0);

            // The valance catches the low late-day sun the canopy front edge lets past: on a west
            // facade those hours exist, and the first-hit accounting credits real energy to it.
            Assert.True(operation.ValanceEffectiveHours > 0, "the valance must be effective in some deployed hours on a west facade");
            Assert.True(operation.ValanceAttributedEnergy > 0, "the valance must intercept energy in the current geometry");

            output.WriteLine($"{operation}");
        }

        [Fact]
        public void Canopy_And_Valance_Effective_Hours_Are_Each_Subsets_Of_Deployed_Hours()
        {
            ApertureSolarTarget target = Target(SyntheticTargets.West, GridSize);
            WeatherData weatherData = SyntheticWeather();

            ShadingOperationProfile operation = Operate(target, weatherData, WithValance);

            Assert.NotNull(operation);

            List<int> deployed = operation.DeployedHoursOfYear;
            Assert.Empty(operation.CanopyEffectiveHoursOfYear.Except(deployed));
            Assert.Empty(operation.ValanceEffectiveHoursOfYear.Except(deployed));

            // CORRECTION A: do NOT assert the two sets are disjoint. Within one hour some cells
            // first-hit the canopy while others first-hit the valance, so an hour can legitimately
            // be effective for both. The exclusivity lives per ray, asserted below.
            output.WriteLine($"deployed {operation.DeployedHours} h | canopy effective {operation.CanopyEffectiveHours} h | valance effective {operation.ValanceEffectiveHours} h");
        }

        [Fact]
        public void First_Hit_Is_Canopy_XOR_Valance_Per_Ray()
        {
            ApertureSolarTarget target = Target(SyntheticTargets.West, GridSize);

            List<ShadingElement> elements = WithValance.ShadingElements(target);
            Guid canopyGuid = elements.Single(x => x.Name == "RetractableAwning_Canopy").Guid;
            Guid valanceGuid = elements.Single(x => x.Name == "RetractableAwning_Valance").Guid;
            Assert.NotEqual(canopyGuid, valanceGuid);

            // The same public attribution builder the production accounting uses: context (none)
            // plus the two device faces, over this target's cells.
            SolarVisibilityCache cache = Cache(target);
            SolarAttributionCache attribution = cache.SolarAttributionCache(
                elements.Select(x => x.LinkedFace3D).ToList(), target.AnalysisCells, 0);

            Assert.NotNull(attribution);
            Assert.Equal(cache.Bins.Count, attribution.BinCount);

            int canopyHits = 0, valanceHits = 0, otherHits = 0;
            for (int b = 0; b < attribution.BinCount; b++)
            {
                for (int c = 0; c < attribution.CellCount; c++)
                {
                    Guid firstHit = attribution.FirstHitGuid(b, c);
                    bool canopy = firstHit == canopyGuid;
                    bool valance = firstHit == valanceGuid;

                    // THE exclusivity that IS true: one traced ray has exactly one first hit, so it
                    // can never be both the canopy and the valance.
                    Assert.False(canopy && valance, $"bin {b} cell {c} first-hit both the canopy and the valance");

                    if (canopy) { canopyHits++; }
                    else if (valance) { valanceHits++; }
                    else { otherHits++; }
                }
            }

            // The fixture must actually exercise the valance for the XOR to be a statement about
            // two live candidates rather than one face and one absent face.
            Assert.True(canopyHits > 0);
            Assert.True(valanceHits > 0);

            output.WriteLine($"bin x cell first hits: canopy {canopyHits}, valance {valanceHits}, neither {otherHits}");
        }

        // ------------------------------------------------------------------- the grouped device ----

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

        private class GroupScenario
        {
            public List<ApertureSolarTarget> Targets;
            public SolarVisibilityCache SharedCache;
            public List<int> Offsets;
            public List<LinkedFace3D> Context;
            public ApertureShadingGroup Group;
            public WeatherData Weather;
            public List<SolarControlProfile> Profiles;
        }

        /// <summary>
        /// Three adjacent south-facing apertures sharing one cell space and one control rule — the
        /// Kołobrzeg layout in miniature, as in AwningGroupPerformanceTests.
        /// </summary>
        private static GroupScenario BuildGroup(SolarControlSettings settings, WeatherData weatherData)
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
                cellSize: GridSize, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: Shift);

            List<SolarControlProfile> profiles = new List<SolarControlProfile>();
            foreach (ApertureSolarTarget target in targets)
            {
                SolarControlProfile profile = target.SolarControlProfile(weatherData, settings, Year);
                Assert.NotNull(profile);
                profiles.Add(profile);
            }

            List<ApertureShadingGroup> groups = targets.ApertureShadingGroups(AwningSpecification.Dakar, 0.15);
            Assert.Single(groups);
            ApertureShadingGroup group = groups[0];
            group.SetCellIndexOffsets(offsets);

            return new GroupScenario
            {
                Targets = targets,
                SharedCache = sharedCache,
                Offsets = offsets,
                Context = context,
                Group = group,
                Weather = weatherData,
                Profiles = profiles,
            };
        }

        [Fact]
        public void Grouped_Device_Headline_Is_The_Union_Of_Member_Schedules()
        {
            GroupScenario scenario = BuildGroup(new SolarControlSettings(200.0), SyntheticWeather());

            GroupedShadingOperationProfile device = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                scenario.Group, scenario.Profiles, scenario.SharedCache, scenario.Context,
                new RetractableAwning(2.6, 15.0, 0.0, 0.15, 0.0), scenario.Weather, out string message, scenario.Offsets);

            Assert.NotNull(device);
            Assert.Null(message);

            // The headline is the union of the member schedules, never the tallest window's.
            List<int> demand = new List<int>();
            List<int> deployed = new List<int>();
            foreach (SolarControlProfile profile in scenario.Profiles)
            {
                demand.AddRange(profile.ShadeDemandHoursOfYear);
                deployed.AddRange(profile.ShadeOnHoursOfYear);
            }

            Assert.Equal(new HashSet<int>(demand), new HashSet<int>(device.DeviceDemandHoursOfYear));
            Assert.Equal(new HashSet<int>(deployed), new HashSet<int>(device.DeviceDeployedHoursOfYear));

            // No wind limit: deployed == demand, nothing retracted, full use.
            Assert.Equal(device.DeviceDemandHoursOfYear, device.DeviceDeployedHoursOfYear);
            Assert.Empty(device.DeviceWindRetractedHoursOfYear);
            Assert.Equal(1.0, device.DeviceShadeUseFraction, 12);

            // The partition identity holds at device level too.
            Assert.Equal(device.DeviceDemandHours, device.DeviceDeployedHours + device.DeviceWindRetractedHours);
            Assert.Empty(device.DeviceDeployedHoursOfYear.Intersect(device.DeviceWindRetractedHoursOfYear));

            output.WriteLine($"device: {device.DeviceDemandHours} h requested, {device.DeviceDeployedHours} h deployed; members requested {string.Join(", ", scenario.Profiles.Select(x => x.ShadeDemandHours))} h");
        }

        [Fact]
        public void Grouped_Device_Refuses_Members_That_Disagree_On_The_Rule_Or_The_Timeline()
        {
            GroupScenario scenario = BuildGroup(new SolarControlSettings(200.0), SyntheticWeather());
            RetractableAwning awning = new RetractableAwning(2.6, 15.0, 0.0, 0.15, 0.0);

            // A member with a different WIND LIMIT is refused, not merged.
            List<SolarControlProfile> differentWind = new List<SolarControlProfile>(scenario.Profiles);
            SolarControlProfile mid = scenario.Targets[1].SolarControlProfile(scenario.Weather, new SolarControlSettings(200.0, double.NaN, 10.0), Year);
            differentWind[1] = mid;

            GroupedShadingOperationProfile refused = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                scenario.Group, differentWind, scenario.SharedCache, scenario.Context, awning, scenario.Weather, out string message, scenario.Offsets);

            Assert.Null(refused);
            Assert.Contains("wind limit", message, StringComparison.OrdinalIgnoreCase);

            // A member on a DIFFERENT YEAR is refused.
            WeatherData otherYearWeather = SyntheticWeather(2019, TestHelpers.London(), 900.0, x => 2.0);
            SolarControlProfile otherYear = scenario.Targets[1].SolarControlProfile(otherYearWeather, new SolarControlSettings(200.0), 2019);
            List<SolarControlProfile> differentYear = new List<SolarControlProfile>(scenario.Profiles);
            differentYear[1] = otherYear;

            refused = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                scenario.Group, differentYear, scenario.SharedCache, scenario.Context, awning, scenario.Weather, out message, scenario.Offsets);

            Assert.Null(refused);
            Assert.Contains("year", message, StringComparison.OrdinalIgnoreCase);

            // A member on a DIFFERENT SUN-POSITION SHIFT is refused.
            SolarControlProfile otherShift = scenario.Targets[1].SolarControlProfile(scenario.Weather, new SolarControlSettings(200.0), Year, null, SunTimeConvention.OnTheHour);
            List<SolarControlProfile> differentShift = new List<SolarControlProfile>(scenario.Profiles);
            differentShift[1] = otherShift;

            refused = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                scenario.Group, differentShift, scenario.SharedCache, scenario.Context, awning, scenario.Weather, out message, scenario.Offsets);

            Assert.Null(refused);
            Assert.Contains("shift", message, StringComparison.OrdinalIgnoreCase);

            // A profile from OUTSIDE the group is refused too — it is not the device's to merge.
            ApertureSolarTarget outsiderTarget = Target(SyntheticTargets.South);
            SolarControlProfile outsider = outsiderTarget.SolarControlProfile(scenario.Weather, new SolarControlSettings(200.0), Year);
            List<SolarControlProfile> withOutsider = new List<SolarControlProfile>(scenario.Profiles);
            withOutsider[2] = outsider;

            refused = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                scenario.Group, withOutsider, scenario.SharedCache, scenario.Context, awning, scenario.Weather, out message, scenario.Offsets);

            Assert.Null(refused);
            Assert.Contains("member", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Grouped_Device_Keeps_Every_Member_Diagnostic()
        {
            GroupScenario scenario = BuildGroup(new SolarControlSettings(200.0), SyntheticWeather());

            GroupedShadingOperationProfile device = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                scenario.Group, scenario.Profiles, scenario.SharedCache, scenario.Context,
                new RetractableAwning(2.6, 15.0, 0.0, 0.15, 0.0), scenario.Weather, out string _, scenario.Offsets);

            Assert.NotNull(device);
            Assert.Equal(3, device.Members.Count);

            // Every member aperture keeps its own diagnostic, and its own request is recoverable
            // from it: demand = deployed + wind-retracted, exactly.
            foreach (SolarControlProfile profile in scenario.Profiles)
            {
                ShadingOperationProfile member = device.Member(profile.ApertureGuid);
                Assert.NotNull(member);
                Assert.Equal(profile.ShadeOnHoursOfYear, member.DeployedHoursOfYear);
                Assert.Equal(profile.HighWindHoursOfYear, member.WindRetractedHoursOfYear);
                Assert.Equal(profile.ShadeDemandHoursOfYear, member.DeployedHoursOfYear.Concat(member.WindRetractedHoursOfYear).OrderBy(x => x));
            }

            // The device energy is the existing grouped accounting: the same number the summed
            // member performances give.
            Assert.NotNull(device.Performance);
            Assert.Equal(3, device.Performance.PerAperture.Count);
        }

        // ------------------------------------------------------------------ identity and refusals --

        [Fact]
        public void A_Profile_From_Another_Aperture_Or_Another_Timeline_Is_Refused()
        {
            ApertureSolarTarget target = Target(SyntheticTargets.South);
            WeatherData weatherData = SyntheticWeather();

            SolarControlProfile profile = target.SolarControlProfile(weatherData, new SolarControlSettings(200.0), Year);
            SolarVisibilityCache cache = Cache(target);

            // The profile of a DIFFERENT aperture is refused: the schedule and the window it was
            // built for are one thing, and the device answers for the window it is measured on.
            ApertureSolarTarget other = Target(SyntheticTargets.East);
            SolarControlProfile otherProfile = other.SolarControlProfile(weatherData, new SolarControlSettings(200.0), Year);
            Assert.Null(Analytical.SolarCalculator.Create.ShadingOperationProfile(target, otherProfile, cache, new List<LinkedFace3D>(), NoValance, weatherData));

            // A profile on a different YEAR than the cache is refused.
            WeatherData otherYearWeather = SyntheticWeather(2019, TestHelpers.London(), 900.0, x => 2.0);
            SolarControlProfile otherYear = target.SolarControlProfile(otherYearWeather, new SolarControlSettings(200.0), 2019);
            Assert.Null(Analytical.SolarCalculator.Create.ShadingOperationProfile(target, otherYear, cache, new List<LinkedFace3D>(), NoValance, weatherData));

            // A profile on a different SUN-POSITION SHIFT than the cache is refused.
            SolarControlProfile otherShift = target.SolarControlProfile(weatherData, new SolarControlSettings(200.0), Year, null, SunTimeConvention.OnTheHour);
            Assert.Null(Analytical.SolarCalculator.Create.ShadingOperationProfile(target, otherShift, cache, new List<LinkedFace3D>(), NoValance, weatherData));

            // The matching timeline still works — the refusals above are the gate, not a dead path.
            Assert.NotNull(Analytical.SolarCalculator.Create.ShadingOperationProfile(target, profile, cache, new List<LinkedFace3D>(), NoValance, weatherData));
        }

        // ------------------------------------------------------------------------------- JSON ----

        [Fact]
        public void Json_Round_Trips_Preserve_Hour_Sets_And_Energies()
        {
            ApertureSolarTarget target = Target(SyntheticTargets.West, GridSize);
            WeatherData weatherData = SyntheticWeather();

            ShadingOperationProfile operation = Operate(target, weatherData, WithValance);
            Assert.NotNull(operation);

            // A NaN must never be written as a JSON number; the real-text round trip throws if it is.
            string text = operation.ToJsonObject().ToJsonString();
            Assert.DoesNotContain("NaN", text);

            ShadingOperationProfile restored = new ShadingOperationProfile(operation.ToJsonObject());

            Assert.Equal(operation.ApertureGuid, restored.ApertureGuid);
            Assert.Equal(operation.TypologyName, restored.TypologyName);
            Assert.Equal(operation.Year, restored.Year);
            Assert.Equal(operation.TimeShiftInMinutes, restored.TimeShiftInMinutes, 12);
            Assert.Equal(operation.Settings.MaximumWindSpeed, restored.Settings.MaximumWindSpeed, 12);
            Assert.Equal(operation.Settings.MinimumApertureIrradiance, restored.Settings.MinimumApertureIrradiance, 12);
            Assert.Equal(operation.DeployedHoursOfYear, restored.DeployedHoursOfYear);
            Assert.Equal(operation.WindRetractedHoursOfYear, restored.WindRetractedHoursOfYear);
            Assert.Equal(operation.ShadeUseFraction, restored.ShadeUseFraction, 12);
            Assert.Equal(operation.CanopyEffectiveHoursOfYear, restored.CanopyEffectiveHoursOfYear);
            Assert.Equal(operation.ValanceEffectiveHoursOfYear, restored.ValanceEffectiveHoursOfYear);
            Assert.Equal(operation.CanopyAttributedEnergy, restored.CanopyAttributedEnergy, 9);
            Assert.Equal(operation.ValanceAttributedEnergy, restored.ValanceAttributedEnergy, 9);
            Assert.Equal(operation.ControlledDirectSolarIntercepted, restored.ControlledDirectSolarIntercepted, 9);
            Assert.Equal(operation.ControlledUnwantedSolarIntercepted, restored.ControlledUnwantedSolarIntercepted, 9);
            Assert.Equal(operation.UncontrolledUnwantedSolarIntercepted, restored.UncontrolledUnwantedSolarIntercepted, 9);
            Assert.Equal(operation.EnergyPerElement.Keys, restored.EnergyPerElement.Keys);
            foreach (Guid guid in operation.EnergyPerElement.Keys)
            {
                Assert.Equal(operation.EnergyPerElement[guid], restored.EnergyPerElement[guid], 9);
            }
        }

        [Fact]
        public void Grouped_Json_Round_Trip_Preserves_The_Device_Sets()
        {
            GroupScenario scenario = BuildGroup(new SolarControlSettings(200.0), SyntheticWeather());

            GroupedShadingOperationProfile device = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                scenario.Group, scenario.Profiles, scenario.SharedCache, scenario.Context,
                new RetractableAwning(2.6, 15.0, 0.0, 0.15, 0.0), scenario.Weather, out string _, scenario.Offsets);

            Assert.NotNull(device);

            GroupedShadingOperationProfile restored = new GroupedShadingOperationProfile(device.ToJsonObject());

            Assert.Equal(device.GroupGuid, restored.GroupGuid);
            Assert.Equal(device.Year, restored.Year);
            Assert.Equal(device.TimeShiftInMinutes, restored.TimeShiftInMinutes, 12);
            Assert.Equal(device.Settings.MaximumWindSpeed, restored.Settings.MaximumWindSpeed, 12);
            Assert.Equal(device.DeviceDemandHoursOfYear, restored.DeviceDemandHoursOfYear);
            Assert.Equal(device.DeviceDeployedHoursOfYear, restored.DeviceDeployedHoursOfYear);
            Assert.Equal(device.DeviceWindRetractedHoursOfYear, restored.DeviceWindRetractedHoursOfYear);
            Assert.Equal(device.CanopyEffectiveHoursOfYear, restored.CanopyEffectiveHoursOfYear);
            Assert.Equal(device.ValanceEffectiveHoursOfYear, restored.ValanceEffectiveHoursOfYear);
            Assert.Equal(device.Members.Count, restored.Members.Count);
            Assert.Equal(device.Performance.UnwantedSolarIntercepted, restored.Performance.UnwantedSolarIntercepted, 9);

            foreach (ShadingOperationProfile member in device.Members)
            {
                ShadingOperationProfile restoredMember = restored.Member(member.ApertureGuid);
                Assert.NotNull(restoredMember);
                Assert.Equal(member.DeployedHoursOfYear, restoredMember.DeployedHoursOfYear);
            }
        }
    }
}
