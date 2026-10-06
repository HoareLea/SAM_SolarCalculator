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
    /// A SHARED CELL SPACE: several apertures of different orientations whose analysis cells are
    /// flattened into one visibility cache, exactly as Create.ApertureSolarContext does for a real
    /// model.
    ///
    /// Why this fixture had to exist. Every Stage 6-9 scenario before it built its visibility cache
    /// from ONE target's cells, so the cache's cell count and the target's cell count were always
    /// equal and every index into one was a valid index into the other. That is a coincidence of
    /// a single-window test, not a property of the code: the moment a model has two windows the two
    /// spaces differ, and a whole class of indexing mistakes becomes possible that no single-window
    /// test can see. This fixture makes that difference the normal case.
    ///
    /// Each scenario also keeps an ISOLATED cache per target — the same aperture analysed on its
    /// own — because "one aperture of many gives the same answer as that aperture alone" is the
    /// property the shared cell space has to preserve, and it can only be asserted against
    /// something.
    /// </summary>
    public class MultiApertureFixture
    {
        public const int Year = 2018;
        public const double SunAngleStep = 2.0;
        public const double GridSize = 0.5;

        private static readonly object padlock = new object();
        private static Scenario scenario;

        public class Scenario
        {
            /// <summary>Targets in the order their cells occupy the shared cell space.</summary>
            public List<ApertureSolarTarget> Targets;

            /// <summary>Visibility over EVERY target's cells, flattened in target order.</summary>
            public SolarVisibilityCache SharedCache;

            /// <summary>Each target's first cell index within SharedCache.</summary>
            public List<int> Offsets;

            /// <summary>Visibility over one target's cells alone — what the shared answer must match.</summary>
            public List<SolarVisibilityCache> IsolatedCaches;

            /// <summary>Desirability per target, computed against the shared cache's sun groups.</summary>
            public List<ApertureDesirability> Desirabilities;

            /// <summary>Desirability per target against its own isolated cache.</summary>
            public List<ApertureDesirability> IsolatedDesirabilities;

            public List<LinkedFace3D> Context;

            public int TotalCellCount;
        }

        /// <summary>South-facing, 2 m x 1 m, sill at z = 1, at the origin.</summary>
        private static Face3D South()
        {
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 1), new Point3D(2, 0, 1), new Point3D(2, 0, 2), new Point3D(0, 0, 2),
            }));
        }

        /// <summary>East-facing, 2 m x 1 m, well away from the others so nothing is accidentally shared.</summary>
        private static Face3D East()
        {
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(20, 0, 1), new Point3D(20, 2, 1), new Point3D(20, 2, 2), new Point3D(20, 0, 2),
            }));
        }

        /// <summary>North-facing, 2 m x 1 m.</summary>
        private static Face3D North()
        {
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(40, 0, 1), new Point3D(40, 0, 2), new Point3D(42, 0, 2), new Point3D(42, 0, 1),
            }));
        }

        /// <summary>
        /// A deep soffit over the EAST window only.
        ///
        /// It is here to make the three apertures' lit bits genuinely different. With no context at
        /// all every cell is lit whenever it faces the sun, so the three windows' rows of the shared
        /// cache look alike and reading one aperture at another's offset can accidentally produce
        /// the right number — which would make the offset look cosmetic and leave the regression
        /// untested. One window standing in real shade removes that coincidence.
        ///
        /// It sits over the EAST window rather than the south one so the south window remains a
        /// clean, unobstructed case with a genuine shading problem to solve; the tests that need a
        /// real recommended device use that one.
        /// </summary>
        private static List<LinkedFace3D> EastSoffit()
        {
            return new List<LinkedFace3D>
            {
                new LinkedFace3D(new Guid("eeeeeeee-0000-0000-0000-000000000001"), new Face3D(new Polygon3D(new List<Point3D>
                {
                    new Point3D(20, -2, 2.6),
                    new Point3D(20, 4, 2.6),
                    new Point3D(22.5, 4, 2.6),
                    new Point3D(22.5, -2, 2.6),
                }))),
            };
        }

        private static ApertureSolarTarget Target(int ordinal, Face3D face3D)
        {
            return new ApertureSolarTarget(
                new Guid("cccccccc-0000-0000-0000-00000000000" + ordinal),
                new Guid("dddddddd-0000-0000-0000-00000000000" + ordinal),
                face3D,
                Geometry.SolarCalculator.Query.AnalysisCells(face3D, GridSize));
        }

        public static IDesirabilityStrategy SummerUnwantedWinterWanted()
        {
            return new SeasonalDesirability(new AnalysisPeriod(Year, 6, 1, 8, 31), new AnalysisPeriod(Year, 11, 1, 2, 28));
        }

        /// <summary>Three orientations sharing one cell space. Built once; nothing mutable is shared.</summary>
        public static Scenario ThreeOrientations()
        {
            lock (padlock)
            {
                if (scenario != null)
                {
                    return scenario;
                }

                List<ApertureSolarTarget> targets = new List<ApertureSolarTarget>
                {
                    Target(1, South()),
                    Target(2, East()),
                    Target(3, North()),
                };

                List<LinkedFace3D> context = EastSoffit();
                WeatherData weatherData = TestHelpers.SolarSymmetricWeatherData(Year, TestHelpers.London(), 30.0);
                IDesirabilityStrategy strategy = SummerUnwantedWinterWanted();

                // The shared cell space, exactly as Create.ApertureSolarContext builds it.
                List<AnalysisCell> cells = new List<AnalysisCell>();
                List<int> offsets = new List<int>();
                foreach (ApertureSolarTarget target in targets)
                {
                    offsets.Add(cells.Count);
                    cells.AddRange(target.AnalysisCells);
                }

                SolarVisibilityCache sharedCache = Weather.SolarCalculator.Create.SolarVisibilityCache(
                    TestHelpers.London(), Year, SunAngleStep, context, cells,
                    cellSize: GridSize, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);

                List<SolarVisibilityCache> isolatedCaches = new List<SolarVisibilityCache>();
                List<ApertureDesirability> desirabilities = new List<ApertureDesirability>();
                List<ApertureDesirability> isolatedDesirabilities = new List<ApertureDesirability>();

                foreach (ApertureSolarTarget target in targets)
                {
                    SolarVisibilityCache isolated = Weather.SolarCalculator.Create.SolarVisibilityCache(
                        TestHelpers.London(), Year, SunAngleStep, context, target.AnalysisCells,
                        cellSize: GridSize, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);

                    isolatedCaches.Add(isolated);
                    desirabilities.Add(Analytical.SolarCalculator.Create.ApertureDesirability(target, sharedCache, strategy, weatherData));
                    isolatedDesirabilities.Add(Analytical.SolarCalculator.Create.ApertureDesirability(target, isolated, strategy, weatherData));
                }

                scenario = new Scenario
                {
                    Targets = targets,
                    SharedCache = sharedCache,
                    Offsets = offsets,
                    IsolatedCaches = isolatedCaches,
                    Desirabilities = desirabilities,
                    IsolatedDesirabilities = isolatedDesirabilities,
                    Context = context,
                    TotalCellCount = cells.Count,
                };

                return scenario;
            }
        }
    }
}
