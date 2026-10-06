// SPDX-License-Identifier: LGPL-3.0-or-later
// Copyright (c) 2020–2026 Michal Dengusiak & Jakub Ziolkowski and contributors

using System;
using System.Collections.Generic;
using SAM.Geometry.SolarCalculator;
using SAM.Geometry.Spatial;

namespace SAM.SolarCalculator.Tests
{
    /// <summary>
    /// Free-standing analysis targets built directly from geometry (no AnalyticalModel), used by the
    /// validation studies where the model machinery would only add noise. The face is always
    /// re-oriented to the requested OUTWARD normal, exactly as
    /// SAM.Analytical.SolarCalculator.Create.ApertureSolarTargets does, so the analysis-grid plane
    /// normal and the evaluation normal agree by construction.
    /// </summary>
    internal static class SyntheticTargets
    {
        public static readonly Vector3D North = new Vector3D(0, 1, 0);
        public static readonly Vector3D East = new Vector3D(1, 0, 0);
        public static readonly Vector3D South = new Vector3D(0, -1, 0);
        public static readonly Vector3D West = new Vector3D(-1, 0, 0);
        public static readonly Vector3D Up = new Vector3D(0, 0, 1);

        /// <summary>A single square analysis cell of the given size, centred at centre, facing outward.</summary>
        public static List<AnalysisCell> Cell(Vector3D outward, Point3D centre = null, double size = 1.0)
        {
            return Geometry.SolarCalculator.Query.AnalysisCells(Face(outward, centre, size), size);
        }

        /// <summary>A square face of the given size, centred at centre, with its plane normal = outward.</summary>
        public static Face3D Face(Vector3D outward, Point3D centre = null, double size = 1.0)
        {
            return Face(outward, centre, size, size);
        }

        /// <summary>
        /// A rectangular face of the given width and height, centred at centre, with its plane
        /// normal = outward. Width runs along the local X axis (horizontal for a vertical window),
        /// height along the local Y axis (up-slope).
        /// </summary>
        public static Face3D Face(Vector3D outward, Point3D centre, double width, double height)
        {
            Vector3D normal = outward.Unit;
            centre = centre ?? new Point3D(0, 0, 5);

            Vector3D axisX = Vector3D.WorldZ.CrossProduct(normal);
            axisX = axisX == null || axisX.Length < 1e-9 ? Vector3D.WorldX : axisX.Unit;
            Vector3D axisY = normal.CrossProduct(axisX).Unit;

            double halfX = width / 2.0;
            double halfY = height / 2.0;
            Face3D result = new Face3D(new Polygon3D(new List<Point3D>
            {
                Corner(centre, axisX, axisY, -halfX, -halfY),
                Corner(centre, axisX, axisY, halfX, -halfY),
                Corner(centre, axisX, axisY, halfX, halfY),
                Corner(centre, axisX, axisY, -halfX, halfY),
            }));

            Plane plane = result.GetPlane();
            if (plane?.Normal != null && plane.Normal.DotProduct(normal) < 0)
            {
                result.FlipNormal(true);
            }

            return result;
        }

        private static Point3D Corner(Point3D centre, Vector3D axisX, Vector3D axisY, double u, double v)
        {
            return new Point3D(
                centre.X + axisX.X * u + axisY.X * v,
                centre.Y + axisX.Y * u + axisY.Y * v,
                centre.Z + axisX.Z * u + axisY.Z * v);
        }

        /// <summary>The outward normal repeated once per cell, in cell order (the cellNormals contract).</summary>
        public static List<Vector3D> Normals(List<AnalysisCell> cells, Vector3D outward)
        {
            return cells.ConvertAll(x => new Vector3D(outward.Unit));
        }
    }
}
