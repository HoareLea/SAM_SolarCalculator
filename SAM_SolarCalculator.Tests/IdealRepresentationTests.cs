// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical.SolarCalculator;
using SAM.Core.SolarCalculator;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;
using SAM.Weather.SolarCalculator;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Gate 0 Review A. How the Stage 7 ideal shape is turned into ray-traceable geometry for the
    /// ideal-versus-rationalised comparison, and how much that choice moves the answer.
    ///
    /// Three representations of the SAME selected voxel set are measured through the SAME first-hit
    /// engine:
    ///
    ///   plates  — one horizontal square per selected voxel, at the voxel centre. The Stage 8
    ///             stand-in. A ray only intercepts it if it crosses that one mid-height plane
    ///             inside the voxel footprint, so a shallow (low-sun) ray can pass through the
    ///             voxel entirely without meeting the plate.
    ///   solid   — the boundary faces of the selected voxel set. This is what the Stage 6 field
    ///             actually means, because the field credits a voxel when a ray ENTERS it.
    ///   mesh    — the extracted marching-tetrahedra surface, one element per triangle. The shape
    ///             a user is shown; interpolated to the iso-level, so slightly smaller than the
    ///             voxel solid.
    ///
    /// The solid is the reference. The point of the exercise is to quantify the plate bias rather
    /// than to keep asserting it is "the closest ray-traceable stand-in".
    /// </summary>
    public class IdealRepresentationTests
    {
        private readonly ITestOutputHelper output;

        public IdealRepresentationTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        private static readonly int Year = 2018;

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

        private static ApertureSolarTarget Target(double gridSize = 0.25)
        {
            Face3D face = SouthWindowFace();
            return new ApertureSolarTarget(Guid.NewGuid(), Guid.NewGuid(), face, SAM.Geometry.SolarCalculator.Query.AnalysisCells(face, gridSize));
        }

        private static SolarVisibilityCache Cache(ApertureSolarTarget target, double gridSize = 0.25)
        {
            return Weather.SolarCalculator.Create.SolarVisibilityCache(
                TestHelpers.London(), Year, 2.0, new List<LinkedFace3D>(), target.AnalysisCells,
                cellSize: gridSize, minHorizonAngle: 0.0349066, sunPositionShiftInMinutes: 30.0);
        }

        private static ApertureDesirability Desirability(ApertureSolarTarget target, SolarVisibilityCache cache)
        {
            return Analytical.SolarCalculator.Create.ApertureDesirability(
                target, cache,
                new SeasonalDesirability(new AnalysisPeriod(Year, 6, 1, 8, 31), new AnalysisPeriod(Year, 11, 1, 2, 28)),
                TestHelpers.SolarSymmetricWeatherData(Year, TestHelpers.London(), 30.0, 900.0, 0.2));
        }

        /// <summary>The Stage 8 stand-in: one horizontal plate per selected voxel, at its centre.</summary>
        private static List<ShadingElement> Plates(ApertureSolarTarget target, ShadingVolume volume, List<int> voxelIndices)
        {
            List<ShadingElement> result = new List<ShadingElement>();
            double half = 0.5 * volume.VoxelSize;
            Plane plane = target.Plane;
            Vector3D axisX = plane.AxisX;
            Vector3D axisZ = plane.Normal;

            foreach (int index in voxelIndices)
            {
                Point3D centre = volume.GetCentre(index);
                if (centre == null)
                {
                    continue;
                }

                List<Point3D> points = new List<Point3D>
                {
                    Offset(centre, axisX, -half, axisZ, -half),
                    Offset(centre, axisX, half, axisZ, -half),
                    Offset(centre, axisX, half, axisZ, half),
                    Offset(centre, axisX, -half, axisZ, half),
                };

                byte[] bytes = new byte[16];
                BitConverter.GetBytes(index).CopyTo(bytes, 0);
                bytes[15] = 0x7A;
                result.Add(new ShadingElement(new Guid(bytes), "Voxel plate " + index, new Face3D(new Polygon3D(points))));
            }

            return result;
        }

        private static Point3D Offset(Point3D origin, Vector3D a, double da, Vector3D b, double db)
        {
            return new Point3D(
                origin.X + da * a.X + db * b.X,
                origin.Y + da * a.Y + db * b.Y,
                origin.Z + da * a.Z + db * b.Z);
        }

        private static ShadingPerformance Evaluate(ApertureSolarTarget target, SolarVisibilityCache baseCache, ApertureDesirability desirability, List<ShadingElement> elements, string label)
        {
            List<LinkedFace3D> occluders = new List<LinkedFace3D>();
            foreach (ShadingElement element in elements)
            {
                occluders.Add(element.LinkedFace3D);
            }

            SolarAttributionCache attribution = Weather.SolarCalculator.Create.SolarAttributionCache(baseCache, occluders, target.AnalysisCells);
            return Analytical.SolarCalculator.Create.ShadingPerformance(
                target, baseCache, attribution, desirability, elements, label, double.NaN);
        }

        [Fact]
        [Trait("Category", "LongRunning")]
        public void Voxel_Plates_Understate_The_Ideal_Shape_Against_The_Voxel_Solid()
        {
            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target);
            ApertureDesirability desirability = Desirability(target, baseCache);
            ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(
                target, maxDepth: 1.5, up: 1.0, down: 0.2, left: 0.5, right: 0.5, voxelSize: 0.1);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(target, baseCache, desirability, volume);

            IdealShadingResult ideal = Analytical.SolarCalculator.Create.IdealShadingResult(
                field, ShadingThresholdMethod.CumulativeCapture, 0.9, 1.0, true, false);

            Assert.True(ideal.SelectedVoxelCount > 0);
            List<int> voxelIndices = ideal.VoxelIndices;

            List<ShadingElement> plates = Plates(target, volume, voxelIndices);
            List<ShadingElement> solid = Analytical.SolarCalculator.Query.VoxelSurfaceShadingElements(volume, voxelIndices);
            List<ShadingElement> mesh = Analytical.SolarCalculator.Query.ShadingElements(ideal);

            Assert.NotNull(solid);
            Assert.NotNull(mesh);
            Assert.NotEmpty(solid);
            Assert.NotEmpty(mesh);

            ShadingPerformance platePerformance = Evaluate(target, baseCache, desirability, plates, "Ideal (plates)");
            ShadingPerformance solidPerformance = Evaluate(target, baseCache, desirability, solid, "Ideal (voxel solid)");
            ShadingPerformance meshPerformance = Evaluate(target, baseCache, desirability, mesh, "Ideal (Stage 7 mesh)");

            output.WriteLine($"selection: {ideal.SelectedVoxelCount} voxels, {ideal.EnclosedVolume:0.####} m3, projected {ideal.ProjectedArea:0.####} m2, max depth {ideal.MaxProjectionDepth:0.###} m");
            output.WriteLine($"unshaded admitted: direct {solidPerformance.AdmittedDirectEnergy:0.#} kWh, unwanted {solidPerformance.AdmittedUnwantedEnergy:0.#} kWh, wanted {solidPerformance.AdmittedWantedEnergy:0.#} kWh");
            output.WriteLine("");
            output.WriteLine("representation      | elements | intercepted kWh | efficiency | unwanted blocked | wanted retained");
            Report("plates", plates.Count, platePerformance);
            Report("voxel solid", solid.Count, solidPerformance);
            Report("Stage 7 mesh", mesh.Count, meshPerformance);

            double interceptBias = (platePerformance.DirectSolarIntercepted - solidPerformance.DirectSolarIntercepted) / solidPerformance.DirectSolarIntercepted;
            double unwantedBias = platePerformance.UnwantedSolarBlocked - solidPerformance.UnwantedSolarBlocked;
            double wantedBias = platePerformance.WantedSolarRetained - solidPerformance.WantedSolarRetained;

            double meshIntercept = (meshPerformance.DirectSolarIntercepted - solidPerformance.DirectSolarIntercepted) / solidPerformance.DirectSolarIntercepted;
            double meshUnwanted = meshPerformance.UnwantedSolarBlocked - solidPerformance.UnwantedSolarBlocked;
            double meshWanted = meshPerformance.WantedSolarRetained - solidPerformance.WantedSolarRetained;

            output.WriteLine("");
            output.WriteLine($"plates vs solid: intercepted {interceptBias * 100:+0.#;-0.#} %, unwanted blocked {unwantedBias * 100:+0.#;-0.#} points, wanted retained {wantedBias * 100:+0.#;-0.#} points");
            output.WriteLine($"mesh   vs solid: intercepted {meshIntercept * 100:+0.#;-0.#} %, unwanted blocked {meshUnwanted * 100:+0.#;-0.#} points, wanted retained {meshWanted * 100:+0.#;-0.#} points");

            // Conservation must hold for every representation, however crude.
            Assert.Equal(platePerformance.DirectSolarIntercepted, platePerformance.ReconciledInterceptedEnergy, 6);
            Assert.Equal(solidPerformance.DirectSolarIntercepted, solidPerformance.ReconciledInterceptedEnergy, 6);
            Assert.Equal(meshPerformance.DirectSolarIntercepted, meshPerformance.ReconciledInterceptedEnergy, 6);

            // The solid cannot be beaten by either surrogate: both are subsets of the same
            // occupied space, so neither can intercept more than the filled voxels do.
            Assert.True(platePerformance.DirectSolarIntercepted <= solidPerformance.DirectSolarIntercepted + 1e-6,
                "plates cannot intercept more than the solid they stand in for");
            Assert.True(meshPerformance.DirectSolarIntercepted <= solidPerformance.DirectSolarIntercepted + 1e-6,
                "the extracted surface cannot intercept more than the voxel solid it was cut from");

            // The measured bias, pinned. This is the number the Stage 8 comparison was silently
            // carrying, and it is what justifies using the solid as the ideal reference instead.
            Assert.True(interceptBias <= 0, "plates must not flatter the ideal");
        }

        private void Report(string label, int elements, ShadingPerformance performance)
        {
            output.WriteLine($"{label,-19} | {elements,8} | {performance.DirectSolarIntercepted,15:0.#} | {performance.DirectShadingEfficiency * 100,9:0.#} % | {performance.UnwantedSolarBlocked * 100,15:0.#} % | {performance.WantedSolarRetained * 100,14:0.#} %");
        }

        [Fact]
        public void The_Stage7_Mesh_Converts_To_Ray_Traceable_Geometry_Deterministically()
        {
            // The conversion itself: every triangle becomes an opaque element with a stable Guid,
            // so an ideal shape can enter the ray engine and be attributed like any other device.
            ApertureSolarTarget target = Target();
            SolarVisibilityCache baseCache = Cache(target);
            ApertureDesirability desirability = Desirability(target, baseCache);
            ShadingVolume volume = Analytical.SolarCalculator.Create.ShadingVolume(
                target, maxDepth: 1.5, up: 1.0, down: 0.2, left: 0.5, right: 0.5, voxelSize: 0.1);
            ShadingPotentialField field = Analytical.SolarCalculator.Create.ShadingPotentialField(target, baseCache, desirability, volume);

            IdealShadingResult ideal = Analytical.SolarCalculator.Create.IdealShadingResult(
                field, ShadingThresholdMethod.CumulativeCapture, 0.9, 1.0, true, false);

            Assert.True(ideal.HasMesh, "this case must produce a mesh, or the conversion is untested");

            List<ShadingElement> first = Analytical.SolarCalculator.Query.ShadingElements(ideal);
            List<ShadingElement> second = Analytical.SolarCalculator.Query.ShadingElements(ideal);

            Assert.Equal(ideal.Mesh.TrianglesCount, first.Count);
            Assert.Equal(first.Count, second.Count);

            HashSet<Guid> guids = new HashSet<Guid>();
            double area = 0;
            for (int i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].Guid, second[i].Guid);
                Assert.True(first[i].Area > 0);
                Assert.True(guids.Add(first[i].Guid), "mesh element Guids must be distinct");
                area += first[i].Area;
            }

            output.WriteLine($"Stage 7 mesh: {ideal.Mesh.TrianglesCount} triangles -> {first.Count} opaque elements, {area:0.###} m2 of surface, {guids.Count} distinct Guids");

            // An empty or absent mesh converts to null rather than to a misleading empty device.
            Assert.Null(Analytical.SolarCalculator.Query.ShadingElements((IdealShadingResult)null));
        }

        [Fact]
        public void The_Voxel_Solid_Surface_Is_Closed_And_Free_Of_Internal_Faces()
        {
            // The property that makes the voxel solid a valid reference: it emits exactly the faces
            // on the boundary of the selection. A single voxel gives 6; two face-adjacent voxels
            // give 10, not 12, because the shared face is internal.
            ShadingVolume volume = new ShadingVolume(
                new Point3D(0, 0, 0), Vector3D.WorldX, Vector3D.WorldY, Vector3D.WorldZ, 0.25, 4, 4, 4);

            List<ShadingElement> single = Analytical.SolarCalculator.Query.VoxelSurfaceShadingElements(
                volume, new List<int> { volume.VoxelIndex(1, 1, 1) });
            Assert.Equal(6, single.Count);

            List<ShadingElement> pair = Analytical.SolarCalculator.Query.VoxelSurfaceShadingElements(
                volume, new List<int> { volume.VoxelIndex(1, 1, 1), volume.VoxelIndex(2, 1, 1) });
            Assert.Equal(10, pair.Count);

            // A 2x2x2 block: 8 voxels, 24 boundary faces, 24 internal.
            List<int> block = new List<int>();
            for (int i = 1; i <= 2; i++)
            {
                for (int j = 1; j <= 2; j++)
                {
                    for (int k = 1; k <= 2; k++)
                    {
                        block.Add(volume.VoxelIndex(i, j, k));
                    }
                }
            }

            List<ShadingElement> cube = Analytical.SolarCalculator.Query.VoxelSurfaceShadingElements(volume, block);
            output.WriteLine($"1 voxel -> {single.Count} faces, 2 adjacent -> {pair.Count} faces, 2x2x2 block -> {cube.Count} faces (48 total, 24 internal)");
            Assert.Equal(24, cube.Count);

            // Total surface area of the block: 6 sides of 0.5 m x 0.5 m.
            double area = 0;
            foreach (ShadingElement element in cube)
            {
                area += element.Area;
            }

            Assert.Equal(6 * 0.5 * 0.5, area, 9);

            // Deterministic ordering, whatever order the selection arrives in.
            block.Reverse();
            List<ShadingElement> again = Analytical.SolarCalculator.Query.VoxelSurfaceShadingElements(volume, block);
            for (int i = 0; i < cube.Count; i++)
            {
                Assert.Equal(cube[i].Guid, again[i].Guid);
            }
        }
    }
}
