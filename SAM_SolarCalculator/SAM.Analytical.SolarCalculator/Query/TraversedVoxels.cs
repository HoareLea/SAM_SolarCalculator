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
        /// Every voxel of the volume a ray TRAVERSES with positive path length, in march order.
        ///
        /// This is the Stage 6 traversal itself, not a reimplementation of it: Create.ShadingPotentialField
        /// marches through the same code. It is exposed so the traversal can be probed directly with
        /// synthetic rays — lattice-degenerate origins, extreme world offsets, negative directions —
        /// instead of only being observable through a whole field build.
        /// </summary>
        /// <param name="volume">The voxel grid.</param>
        /// <param name="origin">Ray start, world coordinates.</param>
        /// <param name="direction">Ray direction, world coordinates (need not be unit).</param>
        public static List<int> TraversedVoxels(this ShadingVolume volume, Point3D origin, Vector3D direction)
        {
            if (volume == null || origin == null || direction == null || !direction.IsValid())
            {
                return null;
            }

            if (!volume.TryToLocal(origin, out double sx, out double sy, out double sz))
            {
                return null;
            }

            Vector3D unit = direction.Unit;
            Vector3D axisX = volume.AxisX;
            Vector3D axisY = volume.AxisY;
            Vector3D axisZ = volume.AxisZ;
            if (unit == null || axisX == null || axisY == null || axisZ == null)
            {
                return null;
            }

            double dx = unit.X * axisX.X + unit.Y * axisX.Y + unit.Z * axisX.Z;
            double dy = unit.X * axisY.X + unit.Y * axisY.Y + unit.Z * axisY.Z;
            double dz = unit.X * axisZ.X + unit.Y * axisZ.Y + unit.Z * axisZ.Z;

            List<int> result = new List<int>();
            March(volume, sx, sy, sz, dx, dy, dz, index => result.Add(index));
            return result;
        }

        /// <summary>
        /// Amanatides-Woo 3-D DDA traversal of the volume grid by a ray given in LOCAL
        /// coordinates. Every voxel the ray TRAVERSES (positive path length inside the voxel) is
        /// reported exactly once, in march order, until the ray leaves the grid. Rays starting
        /// outside are advanced to the grid entry point (slab test); rays missing the grid, or
        /// merely touching it tangentially at a corner/edge, report nothing.
        ///
        /// Lattice-degenerate starts are handled explicitly. Analysis-cell ray origins routinely
        /// land EXACTLY on a voxel plane (any gridSize that is a multiple of voxelSize does it —
        /// 0.5 / 0.25 is the obvious case), and there the naive floor() start voxel is wrong
        /// whenever the ray steps negatively on that axis: floor() names the voxel on the far side
        /// of the plane, which the ray only touches with zero path length. StartVoxel steps such an
        /// axis back by one so the first reported voxel is the one actually entered. Without this
        /// the field over-credits a shell of zero-thickness grazes along the aperture lattice.
        /// </summary>
        internal static void March(ShadingVolume volume, double sx, double sy, double sz, double dx, double dy, double dz, Action<int> visit)
        {
            double voxelSize = volume.VoxelSize;
            int countX = volume.CountX;
            int countY = volume.CountY;
            int countZ = volume.CountZ;

            double extentX = countX * voxelSize;
            double extentY = countY * voxelSize;
            double extentZ = countZ * voxelSize;

            // The lattice-coincidence tolerance, resolved for THIS ray against THIS grid.
            double tolerance = LatticeTolerance(volume, sx, sy, sz);

            // Slab entry/exit against the grid box [0, extent] on each axis.
            double tEnter = 0.0;
            double tExit = double.PositiveInfinity;
            if (!Slab(sx, dx, extentX, ref tEnter, ref tExit) ||
                !Slab(sy, dy, extentY, ref tEnter, ref tExit) ||
                !Slab(sz, dz, extentZ, ref tEnter, ref tExit))
            {
                return;
            }

            double t = Math.Max(tEnter, 0.0);

            // A ray that only grazes the grid (corner/edge touch) traverses no voxel at all.
            if (tExit - t <= tolerance * voxelSize)
            {
                return;
            }

            double px = sx + dx * t;
            double py = sy + dy * t;
            double pz = sz + dz * t;

            int stepX = dx > 0 ? 1 : -1;
            int stepY = dy > 0 ? 1 : -1;
            int stepZ = dz > 0 ? 1 : -1;

            int i = StartVoxel(px, voxelSize, countX, stepX, tolerance);
            int j = StartVoxel(py, voxelSize, countY, stepY, tolerance);
            int k = StartVoxel(pz, voxelSize, countZ, stepZ, tolerance);

            double tMaxX = FirstBoundary(px, dx, i, voxelSize, stepX);
            double tMaxY = FirstBoundary(py, dy, j, voxelSize, stepY);
            double tMaxZ = FirstBoundary(pz, dz, k, voxelSize, stepZ);

            double tDeltaX = dx == 0 ? double.PositiveInfinity : voxelSize / Math.Abs(dx);
            double tDeltaY = dy == 0 ? double.PositiveInfinity : voxelSize / Math.Abs(dy);
            double tDeltaZ = dz == 0 ? double.PositiveInfinity : voxelSize / Math.Abs(dz);

            double tRemaining = tExit - t;

            // Boundaries closer together than this along the ray are the SAME crossing. The
            // direction is unit, so this is a path length; below it the two boundaries are not
            // distinguishable and pretending they are is what produces the staircase below.
            double tieBreak = tolerance * voxelSize;

            while (true)
            {
                visit(volume.VoxelIndex(i, j, k));

                double tNext = Math.Min(tMaxX, Math.Min(tMaxY, tMaxZ));
                if (tNext > tRemaining)
                {
                    break;
                }

                // Step EVERY axis whose boundary falls at this same crossing, not one at a time.
                // A ray leaving a voxel exactly through an edge or a corner enters the diagonal
                // neighbour directly; stepping a single axis first would report the intervening
                // staircase voxels, each of which the ray touches with ZERO path length. Those are
                // exactly the phantom visits the lattice handling exists to prevent, and an
                // axis-aligned or diagonal sun direction reaches them on a regular grid.
                bool left = false;

                if (tMaxX <= tNext + tieBreak)
                {
                    i += stepX;
                    tMaxX += tDeltaX;
                    left |= i < 0 || i >= countX;
                }

                if (tMaxY <= tNext + tieBreak)
                {
                    j += stepY;
                    tMaxY += tDeltaY;
                    left |= j < 0 || j >= countY;
                }

                if (tMaxZ <= tNext + tieBreak)
                {
                    k += stepZ;
                    tMaxZ += tDeltaZ;
                    left |= k < 0 || k >= countZ;
                }

                if (left)
                {
                    break;
                }
            }
        }

        private static bool Slab(double s, double d, double extent, ref double tEnter, ref double tExit)
        {
            const double epsilon = 1e-15;
            if (Math.Abs(d) < epsilon)
            {
                return s >= 0.0 && s <= extent;
            }

            double t1 = (0.0 - s) / d;
            double t2 = (extent - s) / d;
            if (t1 > t2)
            {
                double temp = t1;
                t1 = t2;
                t2 = temp;
            }

            if (t1 > tEnter) { tEnter = t1; }
            if (t2 < tExit) { tExit = t2; }
            return tEnter <= tExit;
        }

        /// <summary>Floor of the relative tolerance, in voxel-size units (dimensionless).</summary>
        internal const double MinimumLatticeTolerance = 1e-9;

        /// <summary>
        /// The lattice-coincidence tolerance for one traversal, in VOXEL-SIZE units.
        ///
        /// The quantity actually tested is q = p / voxelSize, so the tolerance has to bound the
        /// representation error of q — and that error is NOT fixed. Local coordinates arrive from
        /// ShadingVolume.TryToLocal as (worldPoint - volumeOrigin) . axis: a difference of world
        /// coordinates, so its absolute error grows with the magnitude of the world coordinates
        /// involved, and dividing by a small voxel size amplifies it further. A model sited on a
        /// national grid (eastings of order 1e5-1e6 m) therefore carries an error in q several
        /// orders of magnitude larger than one sited near the origin, for identical geometry.
        ///
        /// A fixed 1e-9 is right for a model near the origin and WRONG at scale in both directions:
        /// too tight beyond roughly 1e6 m of offset, where a genuine lattice coincidence no longer
        /// registers and the negative-direction start voxel silently reverts to the phantom
        /// zero-length visit it was written to prevent; and needlessly loose for a fine grid near
        /// the origin. So the tolerance is scale-aware — proportional to the largest coordinate the
        /// traversal has actually handled, expressed in voxels — with 1e-9 kept as a floor so
        /// behaviour near the origin is exactly what it was before.
        ///
        /// The scale that governs the cancellation is the VOLUME ORIGIN's world magnitude, because
        /// every local coordinate here is worldPoint - origin with worldPoint inside the grid, so
        /// both operands have that magnitude. The local coordinates and grid extents are folded in
        /// as well so an enormous grid at a modest offset is covered too.
        ///
        /// This does not manufacture precision that is not there. At a 1e8 m offset the tolerance
        /// works out around 1.4 micrometres, and a double cannot resolve better than ~15 nanometres
        /// at that magnitude anyway; the point is to keep the tolerance ABOVE the noise floor
        /// instead of below it, which is what a fixed 1e-9 fails to do.
        /// </summary>
        internal static double LatticeTolerance(ShadingVolume volume, double sx, double sy, double sz)
        {
            double voxelSize = volume == null ? double.NaN : volume.VoxelSize;
            if (double.IsNaN(voxelSize) || voxelSize <= 0)
            {
                return MinimumLatticeTolerance;
            }

            double scale = Math.Abs(sx);
            scale = Math.Max(scale, Math.Abs(sy));
            scale = Math.Max(scale, Math.Abs(sz));
            scale = Math.Max(scale, volume.CountX * voxelSize);
            scale = Math.Max(scale, volume.CountY * voxelSize);
            scale = Math.Max(scale, volume.CountZ * voxelSize);

            scale = Math.Max(scale, volume.OriginMagnitude);

            if (double.IsNaN(scale) || double.IsInfinity(scale))
            {
                return MinimumLatticeTolerance;
            }

            // Several ulps of the largest coordinate, converted into voxel units. 64 is a working
            // margin over the handful of rounding steps between the world point and q, not a
            // tuned constant.
            double relative = 64.0 * scale * 2.220446049250313e-16 / voxelSize;
            return Math.Max(MinimumLatticeTolerance, relative);
        }

        /// <summary>
        /// The voxel a ray actually ENTERS at its grid-entry point, given the per-axis step sign.
        /// Ordinarily floor(p / voxelSize). When p lies exactly on a voxel plane and the ray steps
        /// negatively on that axis, floor() names the voxel the ray is leaving rather than the one
        /// it enters, so the index is decremented. Clamped into range against float drift at the
        /// slab-entry point.
        /// </summary>
        internal static int StartVoxel(double p, double voxelSize, int count, int step, double tolerance)
        {
            double q = p / voxelSize;
            int index = (int)Math.Floor(q);

            if (step < 0)
            {
                double nearest = Math.Round(q);
                if (Math.Abs(q - nearest) < tolerance)
                {
                    index = (int)nearest - 1;
                }
            }

            if (index < 0) { return 0; }
            if (index >= count) { return count - 1; }
            return index;
        }

        private static double FirstBoundary(double p, double d, int index, double voxelSize, int step)
        {
            if (d == 0)
            {
                return double.PositiveInfinity;
            }

            double boundary = step > 0 ? (index + 1) * voxelSize : index * voxelSize;
            return (boundary - p) / d;
        }
    }
}
