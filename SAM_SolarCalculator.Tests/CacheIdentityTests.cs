// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical;
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
    /// T4 — cache identity, both directions.
    ///
    /// NEGATIVE (must invalidate): reordered analysis targets/cells, a flipped analysis face,
    /// a time-zone change, a sun-time-convention change, and geometry changes.
    /// POSITIVE (must NOT invalidate): reordered occluder enumeration, a re-wound but geometrically
    /// identical polygon, a weather swap, and an AnalysisPeriod change.
    ///
    /// The positive half matters as much as the negative one: order sensitivity was introduced to
    /// fix stale-cache false negatives, and this suite pins that it was NOT applied where geometry
    /// is genuinely order-independent.
    /// </summary>
    public class CacheIdentityTests
    {
        private readonly ITestOutputHelper output;

        public CacheIdentityTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private const int Year = 2018;

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

        private static Location London(string timeZone)
        {
            Location location = new Location("London", -0.1278, 51.5074, 0);
            location.SetValue(LocationParameter.TimeZone, timeZone);
            return location;
        }

        /// <summary>Two side-by-side south-facing panels, so cell ORDER is a meaningful variable.</summary>
        private static List<AnalysisCell> TwoPanelCells(bool secondFirst)
        {
            List<AnalysisCell> a = SyntheticTargets.Cell(SyntheticTargets.South, new Point3D(0, 0, 5));
            List<AnalysisCell> b = SyntheticTargets.Cell(SyntheticTargets.South, new Point3D(3, 0, 5));

            List<AnalysisCell> result = new List<AnalysisCell>();
            result.AddRange(secondFirst ? b : a);
            result.AddRange(secondFirst ? a : b);
            return result;
        }

        private static List<LinkedFace3D> TwoOccluders(bool reversed)
        {
            List<LinkedFace3D> result = new List<LinkedFace3D>
            {
                new LinkedFace3D(Guid.NewGuid(), SyntheticTargets.Face(SyntheticTargets.Up, new Point3D(0, -1, 7), 4.0)),
                new LinkedFace3D(Guid.NewGuid(), SyntheticTargets.Face(SyntheticTargets.Up, new Point3D(3, -1, 7), 4.0)),
            };

            if (reversed)
            {
                result.Reverse();
            }

            return result;
        }

        [Fact]
        public void T4_Reordered_Analysis_Cells_Invalidate_The_Cache()
        {
            // The lit bitsets and the view-factor arrays are indexed by cell POSITION, so the same
            // geometry in a different order is a different cache, not a reusable one.
            List<AnalysisCell> ordered = TwoPanelCells(false);
            List<AnalysisCell> reordered = TwoPanelCells(true);
            Assert.Equal(ordered.Count, reordered.Count);

            string hashOrdered = Geometry.SolarCalculator.Query.TargetHash(ordered);
            string hashReordered = Geometry.SolarCalculator.Query.TargetHash(reordered);
            output.WriteLine($"target hash ordered   = {hashOrdered.Substring(0, 16)}...");
            output.WriteLine($"target hash reordered = {hashReordered.Substring(0, 16)}...");
            Assert.NotEqual(hashOrdered, hashReordered);

            SolarVisibilityCache cache = Weather.SolarCalculator.Create.SolarVisibilityCache(
                London("UTC+00:00"), Year, 5.0, TwoOccluders(false), ordered, 1.0);
            Assert.True(Matches(cache, ordered, TwoOccluders(false), London("UTC+00:00")));
            Assert.False(Matches(cache, reordered, TwoOccluders(false), London("UTC+00:00")),
                "a permuted cell order must NOT match the cache");

            SkyVisibilityCache skyCache = Weather.SolarCalculator.Create.SkyVisibilityCache(TwoOccluders(false), ordered, 1.0);
            Assert.True(SkyMatches(skyCache, ordered, TwoOccluders(false)));
            Assert.False(SkyMatches(skyCache, reordered, TwoOccluders(false)));
        }

        [Fact]
        public void T4_Flipped_Analysis_Face_Invalidates_The_Cache()
        {
            // A flipped face reverses the outward normal: the front-facing test, the incidence angle
            // and every view factor change, while the vertex set does not. An order- and
            // orientation-blind hash would silently reuse an inverted cache.
            Face3D face = SyntheticTargets.Face(SyntheticTargets.South);
            Face3D flipped = Core.Query.Clone(face);
            flipped.FlipNormal(true);

            Assert.True(face.GetPlane().Normal.DotProduct(flipped.GetPlane().Normal) < 0, "the fixture must actually be flipped");

            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(face, 1.0);
            List<AnalysisCell> flippedCells = Geometry.SolarCalculator.Query.AnalysisCells(flipped, 1.0);
            Assert.Equal(cells.Count, flippedCells.Count);

            string hash = Geometry.SolarCalculator.Query.TargetHash(cells);
            string flippedHash = Geometry.SolarCalculator.Query.TargetHash(flippedCells);
            output.WriteLine($"target hash south-facing = {hash.Substring(0, 16)}...");
            output.WriteLine($"target hash flipped      = {flippedHash.Substring(0, 16)}...");
            Assert.NotEqual(hash, flippedHash);

            List<LinkedFace3D> noOccluders = new List<LinkedFace3D>();
            SolarVisibilityCache cache = Weather.SolarCalculator.Create.SolarVisibilityCache(London("UTC+00:00"), Year, 5.0, noOccluders, cells, 1.0);
            Assert.True(Matches(cache, cells, noOccluders, London("UTC+00:00")));
            Assert.False(Matches(cache, flippedCells, noOccluders, London("UTC+00:00")), "a flipped analysis face must NOT match the cache");

            // And the flip really does change the answer, so the invalidation is load-bearing.
            SolarVisibilityCache flippedCache = Weather.SolarCalculator.Create.SolarVisibilityCache(London("UTC+00:00"), Year, 5.0, noOccluders, flippedCells, 1.0);
            int litSouth = LitCount(cache);
            int litNorth = LitCount(flippedCache);
            output.WriteLine($"lit (bin x cell) pairs: south-facing={litSouth} flipped(north-facing)={litNorth}");
            Assert.True(litSouth > litNorth, "the flipped (north-facing) cell must see fewer sun groups");
        }

        [Fact]
        public void T4_TimeZone_Change_Invalidates_The_Cache()
        {
            // Same latitude, longitude and year, different UTC offset: the sun path against the
            // local clock moves by a full hour, so the sun groups and their member hours differ.
            Location utc0 = London("UTC+00:00");
            Location utc1 = London("UTC+01:00");
            Assert.Equal(0.0, Geometry.SolarCalculator.Query.TimeZoneOffset(utc0));
            Assert.Equal(1.0, Geometry.SolarCalculator.Query.TimeZoneOffset(utc1));

            List<AnalysisCell> cells = SyntheticTargets.Cell(SyntheticTargets.South);
            List<LinkedFace3D> noOccluders = new List<LinkedFace3D>();

            SolarVisibilityCache cache = Weather.SolarCalculator.Create.SolarVisibilityCache(utc0, Year, 5.0, noOccluders, cells, 1.0);
            Assert.True(Matches(cache, cells, noOccluders, utc0));
            Assert.False(Matches(cache, cells, noOccluders, utc1), "a time-zone change must NOT match the cache");

            // A fractional offset must be distinguishable from its rounded neighbours too.
            Location utc0530 = London("UTC+05:30");
            Location utc0500 = London("UTC+05:00");
            Assert.Equal(5.5, Geometry.SolarCalculator.Query.TimeZoneOffset(utc0530));
            SolarVisibilityCache cache0530 = Weather.SolarCalculator.Create.SolarVisibilityCache(utc0530, Year, 5.0, noOccluders, cells, 1.0);
            Assert.True(Matches(cache0530, cells, noOccluders, utc0530));
            Assert.False(Matches(cache0530, cells, noOccluders, utc0500), "UTC+05:30 must not collide with UTC+05:00");

            output.WriteLine($"identity UTC+00 = {Short(cache.GetIdentity())}");
            output.WriteLine($"identity UTC+05:30 = {Short(cache0530.GetIdentity())}");
            Assert.NotEqual(cache.GetIdentity(), cache0530.GetIdentity());
        }

        [Fact]
        public void T4_SunTimeConvention_Change_Invalidates_The_Cache()
        {
            List<AnalysisCell> cells = SyntheticTargets.Cell(SyntheticTargets.South);
            List<LinkedFace3D> noOccluders = new List<LinkedFace3D>();
            Location location = London("UTC+00:00");

            SolarVisibilityCache intervalStart = Weather.SolarCalculator.Create.SolarVisibilityCache(location, Year, 5.0, noOccluders, cells, 1.0, Core.Tolerance.Angle, 30.0);
            Assert.True(Matches(intervalStart, cells, noOccluders, location, 30.0));
            Assert.False(Matches(intervalStart, cells, noOccluders, location, -30.0), "IntervalEnd must not reuse an IntervalStart cache");
            Assert.False(Matches(intervalStart, cells, noOccluders, location, 0.0), "OnTheHour must not reuse an IntervalStart cache");
        }

        [Fact]
        public void T4_Reordered_Occluders_Do_NOT_Invalidate_The_Cache()
        {
            // The counter-check. Occlusion is a set property: which faces block a ray does not
            // depend on the order they were enumerated in, and the cache stores nothing indexed by
            // occluder position. Enumerating them the other way round must reuse, not rebuild.
            List<AnalysisCell> cells = TwoPanelCells(false);
            List<LinkedFace3D> occluders = TwoOccluders(false);
            List<LinkedFace3D> reversed = TwoOccluders(true);

            // Distinct Guids on both sides: identity must depend on the geometry, not on the
            // accidental identity of the LinkedFace3D wrappers.
            Assert.NotEqual(occluders[0].Guid, reversed[0].Guid);
            Assert.Equal(
                Geometry.SolarCalculator.Query.GeometryHash(occluders),
                Geometry.SolarCalculator.Query.GeometryHash(reversed));

            Location location = London("UTC+00:00");
            SolarVisibilityCache cache = Weather.SolarCalculator.Create.SolarVisibilityCache(location, Year, 5.0, occluders, cells, 1.0);
            Assert.True(Matches(cache, cells, reversed, location), "reordered occluders must still match the cache");

            SkyVisibilityCache skyCache = Weather.SolarCalculator.Create.SkyVisibilityCache(occluders, cells, 1.0);
            Assert.True(SkyMatches(skyCache, cells, reversed));

            // A geometrically identical but re-wound (start-index-shifted) analysis polygon must
            // also still match: the canonical ring rotation exists precisely so that authoring
            // trivia does not force a rebuild.
            Face3D face = SyntheticTargets.Face(SyntheticTargets.South, new Point3D(0, 0, 5));
            List<Point3D> points = ((ISegmentable3D)face.GetExternalEdge3D()).GetPoints();
            List<Point3D> rotated = new List<Point3D>();
            for (int i = 0; i < points.Count; i++)
            {
                rotated.Add(points[(i + 2) % points.Count]);
            }

            Face3D rewound = new Face3D(new Polygon3D(rotated));
            if (rewound.GetPlane().Normal.DotProduct(face.GetPlane().Normal) < 0)
            {
                rewound.FlipNormal(true);
            }

            Assert.Equal(
                Geometry.SolarCalculator.Query.TargetHash(Geometry.SolarCalculator.Query.AnalysisCells(face, 1.0)),
                Geometry.SolarCalculator.Query.TargetHash(Geometry.SolarCalculator.Query.AnalysisCells(rewound, 1.0)));

            // A geometry change, on the other hand, must invalidate.
            List<LinkedFace3D> moved = new List<LinkedFace3D>
            {
                new LinkedFace3D(Guid.NewGuid(), SyntheticTargets.Face(SyntheticTargets.Up, new Point3D(0, -1, 7.25), 4.0)),
                new LinkedFace3D(Guid.NewGuid(), SyntheticTargets.Face(SyntheticTargets.Up, new Point3D(3, -1, 7), 4.0)),
            };
            Assert.False(Matches(cache, cells, moved, location), "a moved occluder must invalidate the cache");
        }

        [Fact]
        public void T4_Weather_And_Period_Changes_Do_NOT_Invalidate_The_Cache()
        {
            // End-to-end on the real model: an AnalysisPeriod change and a WeatherData swap (same
            // site, same year, same convention) must both reuse the previous calculation, while a
            // grid-size change must not.
            AnalyticalModel analyticalModel = Load("ModelB-NoShadeSolarSimulation.sam");
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            int year = weatherData.WeatherYears.First(x => x != null).Year;
            Guid apertureGuid = analyticalModel.ApertureSolarTargets(null, 0.5)[0].ApertureGuid;
            List<Guid> selection = new List<Guid> { apertureGuid };

            analyticalModel.SimulateApertures(new AnalysisPeriod(year), out bool first, null, selection, 0.5);
            Assert.False(first);

            analyticalModel.SimulateApertures(new AnalysisPeriod(year, 6, 1, 8, 31), out bool afterPeriodChange, null, selection, 0.5);
            Assert.True(afterPeriodChange, "an AnalysisPeriod change must reuse the previous calculation");

            WeatherData synthetic = TestHelpers.SolarSymmetricWeatherData(year, weatherData.Location ?? analyticalModel.Location, 30.0);
            analyticalModel.SimulateApertures(new AnalysisPeriod(year), out bool afterWeatherSwap, synthetic, selection, 0.5);
            Assert.True(afterWeatherSwap, "a WeatherData swap must reuse the previous calculation");

            analyticalModel.SimulateApertures(new AnalysisPeriod(year), out bool afterGridChange, null, selection, 0.25);
            Assert.False(afterGridChange, "a grid-size change must rebuild");

            // And the explicit override forces a rebuild even when everything matches.
            analyticalModel.SimulateApertures(new AnalysisPeriod(year), out bool afterRecalculate, null, selection, 0.25, SkyModel.PerezAnisotropic, 2.0, true);
            Assert.False(afterRecalculate, "recalculate: true must force a rebuild");
        }

        [Fact]
        public void T4_Supplied_Weather_Takes_Precedence_Over_The_Model_Weather()
        {
            // The Grasshopper contract, enforced in the API rather than in the component: supplied
            // WeatherData wins; otherwise the model's own is used; with neither, no result.
            AnalyticalModel analyticalModel = Load("ModelB-NoShadeSolarSimulation.sam");
            WeatherData modelWeather = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            int year = modelWeather.WeatherYears.First(x => x != null).Year;
            Guid apertureGuid = analyticalModel.ApertureSolarTargets(null, 0.5)[0].ApertureGuid;
            List<Guid> selection = new List<Guid> { apertureGuid };
            AnalysisPeriod period = new AnalysisPeriod(year);

            List<ApertureIrradianceResult> fromModel = analyticalModel.SimulateApertures(period, out _, null, selection, 0.5);

            // A deliberately dark synthetic year: if the supplied weather is honoured the result is
            // zero, if the model's own weather leaked through it is not.
            WeatherData dark = TestHelpers.SyntheticWeatherData(year, modelWeather.Location ?? analyticalModel.Location, dateTime => Tuple.Create(0.0, 0.0, 0.0));
            List<ApertureIrradianceResult> fromSupplied = analyticalModel.SimulateApertures(period, out _, dark, selection, 0.5);

            output.WriteLine($"annual kWh: model weather={fromModel[0].TotalEnergy:0.###} supplied dark weather={fromSupplied[0].TotalEnergy:0.###}");
            Assert.True(fromModel[0].TotalEnergy > 1.0);
            Assert.Equal(0.0, fromSupplied[0].TotalEnergy, 9);

            // No weather anywhere -> no result (the component turns this into an actionable error).
            AnalyticalModel withoutWeather = Load("ModelB-WithShadeSolarSimulation.sam");
            Assert.Null(withoutWeather.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData));
            Assert.Null(withoutWeather.SimulateApertures(period, out bool reused));
            Assert.False(reused);
        }

        private static bool Matches(SolarVisibilityCache cache, List<AnalysisCell> cells, List<LinkedFace3D> occluders, Location location, double sunPositionShiftInMinutes = 0)
        {
            return cache.Matches(
                Geometry.SolarCalculator.Query.GeometryHash(occluders, Core.Tolerance.Distance),
                Geometry.SolarCalculator.Query.TargetHash(cells, Core.Tolerance.Distance),
                1.0, 5.0, Core.Tolerance.Angle,
                Core.Tolerance.MacroDistance, Core.Tolerance.MacroDistance, Core.Tolerance.Angle, Core.Tolerance.Distance,
                location.Latitude, location.Longitude, Geometry.SolarCalculator.Query.TimeZoneOffset(location),
                sunPositionShiftInMinutes, Year, cells.Count);
        }

        private static bool SkyMatches(SkyVisibilityCache cache, List<AnalysisCell> cells, List<LinkedFace3D> occluders)
        {
            return cache.Matches(
                Geometry.SolarCalculator.Query.GeometryHash(occluders, Core.Tolerance.Distance),
                Geometry.SolarCalculator.Query.TargetHash(cells, Core.Tolerance.Distance),
                1.0, SkyPatchSubdivision.Tregenza145,
                Core.Tolerance.MacroDistance, Core.Tolerance.MacroDistance, Core.Tolerance.Angle, Core.Tolerance.Distance,
                cells.Count);
        }

        private static int LitCount(SolarVisibilityCache cache)
        {
            int count = 0;
            for (int b = 0; b < cache.BinCount; b++)
            {
                for (int c = 0; c < cache.CellCount; c++)
                {
                    if (cache.IsLit(b, c))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static string Short(string identity)
        {
            return identity.Length <= 90 ? identity : identity.Substring(0, 40) + " ... " + identity.Substring(identity.Length - 45);
        }
    }
}
