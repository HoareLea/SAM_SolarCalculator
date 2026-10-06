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
    /// Stage 7 tests: iso-surface extraction (marching tetrahedra) and IdealShadingResult.
    ///
    /// The mesh tests measure the surface rather than eyeballing it: enclosed volume by the
    /// divergence theorem, closure by counting how many times each edge is used, and the analytical
    /// solar-funnel boundary. Visual plausibility proves nothing about a level set.
    /// </summary>
    public class IdealShadingTests
    {
        private readonly ITestOutputHelper output;

        public IdealShadingTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static readonly int Year = 2018;

        // ------------------------------------------------------------------ iso-surface core ---

        [Fact]
        public void IsoSurface_Sphere_Volume_And_Closure_Are_Right()
        {
            // A sphere is the sharpest available test of the case table: every one of the sixteen
            // tetrahedron configurations occurs somewhere on it, and both the enclosed volume and
            // the closure of the surface have exact expected values.
            int count = 41;
            double spacing = 0.05;
            double radius = 0.8;
            Point3D centre = new Point3D(1.0, 1.0, 1.0);

            double[] values = new double[count * count * count];
            for (int k = 0; k < count; k++)
            {
                for (int j = 0; j < count; j++)
                {
                    for (int i = 0; i < count; i++)
                    {
                        double x = i * spacing;
                        double y = j * spacing;
                        double z = k * spacing;
                        double distance = Math.Sqrt((x - centre.X) * (x - centre.X) + (y - centre.Y) * (y - centre.Y) + (z - centre.Z) * (z - centre.Z));
                        values[i + count * (j + count * k)] = radius - distance; // > 0 inside
                    }
                }
            }

            Mesh3D mesh = Geometry.SolarCalculator.Create.IsoSurface(
                values, count, count, count, 0.0,
                new Point3D(0, 0, 0), Vector3D.WorldX, Vector3D.WorldY, Vector3D.WorldZ, spacing);

            Assert.NotNull(mesh);
            Assert.True(mesh.TrianglesCount > 0);

            double expected = 4.0 / 3.0 * Math.PI * radius * radius * radius;
            double measured = EnclosedVolume(mesh);
            double error = Math.Abs(measured - expected) / expected;
            output.WriteLine($"sphere r = {radius} m: {mesh.TrianglesCount} triangles, {mesh.PointsCount} vertices, volume {measured:0.#####} m3 vs analytical {expected:0.#####} m3 ({error * 100:0.##} %)");

            // Sign proves the winding convention, magnitude proves the case table.
            Assert.True(measured > 0, $"outward winding must give a positive enclosed volume, got {measured}");
            Assert.True(error < 0.01, $"iso-surface volume is off by {error * 100:0.##} %");

            // A closed manifold uses every edge exactly twice, once in each direction.
            Assert.Equal(0, OpenEdgeCount(mesh));
        }

        [Fact]
        public void IsoSurface_Box_Volume_Matches_Within_One_Voxel_Shell()
        {
            // A box aligned to the lattice: the extracted volume is pinned by the sample positions,
            // so the expected answer is exact rather than an approximation.
            int count = 21;
            double spacing = 0.1;
            double[] values = new double[count * count * count];
            for (int k = 0; k < count; k++)
            {
                for (int j = 0; j < count; j++)
                {
                    for (int i = 0; i < count; i++)
                    {
                        bool inside = i >= 5 && i <= 15 && j >= 5 && j <= 12 && k >= 5 && k <= 9;
                        values[i + count * (j + count * k)] = inside ? 1.0 : -1.0;
                    }
                }
            }

            Mesh3D mesh = Geometry.SolarCalculator.Create.IsoSurface(
                values, count, count, count, 0.0,
                new Point3D(0, 0, 0), Vector3D.WorldX, Vector3D.WorldY, Vector3D.WorldZ, spacing);

            Assert.NotNull(mesh);

            // The iso level sits halfway between the inside and outside samples, so the ideal
            // surface runs half a spacing outside the last inside sample on every face.
            double ideal = ((15 - 5) + 1) * spacing * ((12 - 5) + 1) * spacing * ((9 - 5) + 1) * spacing;
            double measured = EnclosedVolume(mesh);
            double error = (ideal - measured) / ideal;
            output.WriteLine($"box: volume {measured:0.#####} m3 vs ideal {ideal:0.#####} m3 (deficit {error * 100:0.##} %, {ideal - measured:0.#####} m3 over {4 * (1.1 + 0.8 + 0.5):0.#} m of edge)");

            // A BINARY field has no gradient information, so the interpolated cut on every
            // tetrahedron edge — including the cube diagonals — lands exactly at the midpoint. On a
            // convex edge of the box that bevels the corner instead of reproducing a sharp 90 deg
            // arris, and the extracted solid is therefore always slightly SMALLER than the ideal
            // box, by roughly (edge length x spacing^2). This is inherent to marching tetrahedra on
            // a step function, not an implementation error; the sphere test above, where the field
            // is smooth and the interpolation carries real information, lands within 0.2 %.
            Assert.True(measured < ideal, "midpoint cuts bevel convex edges, so the volume must be a deficit, not a surplus");
            Assert.True(error < 0.05, $"box volume deficit {error * 100:0.##} % exceeds the 5 % extraction target");
            Assert.Equal(0, OpenEdgeCount(mesh));
        }

        [Fact]
        public void IsoSurface_Two_Disconnected_Blobs_Are_Both_Meshed_And_Closed()
        {
            int count = 25;
            double spacing = 0.1;
            double[] values = new double[count * count * count];
            for (int k = 0; k < count; k++)
            {
                for (int j = 0; j < count; j++)
                {
                    for (int i = 0; i < count; i++)
                    {
                        bool blobA = i >= 3 && i <= 7 && j >= 3 && j <= 7 && k >= 3 && k <= 7;
                        bool blobB = i >= 15 && i <= 20 && j >= 15 && j <= 20 && k >= 15 && k <= 20;
                        values[i + count * (j + count * k)] = blobA || blobB ? 1.0 : -1.0;
                    }
                }
            }

            Mesh3D mesh = Geometry.SolarCalculator.Create.IsoSurface(
                values, count, count, count, 0.0,
                new Point3D(0, 0, 0), Vector3D.WorldX, Vector3D.WorldY, Vector3D.WorldZ, spacing);

            Assert.NotNull(mesh);

            double ideal = 5 * 5 * 5 * spacing * spacing * spacing + 6 * 6 * 6 * spacing * spacing * spacing;
            double measured = EnclosedVolume(mesh);
            output.WriteLine($"two blobs: {mesh.TrianglesCount} triangles, volume {measured:0.#####} m3 vs ideal {ideal:0.#####} m3 (deficit {(ideal - measured) / ideal * 100:0.##} %)");

            // Same convex-edge bevel as the single box; what matters here is that BOTH regions are
            // present. Meshing only the larger one would leave a 63 % deficit, not a few per cent.
            Assert.True((ideal - measured) / ideal < 0.05, "both disconnected regions must be meshed");
            Assert.True(measured > 0.5 * ideal, "a single meshed region would fall far short of the combined volume");
            Assert.Equal(0, OpenEdgeCount(mesh));
        }

        [Fact]
        public void IsoSurface_Empty_And_Degenerate_Inputs_Are_Handled()
        {
            int count = 8;
            double[] empty = new double[count * count * count];
            for (int i = 0; i < empty.Length; i++)
            {
                empty[i] = -1.0;
            }

            Mesh3D mesh = Geometry.SolarCalculator.Create.IsoSurface(
                empty, count, count, count, 0.0,
                new Point3D(0, 0, 0), Vector3D.WorldX, Vector3D.WorldY, Vector3D.WorldZ, 0.1);

            // Nothing above the level is an empty surface, not an exception and not null.
            Assert.NotNull(mesh);
            Assert.Equal(0, mesh.TrianglesCount);

            Assert.Null(Geometry.SolarCalculator.Create.IsoSurface(null, count, count, count, 0.0, new Point3D(0, 0, 0), Vector3D.WorldX, Vector3D.WorldY, Vector3D.WorldZ, 0.1));
            Assert.Null(Geometry.SolarCalculator.Create.IsoSurface(empty, 1, count, count, 0.0, new Point3D(0, 0, 0), Vector3D.WorldX, Vector3D.WorldY, Vector3D.WorldZ, 0.1));
            Assert.Null(Geometry.SolarCalculator.Create.IsoSurface(empty, count, count, count, 0.0, new Point3D(0, 0, 0), Vector3D.WorldX, Vector3D.WorldY, Vector3D.WorldZ, 0.0));
        }

        // ------------------------------------------------------------- IdealShadingResult -------

        [Fact]
        public void IdealShadingResult_Threshold_Methods_Are_Pinned()
        {
            ShadingPotentialField field = SouthField(out ApertureSolarTarget _);

            IdealShadingResult capture90 = Analytical.SolarCalculator.Create.IdealShadingResult(field, ShadingThresholdMethod.CumulativeCapture, 0.9);
            IdealShadingResult capture50 = Analytical.SolarCalculator.Create.IdealShadingResult(field, ShadingThresholdMethod.CumulativeCapture, 0.5);
            IdealShadingResult maxHalf = Analytical.SolarCalculator.Create.IdealShadingResult(field, ShadingThresholdMethod.MaxFraction, 0.5);

            output.WriteLine($"capture 90 %: threshold {capture90.Threshold:0.###} kWh, {capture90.SelectedVoxelCount} voxels, captured {capture90.CapturedPotentialFraction * 100:0.#} %, {capture90.RegionCount} regions");
            output.WriteLine($"capture 50 %: threshold {capture50.Threshold:0.###} kWh, {capture50.SelectedVoxelCount} voxels, captured {capture50.CapturedPotentialFraction * 100:0.#} %");
            output.WriteLine($"max fraction 0.5: threshold {maxHalf.Threshold:0.###} kWh, {maxHalf.SelectedVoxelCount} voxels, captured {maxHalf.CapturedPotentialFraction * 100:0.#} %");

            // Asking for more benefit must lower the bar and take more voxels.
            Assert.True(capture90.Threshold < capture50.Threshold);
            Assert.True(capture90.SelectedVoxelCount > capture50.SelectedVoxelCount);
            Assert.True(capture90.CapturedPotentialFraction >= 0.9);
            Assert.True(capture50.CapturedPotentialFraction >= 0.5);

            // MaxFraction is exactly half of the best voxel.
            Assert.Equal(0.5 * field.MaxScore(), maxHalf.Threshold, 9);

            // Metrics must be internally consistent with the voxel count.
            double voxelSize = field.Volume.VoxelSize;
            Assert.Equal(capture90.SelectedVoxelCount * voxelSize * voxelSize * voxelSize, capture90.EnclosedVolume, 9);
            Assert.True(capture90.MaxProjectionDepth > 0 && capture90.MaxProjectionDepth <= field.Volume.MaxDepth);
        }

        [Fact]
        public void IdealShadingResult_Mesh_Encloses_The_Selected_Voxels()
        {
            ShadingPotentialField field = SouthField(out ApertureSolarTarget _);
            IdealShadingResult result = Analytical.SolarCalculator.Create.IdealShadingResult(field, ShadingThresholdMethod.CumulativeCapture, 0.9);

            Assert.True(result.HasMesh, $"a non-empty selection must mesh; reason was {result.MeshFailureReason}");
            Assert.Null(result.MeshFailureReason);

            double meshVolume = EnclosedVolume(result.Mesh);
            double voxelVolume = result.EnclosedVolume;
            output.WriteLine($"{result.SelectedVoxelCount} voxels: voxel volume {voxelVolume:0.####} m3, mesh volume {meshVolume:0.####} m3, ratio {meshVolume / voxelVolume:0.###}");
            output.WriteLine($"mesh: {result.Mesh.TrianglesCount} triangles, {result.Mesh.PointsCount} vertices, open edges {OpenEdgeCount(result.Mesh)}");

            Assert.Equal(0, OpenEdgeCount(result.Mesh));

            // The iso-surface cuts halfway to the neighbouring samples, so it necessarily encloses
            // less than the full voxel count for a ragged region (a lone voxel meshes as an
            // octahedron of 1/6 its volume) and cannot exceed it.
            Assert.True(meshVolume > 0);
            Assert.True(meshVolume <= voxelVolume * 1.001, $"mesh volume {meshVolume} must not exceed the voxel volume {voxelVolume}");
        }

        [Fact]
        public void IdealShadingResult_Empty_Field_Gives_A_Valid_Empty_Result()
        {
            // No unwanted solar at all: nothing is worth shading. This must be an ordinary,
            // fully-formed answer rather than a null, an exception or a bogus mesh.
            ApertureSolarTarget target = Target(SouthWindowFace());
            SolarVisibilityCache cache = Cache(target);
            ApertureDesirability desirability = Desirability(target, cache, new SeasonalDesirability(null, new AnalysisPeriod(Year)));
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(target, cache, desirability, Volume(target));

            IdealShadingResult result = Analytical.SolarCalculator.Create.IdealShadingResult(field, ShadingThresholdMethod.CumulativeCapture, 0.9);

            Assert.NotNull(result);
            Assert.Equal(0, result.SelectedVoxelCount);
            Assert.Equal(0, result.RegionCount);
            Assert.False(result.HasMesh);
            Assert.NotNull(result.MeshFailureReason);
            Assert.True(double.IsNaN(result.Threshold));
            Assert.Equal(0.0, result.EnclosedVolume);
            output.WriteLine($"empty field: {result.SelectedVoxelCount} voxels, mesh reason \"{result.MeshFailureReason}\"");

            // The numeric result survives the absent mesh, which is the whole point.
            Assert.Equal(field.ApertureGuid, result.ApertureGuid);
            Assert.Equal(0.0, result.ProjectedArea);
        }

        [Fact]
        public void IdealShadingResult_Region_Filters_Are_Deterministic()
        {
            ShadingPotentialField field = SouthField(out ApertureSolarTarget _);

            IdealShadingResult all = Analytical.SolarCalculator.Create.IdealShadingResult(field, ShadingThresholdMethod.MaxFraction, 0.25);
            IdealShadingResult largest = Analytical.SolarCalculator.Create.IdealShadingResult(field, ShadingThresholdMethod.MaxFraction, 0.25, 1.0, false, true);
            IdealShadingResult facade = Analytical.SolarCalculator.Create.IdealShadingResult(field, ShadingThresholdMethod.MaxFraction, 0.25, 1.0, true, false);

            output.WriteLine($"unfiltered {all.SelectedVoxelCount} voxels in {all.RegionCount} regions {string.Join(",", all.RegionSizes.Take(5))}");
            output.WriteLine($"largest region only {largest.SelectedVoxelCount} voxels in {largest.RegionCount} region(s)");
            output.WriteLine($"facade contact only {facade.SelectedVoxelCount} voxels in {facade.RegionCount} region(s)");

            Assert.Equal(1, largest.RegionCount);
            Assert.Equal(all.RegionSizes[0], largest.SelectedVoxelCount);
            Assert.True(facade.SelectedVoxelCount <= all.SelectedVoxelCount);

            // Repeating the extraction must reproduce the selection exactly.
            IdealShadingResult again = Analytical.SolarCalculator.Create.IdealShadingResult(field, ShadingThresholdMethod.MaxFraction, 0.25, 1.0, false, true);
            Assert.Equal(largest.VoxelIndices, again.VoxelIndices);
            Assert.Equal(largest.Threshold, again.Threshold);
        }

        [Fact]
        public void IdealShadingResult_Json_RoundTrip()
        {
            ShadingPotentialField field = SouthField(out ApertureSolarTarget _);
            IdealShadingResult result = Analytical.SolarCalculator.Create.IdealShadingResult(field, ShadingThresholdMethod.CumulativeCapture, 0.8, 1.0, true, false);

            IdealShadingResult restored = new IdealShadingResult(result.ToJsonObject());

            Assert.Equal(result.ApertureGuid, restored.ApertureGuid);
            Assert.Equal(result.Threshold, restored.Threshold);
            Assert.Equal(result.ThresholdMethod, restored.ThresholdMethod);
            Assert.Equal(result.ThresholdParameter, restored.ThresholdParameter);
            Assert.Equal(result.RequireFacadeContact, restored.RequireFacadeContact);
            Assert.Equal(result.VoxelIndices, restored.VoxelIndices);
            Assert.Equal(result.RegionSizes, restored.RegionSizes);
            Assert.Equal(result.CapturedPotentialFraction, restored.CapturedPotentialFraction, 12);
            Assert.Equal(result.ProjectedArea, restored.ProjectedArea, 12);
            Assert.Equal(result.HasMesh, restored.HasMesh);
            Assert.Equal(result.Mesh.TrianglesCount, restored.Mesh.TrianglesCount);
        }

        [Fact]
        public void IdealShadingResult_SolarFunnel_Boundary_Is_Analytically_Correct()
        {
            // The extracted shape must be a solar funnel: it occupies the wedge the high summer sun
            // passes through and stops before the wedge the low winter sun needs. The quantitative
            // statement is about the SLOPE of its upper boundary, which is a profile angle and can
            // be computed independently from the sun positions.
            //
            // For each depth, the highest selected voxel above the head defines the boundary. The
            // profile angle from the window head to that point must lie between the winter
            // (wanted, must pass) and summer (unwanted, must be blocked) energy-weighted profile
            // angles: shallower than the summer sun it is there to intercept, steeper than the
            // winter sun it must not touch.
            double voxelSize = 0.1;
            ApertureSolarTarget target = Target(SouthWindowFace());
            SolarVisibilityCache cache = Cache(target);
            ApertureDesirability desirability = Desirability(target, cache, SummerUnwantedWinterWanted());
            ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(target, maxDepth: 1.5, up: 1.0, down: 0.2, left: 0.5, right: 0.5, voxelSize: voxelSize);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(target, cache, desirability, volume);

            IdealShadingResult result = Analytical.SolarCalculator.Create.IdealShadingResult(field, ShadingThresholdMethod.CumulativeCapture, 0.9, 1.0, true, false);
            Assert.True(result.SelectedVoxelCount > 0);

            WeightedProfileAngles(target, out double summerProfile, out double winterProfile);
            output.WriteLine($"energy-weighted profile angle: summer (unwanted) {summerProfile:0.#} deg, winter (wanted) {winterProfile:0.#} deg");

            Assert.True(summerProfile > winterProfile, "summer sun must be steeper than winter sun on a south facade");

            // Boundary slope of the selected shape, measured from the window head.
            double maxDepthSelected = 0;
            double heightAtMaxDepth = double.NegativeInfinity;
            HashSet<int> selected = new HashSet<int>(result.VoxelIndices);
            foreach (int index in selected)
            {
                if (!LocalCentre(target, volume, index, out double x, out double y, out double z))
                {
                    continue;
                }

                if (Math.Abs(x) > 0.5)
                {
                    continue; // keep to the middle of the window, away from the jamb edges
                }

                if (z > maxDepthSelected)
                {
                    maxDepthSelected = z;
                    heightAtMaxDepth = y;
                }
                else if (Math.Abs(z - maxDepthSelected) < 1e-9 && y > heightAtMaxDepth)
                {
                    heightAtMaxDepth = y;
                }
            }

            // Profile angle of the ray from the window head (y = 0.5, z = 0) to the outermost
            // selected point: the shallowest sun the shape still intercepts.
            double boundaryProfile = Math.Atan2(heightAtMaxDepth - 0.5, maxDepthSelected) * 180.0 / Math.PI;
            output.WriteLine($"outermost selected point at depth {maxDepthSelected:0.###} m, height {heightAtMaxDepth:0.###} m -> boundary profile angle {boundaryProfile:0.#} deg");

            Assert.True(boundaryProfile < summerProfile,
                $"the shape must reach out far enough to intercept the summer sun: boundary {boundaryProfile:0.#} deg vs summer {summerProfile:0.#} deg");
            Assert.True(boundaryProfile > winterProfile,
                $"the shape must stop short of the winter sun path: boundary {boundaryProfile:0.#} deg vs winter {winterProfile:0.#} deg");
        }

        // ------------------------------------------------------------------------- helpers ------

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

        private static ApertureSolarTarget Target(Face3D face, double gridSize = 0.5)
        {
            return new ApertureSolarTarget(Guid.NewGuid(), Guid.NewGuid(), face, Geometry.SolarCalculator.Query.AnalysisCells(face, gridSize));
        }

        private static SolarVisibilityCache Cache(ApertureSolarTarget target, double gridSize = 0.5)
        {
            return Weather.SolarCalculator.Create.SolarVisibilityCache(
                TestHelpers.London(), Year, 2.0, new List<LinkedFace3D>(), target.AnalysisCells,
                cellSize: gridSize, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);
        }

        private static ApertureDesirability Desirability(ApertureSolarTarget target, SolarVisibilityCache cache, IDesirabilityStrategy strategy)
        {
            return Analytical.SolarCalculator.Create.ApertureDesirability(
                target, cache, strategy, TestHelpers.SolarSymmetricWeatherData(Year, TestHelpers.London(), 30.0, 900.0, 0.2));
        }

        private static SeasonalDesirability SummerUnwantedWinterWanted()
        {
            return new SeasonalDesirability(new AnalysisPeriod(Year, 6, 1, 8, 31), new AnalysisPeriod(Year, 11, 1, 2, 28));
        }

        private static ShadingVolume Volume(ApertureSolarTarget target, double voxelSize = 0.1)
        {
            return Analytical.SolarCalculator.Create.ShadingVolume(target, maxDepth: 1.5, up: 1.0, down: 0.2, left: 0.5, right: 0.5, voxelSize: voxelSize);
        }

        private static ShadingPotentialField SouthField(out ApertureSolarTarget target)
        {
            target = Target(SouthWindowFace());
            SolarVisibilityCache cache = Cache(target);
            return Analytical.SolarCalculator.Create.ShadingPotentialField(
                target, cache, Desirability(target, cache, SummerUnwantedWinterWanted()), Volume(target));
        }

        /// <summary>Voxel centre in the aperture frame (origin at the centroid, X across, Y up, Z out).</summary>
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

        /// <summary>
        /// Energy-weighted mean profile angle (the vertical shadow angle in the aperture's own
        /// section) of the unwanted and wanted beam, computed per hour from sun position and
        /// weather. Independent of the field and of the mesh.
        /// </summary>
        private static void WeightedProfileAngles(ApertureSolarTarget target, out double summerProfile, out double winterProfile)
        {
            Location location = TestHelpers.London();
            WeatherData weatherData = TestHelpers.SolarSymmetricWeatherData(Year, location, 30.0, 900.0, 0.2);
            IDesirabilityStrategy strategy = SummerUnwantedWinterWanted();
            Vector3D outward = target.OutwardNormal.Unit;
            Plane plane = target.Plane;
            double minSinElevation = Math.Sin(5.0 * Math.PI / 180.0);

            double summerSum = 0, summerWeight = 0, winterSum = 0, winterWeight = 0;
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

                double ty = toward.X * plane.AxisY.X + toward.Y * plane.AxisY.Y + toward.Z * plane.AxisY.Z;
                double tz = toward.X * plane.Normal.X + toward.Y * plane.Normal.Y + toward.Z * plane.Normal.Z;
                if (tz <= 1e-9)
                {
                    continue;
                }

                double profile = Math.Atan2(ty, tz) * 180.0 / Math.PI;
                double beamHorizontal = Math.Max(0.0, weatherHour.GlobalSolarRadiation - weatherHour.DiffuseSolarRadiation);
                double energy = beamHorizontal / Math.Max(Math.Sin(elevation), minSinElevation) * cosThetaI / 1000.0;
                double weight = strategy.Weight(dateTime, weatherHour, target);

                if (weight > 0)
                {
                    summerSum += profile * weight * energy;
                    summerWeight += weight * energy;
                }
                else if (weight < 0)
                {
                    winterSum += profile * -weight * energy;
                    winterWeight += -weight * energy;
                }
            }

            summerProfile = summerWeight > 0 ? summerSum / summerWeight : double.NaN;
            winterProfile = winterWeight > 0 ? winterSum / winterWeight : double.NaN;
        }

        /// <summary>
        /// Enclosed volume of a closed mesh by the divergence theorem: the signed sum of the
        /// tetrahedra from the origin to each triangle. Positive for outward-facing winding.
        /// </summary>
        private static double EnclosedVolume(Mesh3D mesh)
        {
            double total = 0;
            foreach (Triangle3D triangle in mesh.GetTriangles())
            {
                List<Point3D> points = triangle.GetPoints();
                Point3D a = points[0];
                Point3D b = points[1];
                Point3D c = points[2];

                total += (a.X * (b.Y * c.Z - b.Z * c.Y)
                        - a.Y * (b.X * c.Z - b.Z * c.X)
                        + a.Z * (b.X * c.Y - b.Y * c.X)) / 6.0;
            }

            return total;
        }

        /// <summary>
        /// Number of directed edges without an opposing partner. Zero means the surface is a closed,
        /// consistently oriented manifold — the strongest cheap statement about an iso-surface.
        /// </summary>
        private static int OpenEdgeCount(Mesh3D mesh)
        {
            Dictionary<long, int> edges = new Dictionary<long, int>();
            for (int t = 0; t < mesh.TrianglesCount; t++)
            {
                Tuple<int, int, int> triangle = mesh.GetTriangleIndexes(t);
                Count(edges, triangle.Item1, triangle.Item2);
                Count(edges, triangle.Item2, triangle.Item3);
                Count(edges, triangle.Item3, triangle.Item1);
            }

            int open = 0;
            foreach (KeyValuePair<long, int> pair in edges)
            {
                if (pair.Value != 0)
                {
                    open++;
                }
            }

            return open;
        }

        private static void Count(Dictionary<long, int> edges, int a, int b)
        {
            long key = a < b ? (long)a * int.MaxValue + b : (long)b * int.MaxValue + a;
            int direction = a < b ? 1 : -1;
            edges.TryGetValue(key, out int current);
            edges[key] = current + direction;
        }
    }
}
