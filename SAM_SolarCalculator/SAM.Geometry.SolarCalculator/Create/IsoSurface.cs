// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// Iso-surface of a regular scalar lattice by MARCHING TETRAHEDRA, returned as a Mesh3D
        /// whose triangle winding puts the normal on the low-value side (outward from the region
        /// above isoLevel).
        ///
        /// Marching tetrahedra rather than marching cubes. The plan named marching cubes; the
        /// divergence is deliberate and is about correctness risk, not novelty. A cube has 256
        /// sign configurations that reduce to 15 base cases plus a set of genuinely ambiguous ones
        /// (faces where two diagonally opposite corners are inside and the other two are outside
        /// can be joined either way), and resolving those consistently needs either an extended
        /// table or asymptotic-decider logic; getting it wrong produces holes in the surface. A
        /// tetrahedron has only 16 configurations, every one of them unambiguous, and each reduces
        /// to a single triangle or a single quad. The cost is roughly twice the triangle count for
        /// the same lattice, which is irrelevant here because the mesh is a display and
        /// quantity-take-off artefact, never the source of truth for the numbers.
        ///
        /// Each cube of eight neighbouring samples is split into six tetrahedra sharing the 0-7
        /// main diagonal, in a fixed order, so neighbouring cubes always split their shared face
        /// the same way and the surface cannot crack. Every tetrahedron is normalised to positive
        /// orientation before its case is emitted, which is what makes the winding rule below
        /// hold uniformly.
        ///
        /// Vertices are welded by lattice EDGE rather than by position: an interpolated vertex is
        /// keyed on the ordered pair of sample indices it lies between, so the tetrahedra sharing
        /// that edge produce exactly one mesh vertex, with no tolerance-based point merging and no
        /// dependence on floating-point equality.
        /// </summary>
        /// <param name="values">Sample values, linear index i + countX x (j + countY x k).</param>
        /// <param name="countX">Sample count along axisX (not cell count).</param>
        /// <param name="countY">Sample count along axisY.</param>
        /// <param name="countZ">Sample count along axisZ.</param>
        /// <param name="isoLevel">The level set to extract. Samples strictly above it are "inside".</param>
        /// <param name="origin">World position of sample (0, 0, 0).</param>
        /// <param name="axisX">Unit lattice axis for i.</param>
        /// <param name="axisY">Unit lattice axis for j.</param>
        /// <param name="axisZ">Unit lattice axis for k.</param>
        /// <param name="spacing">Lattice spacing, m.</param>
        /// <returns>The iso-surface, or null when the inputs are unusable. An empty surface (no
        /// sample above isoLevel) returns a Mesh3D with no triangles, never null.</returns>
        public static Mesh3D IsoSurface(double[] values, int countX, int countY, int countZ, double isoLevel, Point3D origin, Vector3D axisX, Vector3D axisY, Vector3D axisZ, double spacing)
        {
            if (values == null || origin == null || axisX == null || axisY == null || axisZ == null)
            {
                return null;
            }

            if (countX < 2 || countY < 2 || countZ < 2 || values.Length != countX * countY * countZ)
            {
                return null;
            }

            if (double.IsNaN(isoLevel) || double.IsNaN(spacing) || spacing <= 0)
            {
                return null;
            }

            Vector3D unitX = axisX.Unit;
            Vector3D unitY = axisY.Unit;
            Vector3D unitZ = axisZ.Unit;

            List<Point3D> points = new List<Point3D>();
            List<Tuple<int, int, int>> indexes = new List<Tuple<int, int, int>>();
            Dictionary<long, int> edgeVertices = new Dictionary<long, int>();

            // Cube-corner offsets, corner index = dx + 2 dy + 4 dz.
            int[] cornerX = new int[] { 0, 1, 0, 1, 0, 1, 0, 1 };
            int[] cornerY = new int[] { 0, 0, 1, 1, 0, 0, 1, 1 };
            int[] cornerZ = new int[] { 0, 0, 0, 0, 1, 1, 1, 1 };

            // Six tetrahedra sharing the 0-7 diagonal, walking the hexagonal cycle of the other six
            // corners: 1 -> 3 -> 2 -> 6 -> 4 -> 5 -> 1.
            int[][] tetrahedra = new int[][]
            {
                new int[] { 0, 7, 1, 3 },
                new int[] { 0, 7, 3, 2 },
                new int[] { 0, 7, 2, 6 },
                new int[] { 0, 7, 6, 4 },
                new int[] { 0, 7, 4, 5 },
                new int[] { 0, 7, 5, 1 },
            };

            int[] sampleIndex = new int[4];
            double[] sampleValue = new double[4];
            Point3D[] samplePoint = new Point3D[4];

            for (int k = 0; k < countZ - 1; k++)
            {
                for (int j = 0; j < countY - 1; j++)
                {
                    for (int i = 0; i < countX - 1; i++)
                    {
                        foreach (int[] tetrahedron in tetrahedra)
                        {
                            for (int c = 0; c < 4; c++)
                            {
                                int corner = tetrahedron[c];
                                int si = i + cornerX[corner];
                                int sj = j + cornerY[corner];
                                int sk = k + cornerZ[corner];

                                sampleIndex[c] = si + countX * (sj + countY * sk);
                                sampleValue[c] = values[sampleIndex[c]];
                                samplePoint[c] = LatticePoint(origin, unitX, unitY, unitZ, spacing, si, sj, sk);
                            }

                            Emit(sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices);
                        }
                    }
                }
            }

            return new Mesh3D(points, indexes);
        }

        private static Point3D LatticePoint(Point3D origin, Vector3D axisX, Vector3D axisY, Vector3D axisZ, double spacing, int i, int j, int k)
        {
            double x = i * spacing;
            double y = j * spacing;
            double z = k * spacing;

            return new Point3D(
                origin.X + x * axisX.X + y * axisY.X + z * axisZ.X,
                origin.Y + x * axisX.Y + y * axisY.Y + z * axisZ.Y,
                origin.Z + x * axisX.Z + y * axisY.Z + z * axisZ.Z);
        }

        /// <summary>
        /// Emits the triangles of one tetrahedron. The tetrahedron is first normalised to positive
        /// orientation (((b-a) x (c-a)) . (d-a) &gt; 0) by swapping two vertices, so a single winding
        /// convention covers every case: with exactly one vertex inside, the triangle taken in the
        /// order (other, other, other) around that vertex has its normal pointing away from it,
        /// i.e. from the inside toward the outside.
        /// </summary>
        private static void Emit(int[] sampleIndex, double[] sampleValue, Point3D[] samplePoint, double isoLevel, List<Point3D> points, List<Tuple<int, int, int>> indexes, Dictionary<long, int> edgeVertices)
        {
            if (Orientation(samplePoint) < 0)
            {
                Swap(sampleIndex, sampleValue, samplePoint, 0, 1);
            }

            int caseIndex = 0;
            if (sampleValue[0] > isoLevel) { caseIndex |= 1; }
            if (sampleValue[1] > isoLevel) { caseIndex |= 2; }
            if (sampleValue[2] > isoLevel) { caseIndex |= 4; }
            if (sampleValue[3] > isoLevel) { caseIndex |= 8; }

            if (caseIndex == 0 || caseIndex == 15)
            {
                return;
            }

            switch (caseIndex)
            {
                // ---- exactly one vertex inside: one triangle, normal away from that vertex ----
                case 1: Triangle(0, 1, 0, 2, 0, 3, false, sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices); break;
                case 2: Triangle(1, 0, 1, 2, 1, 3, true, sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices); break;
                case 4: Triangle(2, 0, 2, 1, 2, 3, false, sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices); break;
                case 8: Triangle(3, 0, 3, 1, 3, 2, true, sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices); break;

                // ---- exactly one vertex outside: same triangle, reversed ----
                case 14: Triangle(0, 1, 0, 2, 0, 3, true, sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices); break;
                case 13: Triangle(1, 0, 1, 2, 1, 3, false, sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices); break;
                case 11: Triangle(2, 0, 2, 1, 2, 3, true, sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices); break;
                case 7: Triangle(3, 0, 3, 1, 3, 2, false, sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices); break;

                // ---- two in, two out: a quad across the four cut edges, as two triangles ----
                // With inside {a, b} and outside {c, d} the cut edges form the perimeter cycle
                // (a-c, a-d, b-d, b-c) — consecutive pairs share a, d, b and c respectively. That
                // cycle winds outward exactly when (a, b, c, d) is an EVEN permutation of
                // (0, 1, 2, 3); an odd one relabels the (already positively oriented) tetrahedron
                // into a negative frame and flips the normal, so it is emitted reversed.
                case 3: Quad(0, 2, 0, 3, 1, 3, 1, 2, false, sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices); break;   // (0,1,2,3) even
                case 12: Quad(2, 0, 2, 1, 3, 1, 3, 0, false, sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices); break;  // (2,3,0,1) even
                case 5: Quad(0, 1, 0, 3, 2, 3, 2, 1, true, sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices); break;    // (0,2,1,3) odd
                case 10: Quad(1, 0, 1, 2, 3, 2, 3, 0, true, sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices); break;  // (1,3,0,2) odd
                case 9: Quad(0, 1, 0, 2, 3, 2, 3, 1, false, sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices); break;  // (0,3,1,2) even
                case 6: Quad(1, 0, 1, 3, 2, 3, 2, 0, false, sampleIndex, sampleValue, samplePoint, isoLevel, points, indexes, edgeVertices); break;  // (1,2,0,3) even
            }
        }

        private static double Orientation(Point3D[] samplePoint)
        {
            double ax = samplePoint[1].X - samplePoint[0].X;
            double ay = samplePoint[1].Y - samplePoint[0].Y;
            double az = samplePoint[1].Z - samplePoint[0].Z;
            double bx = samplePoint[2].X - samplePoint[0].X;
            double by = samplePoint[2].Y - samplePoint[0].Y;
            double bz = samplePoint[2].Z - samplePoint[0].Z;
            double cx = samplePoint[3].X - samplePoint[0].X;
            double cy = samplePoint[3].Y - samplePoint[0].Y;
            double cz = samplePoint[3].Z - samplePoint[0].Z;

            return (ay * bz - az * by) * cx + (az * bx - ax * bz) * cy + (ax * by - ay * bx) * cz;
        }

        private static void Swap(int[] sampleIndex, double[] sampleValue, Point3D[] samplePoint, int a, int b)
        {
            int index = sampleIndex[a]; sampleIndex[a] = sampleIndex[b]; sampleIndex[b] = index;
            double value = sampleValue[a]; sampleValue[a] = sampleValue[b]; sampleValue[b] = value;
            Point3D point = samplePoint[a]; samplePoint[a] = samplePoint[b]; samplePoint[b] = point;
        }

        private static void Triangle(int a1, int b1, int a2, int b2, int a3, int b3, bool reverse, int[] sampleIndex, double[] sampleValue, Point3D[] samplePoint, double isoLevel, List<Point3D> points, List<Tuple<int, int, int>> indexes, Dictionary<long, int> edgeVertices)
        {
            int v1 = EdgeVertex(a1, b1, sampleIndex, sampleValue, samplePoint, isoLevel, points, edgeVertices);
            int v2 = EdgeVertex(a2, b2, sampleIndex, sampleValue, samplePoint, isoLevel, points, edgeVertices);
            int v3 = EdgeVertex(a3, b3, sampleIndex, sampleValue, samplePoint, isoLevel, points, edgeVertices);
            Add(v1, v2, v3, reverse, indexes);
        }

        private static void Quad(int a1, int b1, int a2, int b2, int a3, int b3, int a4, int b4, bool reverse, int[] sampleIndex, double[] sampleValue, Point3D[] samplePoint, double isoLevel, List<Point3D> points, List<Tuple<int, int, int>> indexes, Dictionary<long, int> edgeVertices)
        {
            int v1 = EdgeVertex(a1, b1, sampleIndex, sampleValue, samplePoint, isoLevel, points, edgeVertices);
            int v2 = EdgeVertex(a2, b2, sampleIndex, sampleValue, samplePoint, isoLevel, points, edgeVertices);
            int v3 = EdgeVertex(a3, b3, sampleIndex, sampleValue, samplePoint, isoLevel, points, edgeVertices);
            int v4 = EdgeVertex(a4, b4, sampleIndex, sampleValue, samplePoint, isoLevel, points, edgeVertices);

            // Both triangles must split the quad on the SAME diagonal (v1-v3) so the shared edge
            // cancels and the surface stays closed.
            Add(v1, v2, v3, reverse, indexes);
            Add(v1, v3, v4, reverse, indexes);
        }

        private static void Add(int v1, int v2, int v3, bool reverse, List<Tuple<int, int, int>> indexes)
        {
            // A cut that lands exactly on a lattice sample welds two corners of the triangle onto
            // one vertex: the triangle has no area and must not enter the mesh.
            if (v1 == v2 || v2 == v3 || v1 == v3)
            {
                return;
            }

            indexes.Add(reverse ? new Tuple<int, int, int>(v1, v3, v2) : new Tuple<int, int, int>(v1, v2, v3));
        }

        /// <summary>
        /// The interpolated vertex on the lattice edge between tetrahedron vertices a and b, created
        /// once and reused. Keyed on the ordered pair of SAMPLE indices, so every tetrahedron and
        /// every cube sharing that edge resolves to the same mesh vertex.
        /// </summary>
        private static int EdgeVertex(int a, int b, int[] sampleIndex, double[] sampleValue, Point3D[] samplePoint, double isoLevel, List<Point3D> points, Dictionary<long, int> edgeVertices)
        {
            int indexA = sampleIndex[a];
            int indexB = sampleIndex[b];
            long key = indexA < indexB
                ? (long)indexA * int.MaxValue + indexB
                : (long)indexB * int.MaxValue + indexA;

            if (edgeVertices.TryGetValue(key, out int existing))
            {
                return existing;
            }

            double valueA = sampleValue[a];
            double valueB = sampleValue[b];
            double denominator = valueB - valueA;
            double t = Math.Abs(denominator) < 1e-300 ? 0.5 : (isoLevel - valueA) / denominator;
            if (t < 0.0) { t = 0.0; }
            if (t > 1.0) { t = 1.0; }

            // Interpolate from the lower sample index so the result cannot depend on which
            // tetrahedron reached the edge first.
            double parameter = indexA < indexB ? t : 1.0 - t;
            Point3D from = indexA < indexB ? samplePoint[a] : samplePoint[b];
            Point3D to = indexA < indexB ? samplePoint[b] : samplePoint[a];

            Point3D point = new Point3D(
                from.X + parameter * (to.X - from.X),
                from.Y + parameter * (to.Y - from.Y),
                from.Z + parameter * (to.Z - from.Z));

            int result = points.Count;
            points.Add(point);
            edgeVertices[key] = result;
            return result;
        }
    }
}
