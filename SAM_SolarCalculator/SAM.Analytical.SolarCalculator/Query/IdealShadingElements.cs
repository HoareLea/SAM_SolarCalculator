// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Geometry.Spatial;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// The Stage 7 ideal shape as RAY-TRACEABLE opaque elements, so it can be pushed through
        /// exactly the same first-hit engine as a buildable device instead of being compared as a
        /// field statistic against a ray-traced result.
        ///
        /// Two representations are offered because they answer different questions, and the
        /// difference between them is worth measuring rather than assuming:
        ///
        ///   VoxelSurfaceShadingElements — the boundary faces of the SELECTED VOXEL SET. This is
        ///     the exact ray-traceable equivalent of what the Stage 6 field actually says: the
        ///     field credits a voxel when a ray ENTERS it, so a solid made of those voxels
        ///     intercepts precisely the rays the field counted. Watertight by construction, since
        ///     only faces between a selected and an unselected voxel are emitted.
        ///
        ///   ShadingElements(IdealShadingResult) — the extracted marching-tetrahedra surface, one
        ///     element per triangle. This is the shape a user is shown and would hand to a
        ///     fabricator, interpolated across the iso-level rather than snapped to voxel
        ///     boundaries, so it is slightly SMALLER than the voxel solid.
        ///
        /// Neither replaces the scalar field as the source of truth. They exist so that an
        /// ideal-versus-rationalised comparison is a like-for-like ray-traced measurement.
        /// </summary>
        /// <param name="idealShadingResult">A Stage 7 result carrying a mesh.</param>
        /// <returns>One opaque element per mesh triangle, or null when there is no mesh.</returns>
        public static List<ShadingElement> ShadingElements(this IdealShadingResult idealShadingResult)
        {
            Mesh3D mesh = idealShadingResult?.Mesh;
            if (mesh == null || mesh.TrianglesCount == 0)
            {
                return null;
            }

            List<Triangle3D> triangles = mesh.GetTriangles();
            if (triangles == null)
            {
                return null;
            }

            List<ShadingElement> result = new List<ShadingElement>(triangles.Count);
            for (int i = 0; i < triangles.Count; i++)
            {
                Triangle3D triangle3D = triangles[i];
                if (triangle3D == null)
                {
                    continue;
                }

                Face3D face3D = new Face3D(triangle3D);
                if (face3D == null || double.IsNaN(face3D.GetArea()) || face3D.GetArea() <= 0)
                {
                    continue; // a degenerate triangle intercepts nothing and would only confuse attribution
                }

                result.Add(new ShadingElement(DeterministicGuid("IdealMesh", i), "Ideal triangle " + i, face3D));
            }

            return result;
        }

        /// <summary>
        /// The BOUNDARY faces of a selected voxel set as opaque elements: every face shared by a
        /// selected voxel and a non-selected neighbour (or the outside of the grid), and no
        /// internal face. A ray therefore meets the solid's surface exactly once on the way in,
        /// which is what makes first-hit attribution over this set mean the same thing as the
        /// field's "the ray entered this voxel".
        ///
        /// Internal faces are omitted deliberately. They would be invisible to a first hit anyway,
        /// but they would inflate the element count, the attribution GUID table and the material
        /// measure without changing a single intercepted ray.
        /// </summary>
        /// <param name="volume">The voxel grid the indices refer to.</param>
        /// <param name="voxelIndices">The selected voxels.</param>
        public static List<ShadingElement> VoxelSurfaceShadingElements(this ShadingVolume volume, IEnumerable<int> voxelIndices)
        {
            if (volume == null || voxelIndices == null)
            {
                return null;
            }

            int countX = volume.CountX;
            int countY = volume.CountY;
            int countZ = volume.CountZ;
            double voxelSize = volume.VoxelSize;

            HashSet<int> selected = new HashSet<int>(voxelIndices);

            // Ascending index order, so the element list is deterministic whatever order the
            // selection arrived in.
            List<int> ordered = new List<int>(selected);
            ordered.Sort();

            // Face offsets: (di, dj, dk) of the neighbour across each of the six faces.
            int[][] neighbours = new int[][]
            {
                new int[] { -1, 0, 0 }, new int[] { 1, 0, 0 },
                new int[] { 0, -1, 0 }, new int[] { 0, 1, 0 },
                new int[] { 0, 0, -1 }, new int[] { 0, 0, 1 },
            };

            List<ShadingElement> result = new List<ShadingElement>();
            int ordinal = 0;

            foreach (int index in ordered)
            {
                int i = index % countX;
                int j = (index / countX) % countY;
                int k = index / (countX * countY);

                for (int f = 0; f < neighbours.Length; f++)
                {
                    int ni = i + neighbours[f][0];
                    int nj = j + neighbours[f][1];
                    int nk = k + neighbours[f][2];

                    bool inside = ni >= 0 && ni < countX && nj >= 0 && nj < countY && nk >= 0 && nk < countZ;
                    if (inside && selected.Contains(volume.VoxelIndex(ni, nj, nk)))
                    {
                        continue; // internal face: never a first hit, never material
                    }

                    Face3D face3D = VoxelFace(volume, i, j, k, f, voxelSize);
                    if (face3D == null)
                    {
                        continue;
                    }

                    result.Add(new ShadingElement(DeterministicGuid("IdealVoxelSurface", index * 8 + f), $"Voxel {index} face {f}", face3D));
                    ordinal++;
                }
            }

            return result;
        }

        /// <summary>One of the six faces of voxel (i, j, k), in the volume's local frame.</summary>
        private static Face3D VoxelFace(ShadingVolume volume, int i, int j, int k, int face, double voxelSize)
        {
            double x0 = i * voxelSize, x1 = (i + 1) * voxelSize;
            double y0 = j * voxelSize, y1 = (j + 1) * voxelSize;
            double z0 = k * voxelSize, z1 = (k + 1) * voxelSize;

            double[][] corners;
            switch (face)
            {
                case 0: corners = new double[][] { new[] { x0, y0, z0 }, new[] { x0, y1, z0 }, new[] { x0, y1, z1 }, new[] { x0, y0, z1 } }; break;
                case 1: corners = new double[][] { new[] { x1, y0, z0 }, new[] { x1, y0, z1 }, new[] { x1, y1, z1 }, new[] { x1, y1, z0 } }; break;
                case 2: corners = new double[][] { new[] { x0, y0, z0 }, new[] { x0, y0, z1 }, new[] { x1, y0, z1 }, new[] { x1, y0, z0 } }; break;
                case 3: corners = new double[][] { new[] { x0, y1, z0 }, new[] { x1, y1, z0 }, new[] { x1, y1, z1 }, new[] { x0, y1, z1 } }; break;
                case 4: corners = new double[][] { new[] { x0, y0, z0 }, new[] { x1, y0, z0 }, new[] { x1, y1, z0 }, new[] { x0, y1, z0 } }; break;
                default: corners = new double[][] { new[] { x0, y0, z1 }, new[] { x0, y1, z1 }, new[] { x1, y1, z1 }, new[] { x1, y0, z1 } }; break;
            }

            List<Point3D> points = new List<Point3D>(4);
            foreach (double[] corner in corners)
            {
                Point3D point3D = volume.ToWorld(corner[0], corner[1], corner[2]);
                if (point3D == null)
                {
                    return null;
                }

                points.Add(point3D);
            }

            return new Face3D(new Polygon3D(points));
        }

        /// <summary>A stable Guid from a label and an ordinal, so these elements are comparable across runs.</summary>
        private static Guid DeterministicGuid(string label, int ordinal)
        {
            using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
            {
                string key = label + "|#" + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return new Guid(md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key)));
            }
        }
    }
}
