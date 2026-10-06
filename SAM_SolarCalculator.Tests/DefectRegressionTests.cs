// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using SAM.Core;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Geometry.Object.Spatial;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Regression tests for the pre-existing defects fixed alongside the Stages 0-4 work:
    /// merge == true returning un-merged results, fractional UTC truncation, and the
    /// null-plane dereference in SunExposureFace3Ds.
    /// </summary>
    public class DefectRegressionTests
    {
        private static readonly string FixturesDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");

        private static AnalyticalModel Load(string fileName)
        {
            string path = Path.Combine(FixturesDirectory, fileName);
            Assert.True(File.Exists(path), $"Test fixture missing: {path}");

            List<AnalyticalModel> analyticalModels = SAM.Core.Convert.ToSAM<AnalyticalModel>(path);
            AnalyticalModel analyticalModel = analyticalModels?.FirstOrDefault(x => x != null);
            Assert.NotNull(analyticalModel);
            return analyticalModel;
        }

        [Fact]
        public void Simulate_MergeTrue_Returns_Merged_Results()
        {
            AnalyticalModel analyticalModel = Load("ModelB-SolarSimulation.sam");

            List<DateTime> dateTimes = new List<DateTime>
            {
                new DateTime(2018, 6, 21, 11, 0, 0),
                new DateTime(2018, 6, 21, 12, 0, 0),
            };

            // First run (un-merged) attaches a baseline result per panel.
            List<SolarFaceSimulationResult> first = analyticalModel.Simulate(dateTimes, false);
            Assert.NotNull(first);
            Assert.NotEmpty(first);

            HashSet<Guid> attachedBefore = new HashSet<Guid>(analyticalModel.GetResults<SolarFaceSimulationResult>().Select(x => x.Guid));
            Assert.NotEmpty(attachedBefore);

            // Second run with merge == true: each fresh result is merged into the attached one and
            // the MERGED result is attached. The returned list must be those merged results —
            // previously it returned the un-merged (never attached) list instead.
            List<SolarFaceSimulationResult> second = analyticalModel.Simulate(dateTimes, true);
            Assert.NotNull(second);
            Assert.NotEmpty(second);

            HashSet<Guid> attachedAfter = new HashSet<Guid>(analyticalModel.GetResults<SolarFaceSimulationResult>().Select(x => x.Guid));

            // Every returned result must be attached to the model. Before the fix the returned
            // objects were the raw un-merged results (fresh Guids), which are NOT in the model.
            Assert.All(second, r => Assert.Contains(r.Guid, attachedAfter));

            // And the merged results carry the identity of the previously attached results
            // (Merge keeps the existing result's Guid), so the returned Guids intersect the
            // pre-run attached set — the un-merged raw results never would.
            Assert.True(second.Count(r => attachedBefore.Contains(r.Guid)) > 0,
                "Expected merged results to carry the pre-existing attached results' Guids.");
        }

        [Fact]
        public void SunDirection_FractionalTimeZone_Preserved()
        {
            // UTC+05:30 (India): the fractional 30 minutes must survive into SolarTimes.
            Location location530 = new Location("Kolkata", 88.3639, 22.5726, 0);
            location530.SetValue(LocationParameter.TimeZone, "UTC+05:30");

            Location location500 = new Location("Kolkata", 88.3639, 22.5726, 0);
            location500.SetValue(LocationParameter.TimeZone, "UTC+05:00");

            Assert.Equal(5.5, Geometry.SolarCalculator.Query.TimeZoneOffset(location530));
            Assert.Equal(5.0, Geometry.SolarCalculator.Query.TimeZoneOffset(location500));

            Innovative.SolarCalculator.SolarTimes solarTimes = Geometry.SolarCalculator.Create.SolarTimes(location530, new DateTime(2018, 6, 21, 12, 0, 0));
            Assert.Equal(5.5, (double)solarTimes.TimeZoneOffset, 6);

            // A 30 minute offset shift moves the sun by ~7.5 degrees of hour angle: the two sun
            // positions must differ, and the shift must correspond to ~30 minutes (not 0 or 60).
            DateTime dateTime = new DateTime(2018, 6, 21, 12, 0, 0);
            Vector3D sun530 = Geometry.SolarCalculator.Query.SunDirection(location530, dateTime, true);
            Vector3D sun500 = Geometry.SolarCalculator.Query.SunDirection(location500, dateTime, true);
            Assert.NotNull(sun530);
            Assert.NotNull(sun500);

            double angleDegrees = sun530.SmallestAngle(sun500) * 180.0 / Math.PI;
            Assert.InRange(angleDegrees, 5.0, 10.0);
        }

        [Fact]
        public void TimeZoneOffset_GenuineUTC0_Is_Accepted_Not_NaN()
        {
            // Genuine UTC+00:00 must resolve to a real 0.0, and downstream SolarTimes construction
            // must succeed -- it must never be conflated with "timezone did not resolve".
            Location location = new Location("Greenwich", 0.0, 51.4769, 0);
            location.SetValue(LocationParameter.TimeZone, "UTC+00:00");

            double timeZoneOffset = Geometry.SolarCalculator.Query.TimeZoneOffset(location);
            Assert.False(double.IsNaN(timeZoneOffset));
            Assert.Equal(0.0, timeZoneOffset);

            Innovative.SolarCalculator.SolarTimes solarTimes = Geometry.SolarCalculator.Create.SolarTimes(location, new DateTime(2018, 6, 21, 12, 0, 0));
            Assert.NotNull(solarTimes);
        }

        [Fact]
        public void TimeZoneOffset_Unresolvable_Returns_NaN_And_SolarTimes_Fails_Safely()
        {
            // A Location with no TimeZone parameter at all, and one with a TimeZone string that does
            // not resolve to any known SAM.Core.UTC value, must both be reported as unresolved (NaN)
            // -- never silently defaulted to UTC+00:00 -- and Create.SolarTimes must fail (null)
            // rather than run against a fabricated Greenwich offset.
            Location noTimeZoneParameter = new Location("Nowhere", 0.0, 0.0, 0);

            double offsetForMissingParameter = Geometry.SolarCalculator.Query.TimeZoneOffset(noTimeZoneParameter);
            Assert.True(double.IsNaN(offsetForMissingParameter));
            Assert.Null(Geometry.SolarCalculator.Create.SolarTimes(noTimeZoneParameter, new DateTime(2018, 6, 21, 12, 0, 0)));

            Location unmappedTimeZoneString = new Location("Nowhere", 0.0, 0.0, 0);
            unmappedTimeZoneString.SetValue(LocationParameter.TimeZone, "Not A Real Time Zone");

            double offsetForUnmappedString = Geometry.SolarCalculator.Query.TimeZoneOffset(unmappedTimeZoneString);
            Assert.True(double.IsNaN(offsetForUnmappedString));
            Assert.Null(Geometry.SolarCalculator.Create.SolarTimes(unmappedTimeZoneString, new DateTime(2018, 6, 21, 12, 0, 0)));
        }

        [Fact]
        public void SunExposureFace3Ds_NullExposureList_Returns_Null()
        {
            // No exposure faces at all (the pre-existing guard: face3Ds == null) -> null, must not
            // throw. This exercises the EARLIER guard, distinct from the null-plane guard below.
            Point3D origin = new Point3D(0, 0, 0);
            Face3D face3D = new Face3D(new Polygon3D(new List<Point3D>
            {
                origin,
                new Point3D(1, 0, 0),
                new Point3D(1, 1, 0),
                new Point3D(0, 1, 0),
            }));

            List<Tuple<DateTime, Radiation, List<Face3D>>> sunExposure = new List<Tuple<DateTime, Radiation, List<Face3D>>>
            {
                new Tuple<DateTime, Radiation, List<Face3D>>(new DateTime(2018, 6, 21, 12, 0, 0), null, null),
            };

            SolarFaceSimulationResult result = Geometry.SolarCalculator.Create.SolarFaceSimulationResult(Guid.NewGuid(), face3D, sunExposure);
            Assert.NotNull(result);

            Assert.Null(result.SunExposureFace3Ds(face3D, new DateTime(2018, 6, 21, 12, 0, 0)));
        }

        [Fact]
        public void SunExposureFace3Ds_NullPlane_Returns_Null()
        {
            // A SolarFaceSimulationResult whose sun-exposure list is non-null and non-empty, but
            // every face in it is degenerate (collinear points -> Create.Plane returns null, so
            // Face3D.GetPlane() also returns null), must reach and pass the null-plane guard in
            // SunExposureFace3Ds -- returning null rather than throwing when
            // plane.Coplanar(face3D, tolerance) would otherwise NRE on a null plane. A sun-exposure
            // list containing a null Face3Ds element (the previous version of this test) exits
            // through the earlier "face3Ds == null || face3Ds.Count == 0" guard instead, and never
            // reaches this code path.
            Point3D origin = new Point3D(0, 0, 0);
            Face3D face3D = new Face3D(new Polygon3D(new List<Point3D>
            {
                origin,
                new Point3D(1, 0, 0),
                new Point3D(1, 1, 0),
                new Point3D(0, 1, 0),
            }));

            // Three collinear points: Query.Normal returns null for a collinear point set, so
            // Create.Plane(points) returns null and this Polygon3D (hence the Face3D built on it)
            // carries a null internal plane -- GetPlane() returns null, not a valid degenerate plane.
            Polygon3D degeneratePolygon3D = new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 0),
                new Point3D(1, 0, 0),
                new Point3D(2, 0, 0),
            });
            Assert.Null(degeneratePolygon3D.GetPlane());

            Face3D degenerateFace3D = new Face3D(degeneratePolygon3D);
            Assert.Null(degenerateFace3D.GetPlane());

            List<Tuple<DateTime, Radiation, List<Face3D>>> sunExposure = new List<Tuple<DateTime, Radiation, List<Face3D>>>
            {
                new Tuple<DateTime, Radiation, List<Face3D>>(new DateTime(2018, 6, 21, 12, 0, 0), null, new List<Face3D> { degenerateFace3D }),
            };

            SolarFaceSimulationResult result = Geometry.SolarCalculator.Create.SolarFaceSimulationResult(Guid.NewGuid(), face3D, sunExposure);
            Assert.NotNull(result);

            // GetSunExposureFace3Ds must actually hand back the non-empty degenerate list, confirming
            // the test reaches the null-plane guard rather than the earlier empty-list guard.
            List<Face3D> exposureFace3Ds = result.GetSunExposureFace3Ds(new DateTime(2018, 6, 21, 12, 0, 0));
            Assert.NotNull(exposureFace3Ds);
            Assert.NotEmpty(exposureFace3Ds);

            Exception exception = Record.Exception(() => result.SunExposureFace3Ds(face3D, new DateTime(2018, 6, 21, 12, 0, 0)));
            Assert.Null(exception);
            Assert.Null(result.SunExposureFace3Ds(face3D, new DateTime(2018, 6, 21, 12, 0, 0)));
        }
    }
}
