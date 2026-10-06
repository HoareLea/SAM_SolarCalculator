// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using SAM.Geometry.Object.Spatial;
using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Weather.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// Binary per-cell visibility of a single direction, using the same sun-projection machinery
        /// as the sampled simulation (sun-perpendicular plane, STRtree-indexed projected occluders,
        /// ray test against candidates only). A cell is visible when:
        ///   1. its outward normal faces the source (normal · towardDirection &gt; 0), AND
        ///   2. the ray from the cell's interior point TOWARD the source hits no occluder.
        /// The ray start is offset by tolerance_Snap toward the source so a ray running in a
        /// coplanar occluder's plane (grazing sun along the host wall) cannot self-intersect it.
        /// This is the single internal primitive the visibility caches are built from.
        /// </summary>
        /// <param name="occluders">Context faces. Every face occludes regardless of orientation.</param>
        /// <param name="points">Cell interior points.</param>
        /// <param name="normals">Cell outward normals (unit), one per point.</param>
        /// <param name="towardDirection">Unit vector pointing FROM the cell TOWARD the source (for direct sun: the negated sun direction).</param>
        internal static bool[] CellVisibility(List<LinkedFace3D> occluders, List<Point3D> points, List<Vector3D> normals, Vector3D towardDirection, double tolerance_Area, double tolerance_Angle, double tolerance_Distance, double tolerance_Snap)
        {
            if (points == null || normals == null || towardDirection == null || !towardDirection.IsValid())
            {
                return null;
            }

            bool[] result = new bool[points.Count];
            if (occluders == null || occluders.Count == 0)
            {
                // Nothing can shade: visibility is purely the front-facing test.
                for (int i = 0; i < points.Count; i++)
                {
                    result[i] = points[i] != null && normals[i] != null && normals[i].DotProduct(towardDirection) > 0;
                }

                return result;
            }

            // The shared projection machinery works in the light-propagation convention
            // (sun -> surface); negate the toward-source vector for it.
            Vector3D sunDirection = new Vector3D(towardDirection).GetNegated();

            Plane plane = Modify.SunPlane(occluders, sunDirection, out Vector3D vector3D, out Vector3D vector3D_Ray, tolerance_Distance);
            if (plane == null)
            {
                return null;
            }

            List<Modify.ProjectedFace> projectedFaces = Modify.ProjectedFaces(occluders, plane, vector3D, sunDirection, tolerance_Area, tolerance_Angle, tolerance_Distance);
            if (projectedFaces == null || projectedFaces.Count == 0)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    result[i] = points[i] != null && normals[i] != null && normals[i].DotProduct(towardDirection) > 0;
                }

                return result;
            }

            STRtree<int> index = new STRtree<int>();
            for (int i = 0; i < projectedFaces.Count; i++)
            {
                Envelope envelope = Modify.ToEnvelope(projectedFaces[i].BoundingBox2D, tolerance_Distance);
                if (envelope != null)
                {
                    index.Insert(envelope, i);
                }
            }
            index.Build();

            Vector3D ray = new Vector3D(towardDirection).Unit * vector3D_Ray.Length;
            Vector3D offset = new Vector3D(towardDirection).Unit * tolerance_Snap;

            for (int i = 0; i < points.Count; i++)
            {
                Point3D point3D = points[i];
                Vector3D normal = normals[i];
                if (point3D == null || normal == null)
                {
                    continue;
                }

                if (normal.DotProduct(towardDirection) <= 0)
                {
                    // Source behind the surface: shaded regardless of context (no ray needed).
                    continue;
                }

                Point3D point3D_Start = plane.Project(point3D, vector3D, tolerance_Distance);
                if (point3D_Start == null)
                {
                    continue;
                }

                Geometry.Planar.Point2D point2D = plane.Convert(point3D_Start);
                if (point2D == null)
                {
                    continue;
                }

                Envelope envelope = Modify.ToEnvelope(point2D, tolerance_Distance);
                if (envelope == null)
                {
                    continue;
                }

                IList<int> indexes = index.Query(envelope);
                if (indexes == null || indexes.Count == 0)
                {
                    result[i] = true;
                    continue;
                }

                List<LinkedFace3D> candidates = new List<LinkedFace3D>();
                foreach (int index_Temp in indexes)
                {
                    Modify.ProjectedFace projectedFace = projectedFaces[index_Temp];
                    if (projectedFace?.BoundingBox2D == null || projectedFace.Face2D == null || projectedFace.LinkedFace3D == null)
                    {
                        continue;
                    }

                    if (!projectedFace.BoundingBox2D.InRange(point2D, tolerance_Distance))
                    {
                        continue;
                    }

                    if (!projectedFace.Face2D.Inside(point2D, tolerance_Distance) && !projectedFace.Face2D.On(point2D, tolerance_Distance))
                    {
                        continue;
                    }

                    candidates.Add(projectedFace.LinkedFace3D);
                }

                if (candidates.Count == 0)
                {
                    result[i] = true;
                    continue;
                }

                Point3D point3D_RayStart = point3D.GetMoved(offset) as Point3D;
                Point3D point3D_RayEnd = point3D_RayStart?.GetMoved(ray) as Point3D;
                if (point3D_RayStart == null || point3D_RayEnd == null)
                {
                    continue;
                }

                Segment3D segment3D = new Segment3D(point3D_RayStart, point3D_RayEnd);
                List<System.Tuple<LinkedFace3D, Point3D>> tuples_Intersection = Geometry.Object.Spatial.Query.IntersectionTuples(segment3D, candidates, false, tolerance_Distance);

                result[i] = tuples_Intersection == null || tuples_Intersection.Count == 0;
            }

            return result;
        }
    }
}
