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
    /// Stage 4 tests: per-aperture irradiance over arbitrary AnalysisPeriods, cache reuse across
    /// period and weather changes, component-aware obstruction, conservation and grid convergence.
    /// </summary>
    public class ApertureIrradianceTests
    {
        private readonly ITestOutputHelper output;

        public ApertureIrradianceTests(ITestOutputHelper output)
        {
            this.output = output;
        }

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

        private static int WeatherYearOf(AnalyticalModel analyticalModel)
        {
            WeatherData weatherData = analyticalModel.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData);
            Assert.NotNull(weatherData);
            WeatherYear weatherYear = weatherData.WeatherYears?.FirstOrDefault(x => x != null);
            Assert.NotNull(weatherYear);
            return weatherYear.Year;
        }

        private static double MeanByAzimuth(List<ApertureIrradianceResult> results, AnalyticalModel analyticalModel, double azimuth)
        {
            List<ApertureSolarTarget> targets = analyticalModel.ApertureSolarTargets(null, 0.5);
            List<double> values = new List<double>();
            foreach (ApertureSolarTarget target in targets)
            {
                if (Math.Abs(target.Azimuth - azimuth) > 1.0)
                {
                    continue;
                }

                ApertureIrradianceResult result = results.Find(x => x.Reference == target.ApertureGuid.ToString());
                if (result != null)
                {
                    values.Add(result.AverageIrradiance);
                }
            }

            Assert.NotEmpty(values);
            return values.Average();
        }

        [Fact]
        public void South_Aperture_Exceeds_North_Annually()
        {
            AnalyticalModel analyticalModel = Load("ModelB-NoShadeSolarSimulation.sam");
            int year = WeatherYearOf(analyticalModel);

            List<ApertureIrradianceResult> results = analyticalModel.SimulateApertures(
                Core.SolarCalculator.Create.AnalysisPeriod(AnalysisPeriodPreset.FullYear, year, analyticalModel.Location),
                out bool reusedPreviousCalculation, gridSize: 0.5, skyModel: SkyModel.PerezAnisotropic);

            Assert.NotNull(results);
            Assert.NotEmpty(results);
            Assert.False(reusedPreviousCalculation);

            double south = MeanByAzimuth(results, analyticalModel, 180);
            double north = MeanByAzimuth(results, analyticalModel, 0);
            double east = MeanByAzimuth(results, analyticalModel, 90);
            double west = MeanByAzimuth(results, analyticalModel, 270);
            output.WriteLine($"annual average kWh/m2: south={south:0.#} north={north:0.#} east={east:0.#} west={west:0.#}");

            Assert.True(south > north, $"south {south:0.#} should exceed north {north:0.#} (northern hemisphere)");

            // Units sanity: a UK vertical facade receives a few hundred kWh/m2 per year.
            Assert.InRange(south, 100, 1500);

            // Component integrity: total = direct + diffuse + ground.
            foreach (ApertureIrradianceResult result in results)
            {
                Assert.Equal(result.TotalEnergy, result.DirectEnergy + result.DiffuseEnergy + result.GroundReflectedEnergy, 6);
                Assert.True(result.DiffuseEnergy > 0);
                Assert.True(result.GroundReflectedEnergy > 0);
            }
        }

        [Fact]
        public void ComplementaryPeriods_Conserve_Energy()
        {
            // Two complementary custom periods whose union is exactly the full year (per the review
            // correction: seasons do not partition the year, so the conservation test uses explicit
            // complementary ranges).
            AnalyticalModel analyticalModel = Load("ModelB-NoShadeSolarSimulation.sam");
            int year = WeatherYearOf(analyticalModel);

            List<ApertureIrradianceResult> full = analyticalModel.SimulateApertures(new AnalysisPeriod(year), out _);
            List<ApertureIrradianceResult> firstHalf = analyticalModel.SimulateApertures(new AnalysisPeriod(year, 1, 1, 6, 30), out _);
            List<ApertureIrradianceResult> secondHalf = analyticalModel.SimulateApertures(new AnalysisPeriod(year, 7, 1, 12, 31), out _);

            Assert.Equal(full.Count, firstHalf.Count);
            foreach (ApertureIrradianceResult fullResult in full)
            {
                ApertureIrradianceResult a = firstHalf.Find(x => x.Reference == fullResult.Reference);
                ApertureIrradianceResult b = secondHalf.Find(x => x.Reference == fullResult.Reference);
                double sum = a.TotalEnergy + b.TotalEnergy;
                double relative = fullResult.TotalEnergy > 1e-9 ? Math.Abs(sum - fullResult.TotalEnergy) / fullResult.TotalEnergy : Math.Abs(sum - fullResult.TotalEnergy);
                Assert.True(relative < 1e-9, $"aperture {fullResult.Reference}: {a.TotalEnergy:0.####} + {b.TotalEnergy:0.####} != {fullResult.TotalEnergy:0.####}");
            }
        }

        [Fact]
        public void PeriodSwitch_Reuses_Cache()
        {
            AnalyticalModel analyticalModel = Load("ModelB-NoShadeSolarSimulation.sam");
            int year = WeatherYearOf(analyticalModel);

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            List<ApertureIrradianceResult> full = analyticalModel.SimulateApertures(new AnalysisPeriod(year), out bool reused_First);
            stopwatch.Stop();
            long buildMs = stopwatch.ElapsedMilliseconds;
            Assert.False(reused_First);

            stopwatch.Restart();
            List<ApertureIrradianceResult> summer = analyticalModel.SimulateApertures(Core.SolarCalculator.Create.AnalysisPeriod(AnalysisPeriodPreset.Summer, year, analyticalModel.Location), out bool reused_Second);
            stopwatch.Stop();
            long reuseMs = stopwatch.ElapsedMilliseconds;

            Assert.True(reused_Second, "second period on the same model must reuse the cache");
            output.WriteLine($"cache build+evaluate: {buildMs} ms; cached re-evaluate (summer): {reuseMs} ms");

            // Different period -> different totals, but results remain consistent per shared hour.
            foreach (ApertureIrradianceResult summerResult in summer)
            {
                ApertureIrradianceResult fullResult = full.Find(x => x.Reference == summerResult.Reference);
                Assert.True(summerResult.TotalEnergy < fullResult.TotalEnergy + 1e-9);
            }

            // A weather change (same site and year) must NOT invalidate the geometric cache.
            WeatherData otherWeather = TestHelpers.SyntheticWeatherData(year, analyticalModel.Location, dt =>
            {
                bool day = dt.Hour >= 6 && dt.Hour <= 20;
                return Tuple.Create(day ? 500.0 : 0.0, day ? 150.0 : 0.0, 0.0);
            });
            analyticalModel.SetValue(AnalyticalModelParameter.WeatherData, otherWeather);

            List<ApertureIrradianceResult> synthetic = analyticalModel.SimulateApertures(new AnalysisPeriod(year), out bool reused_Third);
            Assert.True(reused_Third, "weather swap must not force a geometric rebuild");
            Assert.NotNull(synthetic);
        }

        [Fact]
        public void Shaded_Model_Receives_Less_Than_Unshaded()
        {
            AnalyticalModel unshaded = Load("ModelB-NoShadeSolarSimulation.sam");
            int year = WeatherYearOf(unshaded);

            AnalyticalModel shaded = Load("ModelB-WithShadeSolarSimulation.sam");
            shaded.SetValue(AnalyticalModelParameter.WeatherData, unshaded.GetValue<WeatherData>(AnalyticalModelParameter.WeatherData));

            AnalysisPeriod period = new AnalysisPeriod(year);
            List<ApertureIrradianceResult> unshadedResults = unshaded.SimulateApertures(period, out _);
            List<ApertureIrradianceResult> shadedResults = shaded.SimulateApertures(period, out _);

            // Match apertures by centroid (the two models are a pure +25 m X translation apart).
            double sumUnshaded = 0;
            double sumShaded = 0;
            int matched = 0;
            List<ApertureSolarTarget> unshadedTargets = unshaded.ApertureSolarTargets(null, 0.5);
            List<ApertureSolarTarget> shadedTargets = shaded.ApertureSolarTargets(null, 0.5);
            foreach (ApertureSolarTarget unshadedTarget in unshadedTargets)
            {
                Point3D centroid = unshadedTarget.Face3D.GetCentroid();
                ApertureSolarTarget shadedTarget = shadedTargets.Find(x =>
                {
                    Point3D c = x.Face3D.GetCentroid();
                    return c != null && Math.Abs(c.X - (centroid.X + 25.0)) < 0.5 && Math.Abs(c.Y - centroid.Y) < 0.5 && Math.Abs(c.Z - centroid.Z) < 0.5;
                });
                if (shadedTarget == null)
                {
                    continue;
                }

                ApertureIrradianceResult u = unshadedResults.Find(x => x.Reference == unshadedTarget.ApertureGuid.ToString());
                ApertureIrradianceResult s = shadedResults.Find(x => x.Reference == shadedTarget.ApertureGuid.ToString());
                if (u == null || s == null)
                {
                    continue;
                }

                matched++;
                sumUnshaded += u.TotalEnergy;
                sumShaded += s.TotalEnergy;
                output.WriteLine($"az={unshadedTarget.Azimuth,3:0} unshaded={u.TotalEnergy,7:0.##} kWh shaded={s.TotalEnergy,7:0.##} kWh");
            }

            Assert.Equal(unshadedTargets.Count, matched);
            output.WriteLine($"total: unshaded={sumUnshaded:0} kWh shaded={sumShaded:0} kWh ({100.0 * sumShaded / sumUnshaded:0.#} %)");
            Assert.True(sumShaded < sumUnshaded, $"shaded {sumShaded:0} should be less than unshaded {sumUnshaded:0}");
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void CellSize_Convergence()
        {
            AnalyticalModel analyticalModel = Load("ModelB-NoShadeSolarSimulation.sam");
            int year = WeatherYearOf(analyticalModel);
            AnalysisPeriod period = new AnalysisPeriod(year);

            List<ApertureIrradianceResult> coarse = analyticalModel.SimulateApertures(period, out _, gridSize: 1.0);
            List<ApertureIrradianceResult> medium = analyticalModel.SimulateApertures(period, out _, gridSize: 0.5);
            List<ApertureIrradianceResult> fine = analyticalModel.SimulateApertures(period, out _, gridSize: 0.25);

            foreach (ApertureIrradianceResult m in medium)
            {
                ApertureIrradianceResult c = coarse.Find(x => x.Reference == m.Reference);
                ApertureIrradianceResult f = fine.Find(x => x.Reference == m.Reference);
                double delta = Math.Abs(f.TotalEnergy - m.TotalEnergy) / m.TotalEnergy;
                output.WriteLine($"az-agnostic aperture {m.Reference.Substring(0, 8)}: 1.0m={c.TotalEnergy:0.##} 0.5m={m.TotalEnergy:0.##} 0.25m={f.TotalEnergy:0.##} kWh (delta {delta:0.###})");
                Assert.True(delta < 0.02, $"0.25 m vs 0.5 m cell-size change {delta:0.####} exceeds 2 %");
            }
        }

        [Fact]
        public void Circumsolar_Removed_When_Sun_Obstructed()
        {
            // Component-awareness gate (review correction 3): a thin overhang that blocks the high
            // summer sun must remove BOTH the beam AND the Perez circumsolar diffuse bump, while
            // leaving the isotropic term (SVF) nearly untouched.
            Location location = TestHelpers.London();
            const int year = 2018;

            Face3D window = new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 1), new Point3D(2, 0, 1), new Point3D(2, 0, 2), new Point3D(0, 0, 2),
            }));
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(window, 0.5);
            Vector3D outward = new Vector3D(0, -1, 0);
            List<Vector3D> normals = cells.ConvertAll(x => outward);

            Face3D overhang = new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(-0.5, 0, 2.4), new Point3D(2.5, 0, 2.4), new Point3D(2.5, -1.5, 2.4), new Point3D(-0.5, -1.5, 2.4),
            }));

            // Clear-sky weather for the solstice week: high DNI, low DHI.
            WeatherData weatherData = TestHelpers.SyntheticWeatherData(year, location, dt =>
            {
                bool day = dt.Hour >= 5 && dt.Hour <= 21;
                return Tuple.Create(day ? 850.0 : 0.0, day ? 90.0 : 0.0, 0.0);
            });

            AnalysisPeriod period = new AnalysisPeriod(year, 6, 18, 6, 24);

            CachedIrradianceResult open = RunSynthetic(location, year, cells, normals, new List<LinkedFace3D>(), weatherData, period);
            CachedIrradianceResult shaded = RunSynthetic(location, year, cells, normals, new List<LinkedFace3D> { new LinkedFace3D(Guid.NewGuid(), overhang) }, weatherData, period);

            double openDirect = open.Direct.Sum();
            double shadedDirect = shaded.Direct.Sum();
            double openDiffuse = open.Diffuse.Sum();
            double shadedDiffuse = shaded.Diffuse.Sum();
            output.WriteLine($"solstice week per-cell sums: direct open={openDirect:0.##} shaded={shadedDirect:0.##}; diffuse open={openDiffuse:0.##} shaded={shadedDiffuse:0.##}");

            Assert.True(shadedDirect < 0.35 * openDirect, $"overhang should remove most solstice beam ({openDirect:0.##} -> {shadedDirect:0.##})");
            Assert.True(shadedDiffuse < openDiffuse, "Perez diffuse must drop when the sun is obstructed");

            // Isolation of the circumsolar component: a SMALL plate covering only the sun's disc
            // region for the 11:00-13:00 solstice hours - big enough to block the beam, small enough
            // to leave the sky view factor nearly intact. If diffuse visibility were a single scalar
            // SVF multiplier, the Perez and isotropic diffuse ratios would be equal; they must differ
            // because the F1 term is switched off by the sun-direction lit bit alone.
            Face3D plate = new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, -0.05, 2.82), new Point3D(2, -0.05, 2.82), new Point3D(2, -1.4, 2.82), new Point3D(0, -1.4, 2.82),
            }));

            AnalysisPeriod midday = new AnalysisPeriod(year, 6, 21, 6, 21, 11, 13);

            CachedIrradianceResult openPerez = RunSynthetic(location, year, cells, normals, new List<LinkedFace3D>(), weatherData, midday, SkyModel.PerezAnisotropic);
            CachedIrradianceResult platePerez = RunSynthetic(location, year, cells, normals, new List<LinkedFace3D> { new LinkedFace3D(Guid.NewGuid(), plate) }, weatherData, midday, SkyModel.PerezAnisotropic);
            CachedIrradianceResult openIso = RunSynthetic(location, year, cells, normals, new List<LinkedFace3D>(), weatherData, midday, SkyModel.Isotropic);
            CachedIrradianceResult plateIso = RunSynthetic(location, year, cells, normals, new List<LinkedFace3D> { new LinkedFace3D(Guid.NewGuid(), plate) }, weatherData, midday, SkyModel.Isotropic);

            double openPerezDirect = openPerez.Direct.Sum();
            double platePerezDirect = platePerez.Direct.Sum();
            double perezRatio = platePerez.Diffuse.Sum() / openPerez.Diffuse.Sum();
            double isoRatio = plateIso.Diffuse.Sum() / openIso.Diffuse.Sum();
            output.WriteLine($"midday plate: direct open={openPerezDirect:0.##} plate={platePerezDirect:0.##}; diffuse ratios isotropic={isoRatio:0.###} perez={perezRatio:0.###}");

            Assert.True(platePerezDirect < 0.2 * openPerezDirect, $"plate should block most of the midday beam ({openPerezDirect:0.##} -> {platePerezDirect:0.##})");
            Assert.True(isoRatio > 0.75, $"the small plate should leave SVF nearly intact (isotropic ratio {isoRatio:0.###})");
            Assert.True(perezRatio < isoRatio - 0.1, $"circumsolar removal must drop Perez diffuse well beyond the SVF effect (perez {perezRatio:0.###} vs isotropic {isoRatio:0.###})");
        }

        private static CachedIrradianceResult RunSynthetic(Location location, int year, List<AnalysisCell> cells, List<Vector3D> normals, List<LinkedFace3D> occluders, WeatherData weatherData, AnalysisPeriod period, SkyModel skyModel = SkyModel.PerezAnisotropic, double timeShiftInMinutes = 0)
        {
            SolarVisibilityCache solarCache = Weather.SolarCalculator.Create.SolarVisibilityCache(location, year, 2.0, occluders, cells, 0.5, sunPositionShiftInMinutes: timeShiftInMinutes);
            SkyVisibilityCache skyCache = Weather.SolarCalculator.Create.SkyVisibilityCache(occluders, cells, 0.5);
            return Analytical.SolarCalculator.Query.CachedIrradiance(solarCache, skyCache, weatherData, period, normals, skyModel, 0.2);
        }
    }
}
