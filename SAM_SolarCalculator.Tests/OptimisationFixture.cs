// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
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
    /// Shared scenarios for the Stage 9 validation cases.
    ///
    /// Visibility caches are the expensive part of every case and are identical between tests that
    /// share a scenario, so they are built once and memoised. Nothing MUTABLE is shared: each
    /// accessor hands back the same immutable-by-use cache and desirability, and every test builds
    /// its own candidates.
    ///
    /// The sun-angle step stays at the accepted 2 degree default throughout. Test cost is
    /// controlled by keeping the apertures small and the evaluation budgets explicit, not by
    /// coarsening the physics.
    /// </summary>
    public class OptimisationFixture
    {
        public const int Year = 2018;
        public const double SunAngleStep = 2.0;

        private static readonly object padlock = new object();
        private static readonly Dictionary<string, Scenario> scenarios = new Dictionary<string, Scenario>();

        public class Scenario
        {
            public ApertureSolarTarget Target;
            public SolarVisibilityCache BaseCache;
            public ApertureDesirability Desirability;
            public List<LinkedFace3D> Context;
        }

        /// <summary>South-facing window (outward normal (0,-1,0)), 2 m x 1 m, sill at z = 1.</summary>
        public static Face3D SouthWindow()
        {
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 1),
                new Point3D(2, 0, 1),
                new Point3D(2, 0, 2),
                new Point3D(0, 0, 2),
            }));
        }

        /// <summary>East-facing window (azimuth 90), 2 m x 1 m, sill at z = 1: oblique morning sun.</summary>
        public static Face3D EastWindow()
        {
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 1),
                new Point3D(0, 2, 1),
                new Point3D(0, 2, 2),
                new Point3D(0, 0, 2),
            }));
        }

        /// <summary>North-facing window (azimuth 0), 2 m x 1 m: almost no high sun at all.</summary>
        public static Face3D NorthWindow()
        {
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 1),
                new Point3D(0, 0, 2),
                new Point3D(2, 0, 2),
                new Point3D(2, 0, 1),
            }));
        }

        /// <summary>
        /// A wide horizontal slab 2.5 m above the head of a south window, projecting 6 m out — a
        /// balcony or a deep soffit. It pre-blocks everything above roughly 27 degrees of profile
        /// angle, which is precisely the high summer sun an overhang exists to catch. What survives
        /// is low-profile sun that no device above the head can reach, so this is the context that
        /// tests whether the optimiser will buy material for energy that is not there.
        /// </summary>
        public static List<LinkedFace3D> HighSlabContext()
        {
            return new List<LinkedFace3D>
            {
                new LinkedFace3D(new Guid("aaaaaaaa-0000-0000-0000-000000000001"), new Face3D(new Polygon3D(new List<Point3D>
                {
                    new Point3D(-6, 0, 4.5),
                    new Point3D(8, 0, 4.5),
                    new Point3D(8, -6, 4.5),
                    new Point3D(-6, -6, 4.5),
                }))),
            };
        }

        public static IDesirabilityStrategy SummerUnwantedWinterWanted()
        {
            return new SeasonalDesirability(new AnalysisPeriod(Year, 6, 1, 8, 31), new AnalysisPeriod(Year, 11, 1, 2, 28));
        }

        public static IDesirabilityStrategy EverythingWanted()
        {
            return new SeasonalDesirability(null, new AnalysisPeriod(Year));
        }

        public static IDesirabilityStrategy EverythingUnwanted()
        {
            return new SeasonalDesirability(new AnalysisPeriod(Year), null);
        }

        /// <summary>One August day unwanted, nothing wanted: the narrow-brief regime.</summary>
        public static IDesirabilityStrategy OneDayUnwanted()
        {
            return new SeasonalDesirability(new AnalysisPeriod(Year, 8, 1, 8, 1), null);
        }

        /// <summary>One August day unwanted, one January day wanted: both periods narrow.</summary>
        public static IDesirabilityStrategy OneDayUnwantedOneDayWanted()
        {
            return new SeasonalDesirability(new AnalysisPeriod(Year, 8, 1, 8, 1), new AnalysisPeriod(Year, 1, 15, 1, 15));
        }

        /// <summary>
        /// A memoised scenario. The key must capture everything that changes the result, or two
        /// different scenarios would silently share one cache.
        /// </summary>
        /// <param name="sunAngleStep">
        /// Angular resolution of the sun grouping, degrees. Defaults to the accepted 2°. Stage 11's
        /// grouping-bias study varies it deliberately; nothing else should.
        /// </param>
        public static Scenario Get(string key, Func<Face3D> face, Func<List<LinkedFace3D>> context, Func<IDesirabilityStrategy> strategy, double gridSize = 0.25, double peakGlobal = 900.0, double sunAngleStep = SunAngleStep)
        {
            lock (padlock)
            {
                if (scenarios.TryGetValue(key, out Scenario existing))
                {
                    return existing;
                }

                Face3D face3D = face();
                List<LinkedFace3D> occluders = context == null ? new List<LinkedFace3D>() : context();

                ApertureSolarTarget target = new ApertureSolarTarget(
                    new Guid("bbbbbbbb-0000-0000-0000-000000000001"),
                    new Guid("bbbbbbbb-0000-0000-0000-000000000002"),
                    face3D,
                    SAM.Geometry.SolarCalculator.Query.AnalysisCells(face3D, gridSize));

                SolarVisibilityCache baseCache = Weather.SolarCalculator.Create.SolarVisibilityCache(
                    TestHelpers.London(), Year, sunAngleStep, occluders, target.AnalysisCells,
                    cellSize: gridSize, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);

                WeatherData weatherData = TestHelpers.SolarSymmetricWeatherData(Year, TestHelpers.London(), 30.0, peakGlobal, 0.2);

                ApertureDesirability desirability = Analytical.SolarCalculator.Create.ApertureDesirability(
                    target, baseCache, strategy(), weatherData);

                Scenario result = new Scenario
                {
                    Target = target,
                    BaseCache = baseCache,
                    Desirability = desirability,
                    Context = occluders,
                };

                scenarios[key] = result;
                return result;
            }
        }

        public static Scenario SouthSeasonal()
        {
            return Get("south-seasonal", SouthWindow, null, SummerUnwantedWinterWanted);
        }

        public static Scenario EastSeasonal()
        {
            return Get("east-seasonal", EastWindow, null, SummerUnwantedWinterWanted);
        }

        public static Scenario NorthSeasonal()
        {
            return Get("north-seasonal", NorthWindow, null, SummerUnwantedWinterWanted);
        }

        public static Scenario SouthBlocked()
        {
            return Get("south-blocked", SouthWindow, HighSlabContext, SummerUnwantedWinterWanted);
        }

        public static Scenario SouthAllWanted()
        {
            return Get("south-all-wanted", SouthWindow, null, EverythingWanted);
        }

        public static Scenario SouthAllUnwanted()
        {
            return Get("south-all-unwanted", SouthWindow, null, EverythingUnwanted);
        }

        public static Scenario SouthOneDayUnwanted()
        {
            return Get("south-one-day-unwanted", SouthWindow, null, OneDayUnwanted);
        }

        public static Scenario SouthOneDayBoth()
        {
            return Get("south-one-day-both", SouthWindow, null, OneDayUnwantedOneDayWanted);
        }
    }
}
