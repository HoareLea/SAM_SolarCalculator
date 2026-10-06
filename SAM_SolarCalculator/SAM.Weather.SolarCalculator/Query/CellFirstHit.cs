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
        /// <summary>A cell whose ray reaches the source unobstructed.</summary>
        public const int FirstHitVisible = -1;

        /// <summary>A cell whose outward normal faces away from the source: no ray is cast.</summary>
        public const int FirstHitBackFacing = -2;

        /// <summary>
        /// A sample deliberately NOT traced, because the caller established it could not be read.
        ///
        /// Stage 8 only ever consults attribution at samples the BASE visibility cache reports as
        /// lit — a sample already shaded by context is not the candidate's to claim, so the
        /// accounting skips it before it ever asks who was hit first. Tracing those samples produces
        /// a number nothing reads. This sentinel records that the ray was skipped rather than
        /// pretending it was cast and found nothing, so the two cases stay distinguishable in a
        /// stored cache. Like the other sentinels it is negative, so every existing reader — all of
        /// which test the sign — treats it as "not intercepted by the candidate".
        /// </summary>
        public const int FirstHitNotEvaluated = -3;

        /// <summary>
        /// Per-cell FIRST occluder along the ray toward the source: the index into <paramref name="occluders"/>
        /// of the nearest face hit, or FirstHitVisible / FirstHitBackFacing.
        ///
        /// This is the attribution twin of CellVisibility and deliberately mirrors it exactly — same
        /// sun-perpendicular projection plane, same STRtree candidate narrowing, same
        /// tolerance_Snap ray-start offset so a ray running in a coplanar occluder's plane cannot
        /// self-intersect it. Anywhere CellVisibility reports "not lit", this reports a non-negative
        /// index; anywhere it reports "lit", this reports FirstHitVisible. Keeping the two in
        /// lockstep is what lets Stage 8 attribute energy without contradicting the accepted
        /// Stage 0-4 visibility.
        ///
        /// Nearest-first comes from IntersectionTuples with sort enabled, which orders the hits by
        /// distance from the segment's start point, so element [0] is the physically first
        /// interception. Overlapping shading elements therefore credit only the one the sun reaches
        /// first, and reordering the input list cannot change the answer.
        /// </summary>
        /// <param name="occluders">Context and candidate faces. Index into this list is the returned attribution.</param>
        /// <param name="points">Cell interior points.</param>
        /// <param name="normals">Cell outward normals (unit), one per point.</param>
        /// <param name="towardDirection">Unit vector FROM the cell TOWARD the source.</param>
        internal static int[] CellFirstHit(List<LinkedFace3D> occluders, List<Point3D> points, List<Vector3D> normals, Vector3D towardDirection, double tolerance_Area, double tolerance_Angle, double tolerance_Distance, double tolerance_Snap)
        {
            if (points == null || normals == null || towardDirection == null || !towardDirection.IsValid())
            {
                return null;
            }

            int[] result = new int[points.Count];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = FirstHitBackFacing;
            }

            if (occluders == null || occluders.Count == 0)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    result[i] = points[i] != null && normals[i] != null && normals[i].DotProduct(towardDirection) > 0
                        ? FirstHitVisible
                        : FirstHitBackFacing;
                }

                return result;
            }

            Vector3D sunDirection = new Vector3D(towardDirection).GetNegated();

            Plane plane = Modify.SunPlane(occluders, sunDirection, out Vector3D vector3D, out Vector3D vector3D_Ray, tolerance_Distance);
            if (plane == null)
            {
                return null;
            }

            // Index of each occluder in the caller's list, so a projected face can be attributed
            // back to the exact input element regardless of how projection reorders or drops them.
            Dictionary<LinkedFace3D, int> occluderIndexes = new Dictionary<LinkedFace3D, int>();
            for (int i = 0; i < occluders.Count; i++)
            {
                if (occluders[i] != null && !occluderIndexes.ContainsKey(occluders[i]))
                {
                    occluderIndexes[occluders[i]] = i;
                }
            }

            List<Modify.ProjectedFace> projectedFaces = Modify.ProjectedFaces(occluders, plane, vector3D, sunDirection, tolerance_Area, tolerance_Angle, tolerance_Distance);
            if (projectedFaces == null || projectedFaces.Count == 0)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    result[i] = points[i] != null && normals[i] != null && normals[i].DotProduct(towardDirection) > 0
                        ? FirstHitVisible
                        : FirstHitBackFacing;
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
                    continue; // stays FirstHitBackFacing
                }

                Point3D point3D_Start = plane.Project(point3D, vector3D, tolerance_Distance);
                Geometry.Planar.Point2D point2D = point3D_Start == null ? null : plane.Convert(point3D_Start);
                Envelope envelope = point2D == null ? null : Modify.ToEnvelope(point2D, tolerance_Distance);
                if (envelope == null)
                {
                    continue;
                }

                IList<int> indexes = index.Query(envelope);
                if (indexes == null || indexes.Count == 0)
                {
                    result[i] = FirstHitVisible;
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
                    result[i] = FirstHitVisible;
                    continue;
                }

                Point3D point3D_RayStart = point3D.GetMoved(offset) as Point3D;
                Point3D point3D_RayEnd = point3D_RayStart?.GetMoved(ray) as Point3D;
                if (point3D_RayStart == null || point3D_RayEnd == null)
                {
                    continue;
                }

                Segment3D segment3D = new Segment3D(point3D_RayStart, point3D_RayEnd);

                // sort: true — nearest hit first, measured from the ray start.
                List<System.Tuple<LinkedFace3D, Point3D>> tuples = Geometry.Object.Spatial.Query.IntersectionTuples(segment3D, candidates, true, tolerance_Distance);
                if (tuples == null || tuples.Count == 0)
                {
                    result[i] = FirstHitVisible;
                    continue;
                }

                result[i] = occluderIndexes.TryGetValue(tuples[0].Item1, out int occluderIndex) ? occluderIndex : FirstHitVisible;
            }

            return result;
        }
    }
}
