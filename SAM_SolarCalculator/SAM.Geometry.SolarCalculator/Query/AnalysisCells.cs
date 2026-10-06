// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using SAM.Geometry.Spatial;
using System.Collections.Generic;

namespace SAM.Geometry.SolarCalculator
{
    public static partial class Query
    {
        /// <summary>
        /// Subdivides a Face3D into a grid of analysis cells in the face's own plane. The grid is
        /// anchored at the face's 2D bounding-box minimum; each candidate rectangle is clipped
        /// against the face, so concave or holed faces are handled by the intersection (a cell that
        /// does not overlap the face is dropped, one partially overlapping is kept at its clipped
        /// shape). Cell order — and therefore AnalysisCell.Index — is stable for a given face and
        /// cell size. Extracted from the sampled-simulation cell builder in
        /// SAM.Weather.SolarCalculator.Modify.Simulate so both share one implementation.
        /// </summary>
        public static List<AnalysisCell> AnalysisCells(this Face3D face3D, double cellSize, double tolerance_Area = Core.Tolerance.MacroDistance, double tolerance_Distance = Core.Tolerance.Distance)
        {
            Plane plane = face3D?.GetPlane();
            if (plane == null || double.IsNaN(cellSize) || cellSize <= tolerance_Distance)
            {
                return null;
            }

            Planar.Face2D face2D = plane.Convert(face3D);
            Planar.BoundingBox2D boundingBox2D = face2D?.GetBoundingBox();
            if (face2D == null || boundingBox2D == null)
            {
                return null;
            }

            Planar.Point2D min = boundingBox2D.Min;
            Planar.Point2D max = boundingBox2D.Max;
            if (min == null || max == null)
            {
                return null;
            }

            List<AnalysisCell> result = new List<AnalysisCell>();
            for (double x = min.X; x < max.X; x += cellSize)
            {
                double width = System.Math.Min(cellSize, max.X - x);
                if (width <= tolerance_Distance)
                {
                    continue;
                }

                for (double y = min.Y; y < max.Y; y += cellSize)
                {
                    double height = System.Math.Min(cellSize, max.Y - y);
                    if (height <= tolerance_Distance)
                    {
                        continue;
                    }

                    // Do NOT reject the cell by whether its centre is inside the face: for concave or
                    // triangular faces a cell can overlap the face (area above tolerance) while its centre
                    // lies outside. Let the clipping below decide — cells that don't overlap produce an
                    // empty intersection and are dropped, so no valid sunlit area is lost.
                    Planar.Rectangle2D rectangle2D = new Planar.Rectangle2D(new Planar.Point2D(x, y), width, height);
                    Planar.Face2D face2D_Cell = rectangle2D;

                    List<Planar.Face2D> face2Ds_Cell = null;
                    if (face2D.Inside(rectangle2D, tolerance_Distance))
                    {
                        face2Ds_Cell = new List<Planar.Face2D>() { face2D_Cell };
                    }
                    else
                    {
                        face2Ds_Cell = Planar.Query.Intersection(face2D_Cell, face2D, tolerance_Distance);
                    }

                    if (face2Ds_Cell == null || face2Ds_Cell.Count == 0)
                    {
                        continue;
                    }

                    foreach (Planar.Face2D face2D_Temp in face2Ds_Cell)
                    {
                        if (face2D_Temp == null || face2D_Temp.GetArea() < tolerance_Area)
                        {
                            continue;
                        }

                        Planar.Point2D point2D = face2D_Temp.GetInternalPoint2D(tolerance_Distance);
                        Point3D point3D = plane.Convert(point2D);
                        Face3D face3D_Cell = plane.Convert(face2D_Temp);
                        if (point3D == null || face3D_Cell == null || !face3D_Cell.IsValid())
                        {
                            continue;
                        }

                        result.Add(new AnalysisCell(result.Count, face3D_Cell, point3D));
                    }
                }
            }

            return result;
        }
    }
}
