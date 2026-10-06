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
    /// Stage 6 tests: the shading potential field. Case A (south overhang), Case B (east side
    /// fin), Case C (existing context), Case D (no unwanted solar), Case E (no wanted solar),
    /// plus determinism, normalisation, JSON and a brute-force independent cross-check of the
    /// DDA traversal, and the profile-angle zero-crossing tie to the analytical shading boundary.
    /// </summary>
    public class ShadingPotentialFieldTests
    {
        private readonly ITestOutputHelper output;

        public ShadingPotentialFieldTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static readonly int Year = 2018;

        /// <summary>South-facing window (outward normal (0,-1,0)), 2 m wide x 1 m high, sill at z = 1.</summary>
        private static Face3D SouthWindowFace()
        {
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 1),
                new Point3D(2, 0, 1),
                new Point3D(2, 0, 2),
                new Point3D(0, 0, 2),
            }));
        }

        /// <summary>East-facing window (outward normal (1,0,0)), 2 m deep (y) x 1 m high, sill at z = 1.</summary>
        private static Face3D EastWindowFace()
        {
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(0, 0, 1),
                new Point3D(0, 2, 1),
                new Point3D(0, 2, 2),
                new Point3D(0, 0, 2),
            }));
        }

        /// <summary>Horizontal context slab, 1.5 m deep, 0.4 m above the south window head.</summary>
        private static Face3D ContextSlabFace()
        {
            return new Face3D(new Polygon3D(new List<Point3D>
            {
                new Point3D(-0.5, 0, 2.4),
                new Point3D(2.5, 0, 2.4),
                new Point3D(2.5, -1.5, 2.4),
                new Point3D(-0.5, -1.5, 2.4),
            }));
        }

        private static ApertureSolarTarget Target(Face3D face, double gridSize = 0.5)
        {
            List<AnalysisCell> cells = Geometry.SolarCalculator.Query.AnalysisCells(face, gridSize);
            return new ApertureSolarTarget(Guid.NewGuid(), Guid.NewGuid(), face, cells);
        }

        private static SolarVisibilityCache Cache(ApertureSolarTarget target, List<LinkedFace3D> occluders, double gridSize = 0.5, double sunAngleStep = 2.0)
        {
            return Weather.SolarCalculator.Create.SolarVisibilityCache(
                TestHelpers.London(), Year, sunAngleStep, occluders, target.AnalysisCells,
                cellSize: gridSize, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);
        }

        private static ApertureDesirability Desirability(ApertureSolarTarget target, SolarVisibilityCache cache, IDesirabilityStrategy strategy)
        {
            WeatherData weatherData = TestHelpers.SolarSymmetricWeatherData(Year, TestHelpers.London(), 30.0, 900.0, 0.2);
            return Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, strategy, weatherData);
        }

        private static SeasonalDesirability SummerUnwantedWinterWanted()
        {
            return new SeasonalDesirability(
                new AnalysisPeriod(Year, 6, 1, 8, 31),
                new AnalysisPeriod(Year, 11, 1, 2, 28));
        }

        private static ShadingVolume DefaultVolume(ApertureSolarTarget target, double voxelSize = 0.1)
        {
            return Analytical.SolarCalculator.Create.ShadingVolume(target, maxDepth: 1.5, up: 1.0, down: 0.2, left: 0.5, right: 0.5, voxelSize: voxelSize);
        }

        /// <summary>
        /// Voxel centre in the APERTURE frame: origin at the aperture centroid, X horizontal along
        /// the facade, Y up-slope, Z outward. This is the frame every assertion below is written
        /// in (a 2 m x 1 m window spans x in [-1, 1], y in [-0.5, 0.5], glass at z = 0).
        ///
        /// It is NOT the ShadingVolume's own frame: the volume's origin is its minimum corner —
        /// aperture-frame (minX - left, minY - down, 0) — so volume.TryToLocal returns coordinates
        /// shifted by the margins and never negative. Reading those as aperture coordinates
        /// silently selects the wrong voxels (a "head" band lands on the glass, and any x &lt; 0
        /// predicate matches nothing at all).
        /// </summary>
        private static bool LocalCentre(ApertureSolarTarget target, ShadingVolume volume, int index, out double x, out double y, out double z)
        {
            x = y = z = double.NaN;
            Point3D centre = volume.GetCentre(index);
            Plane plane = target?.Plane;
            if (centre == null || plane == null)
            {
                return false;
            }

            Point3D origin = plane.Origin;
            double dx = centre.X - origin.X;
            double dy = centre.Y - origin.Y;
            double dz = centre.Z - origin.Z;

            x = dx * plane.AxisX.X + dy * plane.AxisX.Y + dz * plane.AxisX.Z;
            y = dx * plane.AxisY.X + dy * plane.AxisY.Y + dz * plane.AxisY.Z;
            z = dx * plane.Normal.X + dy * plane.Normal.Y + dz * plane.Normal.Z;
            return true;
        }

        [Fact]
        public void CaseA_SouthWindow_OverhangRegion_Dominates_And_WinterPath_Is_Negative()
        {
            ApertureSolarTarget target = Target(SouthWindowFace());
            SolarVisibilityCache cache = Cache(target, new List<LinkedFace3D>());
            ApertureDesirability desirability = Desirability(target, cache, SummerUnwantedWinterWanted());
            ShadingVolume volume = DefaultVolume(target);

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(target, cache, desirability, volume);
            stopwatch.Stop();

            Assert.NotNull(field);
            output.WriteLine($"field: {volume.CountX}x{volume.CountY}x{volume.CountZ} = {volume.VoxelCount} voxels, build {stopwatch.ElapsedMilliseconds} ms");
            output.WriteLine($"total unwanted intercepted {field.TotalUnwantedEnergy:0.###} kWh, wanted intercepted {field.TotalWantedEnergy:0.###} kWh, positive total {field.PositiveTotal():0.###} kWh");

            // Window local frame: x in [-1, 1] (east-west), y in [-0.5, 0.5] (sill-head), z out.
            double overhangScore = 0;   // above the head, shallow depth: the overhang wedge
            double winterPathScore = 0; // in front of the glass at sill level: the low-winter-sun path
            foreach (int index in Enumerable.Range(0, volume.VoxelCount))
            {
                if (!LocalCentre(target, volume, index, out double x, out double y, out double z))
                {
                    continue;
                }

                double score = field.Score(index);
                if (y > 0.6 && y < 1.3 && z < 1.0 && Math.Abs(x) < 1.0)
                {
                    overhangScore += score;
                }

                if (Math.Abs(y) < 0.4 && z < 0.5 && Math.Abs(x) < 1.0)
                {
                    winterPathScore += score;
                }
            }

            output.WriteLine($"overhang-band score {overhangScore:0.###} kWh, in-front winter-path score {winterPathScore:0.###} kWh");

            Assert.True(overhangScore > 0, $"overhang-like region above the window head must carry positive potential, got {overhangScore}");
            Assert.True(winterPathScore < 0, $"shading directly in front of the glass must be net harmful (wanted winter sun), got {winterPathScore}");

            // The positive region's score-weighted mean height must sit ABOVE the window head.
            double weightedY = 0;
            double positiveSum = 0;
            foreach (int index in Enumerable.Range(0, volume.VoxelCount))
            {
                double score = field.Score(index);
                if (score > 0 && LocalCentre(target, volume, index, out _, out double y, out _))
                {
                    weightedY += score * y;
                    positiveSum += score;
                }
            }

            weightedY /= positiveSum;
            output.WriteLine($"positive-score centroid height (local y, head = 0.5): {weightedY:0.###} m");
            Assert.True(weightedY > 0.5, $"positive potential should sit above the window head, got {weightedY}");
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void CaseA_ProfileAngle_ZeroCrossing_Lies_Within_The_Discretisation_Bracket()
        {
            // The profile-angle gate. Along the voxel row just above the window head the benefit
            // must start positive (a shallow element there can only intercept very steep sun, i.e.
            // summer) and cross to negative once it reaches far enough out to start intercepting
            // the low winter sun. The depth of that crossing is the classic Arumi-Noe profile-angle
            // cut-off D = H / tan(VSA_cut), and it is pinned here against TWO independently
            // computed bounds rather than one tolerance.
            //
            // Why a bracket and not a percentage. The closed-form profile integrates over a
            // CONTINUOUS window; the field marches rays from the analysis-cell CENTRES, so the
            // deepest-reaching ray it can ever see aims at the topmost cell centre, not at the
            // window head. Just above the head that difference is first-order: the lever arm to the
            // head is (yRow - 0.5) = 0.026 m but to the topmost cell centre it is
            // (yRow - 0.5 + gridSize/2) = 0.151 m, a 6x difference in the limiting profile angle,
            // which pushes the discrete crossing DEEPER. So the two analytical evaluations below —
            // continuous window, and window truncated to the cell-centre extent — are genuine
            // lower and upper bounds on any cell-sampled field, and the measured crossing must sit
            // between them. A voxel is a finite box rather than a point, so the field recovers part
            // of the continuum and lands inside the bracket rather than at its top.
            //
            // Both bounds are computed here per hour, from sun position and weather directly,
            // touching neither the DDA, nor the sun binning, nor the Stage 5 group aggregation.
            double gridSize = 0.25;
            double voxelSize = 0.05;
            ApertureSolarTarget target = Target(SouthWindowFace(), gridSize);
            SolarVisibilityCache cache = Cache(target, new List<LinkedFace3D>(), gridSize, sunAngleStep: 1.0);
            IDesirabilityStrategy strategy = SummerUnwantedWinterWanted();
            ApertureDesirability desirability = Desirability(target, cache, strategy);
            ShadingVolume volume = DefaultVolume(target, voxelSize);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(target, cache, desirability, volume);
            Assert.NotNull(field);

            double yRow = 0.5 + 0.5 * voxelSize + 0.001; // centre of the first voxel row above the head

            // Continuous window: the physical cut-off a real (infinitely sampled) overhang sees.
            double continuousCrossing = AnalyticalCrossing(target, strategy, yRow, voxelSize, -0.5, 0.5);

            // Window truncated to the cell-centre extent: what a cell-sampled ray set can reach.
            double cellExtent = 0.5 - gridSize / 2.0;
            double cellSampledCrossing = AnalyticalCrossing(target, strategy, yRow, voxelSize, -cellExtent, cellExtent);

            // Measured crossing along the same row of the field.
            double measuredCrossing = double.NaN;
            double previous = double.NaN;
            for (int k = 0; k < volume.CountZ; k++)
            {
                int index = NearestColumn(target, volume, 0.0, yRow, k);
                double score = field.Score(index);
                double depth = (k + 0.5) * voxelSize;
                if (k == 0)
                {
                    Assert.True(score > 0, $"benefit at the head-edge wall voxel should be positive, got {score}");
                }

                if (previous > 0 && score <= 0)
                {
                    measuredCrossing = depth;
                    break;
                }

                previous = score;
            }

            output.WriteLine($"zero-crossing depth: continuous-window bound {continuousCrossing:0.###} m, field {measuredCrossing:0.###} m, cell-sampled bound {cellSampledCrossing:0.###} m");

            Assert.False(double.IsNaN(continuousCrossing), "continuous analytical profile never crosses zero");
            Assert.False(double.IsNaN(cellSampledCrossing), "cell-sampled analytical profile never crosses zero");
            Assert.False(double.IsNaN(measuredCrossing), "field profile never crosses zero");

            output.WriteLine($"implied cut-off profile angle: continuous {Math.Atan((yRow + 0.5) / continuousCrossing) * 180.0 / Math.PI:0.#} deg, field {Math.Atan((yRow + 0.5) / measuredCrossing) * 180.0 / Math.PI:0.#} deg");

            // The bracket must be ordered the way the geometry says it must be.
            Assert.True(cellSampledCrossing > continuousCrossing,
                $"cell-centre truncation must push the cut-off deeper: {cellSampledCrossing:0.###} vs {continuousCrossing:0.###}");

            // One voxel of slack at each end: the field's crossing is itself resolved to voxelSize.
            Assert.InRange(measuredCrossing, continuousCrossing - voxelSize, cellSampledCrossing + voxelSize);

            // Magnitudes must stay in the range a real overhang on a 1 m window occupies.
            Assert.InRange(continuousCrossing, 0.1, 1.5);
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void CaseA_ProfileAngle_Converges_Toward_The_Continuous_Cutoff_As_The_Grid_Refines()
        {
            // The companion to the bracket test: the bracket is wide only because the analysis grid
            // is coarse. Halving gridSize must move the measured cut-off toward the continuous-window
            // value, which is the statement that the field converges on the real physics rather than
            // sitting at some fixed discretisation artefact.
            double voxelSize = 0.05;
            double yRow = 0.5 + 0.5 * voxelSize + 0.001;

            double continuous = AnalyticalCrossing(Target(SouthWindowFace(), 0.25), SummerUnwantedWinterWanted(), yRow, voxelSize, -0.5, 0.5);

            double coarse = MeasuredCrossing(0.25, voxelSize, yRow);
            double fine = MeasuredCrossing(0.125, voxelSize, yRow);

            output.WriteLine($"continuous-window cut-off {continuous:0.###} m; field cut-off at gridSize 0.25 = {coarse:0.###} m, at gridSize 0.125 = {fine:0.###} m");
            output.WriteLine($"error vs continuum: coarse {Math.Abs(coarse - continuous):0.###} m, fine {Math.Abs(fine - continuous):0.###} m");

            Assert.False(double.IsNaN(coarse));
            Assert.False(double.IsNaN(fine));
            Assert.True(Math.Abs(fine - continuous) < Math.Abs(coarse - continuous),
                $"refining the analysis grid must move the cut-off toward the continuum: coarse {coarse:0.###}, fine {fine:0.###}, continuum {continuous:0.###}");
        }

        /// <summary>Measured field zero-crossing along the row just above the head, for one gridSize.</summary>
        private double MeasuredCrossing(double gridSize, double voxelSize, double yRow)
        {
            ApertureSolarTarget target = Target(SouthWindowFace(), gridSize);
            SolarVisibilityCache cache = Cache(target, new List<LinkedFace3D>(), gridSize, sunAngleStep: 1.0);
            ApertureDesirability desirability = Desirability(target, cache, SummerUnwantedWinterWanted());
            ShadingVolume volume = DefaultVolume(target, voxelSize);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(target, cache, desirability, volume);

            double previous = double.NaN;
            for (int k = 0; k < volume.CountZ; k++)
            {
                double score = field.Score(NearestColumn(target, volume, 0.0, yRow, k));
                if (previous > 0 && score <= 0)
                {
                    return (k + 0.5) * voxelSize;
                }

                previous = score;
            }

            return double.NaN;
        }

        /// <summary>Voxel index in depth layer k whose centre is nearest aperture-frame (x, y).</summary>
        private static int NearestColumn(ApertureSolarTarget target, ShadingVolume volume, double xTarget, double yTarget, int k)
        {
            int best = volume.VoxelIndex(0, 0, k);
            double bestDistance = double.MaxValue;
            for (int i = 0; i < volume.CountX; i++)
            {
                for (int j = 0; j < volume.CountY; j++)
                {
                    int index = volume.VoxelIndex(i, j, k);
                    if (!LocalCentre(target, volume, index, out double x, out double y, out _))
                    {
                        continue;
                    }

                    double distance = Math.Abs(x - xTarget) + Math.Abs(y - yTarget);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = index;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Depth at which the energy-weighted benefit of a point at (x = 0, yRow, d) crosses zero,
        /// computed per hour straight from sun position and weather: for each hour the ray through
        /// the point is traced back to the aperture plane, kept when it lands inside
        /// [yMin, yMax] x [-1, 1], and its weighted beam energy accumulated. Independent of the
        /// voxel grid, the DDA, the sun binning and the Stage 5 aggregation.
        /// </summary>
        private static double AnalyticalCrossing(ApertureSolarTarget target, IDesirabilityStrategy strategy, double yRow, double step, double yMin, double yMax)
        {
            Location location = TestHelpers.London();
            WeatherData weatherData = TestHelpers.SolarSymmetricWeatherData(Year, location, 30.0, 900.0, 0.2);
            Vector3D outward = target.OutwardNormal.Unit;
            Plane plane = target.Plane;
            double minSinElevation = Math.Sin(5.0 * Math.PI / 180.0);

            double previous = double.NaN;
            for (double d = step / 2; d < 1.5; d += step)
            {
                double benefit = 0;
                for (int hoy = 0; hoy < 8760; hoy++)
                {
                    DateTime dateTime = new DateTime(Year, 1, 1).AddHours(hoy);
                    WeatherHour weatherHour = weatherData.GetWeatherHour(dateTime);
                    if (weatherHour == null)
                    {
                        continue;
                    }

                    if (!Geometry.SolarCalculator.Query.TryGetSunAngles(location, dateTime.AddMinutes(30.0), out double elevationDegrees, out double azimuthDegrees))
                    {
                        continue;
                    }

                    double elevation = elevationDegrees * Math.PI / 180.0;
                    double azimuth = azimuthDegrees * Math.PI / 180.0;
                    Vector3D toward = new Vector3D(Math.Cos(elevation) * Math.Sin(azimuth), Math.Cos(elevation) * Math.Cos(azimuth), Math.Sin(elevation));
                    double cosThetaI = outward.DotProduct(toward);
                    if (cosThetaI <= 0)
                    {
                        continue;
                    }

                    double tx = toward.X * plane.AxisX.X + toward.Y * plane.AxisX.Y + toward.Z * plane.AxisX.Z;
                    double ty = toward.X * plane.AxisY.X + toward.Y * plane.AxisY.Y + toward.Z * plane.AxisY.Z;
                    double tz = toward.X * plane.AxisZ.X + toward.Y * plane.AxisZ.Y + toward.Z * plane.AxisZ.Z;
                    if (tz <= 1e-9)
                    {
                        continue;
                    }

                    double yW = yRow - d * ty / tz;
                    double xW = -d * tx / tz;
                    if (yW < yMin || yW > yMax || xW < -1.0 || xW > 1.0)
                    {
                        continue;
                    }

                    double weight = strategy.Weight(dateTime, weatherHour, target);
                    if (weight == 0)
                    {
                        continue;
                    }

                    double beamHorizontal = Math.Max(0.0, weatherHour.GlobalSolarRadiation - weatherHour.DiffuseSolarRadiation);
                    double dni = beamHorizontal / Math.Max(Math.Sin(elevation), minSinElevation);
                    benefit += weight * dni * cosThetaI / 1000.0;
                }

                if (previous > 0 && benefit <= 0)
                {
                    return d;
                }

                previous = benefit;
            }

            return double.NaN;
        }

        [Fact]
        public void CaseB_EastWindow_Potential_Is_Laterally_Asymmetric_Unlike_A_South_Window()
        {
            // Case B tests the controlled physics of an oblique-sun facade, not a preconceived
            // device family. The signature of an east window is LATERAL ASYMMETRY: its unwanted
            // summer energy arrives from the north-east (local +X) while its wanted winter energy
            // arrives from the south-east (local -X), so shading value must pile up on the north
            // side and be actively harmful on the south side. A south window under the same
            // (solar-symmetric) weather has no such preference and must come out even.
            //
            // The south window is the control: it isolates the asymmetry as a solar effect rather
            // than a coordinate-frame or volume-margin artefact, which a one-sided test cannot do.
            ApertureSolarTarget east = Target(EastWindowFace());
            SolarVisibilityCache eastCache = Cache(east, new List<LinkedFace3D>());
            ShadingVolume eastVolume = DefaultVolume(east);
            ShadingPotentialField eastField = Analytical.SolarCalculator.Create.ShadingPotentialField(
                east, eastCache, Desirability(east, eastCache, SummerUnwantedWinterWanted()), eastVolume);
            Assert.NotNull(eastField);

            LateralSplit(east, eastVolume, eastField, out double eastPlus, out double eastMinus, out double eastOverhang, out int finVoxels, out int overhangVoxels);
            output.WriteLine($"east window: +X (north) fin {eastPlus:0.###}, -X (south) fin {eastMinus:0.###}, overhang {eastOverhang:0.###} kWh");

            ApertureSolarTarget south = Target(SouthWindowFace());
            SolarVisibilityCache southCache = Cache(south, new List<LinkedFace3D>());
            ShadingVolume southVolume = DefaultVolume(south);
            ShadingPotentialField southField = Analytical.SolarCalculator.Create.ShadingPotentialField(
                south, southCache, Desirability(south, southCache, SummerUnwantedWinterWanted()), southVolume);
            Assert.NotNull(southField);

            LateralSplit(south, southVolume, southField, out double southPlus, out double southMinus, out double southOverhang, out _, out _);
            output.WriteLine($"south window (control): +X (east) fin {southPlus:0.###}, -X (west) fin {southMinus:0.###}, overhang {southOverhang:0.###} kWh");

            // 1. The east window develops real lateral shading value on its morning-sun side.
            Assert.True(eastPlus > 0, "a fin region beyond the north jamb must emerge for summer morning sun");

            // 2. That value is strongly one-sided: the south side sees the WANTED winter sun.
            Assert.True(eastPlus > 10.0 * eastMinus,
                $"east-window potential must be strongly one-sided: +X {eastPlus:0.###} vs -X {eastMinus:0.###}");

            // 3. The control is even, so the asymmetry above is solar, not geometric.
            double southAsymmetry = Math.Abs(southPlus - southMinus) / Math.Max(southPlus, southMinus);
            output.WriteLine($"south-window lateral asymmetry {southAsymmetry * 100:0.#} %, east-window ratio +X/-X {eastPlus / Math.Max(eastMinus, 1e-9):0.#}");
            Assert.True(southAsymmetry < 0.05,
                $"the south control must be laterally symmetric under symmetric weather, got {southAsymmetry * 100:0.#} %");

            // 4. Honest record of what the physics actually says about device family. For an east
            //    window in London most summer beam energy arrives near normal incidence (azimuth
            //    ~90 deg) at 30-40 deg elevation, which is overhang territory; the fin only wins
            //    for azimuths far off normal, where cos(incidence) is already small. So the
            //    overhang legitimately carries more TOTAL and more PER-VOXEL potential here, and
            //    the test records that rather than forcing a fin to win.
            output.WriteLine($"per-voxel potential: fin {eastPlus / Math.Max(1, finVoxels):0.####}, overhang {eastOverhang / Math.Max(1, overhangVoxels):0.####} kWh/voxel");
            Assert.True(eastOverhang > 0, "an east window still benefits from an overhang for high summer morning sun");
        }

        /// <summary>
        /// Splits positive potential into the two lateral (fin) bands beyond the jambs and the band
        /// above the head, in APERTURE-frame coordinates, and reports the voxel count of each so
        /// totals can be read per voxel as well as absolutely.
        /// </summary>
        private static void LateralSplit(ApertureSolarTarget target, ShadingVolume volume, ShadingPotentialField field, out double plusX, out double minusX, out double overhang, out int finVoxels, out int overhangVoxels)
        {
            plusX = 0;
            minusX = 0;
            overhang = 0;
            finVoxels = 0;
            overhangVoxels = 0;

            foreach (int index in Enumerable.Range(0, volume.VoxelCount))
            {
                if (!LocalCentre(target, volume, index, out double x, out double y, out _))
                {
                    continue;
                }

                bool isFin = Math.Abs(x) > 1.0 && Math.Abs(y) < 0.5;
                bool isOverhang = y > 0.6 && Math.Abs(x) < 1.0;
                if (isFin) { finVoxels++; }
                if (isOverhang) { overhangVoxels++; }

                double score = field.Score(index);
                if (score <= 0)
                {
                    continue;
                }

                if (isFin && x > 0) { plusX += score; }
                else if (isFin) { minusX += score; }
                else if (isOverhang) { overhang += score; }
            }

            finVoxels /= 2; // per side
        }

        [Fact]
        public void CaseC_Existing_Context_Removes_Duplicated_Potential()
        {
            ApertureSolarTarget target = Target(SouthWindowFace());
            ShadingVolume volume = DefaultVolume(target);

            ShadingPotentialField fieldWithout = Analytical.SolarCalculator.Create.ShadingPotentialField(
                target, Cache(target, new List<LinkedFace3D>()), Desirability(target, Cache(target, new List<LinkedFace3D>()), SummerUnwantedWinterWanted()), volume);

            List<LinkedFace3D> occluders = new List<LinkedFace3D> { new LinkedFace3D(Guid.NewGuid(), ContextSlabFace()) };
            SolarVisibilityCache cacheWith = Cache(target, occluders);
            ShadingPotentialField fieldWith = Analytical.SolarCalculator.Create.ShadingPotentialField(
                target, cacheWith, Desirability(target, cacheWith, SummerUnwantedWinterWanted()), volume);

            Assert.NotNull(fieldWithout);
            Assert.NotNull(fieldWith);

            double positiveWithout = fieldWithout.PositiveTotal();
            double positiveWith = fieldWith.PositiveTotal();
            output.WriteLine($"positive shading potential: without context {positiveWithout:0.###} kWh, with context {positiveWith:0.###} kWh");

            // The slab already intercepts the high summer sun: the field must not credit a new
            // shade for blocking it again.
            Assert.True(positiveWith < positiveWithout, "existing context must reduce the remaining shading potential");
            Assert.True(positiveWith < 0.6 * positiveWithout, $"context should remove most of the overhang benefit; with/without = {positiveWith / positiveWithout:0.###}");

            // Specifically, the region just under the existing slab (which would duplicate it)
            // must lose nearly all of its positive score.
            double duplicateWithout = 0;
            double duplicateWith = 0;
            foreach (int index in Enumerable.Range(0, volume.VoxelCount))
            {
                if (!LocalCentre(target, volume, index, out double x, out double y, out double z))
                {
                    continue;
                }

                // Slab is 0.4 m above the head (local y ~ 0.9) and up to 1.5 m deep.
                if (y > 0.7 && y < 1.1 && z < 1.4)
                {
                    double scoreWithout = fieldWithout.Score(index);
                    double scoreWith = fieldWith.Score(index);
                    if (scoreWithout > 0) { duplicateWithout += scoreWithout; }
                    if (scoreWith > 0) { duplicateWith += scoreWith; }
                }
            }

            output.WriteLine($"slab-duplicating region positive score: without {duplicateWithout:0.###}, with {duplicateWith:0.###} kWh");
            Assert.True(duplicateWith < 0.2 * duplicateWithout, $"duplicating region should collapse: {duplicateWith:0.###} vs {duplicateWithout:0.###}");
        }

        [Fact]
        public void CaseD_No_Unwanted_Solar_Produces_No_Valuable_Region()
        {
            ApertureSolarTarget target = Target(SouthWindowFace());
            SolarVisibilityCache cache = Cache(target, new List<LinkedFace3D>());
            ApertureDesirability desirability = Desirability(target, cache, new SeasonalDesirability(null, new AnalysisPeriod(Year)));
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(target, cache, desirability, DefaultVolume(target));

            Assert.NotNull(field);
            Assert.Equal(0.0, field.TotalUnwantedEnergy);
            Assert.True(field.TotalWantedEnergy > 0);
            Assert.Equal(0.0, field.PositiveTotal());
            Assert.True(field.MaxScore() <= 0);
            Assert.Empty(Analytical.SolarCalculator.Query.IdealShadingVoxels(field, 0.0));
            Assert.True(double.IsNaN(field.ThresholdForCumulativeCapture(0.9)));
        }

        [Fact]
        public void CaseE_No_Wanted_Solar_Favours_Interception_Everywhere_Lit()
        {
            ApertureSolarTarget target = Target(SouthWindowFace());
            SolarVisibilityCache cache = Cache(target, new List<LinkedFace3D>());
            ApertureDesirability desirability = Desirability(target, cache, new SeasonalDesirability(new AnalysisPeriod(Year), null));
            ShadingVolume volume = DefaultVolume(target);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(target, cache, desirability, volume);

            Assert.NotNull(field);
            Assert.Equal(0.0, field.TotalWantedEnergy);
            Assert.True(field.TotalUnwantedEnergy > 0);
            Assert.True(field.PositiveTotal() > 0);

            // With nothing worth preserving, interception can never be harmful anywhere.
            Assert.True(field.MinScore() >= 0, $"no voxel may score negative when there is no wanted solar, got {field.MinScore()}");

            // Every analysis cell that is lit at some point in the year contributes to the voxel it
            // sits in: that voxel is the first thing every one of its rays traverses, so it must
            // carry positive potential.
            //
            // Note what is NOT asserted: that the whole glass layer is positive. Ray origins are
            // the cell interior points and the sun is always above the horizon, so every ray leaves
            // the aperture travelling UPWARD in the local frame. No ray can ever reach a voxel that
            // lies below the lowest cell-centre row at positive depth, and demanding positivity
            // there asks the field for energy that physically cannot arrive.
            int checkedCells = 0;
            for (int c = 0; c < target.AnalysisCells.Count; c++)
            {
                bool lit = false;
                for (int b = 0; b < cache.Bins.Count && !lit; b++)
                {
                    lit = cache.IsLit(b, c) && (desirability.UnwantedEnergyPerGroup[b] > 0);
                }

                if (!lit)
                {
                    continue;
                }

                Assert.True(volume.TryGetVoxelIndices(target.AnalysisCells[c].InternalPoint3D, out int i, out int j, out int k),
                    $"cell {c} must lie inside its own aperture's shading volume");

                int index = volume.VoxelIndex(i, j, k);
                Assert.True(field.Score(index) > 0,
                    $"the voxel containing lit cell {c} must be positive, got {field.Score(index)}");
                checkedCells++;
            }

            output.WriteLine($"{checkedCells} lit analysis cells, each sitting in a positive-potential voxel; field min score {field.MinScore():0.###}, positive total {field.PositiveTotal():0.###} kWh");
            Assert.True(checkedCells > 0);
        }

        [Fact]
        public void Field_Matches_BruteForce_Independent_RayBox_Accumulation()
        {
            // Independent reference: for every (lit cell, sun group) ray, test EVERY voxel with a
            // ray-box slab intersection (the O(rays x voxels) algorithm the DDA replaces) and
            // accumulate. The two must agree exactly; this is the numerical proof that the
            // traversal — and therefore the field — is right rather than plausible.
            //
            // Two things make this reference a fair judge rather than a different question:
            //
            //  * the ray is NOT truncated at some arbitrary length. Both algorithms must consider
            //    the whole path through the grid; a shallow (low-sun) ray runs the full 2.5 m
            //    lateral extent, far past the old "MaxDepth + 2 voxels" cut-off, and truncating it
            //    silently deletes real interceptions from the reference only.
            //
            //  * interception requires POSITIVE path length inside the voxel. A closed-interval
            //    slab test (tMin <= tMax) also reports tangential corner and face touches, and
            //    with gridSize an exact multiple of voxelSize every analysis-cell origin sits
            //    exactly on a voxel corner, so those degenerate touches are the common case, not a
            //    rarity. Material occupying a voxel the ray only touches at a point intercepts
            //    nothing, so zero-length overlaps must not be counted by either side.
            double gridSize = 0.5;
            double voxelSize = 0.25;
            ApertureSolarTarget target = Target(SouthWindowFace(), gridSize);
            SolarVisibilityCache cache = Cache(target, new List<LinkedFace3D>(), gridSize, sunAngleStep: 5.0);
            ApertureDesirability desirability = Desirability(target, cache, SummerUnwantedWinterWanted());
            ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(target, maxDepth: 0.75, up: 0.5, down: 0.25, left: 0.25, right: 0.25, voxelSize: voxelSize);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(target, cache, desirability, volume);
            Assert.NotNull(field);

            double[] referenceUnwanted = new double[volume.VoxelCount];
            double[] referenceWanted = new double[volume.VoxelCount];

            List<AnalysisCell> cells = target.AnalysisCells;
            List<SunBin> bins = cache.Bins;
            double[] unwantedGroups = desirability.UnwantedEnergyPerGroup;
            double[] wantedGroups = desirability.WantedEnergyPerGroup;

            for (int b = 0; b < bins.Count; b++)
            {
                double uw = unwantedGroups[b];
                double wn = wantedGroups[b];
                if (Math.Abs(uw) + Math.Abs(wn) < 1e-12)
                {
                    continue;
                }

                Vector3D towardSun = bins[b].RepresentativeDirection.GetNegated().Unit;
                double dx = towardSun.X * volume.AxisX.X + towardSun.Y * volume.AxisX.Y + towardSun.Z * volume.AxisX.Z;
                double dy = towardSun.X * volume.AxisY.X + towardSun.Y * volume.AxisY.Y + towardSun.Z * volume.AxisY.Z;
                double dz = towardSun.X * volume.AxisZ.X + towardSun.Y * volume.AxisZ.Y + towardSun.Z * volume.AxisZ.Z;

                for (int c = 0; c < cells.Count; c++)
                {
                    if (!cache.IsLit(b, c))
                    {
                        continue;
                    }

                    Assert.True(volume.TryToLocal(cells[c].InternalPoint3D, out double sx, out double sy, out double sz));
                    double area = cells[c].Area;

                    for (int v = 0; v < volume.VoxelCount; v++)
                    {
                        int i = v % volume.CountX;
                        int j = (v / volume.CountX) % volume.CountY;
                        int k = v / (volume.CountX * volume.CountY);

                        if (!RayBox(sx, sy, sz, dx, dy, dz, i * voxelSize, j * voxelSize, k * voxelSize, voxelSize))
                        {
                            continue;
                        }

                        referenceUnwanted[v] += area * uw;
                        referenceWanted[v] += area * wn;
                    }
                }
            }

            double[] fieldUnwanted = field.UnwantedEnergyPerVoxel;
            double[] fieldWanted = field.WantedEnergyPerVoxel;
            double sumAbsDiff = 0;
            double sumAbsTotal = 0;
            for (int v = 0; v < volume.VoxelCount; v++)
            {
                sumAbsDiff += Math.Abs(fieldUnwanted[v] - referenceUnwanted[v]) + Math.Abs(fieldWanted[v] - referenceWanted[v]);
                sumAbsTotal += referenceUnwanted[v] + referenceWanted[v];
            }

            output.WriteLine($"DDA vs brute force: sum|diff| = {sumAbsDiff:0.########}, sum|reference| = {sumAbsTotal:0.####}, relative = {sumAbsDiff / sumAbsTotal:0.########}");
            Assert.True(sumAbsDiff / sumAbsTotal < 1e-6, $"DDA field disagrees with brute force by {sumAbsDiff / sumAbsTotal:0.######}");
        }

        [Fact]
        public void Field_Is_Deterministic_And_Normalised()
        {
            ApertureSolarTarget target = Target(SouthWindowFace());
            SolarVisibilityCache cache = Cache(target, new List<LinkedFace3D>());
            ApertureDesirability desirability = Desirability(target, cache, SummerUnwantedWinterWanted());
            ShadingVolume volume = DefaultVolume(target);

            ShadingPotentialField field1 = Analytical.SolarCalculator.Create.ShadingPotentialField(target, cache, desirability, volume);
            ShadingPotentialField field2 = Analytical.SolarCalculator.Create.ShadingPotentialField(target, cache, desirability, volume);

            Assert.Equal(field1.UnwantedEnergyPerVoxel, field2.UnwantedEnergyPerVoxel);
            Assert.Equal(field1.WantedEnergyPerVoxel, field2.WantedEnergyPerVoxel);

            // Normalisation: bounded in [-1, 1]; the best voxel is exactly +1.
            double maxNormalised = double.NegativeInfinity;
            foreach (int index in Enumerable.Range(0, volume.VoxelCount))
            {
                double normalised = field1.NormalizedScore(index);
                Assert.InRange(normalised, -1.0, 1.0);
                if (normalised > maxNormalised)
                {
                    maxNormalised = normalised;
                }
            }

            Assert.Equal(1.0, maxNormalised);
        }

        [Fact]
        public void Field_Json_RoundTrip_Preserves_Voxels()
        {
            ApertureSolarTarget target = Target(SouthWindowFace());
            SolarVisibilityCache cache = Cache(target, new List<LinkedFace3D>());
            ApertureDesirability desirability = Desirability(target, cache, SummerUnwantedWinterWanted());
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(target, cache, desirability, DefaultVolume(target));

            ShadingPotentialField restored = new ShadingPotentialField(field.ToJsonObject());
            Assert.Equal(field.UnwantedEnergyPerVoxel, restored.UnwantedEnergyPerVoxel);
            Assert.Equal(field.WantedEnergyPerVoxel, restored.WantedEnergyPerVoxel);
            Assert.Equal(field.Volume.CountX, restored.Volume.CountX);
            Assert.Equal(field.Volume.CountY, restored.Volume.CountY);
            Assert.Equal(field.Volume.CountZ, restored.Volume.CountZ);
            Assert.Equal(field.ApertureGuid, restored.ApertureGuid);
            Assert.Equal(field.PositiveTotal(), restored.PositiveTotal(), 12);
        }

        [Fact]
        public void Field_ZeroEnergy_Weather_Is_All_Zero()
        {
            ApertureSolarTarget target = Target(SouthWindowFace());
            SolarVisibilityCache cache = Cache(target, new List<LinkedFace3D>());

            WeatherData beamless = TestHelpers.SyntheticWeatherData(Year, TestHelpers.London(), dateTime =>
            {
                DateTime sunTime = dateTime.AddMinutes(30.0);
                if (!Geometry.SolarCalculator.Query.TryGetSunAngles(TestHelpers.London(), sunTime, out double altitude, out _) || altitude <= 0)
                {
                    return Tuple.Create(0.0, 0.0, 0.0);
                }

                double global = 400.0 * Math.Sin(altitude * Math.PI / 180.0);
                return Tuple.Create(global, global, 0.0);
            });

            ApertureDesirability desirability = Analytical.SolarCalculator.Create.ApertureDesirability(target, cache, new SeasonalDesirability(new AnalysisPeriod(Year), null), beamless);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(target, cache, desirability, DefaultVolume(target));

            Assert.NotNull(field);
            Assert.Equal(0.0, field.TotalUnwantedEnergy);
            Assert.Equal(0.0, field.PositiveTotal());
            Assert.Equal(0.0, field.MaxScore());
        }

        /// <summary>
        /// Ray/voxel-box slab intersection, the brute-force reference algorithm: true only when the
        /// ray spends a POSITIVE path length inside the box (see the note in the calling test —
        /// tangential corner and face touches intercept nothing and must not count).
        /// </summary>
        private static bool RayBox(double sx, double sy, double sz, double dx, double dy, double dz, double boxX, double boxY, double boxZ, double size)
        {
            double tMin = 0.0;
            double tMax = double.PositiveInfinity;

            if (!SlabAxis(sx, dx, boxX, boxX + size, ref tMin, ref tMax)) { return false; }
            if (!SlabAxis(sy, dy, boxY, boxY + size, ref tMin, ref tMax)) { return false; }
            if (!SlabAxis(sz, dz, boxZ, boxZ + size, ref tMin, ref tMax)) { return false; }

            return tMax - tMin > 1e-9;
        }

        private static bool SlabAxis(double s, double d, double min, double max, ref double tMin, ref double tMax)
        {
            if (Math.Abs(d) < 1e-15)
            {
                return s >= min && s <= max;
            }

            double t1 = (min - s) / d;
            double t2 = (max - s) / d;
            if (t1 > t2)
            {
                double temp = t1;
                t1 = t2;
                t2 = temp;
            }

            if (t1 > tMin) { tMin = t1; }
            if (t2 < tMax) { tMax = t2; }
            return tMin <= tMax;
        }
    }
}
