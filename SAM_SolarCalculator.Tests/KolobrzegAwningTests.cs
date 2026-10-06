// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The Kołobrzeg acceptance cases for the retractable awning, run against the committed
    /// real-project fixture (the SAME file the resolution study used — byte-identical, so no new
    /// fixture is added by this PR).
    ///
    /// The three studied apertures sit on one WSW wall (panel 9c05c380-79da-40e8-9761-fc6ee7aaade3,
    /// azimuth ~257.5°, 3.30 m long) with a combined envelope 2.70 m wide, effectively zero gaps
    /// and aligned heads. With a 0.15 m side extension the awning width is 3.00 m.
    ///
    /// A. the low-level geometry and product-validation check (fast — no weather);
    /// B. the real optimisation (LongRunning — real 2018 Kołobrzeg weather, projection and tilt
    ///    both left to the analysis).
    /// </summary>
    public class KolobrzegAwningTests
    {
        private readonly ITestOutputHelper output;

        public KolobrzegAwningTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static readonly Guid HostWallGuid = new Guid("9c05c380-79da-40e8-9761-fc6ee7aaade3");
        private const double Extension = 0.15;

        private static List<Guid> StudiedGuids()
        {
            return new List<Guid>
            {
                KolobrzegFixture.TallApertureGuid,
                KolobrzegFixture.MidApertureGuid,
                KolobrzegFixture.SmallApertureGuid,
            };
        }

        private static List<ApertureSolarTarget> StudiedTargets()
        {
            List<ApertureSolarTarget> targets = KolobrzegFixture.Model().ApertureSolarTargets(StudiedGuids(), KolobrzegFixture.HistoricalGridSize);
            Assert.Equal(3, targets.Count);
            return targets;
        }

        // -------------------------------- A. geometry and product validation (fast) ----

        [Fact]
        public void Kolobrzeg_Three_Apertures_Form_One_Group_Of_Three_Metres()
        {
            List<ApertureSolarTarget> targets = StudiedTargets();

            List<ApertureShadingGroup> groups = targets.ApertureShadingGroups(AwningSpecification.Dakar, Extension);

            Assert.Single(groups);
            ApertureShadingGroup group = groups[0];
            Assert.Equal(HostWallGuid, group.PanelGuid);
            Assert.Equal(3, group.ApertureGuids.Count);
            Assert.Equal(new HashSet<Guid>(StudiedGuids()), new HashSet<Guid>(group.ApertureGuids));

            // Combined envelope 2.70 m + 2 x 0.15 m extension = 3.00 m.
            Assert.Equal(2.70, group.Width, 6);
            Assert.Equal(3.00, group.Width + 2.0 * Extension, 6);

            // Members are ordered left-to-right; gaps and head spread are effectively zero.
            List<double> extents = new List<double>();
            foreach (ApertureSolarTarget target in group.Targets)
            {
                Geometry.Planar.BoundingBox2D boundingBox2D = group.Plane.Convert(target.Face3D)?.GetBoundingBox();
                Assert.NotNull(boundingBox2D);
                extents.Add(boundingBox2D.Min.X);
                extents.Add(boundingBox2D.Max.X);
            }

            double memberMinX = extents.Min();
            double memberMaxX = extents.Max();
            Assert.Equal(0.0, memberMinX - group.MinX, 6);
            Assert.Equal(0.0, group.MaxX - memberMaxX, 6);

            // Heads align well inside the default 0.02 m tolerance.
            List<ApertureSolarTarget> ordered = group.Targets;
            for (int i = 0; i < ordered.Count - 1; i++)
            {
                Geometry.Planar.BoundingBox2D a = group.Plane.Convert(ordered[i].Face3D)?.GetBoundingBox();
                Geometry.Planar.BoundingBox2D b = group.Plane.Convert(ordered[i + 1].Face3D)?.GetBoundingBox();
                Assert.True(Math.Abs(a.Max.Y - b.Max.Y) <= 0.02, "heads must align within 0.02 m");
                Assert.True(b.Min.X - a.Max.X <= 0.20, "gaps must stay within 0.20 m");
            }

            output.WriteLine($"one group {group.GroupGuid} on wall {group.PanelGuid}: {string.Join(", ", group.ApertureGuids)}, " +
                $"envelope {group.Width:0.###} m, awning width {group.Width + 2.0 * Extension:0.###} m");
        }

        [Fact]
        public void Kolobrzeg_Product_Validation_And_Brackets()
        {
            Assert.True(AwningSpecification.Dakar.IsValid(3.00, 2.60, 15.0, out string message), message);
            Assert.False(AwningSpecification.Dakar.IsValid(3.00, 3.10, 15.0, out message), "3.10 m projection needs at least 3.50 m");
            Assert.Contains("minimum width", message);
            Assert.Equal(2, AwningSpecification.Dakar.RequiredWallBracketCount(3.00));
        }

        [Fact]
        public void Kolobrzeg_Shared_Canopy_Covers_All_Three_Apertures_From_One_Build()
        {
            List<ApertureSolarTarget> targets = StudiedTargets();
            ApertureShadingGroup group = targets.ApertureShadingGroups(AwningSpecification.Dakar, Extension).Single();

            RetractableAwning awning = new RetractableAwning(2.6, 15.0, 0.0, Extension, 0.0);
            List<ShadingElement> elements = awning.ShadingElements(group.Plane, group.MinX, group.MaxX, group.MaxY);

            // ONE canopy face for the whole group — not one canopy rebuilt around each window.
            Assert.Single(elements);
            ShadingElement canopy = elements[0];
            Assert.Equal("RetractableAwning_Canopy", canopy.Name);

            // The canopy is axis-aligned in the group frame: the planar bounding box is the quad.
            Geometry.Planar.BoundingBox2D boundingBox2D = group.Plane.Convert(canopy.Face3D)?.GetBoundingBox();
            Assert.NotNull(boundingBox2D);

            // The canopy lies in front of the facade: back edge at z = 0, front bar at z = 2.60 m.
            Point3D backLeft = Point(group.Plane, boundingBox2D.Min.X, boundingBox2D.Max.Y, 0.0);
            Point3D backRight = Point(group.Plane, boundingBox2D.Max.X, boundingBox2D.Max.Y, 0.0);
            Point3D frontRight = Point(group.Plane, boundingBox2D.Max.X, boundingBox2D.Min.Y, 2.6);
            Point3D frontLeft = Point(group.Plane, boundingBox2D.Min.X, boundingBox2D.Min.Y, 2.6);

            Assert.True(canopy.Face3D.InRange(backLeft, 1e-6));
            Assert.True(canopy.Face3D.InRange(backRight, 1e-6));
            Assert.True(canopy.Face3D.InRange(frontRight, 1e-6));
            Assert.True(canopy.Face3D.InRange(frontLeft, 1e-6));

            foreach (Point3D corner in new Point3D[] { backLeft, backRight, frontRight, frontLeft })
            {
                Assert.True(Analytical.SolarCalculator.Query.TryGetApertureLocal(group.Targets[0], corner, out double x, out double y, out double z));
                Assert.True(z >= -1e-9, "the canopy must lie in front of the facade");
            }

            // The canopy spans the whole group plus both extensions: it covers every member window.
            Assert.Equal(group.MinX - Extension, boundingBox2D.Min.X, 9);
            Assert.Equal(group.MaxX + Extension, boundingBox2D.Max.X, 9);

            foreach (ApertureSolarTarget target in group.Targets)
            {
                Geometry.Planar.BoundingBox2D memberBox = group.Plane.Convert(target.Face3D)?.GetBoundingBox();
                Assert.True(memberBox.Min.X >= boundingBox2D.Min.X + 1e-9 && memberBox.Max.X <= boundingBox2D.Max.X - 1e-9,
                    "the shared canopy must cover every member aperture across the facade");
            }

            // The back edge hangs at or above the highest head.
            Assert.True(boundingBox2D.Max.Y >= group.MaxY - 1e-9);

            // Deterministic identity: rebuilding the same device reproduces the same element.
            List<ShadingElement> rebuilt = new RetractableAwning(2.6, 15.0, 0.0, Extension, 0.0).ShadingElements(group.Plane, group.MinX, group.MaxX, group.MaxY);
            Assert.Equal(elements[0].Guid, rebuilt[0].Guid);
            Assert.Equal(elements[0].Area, rebuilt[0].Area, 9);
        }

        private static Point3D Point(Plane plane, double x, double y, double z)
        {
            return new Point3D(
                plane.Origin.X + x * plane.AxisX.X + y * plane.AxisY.X + z * plane.Normal.X,
                plane.Origin.Y + x * plane.AxisX.Y + y * plane.AxisY.Y + z * plane.Normal.Y,
                plane.Origin.Z + x * plane.AxisX.Z + y * plane.AxisY.Z + z * plane.Normal.Z);
        }

        [Fact]
        public void Kolobrzeg_Valance_Adds_Exactly_One_Face()
        {
            List<ApertureSolarTarget> targets = StudiedTargets();
            ApertureShadingGroup group = targets.ApertureShadingGroups(AwningSpecification.Dakar, Extension).Single();

            RetractableAwning withoutValance = new RetractableAwning(2.6, 15.0, 0.0, Extension, 0.0);
            RetractableAwning withValance = new RetractableAwning(2.6, 15.0, 0.0, Extension, 0.21);

            Assert.Single(withoutValance.ShadingElements(group.Plane, group.MinX, group.MaxX, group.MaxY));

            List<ShadingElement> valanced = withValance.ShadingElements(group.Plane, group.MinX, group.MaxX, group.MaxY);
            Assert.Equal(2, valanced.Count);
            Assert.Equal("RetractableAwning_Valance", valanced[1].Name);
            Assert.True(valanced[1].Area > 0);
        }

        // ------------------------------------- B. the real optimisation (LongRunning) ----

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Kolobrzeg_Optimisation_Selects_Projection_And_Tilt_Deterministically()
        {
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            List<Guid> apertureGuids = StudiedGuids();

            GroupedAwningResult Run()
            {
                List<GroupedAwningResult> results = Analytical.SolarCalculator.Create.AwningGroupResults(
                    analyticalModel, apertureGuids, year, out string message, out bool _,
                    specification: AwningSpecification.Dakar,
                    projection: null,          // left to the analysis
                    tiltDegrees: null,         // left to the analysis (5°-40°)
                    riseAboveHead: 0.0,
                    extensionBeyondJambs: Extension,
                    valanceDepth: 0.0,
                    maximumGap: 0.20,
                    headTolerance: 0.02,
                    gridSize: KolobrzegFixture.HistoricalGridSize,
                    sunAngleStep: 2.0,
                    recalculate: false,
                    objective: new ShadingObjective(1.0, 0.1),
                    maximumEvaluations: 400);

                Assert.Null(message);
                Assert.NotNull(results);
                Assert.Single(results);
                return results[0];
            }

            GroupedAwningResult first = Run();
            GroupedAwningResult second = Run();

            // One physical awning for the three apertures.
            Assert.Equal(3.00, first.Width, 6);
            Assert.Equal(HostWallGuid, first.Group.PanelGuid);
            Assert.Equal(new HashSet<Guid>(apertureGuids), new HashSet<Guid>(first.Group.ApertureGuids));

            // The run must have genuinely searched: distinct awning candidates were measured.
            Assert.True(first.Evaluations > 0);
            Assert.NotEqual(ShadingDesignStatus.NotEvaluated, first.Status);

            // Every evaluated candidate is a Dakar projection valid for the 3.00 m group width —
            // 1.60 m, 2.10 m and 2.60 m — with a tilt inside the 5°-40° product range.
            Assert.NotEmpty(first.EvaluatedCandidates);
            foreach (AwningSearchCandidate candidate in first.EvaluatedCandidates)
            {
                Assert.Contains(candidate.Projection, AwningSpecification.Dakar.AllowedProjections);
                Assert.True(candidate.Projection <= 2.6 + 1e-9, "3.00 m width admits projections up to 2.60 m only");
                Assert.InRange(candidate.TiltDegrees, AwningSpecification.Dakar.MinimumTiltDegrees, AwningSpecification.Dakar.MaximumTiltDegrees);
            }

            if (first.Status == ShadingDesignStatus.NoShading)
            {
                // A successful measured answer: the null device, a real baseline, and no candidate
                // that beat zero.
                Assert.True(first.Device.IsNoShading);
                Assert.NotNull(first.BestCandidateDevice);
                Assert.NotNull(first.Performance);
                Assert.True(first.Performance.AdmittedDirectEnergy > 0);
                Assert.Equal(0.0, first.Performance.DirectSolarIntercepted, 9);
                foreach (AwningSearchCandidate candidate in first.EvaluatedCandidates)
                {
                    Assert.False(candidate.Score > 1e-9, "a NO SHADE verdict must not hide a better evaluated candidate");
                }

                output.WriteLine($"Kołobrzeg awning answer: NO SHADE — best candidate scored {first.EvaluatedCandidates[0].Score:0.###} kWh");
            }
            else
            {
                // The recommendation: a Dakar awning with a projection and tilt selected by the analysis.
                Assert.Equal(ShadingDesignStatus.Ok, first.Status);
                Assert.False(first.Device.IsNoShading);
                Assert.Equal("RetractableAwning", first.Device.TypologyName);
                Assert.Contains(first.Projection, AwningSpecification.Dakar.AllowedProjections);
                Assert.True(first.Projection <= 2.6 + 1e-9);
                Assert.InRange(first.TiltDegrees, AwningSpecification.Dakar.MinimumTiltDegrees, AwningSpecification.Dakar.MaximumTiltDegrees);

                // The selected tilt is a design outcome on the awning-specific 1° granularity.
                Assert.Equal(Math.Round(first.TiltDegrees), first.TiltDegrees, 9);

                // Brackets follow the product table for the 3.00 m width.
                Assert.Equal(2, first.RequiredWallBrackets);

                // The winner heads the search record, and no evaluated candidate beats it.
                Assert.Equal(first.Projection, first.EvaluatedCandidates[0].Projection, 9);
                Assert.Equal(first.TiltDegrees, first.EvaluatedCandidates[0].TiltDegrees, 9);
                double winnerScore = first.EvaluatedCandidates[0].Score;
                foreach (AwningSearchCandidate candidate in first.EvaluatedCandidates)
                {
                    Assert.False(candidate.Score > winnerScore + 1e-9,
                        $"evaluated candidate ({candidate.Projection} m, {candidate.TiltDegrees}°) outscores the reported winner");
                }

                // The reported typology rebuilds the reported geometry: same element Guids, same
                // shared device area.
                RetractableAwning reported = new RetractableAwning(first.Projection, first.TiltDegrees, 0.0, Extension, first.ValanceDepth);
                List<ShadingElement> rebuiltElements = reported.ShadingElements(first.Group.Plane, first.Group.MinX, first.Group.MaxX, first.Group.MaxY);

                HashSet<Guid> rebuiltGuids = new HashSet<Guid>(rebuiltElements.Select(x => x.Guid));
                Assert.Equal(rebuiltGuids, new HashSet<Guid>(first.Performance.EnergyPerElement.Keys));
                Assert.Equal(first.Performance.SharedDeviceArea, rebuiltElements.Sum(x => x.Area), 9);

                // Every member keeps its own aperture GUID and reconciles its own accounting.
                Assert.Equal(3, first.Performance.PerAperture.Count);
                foreach (ShadingPerformance member in first.Performance.PerAperture)
                {
                    Assert.Contains(member.ApertureGuid, apertureGuids);
                    Assert.Equal(member.DirectSolarIntercepted, member.ReconciledInterceptedEnergy, 9);
                }

                output.WriteLine($"Kołobrzeg awning answer: Projection {first.Projection:0.##} m, TiltDegrees {first.TiltDegrees:0.#}°, " +
                    $"valance {first.ValanceDepth:0.##} m, {first.Evaluations} candidates, score {winnerScore:0.###} kWh, " +
                    $"unwanted blocked {100 * first.Performance.UnwantedSolarBlocked:0.#} %, wanted retained {100 * first.Performance.WantedSolarRetained:0.#} %");
            }

            // Determinism: identical inputs give the identical answer.
            Assert.Equal(first.Status, second.Status);
            Assert.Equal(first.Group.GroupGuid, second.Group.GroupGuid);
            Assert.Equal(first.Projection, second.Projection, 9);
            Assert.Equal(first.TiltDegrees, second.TiltDegrees, 9);
            Assert.Equal(first.ValanceDepth, second.ValanceDepth, 9);
            Assert.Equal(first.Width, second.Width, 9);
            Assert.Equal(first.Device.TypologyName, second.Device.TypologyName);
            if (first.Performance != null)
            {
                Assert.Equal(first.Performance.AdmittedDirectEnergy, second.Performance.AdmittedDirectEnergy, 9);
                Assert.Equal(first.Performance.DirectSolarIntercepted, second.Performance.DirectSolarIntercepted, 9);
                Assert.Equal(first.Performance.UnwantedSolarIntercepted, second.Performance.UnwantedSolarIntercepted, 9);
            }

            output.WriteLine(first.DesignSummary);
        }

        // --------------------------------- D. the operation profile validation table ----

        /// <summary>
        /// The full-year operation of the studied device, printed as the validation table this PR
        /// set out to produce. Everything except the two PR-#19 sanity anchors (site daylight,
        /// sun on the tall aperture) is COMPUTED by the test from the fixture — nothing is quoted.
        ///
        /// THE WIND LIMIT AND ITS PROVENANCE. 10.6 m/s = 38 km/h. SELT's Declaration of Performance
        /// puts a Dakar of width up to 4.10 m and projection up to 3.10 m in wind resistance
        /// Class 2 (84 Pa), which SELT describes as resistance up to 38 km/h
        /// (https://www.selt.com/dakar-en). The Kołobrzeg unit — 3.0 m wide, 1.6 m projection —
        /// falls in that row.
        ///
        /// THIS VALUE IS FOR THE VALIDATION FIXTURE ONLY. MaximumWindSpeed stays a fully
        /// configurable input everywhere else and is never hard-coded into production code, defaults
        /// or the Grasshopper component. It is an ANNUAL WEATHER-BASED OPERATIONAL ASSUMPTION derived
        /// from a declared wind class — not a certified sensor retraction setpoint, not a local gust
        /// verification, and not a structural check.
        ///
        /// AT THIS LIMIT THE DEVICE IS NEVER RETRACTED. The 2018 Kołobrzeg demand hours carry
        /// recorded wind between 1 and 6 m/s, so none of them exceed 10.6 m/s. This test asserts
        /// DeviceWindRetractedHours == 0 explicitly, so a reader does not mistake this scenario for
        /// one that exercised wind retraction; the retraction pathway is exercised by the dedicated
        /// validation-threshold scenario next to this one.
        /// </summary>
        [Fact]
        [Trait("Category", "LongRunning")]
        public void Kolobrzeg_Operation_Profile_Reports_The_Device_Year_At_Ten_Point_Six_Metres_Per_Second()
        {
            const double windLimit = 10.6;

            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            Assert.NotNull(weatherData);

            List<ApertureSolarTarget> targets = StudiedTargets();

            // One 3.00 m Dakar (0.15 m side extension), fully deployed position, standard valance.
            ApertureShadingGroup group = targets.ApertureShadingGroups(AwningSpecification.Dakar, Extension).Single();
            Assert.Equal(3.00, group.Width + 2.0 * Extension, 6);

            SolarControlSettings settings = new SolarControlSettings(200.0, double.NaN, windLimit);

            List<SolarControlProfile> profiles = new List<SolarControlProfile>();
            foreach (ApertureSolarTarget target in group.Targets)
            {
                profiles.Add(target.SolarControlProfile(weatherData, settings, year));
            }

            ApertureSolarContext context = Analytical.SolarCalculator.Create.ApertureSolarContext(
                analyticalModel, year, weatherData, StudiedGuids(), KolobrzegFixture.HistoricalGridSize, 2.0);
            Assert.NotNull(context);

            List<int> offsets = new List<int>();
            foreach (Guid guid in group.ApertureGuids)
            {
                offsets.Add(context.CellIndexOffset(guid));
            }

            GroupedShadingOperationProfile device = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                group, profiles, context.SolarVisibilityCache, context.ContextOccluders,
                new RetractableAwning(1.6, 28.0, 0.0, Extension, 0.21), weatherData, out string message, offsets);

            Assert.NotNull(device);
            Assert.Null(message);

            // PR #19 sanity anchors, reproduced from the fixture: the site's daylight and the sun
            // on the tall aperture.
            Assert.Equal(4391, profiles[0].DaylightHours);
            Assert.Equal(1784, profiles.Single(x => x.ApertureGuid == KolobrzegFixture.TallApertureGuid).ApertureSunHours);

            // The device headline is the union of the member schedules, and the partition holds.
            List<int> demand = new List<int>();
            List<int> deployed = new List<int>();
            List<int> windRetracted = new List<int>();
            foreach (SolarControlProfile profile in profiles)
            {
                demand.AddRange(profile.ShadeDemandHoursOfYear);
                deployed.AddRange(profile.ShadeOnHoursOfYear);
                windRetracted.AddRange(profile.HighWindHoursOfYear);
            }

            Assert.Equal(new HashSet<int>(demand), new HashSet<int>(device.DeviceDemandHoursOfYear));
            Assert.Equal(new HashSet<int>(deployed), new HashSet<int>(device.DeviceDeployedHoursOfYear));
            Assert.Equal(new HashSet<int>(windRetracted), new HashSet<int>(device.DeviceWindRetractedHoursOfYear));
            Assert.Equal(device.DeviceDemandHours, device.DeviceDeployedHours + device.DeviceWindRetractedHours);
            Assert.Empty(device.DeviceDeployedHoursOfYear.Intersect(device.DeviceWindRetractedHoursOfYear));

            // At the Dakar 10.6 m/s fixture assumption the 2018 Kołobrzeg demand hours never exceed
            // the operational wind limit (recorded demand-hour wind is 1-6 m/s): nothing is retracted.
            // Asserted, not assumed, so a reader cannot mistake this for a retraction exercise.
            Assert.Equal(0, device.DeviceWindRetractedHours);

            // The controlled interception can never exceed the always-deployed reference.
            Assert.True(device.ControlledUnwantedSolarIntercepted <= device.UncontrolledUnwantedSolarIntercepted + 1e-9);

            // The valance is real on this WSW façade: effective hours and first-hit energy.
            Assert.True(device.ValanceEffectiveHours > 0, "the valance must be effective on the WSW façade");
            Assert.True(device.ValanceAttributedEnergy > 0, "the valance must intercept energy in the current geometry");

            output.WriteLine("257.5 deg | Dakar Retractable Awning");
            output.WriteLine("3.0 m wide | 1.6 m projection | 28 deg tilt | 0.21 m valance");
            output.WriteLine(string.Empty);
            output.WriteLine($"Site daylight                     {profiles[0].DaylightHours} h");
            output.WriteLine($"Sun on member apertures           {string.Join(" / ", profiles.Select(x => x.ApertureSunHours))} h");
            output.WriteLine($"Device shading requested          {device.DeviceDemandHours} h");
            output.WriteLine($"Device deployed                   {device.DeviceDeployedHours} h");
            output.WriteLine($"Wind-retracted @ 10.6 m/s         {device.DeviceWindRetractedHours} h");
            output.WriteLine($"Shade use                         {100.0 * device.DeviceShadeUseFraction:0.#} %");
            output.WriteLine(string.Empty);
            output.WriteLine($"Canopy effective                  {device.CanopyEffectiveHours} h");
            output.WriteLine($"Valance effective                 {device.ValanceEffectiveHours} h");
            output.WriteLine($"Valance attributed energy         {device.ValanceAttributedEnergy:0.##} kWh");
            output.WriteLine(string.Empty);
            output.WriteLine($"Controlled unwanted intercepted   {device.ControlledUnwantedSolarIntercepted:0.##} kWh");
            output.WriteLine($"Always-deployed unwanted          {device.UncontrolledUnwantedSolarIntercepted:0.##} kWh");
            output.WriteLine(device.ToString());
        }

        /// <summary>
        /// The SAME Kołobrzeg device under a validation-only wind threshold chosen to make the wind
        /// pathway actually bite, so the retraction accounting is proven on the real project weather
        /// rather than only on synthetic weather.
        ///
        /// THE THRESHOLD IS VALIDATION-ONLY. 5.0 m/s is NOT a Dakar (or any other) product rating. It
        /// was picked from the fixture itself: the 137 demand hours carry recorded wind between 1 and
        /// 6 m/s, and 5.0 m/s sits inside that range so it retracts a real minority of the demand
        /// (9 hours) while leaving most of it deployed. It exists solely to exercise the retraction
        /// pathway on real weather, and must never become a production default.
        /// </summary>
        [Fact]
        [Trait("Category", "LongRunning")]
        public void Kolobrzeg_Operation_Profile_Retracts_Under_A_Validation_Only_Wind_Threshold()
        {
            const double validationWindLimit = 5.0;

            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);

            List<ApertureSolarTarget> targets = StudiedTargets();
            ApertureShadingGroup group = targets.ApertureShadingGroups(AwningSpecification.Dakar, Extension).Single();

            // One shared solar context and one device geometry serve both scenarios; only the control
            // rule (the wind limit) differs, so the two measurements are otherwise identical.
            ApertureSolarContext context = Analytical.SolarCalculator.Create.ApertureSolarContext(
                analyticalModel, year, weatherData, StudiedGuids(), KolobrzegFixture.HistoricalGridSize, 2.0);

            List<int> offsets = new List<int>();
            foreach (Guid guid in group.ApertureGuids)
            {
                offsets.Add(context.CellIndexOffset(guid));
            }

            RetractableAwning awning = new RetractableAwning(1.6, 28.0, 0.0, Extension, 0.21);

            GroupedShadingOperationProfile noBite = GroupedDevice(group, weatherData, context, offsets, awning, 10.6, year);
            GroupedShadingOperationProfile bite = GroupedDevice(group, weatherData, context, offsets, awning, validationWindLimit, year);

            // The threshold must bite, but not retract everything.
            Assert.True(bite.DeviceWindRetractedHours > 0, "the validation threshold must retract the device");
            Assert.True(bite.DeviceDeployedHours > 0, "the device must still be deployed in some hours");
            Assert.True(bite.DeviceWindRetractedHours < bite.DeviceDemandHours, "retraction must be a minority of the request");
            Assert.True(bite.DeviceDeployedHours < noBite.DeviceDeployedHours, "retraction must cut the deployed hours versus the non-biting scenario");

            // The partition identity holds on real weather: requested = deployed + wind-retracted, disjointly.
            Assert.Empty(bite.DeviceDeployedHoursOfYear.Intersect(bite.DeviceWindRetractedHoursOfYear));
            Assert.Equal(bite.DeviceDemandHours, bite.DeviceDeployedHours + bite.DeviceWindRetractedHours);

            // Retraction must reduce the unwanted energy the control actually achieves versus the
            // always-deployed reference — the whole point of the wind pathway.
            Assert.True(bite.ControlledUnwantedSolarIntercepted < bite.UncontrolledUnwantedSolarIntercepted,
                "retracted hours must reduce the unwanted solar the control intercepts");

            // F1 semantics on real weather: the full-year direct/element attribution channels are
            // geometric and must NOT move with the wind limit, for identical geometry and weather.
            Assert.Equal(noBite.ControlledDirectSolarIntercepted, bite.ControlledDirectSolarIntercepted, 6);
            Assert.Equal(noBite.CanopyAttributedEnergy, bite.CanopyAttributedEnergy, 6);
            Assert.Equal(noBite.ValanceAttributedEnergy, bite.ValanceAttributedEnergy, 6);
            Assert.Equal(noBite.EnergyPerElement.Keys.OrderBy(x => x), bite.EnergyPerElement.Keys.OrderBy(x => x));
            foreach (Guid guid in noBite.EnergyPerElement.Keys)
            {
                Assert.Equal(noBite.EnergyPerElement[guid], bite.EnergyPerElement[guid], 6);
            }

            output.WriteLine($"validation threshold {validationWindLimit} m/s: requested {bite.DeviceDemandHours} h, deployed {bite.DeviceDeployedHours} h, wind-retracted {bite.DeviceWindRetractedHours} h");
            output.WriteLine($"controlled unwanted {bite.ControlledUnwantedSolarIntercepted:0.##} kWh vs always-deployed {bite.UncontrolledUnwantedSolarIntercepted:0.##} kWh");
        }

        private static GroupedShadingOperationProfile GroupedDevice(
            ApertureShadingGroup group,
            WeatherData weatherData,
            ApertureSolarContext context,
            List<int> offsets,
            RetractableAwning awning,
            double windLimit,
            int year)
        {
            List<SolarControlProfile> profiles = new List<SolarControlProfile>();
            foreach (ApertureSolarTarget target in group.Targets)
            {
                profiles.Add(target.SolarControlProfile(weatherData, new SolarControlSettings(200.0, double.NaN, windLimit), year));
            }

            GroupedShadingOperationProfile device = Analytical.SolarCalculator.Create.GroupedShadingOperationProfile(
                group, profiles, context.SolarVisibilityCache, context.ContextOccluders,
                awning, weatherData, out string message, offsets);

            Assert.NotNull(device);
            Assert.Null(message);
            return device;
        }

        // ------------------------------------- C. old positional call compatibility ----
        [Fact]
        [Trait("Category", "LongRunning")]
        public void Kolobrzeg_Old_Positional_Call_Maps_The_Trailing_Integer_To_MaximumEvaluations()
        {
            // The pre-mounting-offset signature ended in (..., valanceDepth, maximumGap, headTolerance,
            // gridSize, sunAngleStep, recalculate, objective, maximumEvaluations). A caller compiled
            // against it passes every argument positionally, so the trailing 400 must mean
            // maximumEvaluations = 400, never mountingOffset = 400 m.
            AnalyticalModel analyticalModel = KolobrzegFixture.Model();
            int year = KolobrzegFixture.Year(analyticalModel);
            List<Guid> apertureGuids = StudiedGuids();

            List<GroupedAwningResult> results = Analytical.SolarCalculator.Create.AwningGroupResults(
                analyticalModel, apertureGuids, year, out string message, out bool _,
                null, null, null, null, AwningSpecification.Dakar,
                null, null, 0.0, Extension, 0.0, 0.20, 0.02,
                KolobrzegFixture.HistoricalGridSize, 2.0, false, new ShadingObjective(1.0, 0.1), 400);

            Assert.Null(message);
            Assert.NotNull(results);
            Assert.Single(results);

            // The trailing 400 must NOT have become a 400 m mounting offset.
            Assert.Equal(0.0, results[0].MountingOffset, 9);
            Assert.True(results[0].Evaluations > 0);
            Assert.NotEqual(ShadingDesignStatus.NotEvaluated, results[0].Status);

            output.WriteLine($"positional Kołobrzeg answer: {results[0].DesignSummary}");
        }
    }
}

