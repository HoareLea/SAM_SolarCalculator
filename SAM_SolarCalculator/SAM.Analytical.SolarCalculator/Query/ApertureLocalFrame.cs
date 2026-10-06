// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;

namespace SAM.Analytical.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// A world point in the APERTURE frame: origin at the aperture centroid, X horizontal along
        /// the facade, Y up-slope, Z outward. A 2 m x 1 m window therefore spans x in [-1, 1],
        /// y in [-0.5, 0.5], with the glass at z = 0.
        ///
        /// This is NOT the ShadingVolume frame. A volume's origin is its minimum CORNER — aperture
        /// (minX - left, minY - down, 0) — so ShadingVolume.TryToLocal returns coordinates shifted
        /// by the margins and never negative. Mixing the two silently addresses the wrong voxels: a
        /// band meant to sit above the head lands on the glass, and any test for the left-hand side
        /// of the aperture matches nothing at all. Anything reasoning about WHERE a voxel is
        /// relative to the opening should use this.
        /// </summary>
        public static bool TryGetApertureLocal(this ApertureSolarTarget target, Point3D point3D, out double x, out double y, out double z)
        {
            x = y = z = double.NaN;

            Plane plane = target?.Plane;
            if (plane == null || point3D == null)
            {
                return false;
            }

            Point3D origin = plane.Origin;
            Vector3D axisX = plane.AxisX;
            Vector3D axisY = plane.AxisY;
            Vector3D axisZ = plane.Normal;

            double dx = point3D.X - origin.X;
            double dy = point3D.Y - origin.Y;
            double dz = point3D.Z - origin.Z;

            x = dx * axisX.X + dy * axisX.Y + dz * axisX.Z;
            y = dx * axisY.X + dy * axisY.Y + dz * axisY.Z;
            z = dx * axisZ.X + dy * axisZ.Y + dz * axisZ.Z;
            return true;
        }

        /// <summary>A voxel centre in the aperture frame. See TryGetApertureLocal for the frame.</summary>
        public static bool TryGetApertureLocalCentre(this ApertureSolarTarget target, ShadingVolume volume, int voxelIndex, out double x, out double y, out double z)
        {
            x = y = z = double.NaN;
            Point3D centre = volume?.GetCentre(voxelIndex);
            return centre != null && TryGetApertureLocal(target, centre, out x, out y, out z);
        }

        /// <summary>The aperture's own extent in its local frame: x across the facade, y up-slope.</summary>
        public static bool TryGetApertureLocalBounds(this ApertureSolarTarget target, out double minX, out double maxX, out double minY, out double maxY)
        {
            minX = maxX = minY = maxY = double.NaN;

            Plane plane = target?.Plane;
            Face3D face3D = target?.Face3D;
            if (plane == null || face3D == null)
            {
                return false;
            }

            Geometry.Planar.BoundingBox2D boundingBox2D = plane.Convert(face3D)?.GetBoundingBox();
            if (boundingBox2D == null)
            {
                return false;
            }

            minX = boundingBox2D.Min.X;
            maxX = boundingBox2D.Max.X;
            minY = boundingBox2D.Min.Y;
            maxY = boundingBox2D.Max.Y;
            return true;
        }
    }
}
