// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using SAM.Analytical.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Gate 0 Review B. Direct probes of the Stage 6 DDA traversal — the same code the field build
    /// marches through, reached via Query.TraversedVoxels rather than reimplemented here.
    ///
    /// The question under test is the lattice-coincidence tolerance. A ray origin sitting EXACTLY
    /// on a voxel plane and stepping negatively on that axis must start in the voxel it enters, not
    /// the one on the far side of the plane which it touches with zero path length. That test is
    /// made on q = p / voxelSize, and the representation error of q depends on how far from the
    /// world origin the model is sited, because p is a difference of world coordinates.
    /// </summary>
    public class VoxelTraversalTests
    {
        private readonly ITestOutputHelper output;

        public VoxelTraversalTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        /// <summary>An axis-aligned grid whose minimum corner sits at (offset, offset, offset).</summary>
        private static ShadingVolume Volume(double offset, double voxelSize = 0.25, int count = 4)
        {
            return new ShadingVolume(
                new Point3D(offset, offset, offset),
                Vector3D.WorldX, Vector3D.WorldY, Vector3D.WorldZ,
                voxelSize, count, count, count);
        }

        private static Point3D Local(double offset, double x, double y, double z)
        {
            return new Point3D(offset + x, offset + y, offset + z);
        }

        /// <summary>
        /// Independent reference: per-voxel ray/box slab intersection counting only POSITIVE path
        /// length, computed in exact local coordinates. It never sees the world offset, so it is
        /// the answer the traversal is supposed to reproduce however far from the origin the model
        /// is sited.
        /// </summary>
        private static List<int> ReferenceTraversal(ShadingVolume volume, double sx, double sy, double sz, double dx, double dy, double dz)
        {
            double voxelSize = volume.VoxelSize;
            List<Tuple<double, int>> hits = new List<Tuple<double, int>>();

            for (int k = 0; k < volume.CountZ; k++)
            {
                for (int j = 0; j < volume.CountY; j++)
                {
                    for (int i = 0; i < volume.CountX; i++)
                    {
                        double tEnter = double.NegativeInfinity;
                        double tExit = double.PositiveInfinity;

                        if (!Slab(sx, dx, i * voxelSize, (i + 1) * voxelSize, ref tEnter, ref tExit)) { continue; }
                        if (!Slab(sy, dy, j * voxelSize, (j + 1) * voxelSize, ref tEnter, ref tExit)) { continue; }
                        if (!Slab(sz, dz, k * voxelSize, (k + 1) * voxelSize, ref tEnter, ref tExit)) { continue; }

                        double start = Math.Max(tEnter, 0.0);
                        if (tExit - start <= 1e-12 * voxelSize)
                        {
                            continue; // a tangential touch intercepts nothing
                        }

                        hits.Add(new Tuple<double, int>(start, volume.VoxelIndex(i, j, k)));
                    }
                }
            }

            hits.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            List<int> result = new List<int>();
            foreach (Tuple<double, int> hit in hits)
            {
                result.Add(hit.Item2);
            }

            return result;
        }

        private static bool Equal(List<int> x, List<int> y)
        {
            if (x == null || y == null || x.Count != y.Count)
            {
                return false;
            }

            for (int i = 0; i < x.Count; i++)
            {
                if (x[i] != y[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Slab(double s, double d, double low, double high, ref double tEnter, ref double tExit)
        {
            if (Math.Abs(d) < 1e-15)
            {
                return s > low && s < high;
            }

            double t1 = (low - s) / d;
            double t2 = (high - s) / d;
            if (t1 > t2) { double temp = t1; t1 = t2; t2 = temp; }
            if (t1 > tEnter) { tEnter = t1; }
            if (t2 < tExit) { tExit = t2; }
            return tEnter <= tExit;
        }

        [Fact]
        public void Traversal_Reports_Exactly_The_Entered_Voxels_On_A_Lattice_Plane()
        {
            // x = 0.5 is exactly the plane between voxels 1 and 2 (0.5 / 0.25 = 2 exactly).
            // y, z sit at voxel centres so only the x axis is degenerate.
            // Marching in -x the ray enters voxel 1 then voxel 0, and NEVER voxel 2, which it
            // touches with zero path length.
            foreach (double offset in new double[] { 0.0, 1.0, 100.0 })
            {
                ShadingVolume volume = Volume(offset);
                List<int> traversed = Analytical.SolarCalculator.Query.TraversedVoxels(volume, Local(offset, 0.5, 0.375, 0.375), new Vector3D(-1, 0, 0));

                List<int> expected = new List<int> { volume.VoxelIndex(1, 1, 1), volume.VoxelIndex(0, 1, 1) };
                output.WriteLine($"offset {offset,10:0.#}: traversed [{string.Join(", ", traversed)}] expected [{string.Join(", ", expected)}]");
                Assert.Equal(expected, traversed);
            }
        }

        [Fact]
        public void Traversal_Matches_An_Independent_PositivePathLength_Box_Traversal()
        {
            // Directions chosen to exercise every step-sign combination, including axis-aligned
            // ones where a component is exactly zero, and oblique ones that cross many voxels.
            double[][] directions = new double[][]
            {
                new double[] { -1, 0, 0 }, new double[] { 1, 0, 0 },
                new double[] { 0, -1, 0 }, new double[] { 0, 1, 0 },
                new double[] { 0, 0, -1 }, new double[] { 0, 0, 1 },
                new double[] { -1, -1, 0 }, new double[] { 1, 1, 1 },
                new double[] { -1, 1, -1 }, new double[] { 0.3, -0.8, 0.5 },
                new double[] { -0.7, -0.2, 0.9 },
            };

            // Start points: exactly on lattice planes, immediately either side of them, and at
            // voxel centres.
            double e = 1e-7;
            double[][] starts = new double[][]
            {
                new double[] { 0.5, 0.5, 0.5 },
                new double[] { 0.5, 0.375, 0.375 },
                new double[] { 0.5 - e, 0.375, 0.375 },
                new double[] { 0.5 + e, 0.375, 0.375 },
                new double[] { 0.375, 0.5, 0.625 },
                new double[] { 0.25, 0.75, 0.5 },
                new double[] { 0.625, 0.625, 0.625 },
            };

            int mismatches = 0;
            int compared = 0;
            int skipped = 0;
            foreach (double offset in new double[] { 0.0, 1000.0, 100000.0 })
            {
                ShadingVolume volume = Volume(offset);
                foreach (double[] start in starts)
                {
                    foreach (double[] direction in directions)
                    {
                        // A ray that lies WITHIN a lattice plane (zero component on an axis whose
                        // start coordinate is exactly on a plane) has zero cross-section with every
                        // voxel on both sides. There is no correct answer to reproduce, only a
                        // tie-break, so it is excluded here and pinned by its own test below.
                        if (RunsInsideALatticePlane(volume, start, direction))
                        {
                            skipped++;
                            continue;
                        }

                        Vector3D unit = new Vector3D(direction[0], direction[1], direction[2]).Unit;
                        List<int> traversed = Analytical.SolarCalculator.Query.TraversedVoxels(volume, Local(offset, start[0], start[1], start[2]), unit);
                        List<int> reference = ReferenceTraversal(volume, start[0], start[1], start[2], unit.X, unit.Y, unit.Z);

                        compared++;
                        if (!Equal(traversed, reference))
                        {
                            mismatches++;
                            output.WriteLine($"MISMATCH offset {offset} start ({start[0]},{start[1]},{start[2]}) dir ({direction[0]},{direction[1]},{direction[2]})");
                            output.WriteLine($"   dda [{string.Join(", ", traversed)}]");
                            output.WriteLine($"   ref [{string.Join(", ", reference)}]");
                        }
                    }
                }
            }

            output.WriteLine($"{compared} ray/grid cases compared against the independent box traversal, {mismatches} mismatches ({skipped} in-plane cases excluded as ambiguous)");
            Assert.True(compared > 150, "the exclusion must not be swallowing the whole comparison");
            Assert.Equal(0, mismatches);
        }

        private static bool RunsInsideALatticePlane(ShadingVolume volume, double[] start, double[] direction)
        {
            double voxelSize = volume.VoxelSize;
            for (int axis = 0; axis < 3; axis++)
            {
                if (direction[axis] != 0)
                {
                    continue;
                }

                double q = start[axis] / voxelSize;
                if (q == Math.Floor(q))
                {
                    return true;
                }
            }

            return false;
        }

        [Fact]
        public void A_Ray_Lying_In_A_Lattice_Plane_Picks_One_Side_Deterministically()
        {
            // The documented tie-break, not a defect. A ray running exactly along the face between
            // two voxels grazes both with zero cross-section. Crediting neither would delete a real
            // ray's whole contribution from the field; crediting both would double it. The
            // traversal credits the LOWER-index side, consistently, on every axis — which is what
            // the negative-step convention in StartVoxel produces.
            //
            // This is reachable in practice, not a synthetic curiosity: a due-south facade gives a
            // sun group whose representative direction has no across-facade component at all, and
            // analysis-cell origins land on voxel planes whenever the grid size is a multiple of
            // the voxel size.
            ShadingVolume volume = Volume(0.0);
            List<int> traversed = Analytical.SolarCalculator.Query.TraversedVoxels(volume, Local(0.0, 0.375, 0.5, 0.375), new Vector3D(-1, 0, 0));

            output.WriteLine($"ray in the y = 0.5 plane travelling -x traverses [{string.Join(", ", traversed)}]");

            // y = 0.5 is the plane between j = 1 and j = 2; the lower side is chosen.
            Assert.Equal(new List<int> { volume.VoxelIndex(1, 1, 1), volume.VoxelIndex(0, 1, 1) }, traversed);

            // And it is stable, not incidental.
            Assert.Equal(traversed, Analytical.SolarCalculator.Query.TraversedVoxels(volume, Local(0.0, 0.375, 0.5, 0.375), new Vector3D(-1, 0, 0)));
        }

        [Fact]
        public void An_Exact_Corner_Crossing_Enters_The_Diagonal_Neighbour_Not_The_Staircase()
        {
            // Gate 0 Review B defect. Stepping one axis at a time when two boundaries fall at the
            // same point along the ray reports the intervening voxels, which the ray touches with
            // zero path length. From (0.5, 0.375, 0.375) along (1, 1, 1) the y and z boundaries at
            // 0.5 are reached together, so the ray leaves (2,1,1) through an edge and enters
            // (2,2,2) directly; (2,1,2) is a phantom.
            ShadingVolume volume = Volume(0.0);
            List<int> traversed = Analytical.SolarCalculator.Query.TraversedVoxels(
                volume, Local(0.0, 0.5, 0.375, 0.375), new Vector3D(1, 1, 1).Unit);

            List<int> reference = ReferenceTraversal(volume, 0.5, 0.375, 0.375, 1 / Math.Sqrt(3), 1 / Math.Sqrt(3), 1 / Math.Sqrt(3));

            output.WriteLine($"dda [{string.Join(", ", traversed)}]");
            output.WriteLine($"ref [{string.Join(", ", reference)}]");

            Assert.Equal(reference, traversed);
            Assert.DoesNotContain(volume.VoxelIndex(2, 1, 2), traversed);
            Assert.DoesNotContain(volume.VoxelIndex(3, 2, 3), traversed);
        }

        [Fact]
        public void Lattice_Coincidence_Survives_Large_World_Coordinate_Offsets()
        {
            // The defect this guards. Local coordinates come from worldPoint - volumeOrigin, so
            // their absolute error grows with the world magnitude. A FIXED tolerance of 1e-9 voxel
            // units drops below that error somewhere around 1e6 m of offset, the coincidence test
            // stops firing, and the negative-direction start voxel silently reverts to the phantom
            // zero-path-length visit the tolerance exists to prevent.
            //
            // National grid coordinates reach these magnitudes routinely: UK OSGB northings run to
            // ~1.2e6 m, and UTM southern-hemisphere zones carry a false northing of 1e7 m.
            //
            // The frame is ROTATED, which is what makes this a fair test. With world-aligned axes
            // and a dyadic offset the subtraction in TryToLocal happens to be exact and nothing is
            // exercised. A real aperture frame is oblique, so the local coordinate comes out of a
            // three-term dot product and carries the world magnitude's rounding error. Measured
            // error in q at a 1e7 m offset is ~4.8e-9 — five times the old fixed 1e-9 tolerance,
            // so the coincidence test stopped firing and the phantom visit came back.
            output.WriteLine("offset m   | voxels | first voxel | expected | verdict");
            foreach (double offset in new double[] { 0.0, 1e3, 1e5, 5.29e5, 1.2e6, 1e7, 1e8 })
            {
                ShadingVolume volume = ObliqueVolume(offset);

                // A cell origin exactly on the x = 0.5 lattice plane, y and z at voxel centres.
                Point3D origin = volume.ToWorld(0.5, 0.375, 0.375);
                Vector3D direction = volume.AxisX.GetNegated();

                List<int> traversed = Analytical.SolarCalculator.Query.TraversedVoxels(volume, origin, direction);

                int expectedFirst = volume.VoxelIndex(1, 1, 1);
                bool ok = traversed.Count == 2 && traversed[0] == expectedFirst;
                output.WriteLine($"{offset,10:0.###e+0} | {traversed.Count,6} | {traversed[0],11} | {expectedFirst,8} | {(ok ? "ok" : "PHANTOM VISIT")}");

                Assert.True(ok, $"at a world offset of {offset} m the traversal reported {traversed.Count} voxels starting at {traversed[0]}, expected 2 starting at {expectedFirst}");
            }
        }

        /// <summary>A grid on an oblique (37 degree) vertical facade, sited at a large world offset.</summary>
        private static ShadingVolume ObliqueVolume(double offset, double voxelSize = 0.25, int count = 4)
        {
            double a = 37.0 * Math.PI / 180.0;
            Vector3D axisX = new Vector3D(Math.Cos(a), Math.Sin(a), 0);
            Vector3D axisY = Vector3D.WorldZ;
            Vector3D axisZ = new Vector3D(-Math.Sin(a), Math.Cos(a), 0);

            return new ShadingVolume(
                new Point3D(offset, offset * 0.7 + 1234.567, 3.25),
                axisX, axisY, axisZ, voxelSize, count, count, count);
        }

        [Fact]
        public void Points_Just_Inside_A_Voxel_Are_Not_Snapped_To_The_Boundary()
        {
            // The other side of the tolerance: it must not swallow real displacement. A point one
            // millimetre inside voxel 2 belongs to voxel 2 even though it is near the plane.
            foreach (double offset in new double[] { 0.0, 1e5 })
            {
                ShadingVolume volume = Volume(offset);
                List<int> traversed = Analytical.SolarCalculator.Query.TraversedVoxels(volume, Local(offset, 0.5 + 0.001, 0.375, 0.375), new Vector3D(-1, 0, 0));

                output.WriteLine($"offset {offset,10:0.###e+0}: 1 mm inside voxel 2 traverses [{string.Join(", ", traversed)}]");
                Assert.Equal(3, traversed.Count);
                Assert.Equal(volume.VoxelIndex(2, 1, 1), traversed[0]);
            }
        }
    }
}
