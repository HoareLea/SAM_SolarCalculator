// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Create
    {
        /// <summary>
        /// Builds the candidate shading volume in front of an aperture: the aperture's local 2D
        /// bounding box expanded by the given margins, extruded outward by maxDepth, discretised at
        /// voxelSize. The frame comes from the ApertureSolarTarget (origin at the centroid, X
        /// horizontal, Y up-slope, Z = outward normal), so the volume is always aperture-local and
        /// deterministic for a given target. Voxel counts round UP so the whole expanded box is
        /// covered.
        /// </summary>
        /// <param name="target">The aperture the volume sits in front of.</param>
        /// <param name="maxDepth">Maximum outward projection distance (search depth), m.</param>
        /// <param name="up">Margin above the aperture's local top edge, m.</param>
        /// <param name="down">Margin below the aperture's local bottom edge, m.</param>
        /// <param name="left">Margin beyond the aperture's local left (minimum-X) edge, m.</param>
        /// <param name="right">Margin beyond the aperture's local right (maximum-X) edge, m.</param>
        /// <param name="voxelSize">Voxel edge length, m (the field resolution).</param>
        /// <param name="tolerance_Distance">Distance tolerance.</param>
        public static ShadingVolume ShadingVolume(this ApertureSolarTarget target, double maxDepth, double up, double down, double left, double right, double voxelSize, double tolerance_Distance = Core.Tolerance.Distance)
        {
            Face3D face3D = target?.Face3D;
            Plane plane = target?.Plane;
            if (face3D == null || plane == null || double.IsNaN(voxelSize) || voxelSize <= tolerance_Distance || double.IsNaN(maxDepth) || maxDepth <= 0)
            {
                return null;
            }

            Geometry.Planar.Face2D face2D = plane.Convert(face3D);
            Geometry.Planar.BoundingBox2D boundingBox2D = face2D?.GetBoundingBox();
            if (boundingBox2D == null)
            {
                return null;
            }

            double minX = boundingBox2D.Min.X - left;
            double maxX = boundingBox2D.Max.X + right;
            double minY = boundingBox2D.Min.Y - down;
            double maxY = boundingBox2D.Max.Y + up;

            int countX = Math.Max(1, (int)Math.Ceiling((maxX - minX) / voxelSize));
            int countY = Math.Max(1, (int)Math.Ceiling((maxY - minY) / voxelSize));
            int countZ = Math.Max(1, (int)Math.Ceiling(maxDepth / voxelSize));

            Point3D origin = new Point3D(
                plane.Origin.X + minX * plane.AxisX.X + minY * plane.AxisY.X,
                plane.Origin.Y + minX * plane.AxisX.Y + minY * plane.AxisY.Y,
                plane.Origin.Z + minX * plane.AxisX.Z + minY * plane.AxisY.Z);

            return new ShadingVolume(origin, plane.AxisX, plane.AxisY, plane.Normal, voxelSize, countX, countY, countZ);
        }
    }
}
