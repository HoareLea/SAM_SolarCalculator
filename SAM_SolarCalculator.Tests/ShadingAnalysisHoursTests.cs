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
    /// The analysis-hour accounting (I15): the counts balance, the unwanted/wanted split comes from
    /// per-hour weights rather than from sun bins (the mixed-bin trap), a leap year reports 8784,
    /// and the context obstruction separates what the surroundings remove.
    /// </summary>
    public class ShadingAnalysisHoursTests
    {
        private const int Year = 2018;

        private static ApertureSolarTarget Target(Guid guid, Vector3D outward, double centreX)
        {
            SAM.Geometry.Spatial.Face3D face = SyntheticTargets.Face(outward, new Point3D(centreX, 0, 5), 1.0, 2.0);
            return new ApertureSolarTarget(guid, new Guid("fffffff1-0000-0000-0000-000000000001"), face, Geometry.SolarCalculator.Query.AnalysisCells(face, 0.5));
        }

        private class Fixture
        {
            public ApertureSolarContext Context;
            public List<ApertureDesirability> Desirabilities;
            public IDesirabilityStrategy Strategy;
        }

        private static Fixture Build(List<ApertureSolarTarget> targets, List<SAM.Geometry.Object.Spatial.LinkedFace3D> context = null, int year = Year, double sunAngleStep = 2.0)
        {
            WeatherData weatherData = TestHelpers.SolarSymmetricWeatherData(year, TestHelpers.London(), 30.0);
            IDesirabilityStrategy strategy = new SeasonalDesirability(new AnalysisPeriod(year, 6, 1, 8, 31), new AnalysisPeriod(year, 11, 1, 2, 28));

            List<AnalysisCell> cells = new List<AnalysisCell>();
            List<Vector3D> normals = new List<Vector3D>();
            foreach (ApertureSolarTarget target in targets)
            {
                cells.AddRange(target.AnalysisCells);
                normals.AddRange(target.AnalysisCells.ConvertAll(x => new Vector3D(target.OutwardNormal)));
            }

            List<SAM.Geometry.Object.Spatial.LinkedFace3D> occluders = context ?? new List<SAM.Geometry.Object.Spatial.LinkedFace3D>();
            SolarVisibilityCache cache = Weather.SolarCalculator.Create.SolarVisibilityCache(
                TestHelpers.London(), year, sunAngleStep, occluders, cells,
                cellSize: 0.5, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);

            string contextHash = Geometry.SolarCalculator.Query.GeometryHash(occluders, Core.Tolerance.Distance);
            string targetHash = Geometry.SolarCalculator.Query.TargetHash(cells, Core.Tolerance.Distance);

            ApertureSolarContext solarContext = new ApertureSolarContext(
                new List<ApertureSolarTarget>(targets), cells, normals,
                occluders, cache, null, weatherData, TestHelpers.London(), year, 0.5, sunAngleStep, 30.0, contextHash, targetHash, false);

            List<ApertureDesirability> desirabilities = new List<ApertureDesirability>();
            foreach (ApertureSolarTarget target in targets)
            {
                desirabilities.Add(Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, strategy, weatherData));
            }

            return new Fixture { Context = solarContext, Desirabilities = desirabilities, Strategy = strategy };
        }

        [Fact]
        public void Hour_Counts_Balance()
        {
            ApertureSolarTarget south = Target(new Guid("eeeeeee1-0000-0000-0000-000000000001"), SyntheticTargets.South, 0.0);
            ApertureSolarTarget east = Target(new Guid("eeeeeee1-0000-0000-0000-000000000002"), SyntheticTargets.East, 10.0);
            Fixture fixture = Build(new List<ApertureSolarTarget> { south, east });

            ShadingAnalysisHours hours = Analytical.SolarCalculator.Create.ShadingAnalysisHours(fixture.Context, fixture.Desirabilities, fixture.Strategy);

            Assert.Equal(hours.EvaluatedHours + hours.MissingWeatherHours, hours.TimelineHours);
            Assert.Equal(hours.UnwantedHours + hours.WantedHours + hours.NeutralHours, hours.BeamAdmittingHours);
            Assert.True(hours.BeamAdmittingHours <= hours.FacadeIncidentHours);
            Assert.True(hours.FacadeIncidentHours <= hours.SunUpHours);
            Assert.True(hours.SunUpHours <= hours.EvaluatedHours);
            Assert.Equal(hours.FacadeIncidentHours - hours.BeamAdmittingHours, hours.ContextObstructedHours);
            Assert.True(hours.ContextObstructedHours >= 0);
        }

        [Fact]
        public void A_Leap_Analysis_Year_Reports_8784()
        {
            ApertureSolarTarget south = Target(new Guid("eeeeeee1-0000-0000-0000-000000000001"), SyntheticTargets.South, 0.0);
            Fixture fixture = Build(new List<ApertureSolarTarget> { south }, year: 2020);

            ShadingAnalysisHours hours = Analytical.SolarCalculator.Create.ShadingAnalysisHours(fixture.Context, fixture.Desirabilities, fixture.Strategy);
            Assert.Equal(8784, hours.TimelineHours);
        }

        [Fact]
        public void With_No_Context_The_Surroundings_Remove_Nothing()
        {
            ApertureSolarTarget south = Target(new Guid("eeeeeee1-0000-0000-0000-000000000001"), SyntheticTargets.South, 0.0);
            Fixture fixture = Build(new List<ApertureSolarTarget> { south });

            ShadingAnalysisHours hours = Analytical.SolarCalculator.Create.ShadingAnalysisHours(fixture.Context, fixture.Desirabilities, fixture.Strategy);
            Assert.Equal(0, hours.ContextObstructedHours);
            Assert.Equal(0.0, hours.ContextObstructedEnergy, 9);
            Assert.Equal(hours.FacadeIncidentHours, hours.BeamAdmittingHours);
        }

        [Fact]
        public void An_Aperture_Facing_Away_Contributes_No_Incident_Hours()
        {
            ApertureSolarTarget south = Target(new Guid("eeeeeee1-0000-0000-0000-000000000001"), SyntheticTargets.South, 0.0);
            // Facing fully away from the sun: a downward-facing aperture. The sun is never below
            // the horizon gate, so the front-facing test contributes zero incident hours — the
            // geometry decides, not the cache.
            ApertureSolarTarget down = Target(new Guid("eeeeeee1-0000-0000-0000-000000000002"), new Vector3D(0, 0, -1), 10.0);
            Fixture fixture = Build(new List<ApertureSolarTarget> { south, down });

            ShadingAnalysisHours hours = Analytical.SolarCalculator.Create.ShadingAnalysisHours(fixture.Context, fixture.Desirabilities, fixture.Strategy);

            Assert.Equal(0, hours.BeamAdmittingHoursOf(down.ApertureGuid));
            Assert.True(hours.BeamAdmittingHoursOf(south.ApertureGuid) > 0);
        }

        [Fact]
        public void Adding_An_Occluder_Increases_Obstruction()
        {
            ApertureSolarTarget south = Target(new Guid("eeeeeee1-0000-0000-0000-000000000001"), SyntheticTargets.South, 0.0);
            Fixture open = Build(new List<ApertureSolarTarget> { south });

            // A deep soffit above the window (top at z = 6, the soffit at z = 7): the surroundings
            // block the highest bins.
            List<SAM.Geometry.Object.Spatial.LinkedFace3D> occluders = new List<SAM.Geometry.Object.Spatial.LinkedFace3D>
            {
                new SAM.Geometry.Object.Spatial.LinkedFace3D(new Guid("ddddddd1-0000-0000-0000-000000000001"),
                    new SAM.Geometry.Spatial.Face3D(new SAM.Geometry.Spatial.Polygon3D(new List<SAM.Geometry.Spatial.Point3D>
                    {
                        new SAM.Geometry.Spatial.Point3D(-2, -3, 7.0),
                        new SAM.Geometry.Spatial.Point3D(2, -3, 7.0),
                        new SAM.Geometry.Spatial.Point3D(2, 3, 7.0),
                        new SAM.Geometry.Spatial.Point3D(-2, 3, 7.0),
                    }))),
            };

            Fixture blocked = Build(new List<ApertureSolarTarget> { south }, occluders);
            ShadingAnalysisHours openHours = Analytical.SolarCalculator.Create.ShadingAnalysisHours(open.Context, open.Desirabilities, open.Strategy);
            ShadingAnalysisHours blockedHours = Analytical.SolarCalculator.Create.ShadingAnalysisHours(blocked.Context, blocked.Desirabilities, blocked.Strategy);

            Assert.True(blockedHours.ContextObstructedHours > 0, "the soffit must block some incident sun");
            Assert.True(blockedHours.ContextObstructedEnergy > 0);
            Assert.Equal(openHours.FacadeIncidentHours, blockedHours.FacadeIncidentHours);
            Assert.True(blockedHours.BeamAdmittingHours < openHours.BeamAdmittingHours);
        }

        [Fact]
        public void The_Mixed_Bin_Trap_Is_Avoided()
        {
            // One sun bin routinely holds hours from June AND December (same altitude, same
            // azimuth). The unwanted/wanted split must count each hour once, from per-hour weights.
            // A 45° step makes the bins coarse enough that June morning hours and December noon
            // hours share one bin deterministically.
            ApertureSolarTarget south = Target(new Guid("eeeeeee1-0000-0000-0000-000000000001"), SyntheticTargets.South, 0.0);
            Fixture fixture = Build(new List<ApertureSolarTarget> { south }, sunAngleStep: 45.0);

            ShadingAnalysisHours hours = Analytical.SolarCalculator.Create.ShadingAnalysisHours(fixture.Context, fixture.Desirabilities, fixture.Strategy);

            // The default seasonal brief marks only summer (unwanted) and winter (wanted); the
            // spring and autumn beam hours are neutral. The split must partition the beam hours.
            Assert.Equal(hours.BeamAdmittingHours, hours.UnwantedHours + hours.WantedHours + hours.NeutralHours);
            Assert.True(hours.UnwantedHours > 0);
            Assert.True(hours.WantedHours > 0);

            // The sun bin membership itself is mixed: at least one bin contains both a summer hour
            // and a winter hour. A bin-derived count would double-count exactly these bins.
            List<SunBin> bins = fixture.Context.SolarVisibilityCache.Bins;
            int mixedBins = 0;
            foreach (SunBin bin in bins)
            {
                bool hasSummer = false;
                bool hasWinter = false;
                foreach (int hour in bin.HoursOfYear ?? new List<int>())
                {
                    int day = hour / 24;
                    if (day >= 151 && day < 243)
                    {
                        hasSummer = true;
                    }
                    if (day >= 334 || day < 59)
                    {
                        hasWinter = true;
                    }
                }

                if (hasSummer && hasWinter)
                {
                    mixedBins++;
                }
            }

            Assert.True(mixedBins > 0, "the fixture must contain at least one mixed bin for the trap to be exercised");
        }
    }
}
