// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using SAM.Analytical;
using SAM.Analytical.SolarCalculator;
using SAM.Core.SolarCalculator;
using SAM.Weather;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// The Kołobrzeg office fixture — a REAL exported project model, kept as a validation fixture
    /// so the resolution findings discovered on it stay enforced as the code evolves.
    ///
    /// WHAT IT IS. A real office project in Kołobrzeg, Poland, with real weather
    /// (POL_ZP_Kolobrzeg, IMGW) embedded. Three WSW-facing apertures were studied across analysis
    /// grids:
    ///
    ///   344d86d7-3390-4857-bf63-3f2c18a6529d   ~0.90 x 2.25 m   ~2.025 m2   (the "tall" aperture)
    ///   6dee4371-95b8-4789-b33a-af0fc9c1bf05   ~1.20 x 1.39 m   ~1.668 m2   (the "mid" aperture)
    ///   f4d03ee2-a83e-41d5-b5c0-8643838482d2   ~0.60 x 1.39 m   ~0.834 m2   (the "small" aperture)
    ///
    /// at the historical/default 0.5 m grid these produce 10, 9 and 6 analysis cells respectively.
    ///
    /// WHY IT MUST NOT BE USED TO TUNE. This fixture exists to VALIDATE the production pipeline and
    /// to preserve the numerical-resolution evidence found on it. It is one project: nothing here
    /// may be used to tune the optimiser, the grid rules, the guidance constants or any other
    /// production behaviour specifically to Kołobrzeg. Tests must assert the engineering
    /// conclusions (family/decision behaviour, artefact magnitude), not incidental decimals.
    /// </summary>
    public class KolobrzegFixture
    {
        public const string FixtureFileName = "KolobrzegOffice.sam";

        /// <summary>The default analysis grid the study was originally run at.</summary>
        public const double HistoricalGridSize = 0.5;

        public static readonly Guid TallApertureGuid = new Guid("344d86d7-3390-4857-bf63-3f2c18a6529d");
        public static readonly Guid MidApertureGuid = new Guid("6dee4371-95b8-4789-b33a-af0fc9c1bf05");
        public static readonly Guid SmallApertureGuid = new Guid("f4d03ee2-a83e-41d5-b5c0-8643838482d2");

        /// <summary>
        /// The studied apertures in study order, with the geometry the fixture must keep. The spans
        /// are the local planar bounding-box extents (across x up), the same mechanism the
        /// production resolution rules use.
        /// </summary>
        public class Expected
        {
            public Expected(Guid guid, double spanAcross, double spanUp, double area, int cellsAtHalfMetre)
            {
                Guid = guid;
                SpanAcross = spanAcross;
                SpanUp = spanUp;
                Area = area;
                CellsAtHalfMetre = cellsAtHalfMetre;
            }

            public Guid Guid { get; }

            public double SpanAcross { get; }

            public double SpanUp { get; }

            public double Area { get; }

            public int CellsAtHalfMetre { get; }
        }

        public static readonly List<Expected> StudiedApertures = new List<Expected>
        {
            new Expected(TallApertureGuid, 0.90, 2.25, 2.025, 10),
            new Expected(MidApertureGuid, 1.20, 1.39, 1.668, 9),
            new Expected(SmallApertureGuid, 0.60, 1.39, 0.834, 6),
        };

        private static readonly object padlock = new object();
        private static AnalyticalModel model;
        private static readonly Dictionary<string, ApertureShadingSetup> setups = new Dictionary<string, ApertureShadingSetup>();
        private static readonly Dictionary<string, List<OptimisedShadingResult>> optimisations = new Dictionary<string, List<OptimisedShadingResult>>();

        /// <summary>The fixture model, loaded once per process.</summary>
        public static AnalyticalModel Model()
        {
            lock (padlock)
            {
                if (model != null)
                {
                    return model;
                }

                string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", FixtureFileName);
                Assert.True(File.Exists(path), $"Test fixture missing: {path}");

                model = SAM.Core.Convert.ToSAM<AnalyticalModel>(path)?.FirstOrDefault(x => x != null);
                Assert.NotNull(model);
                return model;
            }
        }

        /// <summary>The weather year the embedded weather carries.</summary>
        public static int Year(AnalyticalModel analyticalModel)
        {
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            Assert.NotNull(weatherData);
            return weatherData.WeatherYears.First(x => x != null).Year;
        }

        /// <summary>
        /// One aperture ready for the shading stages, through the same production entry point
        /// Grasshopper's RationaliseShading uses — the whole model is the context, all analysable
        /// apertures share the one cell space, and the default summer-unwanted / winter-wanted brief
        /// applies. Memoised per aperture and grid; the expensive solar context is identical between
        /// tests that share a setup.
        /// </summary>
        public static ApertureShadingSetup Setup(Guid apertureGuid, double gridSize)
        {
            string key = apertureGuid + "@" + gridSize;

            lock (padlock)
            {
                if (setups.TryGetValue(key, out ApertureShadingSetup existing))
                {
                    return existing;
                }

                ApertureShadingSetup setup = Analytical.SolarCalculator.Create.ApertureShadingSetup(
                    Model(), apertureGuid, Year(Model()), null, null, null, null, null, gridSize, 2.0);

                Assert.NotNull(setup);
                setups[key] = setup;
                return setup;
            }
        }

        /// <summary>
        /// The optimiser run Grasshopper performs on this aperture and grid: every family searched
        /// under the default objective and budget, best first. Memoised because the search is
        /// deterministic and several tests read the same results.
        /// </summary>
        public static List<OptimisedShadingResult> Optimise(Guid apertureGuid, double gridSize)
        {
            string key = apertureGuid + "@" + gridSize;

            lock (padlock)
            {
                if (optimisations.TryGetValue(key, out List<OptimisedShadingResult> existing))
                {
                    return existing;
                }

                ApertureShadingSetup setup = Setup(apertureGuid, gridSize);

                List<OptimisedShadingResult> results = Analytical.SolarCalculator.Optimise.ShadingDevice(
                    setup.Target, setup.Context.SolarVisibilityCache, setup.Desirability,
                    setup.Context.ContextOccluders, new ShadingObjective(1.0, 0.1), null, null, 400, setup.CellIndexOffset);

                Assert.NotNull(results);
                Assert.NotEmpty(results);

                optimisations[key] = results;
                return results;
            }
        }
    }
}
